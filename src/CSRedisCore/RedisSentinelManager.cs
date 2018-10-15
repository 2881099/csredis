using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

// http://redis.io/topics/sentinel-clients

namespace CSRedis
{
    /// <summary>
    /// Represents a managed connection to a Redis master instance via a set of Redis sentinel nodes
    /// </summary>
    public class RedisSentinelManager : IDisposable
    {
        const int DefaultPort = 26379;
        readonly LinkedList<Tuple<string, int>> _sentinels;
        string _masterName;
        int _connectTimeout;
        RedisClient _redisClient;

        /// <summary>
        /// Occurs when the master connection has sucessfully connected
        /// </summary>
        public event EventHandler Connected;

        /// <summary>
        /// Create a new RedisSentinenlManager
        /// </summary>
        /// <param name="sentinels">Sentinel addresses (host:ip)</param>
        public RedisSentinelManager(params string[] sentinels)
        {
            _sentinels = new LinkedList<Tuple<string, int>>();
            foreach (var host in sentinels)
            {
                string[] parts = host.Split(':');
                string hostname = parts[0].Trim();
                int port = Int32.Parse(parts[1]);
                Add(host, port);
            }
        }

        /// <summary>
        /// Add a new sentinel host using default port
        /// </summary>
        /// <param name="host">Sentinel hostname</param>
        public void Add(string host)
        {
            Add(host, DefaultPort);
        }

        /// <summary>
        /// Add a new sentinel host
        /// </summary>
        /// <param name="host">Sentinel hostname</param>
        /// <param name="port">Sentinel port</param>
        public void Add(string host, int port)
        {
            foreach (var sentinel in _sentinels)
            {
                if (sentinel.Item1 == host && sentinel.Item2 == port)
                    return;
            }
            _sentinels.AddLast(Tuple.Create(host, port));
        }

        /// <summary>
        /// Obtain connection to the specified master node
        /// </summary>
        /// <param name="masterName">Name of Redis master</param>
        /// <param name="timeout">Connection timeout (milliseconds)</param>
        /// <returns>host:port of Sentinel server that responded</returns>
        public string Connect(string masterName, int timeout = 200)
        {
            _masterName = masterName;
            _connectTimeout = timeout;

            string sentinel = SetMaster(masterName, timeout);
            if (sentinel == null)
                throw new IOException("Could not connect to sentinel or master");

            _redisClient.ReconnectAttempts = 0;
            return sentinel;
        }

        /// <summary>
        /// Execute command against the master, reconnecting if necessary
        /// </summary>
        /// <typeparam name="T">Command return type</typeparam>
        /// <param name="redisAction">Command to execute</param>
        /// <returns>Command result</returns>
        public T Call<T>(Func<RedisClient, T> redisAction)
        {
            if (_masterName == null)
                throw new InvalidOperationException("Master not set");

            try
            {
                return redisAction(_redisClient);
            }
            catch (IOException)
            {
                Next();
                Connect(_masterName, _connectTimeout);
                return Call(redisAction);
            }
        }

        /// <summary>
        /// Release resources held by the current RedisSentinelManager
        /// </summary>
        public void Dispose()
        {
            if (_redisClient != null)
                _redisClient.Dispose();
        }

        string SetMaster(string name, int timeout)
        {
            for (int i = 0; i < _sentinels.Count; i++)
            {
                if (i > 0)
                    Next();

                using (var sentinel = Current())
                {
                    try
                    {
                        if (!sentinel.Connect(timeout))
                            continue;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    var master = sentinel.GetMasterAddrByName(name);
                    if (master == null)
                        continue;

                    _redisClient = new RedisClient(master.Item1, master.Item2);
                    _redisClient.Connected += OnConnectionConnected;
                    if (!_redisClient.Connect(timeout))
                        continue;

                    var role = _redisClient.Role();
                    if (role.RoleName != "master")
                        continue;

                    foreach (var remoteSentinel in sentinel.Sentinels(name))
                        Add(remoteSentinel.Ip, remoteSentinel.Port);

                    return sentinel.Host + ':' + sentinel.Port;
                }

            }
            return null;
        }

        RedisSentinelClient Current()
        {
            return new RedisSentinelClient(_sentinels.First.Value.Item1, _sentinels.First.Value.Item2);
        }

        void Next()
        {
            var first = _sentinels.First;
            _sentinels.RemoveFirst();
            _sentinels.AddLast(first.Value);
        }

        void OnConnectionConnected(object sender, EventArgs args)
        {
            if (Connected != null)
                Connected(this, new EventArgs());
        }
    }
}