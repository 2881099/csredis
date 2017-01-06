using CSRedis.Internal.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CSRedis
{
    /// <summary>
    /// Represents a pooled collection of Redis connections
    /// </summary>
    public class RedisConnectionPool : IDisposable
    {
        readonly EndPoint _endPoint;
        readonly SocketPool _pool;
		//readonly string pass;
		//readonly int database;

        /// <summary>
        /// Create a new connection pool
        /// </summary>
        /// <param name="host">Redis server host</param>
        /// <param name="port">Redis server port</param>
        /// <param name="max">Maximum simultaneous connections</param>
        public RedisConnectionPool(string host, int port, int max)
            : this(new DnsEndPoint(host, port), max)
        { }

        /// <summary>
        /// Create a new connection pool
        /// </summary>
        /// <param name="endPoint">Redis server</param>
        /// <param name="max">Maximum simultaneous connections</param>
        
        public RedisConnectionPool(EndPoint endPoint, int max)
        {
            _pool = new SocketPool(endPoint, max);
            _endPoint = endPoint;
        }

        /// <summary>
        /// Get a pooled Redis Client instance
        /// </summary>
        /// <param name="asyncConcurrency">Max concurrent threads (default 1000)</param>
        /// <param name="asyncBufferSize">Async thread buffer size (default 10240 bytes)</param>
        /// <returns>RedisClient instance from pool</returns>
        public RedisClient GetClient(int asyncConcurrency, int asyncBufferSize)
        {
			var rc = new RedisClient(new RedisPooledSocket(_pool), _endPoint, asyncConcurrency, asyncBufferSize);
			//rc.Connected += (s, o) => {
			//	if (database > 0) rc.Select(database);
			//	if (!string.IsNullOrEmpty(pass)) rc.Auth(pass);
			//};
			return rc;
        }

        /// <summary>
        /// Get a pooled Redis Client instance
        /// </summary>
        /// <returns>RedisClient instance from pool</returns>
        public RedisClient GetClient()
        {
            return new RedisClient(new RedisPooledSocket(_pool), _endPoint);
        }

        /// <summary>
        /// Get a pooled Sentinel Client instance
        /// </summary>
        /// <param name="asyncConcurrency">Max concurrent threads (default 1000)</param>
        /// <param name="asyncBufferSize">Async thread buffer size (default 10240 bytes)</param>
        /// <returns>Sentinel Client from pool</returns>
        public RedisSentinelClient GetSentinelClient(int asyncConcurrency, int asyncBufferSize)
        {
            return new RedisSentinelClient(new RedisPooledSocket(_pool), _endPoint, asyncConcurrency, asyncBufferSize);
        }

        /// <summary>
        /// Get a pooled Sentinel Client instance
        /// </summary>
        /// <returns>Sentinel Client from pool</returns>
        public RedisSentinelClient GetSentinelClient()
        {
            return new RedisSentinelClient(new RedisPooledSocket(_pool), _endPoint);
        }

        /// <summary>
        /// Close all open pooled connections
        /// </summary>
        public void Dispose()
        {
            _pool.Dispose();
        }
    }

    class RedisPooledSocket : IRedisSocket
    {
        Socket _socket;
        readonly SocketPool _pool;

        public bool Connected { get { return _socket == null ? false : _socket.Connected; } }

        public int ReceiveTimeout
        {
            get { return _socket.ReceiveTimeout; }
            set { _socket.ReceiveTimeout = value; }
        }

        public int SendTimeout
        {
            get { return _socket.SendTimeout; }
            set { _socket.SendTimeout = value; }
        }

        public RedisPooledSocket(SocketPool pool)
        {
            _pool = pool;
        }

        public void Connect(EndPoint endpoint)
        {
            _socket = _pool.Connect();
            System.Diagnostics.Debug.WriteLine("Got socket #{0}", _socket.LocalEndPoint);
        }

        public bool ConnectAsync(SocketAsyncEventArgs args)
        {
            return _pool.ConnectAsync(args, out _socket);
        }

        public bool SendAsync(SocketAsyncEventArgs args)
        {
            return _socket.SendAsync(args);
        }

        public Stream GetStream()
        {
            return new NetworkStream(_socket);
        }

        public void Dispose()
        {
            _pool.Release(_socket);
        }
    }
}
