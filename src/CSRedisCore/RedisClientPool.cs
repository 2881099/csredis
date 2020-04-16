using CSRedis.Internal.ObjectPool;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;

namespace CSRedis
{
    public class RedisClientPool : ObjectPool<RedisClient>
    {

        public RedisClientPool(string connectionString, Action<RedisClient> onConnected) : base(null)
        {
            _policy = new RedisClientPoolPolicy
            {
                _pool = this
            };
            _policy.Connected += (s, o) =>
            {
                RedisClient rc = s as RedisClient;
                try
                {
                    rc.ReceiveTimeout = _policy._syncTimeout;
                    rc.SendTimeout = _policy._syncTimeout;
                }
                catch { }
                if (!string.IsNullOrEmpty(_policy._password))
                {
                    try
                    {
                        rc.Auth(_policy._password);
                    }
                    catch (Exception authEx)
                    {
                        if (authEx.Message != "ERR Client sent AUTH, but no password is set")
                            throw authEx;
                    }
                }
                if (_policy._database > 0) rc.Select(_policy._database);
                onConnected(s as RedisClient);
            };
            this.Policy = _policy;
            _policy.ConnectionString = connectionString;
        }

        public void Return(Object<RedisClient> obj, Exception exception, bool isRecreate = false)
        {
            if (exception != null)
            {
                try
                {
                    try
                    {
                        if (!obj.Value.IsConnected) obj.Value.Connect(_policy._connectTimeout);
                        obj.Value.Ping();

                        var fcolor = Console.ForegroundColor;
                        Console.WriteLine($"");
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"csreids 错误【{Policy.Name}】：{exception.Message} {exception.StackTrace}");
                        Console.ForegroundColor = fcolor;
                        Console.WriteLine($"");
                    }
                    catch
                    {
                        obj.ResetValue();
                        if (!obj.Value.IsConnected) obj.Value.Connect(_policy._connectTimeout);
                        obj.Value.Ping();
                    }
                }
                catch (Exception ex)
                {
                    base.SetUnavailable(ex);
                }
            }
            base.Return(obj, isRecreate);
        }

        internal bool CheckAvailable() => base.LiveCheckAvailable();

        internal RedisClientPoolPolicy _policy;
        public string Key => _policy.Key;
        public string Prefix => _policy.Prefix;
        public Encoding Encoding { get; set; } = new UTF8Encoding(false);
    }

    public class RedisClientPoolPolicy : IPolicy<RedisClient>
    {

        internal RedisClientPool _pool;
        internal int _port = 6379, _database = 0, _tryit = 0, _connectTimeout = 5000, _syncTimeout = 10000;
        internal string _ip = "127.0.0.1", _password = "", _clientname = "";
        internal bool _ssl = false, _testCluster = true, _asyncPipeline = false;
        internal int _preheat = 5;
        internal string Key => $"{_ip}:{_port}/{_database}";
        internal string Prefix { get; set; }
        public event EventHandler Connected;

        public string Name { get => Key; set { throw new Exception("RedisClientPoolPolicy 不提供设置 Name 属性值。"); } }
        public int PoolSize { get; set; } = 50;
        public TimeSpan SyncGetTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(20);
        public int AsyncGetCapacity { get; set; } = 100000;
        public bool IsThrowGetTimeoutException { get; set; } = true;
        public bool IsAutoDisposeWithSystem { get; set; } = true;
        public int CheckAvailableInterval { get; set; } = 5;

        internal string BuildConnectionString(string endpoint)
        {
            return $"{endpoint},password={_password},defaultDatabase={_database},poolsize={PoolSize}," +
                $"connectTimeout={_connectTimeout},syncTimeout={_syncTimeout},idletimeout={(int)IdleTimeout.TotalMilliseconds}," +
                $"preheat=false,ssl={(_ssl ? "true" : "false")},tryit={_tryit},name={_clientname},prefix={Prefix}," + 
                $"autodispose={(IsAutoDisposeWithSystem ? "true" : "false")},asyncpipeline={(_asyncPipeline ? "true" : "false")}";
        }

        internal void SetHost(string host)
        {
            if (string.IsNullOrEmpty(host?.Trim())) {
                _ip = "127.0.0.1";
                _port = 6379;
                return;
            }
            host = host.Trim();
            var ipv6 = Regex.Match(host, @"^\[([^\]]+)\]\s*(:\s*(\d+))?$");
            if (ipv6.Success) //ipv6+port 格式： [fe80::b164:55b3:4b4f:7ce6%15]:6379
            {
                _ip = ipv6.Groups[1].Value.Trim();
                _port = int.TryParse(ipv6.Groups[3].Value, out var tryint) && tryint > 0 ? tryint : 6379;
                return;
            }
            var spt = (host ?? "").Split(':');
            if (spt.Length == 1) //ipv4 or domain
            {
                _ip = string.IsNullOrEmpty(spt[0].Trim()) == false ? spt[0].Trim() : "127.0.0.1";
                _port = 6379;
                return;
            }
            if (spt.Length == 2) //ipv4:port or domain:port
            {
                if (int.TryParse(spt.Last().Trim(), out var testPort2))
                {
                    _ip = string.IsNullOrEmpty(spt[0].Trim()) == false ? spt[0].Trim() : "127.0.0.1";
                    _port = testPort2;
                    return;
                }
                _ip = host;
                _port = 6379;
                return;
            }
            if (IPAddress.TryParse(host, out var tryip) && tryip.AddressFamily == AddressFamily.InterNetworkV6) //test ipv6
            {
                _ip = host;
                _port = 6379;
                return;
            }
            if (int.TryParse(spt.Last().Trim(), out var testPort)) //test ipv6:port
            {
                var testHost = string.Join(":", spt.Where((a, b) => b < spt.Length - 1));
                if (IPAddress.TryParse(testHost, out tryip) && tryip.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    _ip = testHost;
                    _port = 6379;
                    return;
                }
            }
            _ip = host;
            _port = 6379;
        }

        private string _connectionString;
        public string ConnectionString
        {
            get => _connectionString;
            set
            {
                _connectionString = value;
                if (string.IsNullOrEmpty(_connectionString)) return;

                //支持密码中带有逗号，将原有 split(',') 改成以下处理方式
                var vs = Regex.Split(_connectionString, @"\,([\w \t\r\n]+)=", RegexOptions.Multiline);
                this.SetHost(vs[0]);

                for (var a = 1; a < vs.Length; a += 2)
                {
                    var kv = new[] { vs[a].ToLower().Trim(), vs[a + 1] };
                    switch (kv[0])
                    {
                        case "password":
                            _password = kv.Length > 1 ? kv[1] : "";
                            break;
                        case "prefix":
                            Prefix = kv.Length > 1 ? kv[1] : "";
                            break;
                        case "defaultdatabase":
                            _database = int.TryParse(kv.Length > 1 ? kv[1].Trim() : "0", out _database) ? _database : 0;
                            break;
                        case "poolsize":
                            PoolSize = int.TryParse(kv.Length > 1 ? kv[1].Trim() : "0", out var poolsize) == false || poolsize <= 0 ? 50 : poolsize;
                            break;
                        case "ssl":
                            _ssl = kv.Length > 1 ? kv[1].ToLower().Trim() == "true" : false;
                            break;
                        case "preheat":
                            var kvtrim = kv.Length > 1 ? kv[1].ToLower().Trim() : null;
                            _preheat = kvtrim == "true" ? -1 : (int.TryParse(kvtrim, out _preheat) ? _preheat : 0);
                            break;
                        case "name":
                            _clientname = kv.Length > 1 ? kv[1] : "";
                            break;
                        case "tryit":
                            _tryit = int.TryParse(kv.Length > 1 ? kv[1].Trim() : "0", out _tryit) ? _tryit : 0;
                            break;
                        case "connecttimeout":
                            _connectTimeout = int.TryParse(kv.Length > 1 ? kv[1].Trim() : "5000", out var connectTimeout) == false || connectTimeout <= 0 ? 5000 : connectTimeout;
                            break;
                        case "synctimeout":
                            _syncTimeout = int.TryParse(kv.Length > 1 ? kv[1].Trim() : "10000", out var syncTimeout) == false || syncTimeout <= 0 ? 10000 : syncTimeout;
                            break;
                        case "idletimeout":
                            IdleTimeout = TimeSpan.FromMilliseconds(int.TryParse(kv.Length > 1 ? kv[1].Trim() : "0", out var idleTimeout) == false || idleTimeout <= 0 ? 0 : idleTimeout);
                            break;
                        case "testcluster":
                            _testCluster = kv.Length > 1 ? kv[1].ToLower().Trim() == "true" : true;
                            break;
                        case "autodispose":
                            IsAutoDisposeWithSystem = kv.Length > 1 ? kv[1].ToLower().Trim() == "true" : true;
                            break;
                        case "asyncpipeline":
                            _asyncPipeline = kv.Length > 1 ? kv[1].ToLower().Trim() == "true" : true;
                            break;
                    }
                }

                if (_preheat < 0) _preheat = PoolSize;
                if (_preheat > 0)
                    PrevReheatConnectionPool(_pool, _preheat);
            }
        }

        public bool OnCheckAvailable(Object<RedisClient> obj)
        {
            obj.ResetValue();
            if (!obj.Value.IsConnected) obj.Value.Connect(_connectTimeout);
            return obj.Value.Ping() == "PONG";
        }

        public RedisClient OnCreate()
        {
            RedisClient client = null;
            if (IPAddress.TryParse(_ip, out var tryip))
            {
                client = new RedisClient(new IPEndPoint(tryip, _port), _ssl);
            }
            else
            {
                var ips = Dns.GetHostAddresses(_ip);
                if (ips.Length == 0) throw new Exception($"无法解析“{_ip}”");
                client = new RedisClient(_ip, _port, _ssl);
            }
            client.Connected += (s, o) =>
            {
                Connected(s, o);
                if (!string.IsNullOrEmpty(_clientname)) client.ClientSetName(_clientname);
            };
            return client;
        }

        public void OnDestroy(RedisClient obj)
        {
            if (obj != null)
            {
                //if (obj.IsConnected) try { obj.Quit(); } catch { } 此行会导致，服务器主动断开后，执行该命令超时停留10-20秒
                try { obj.Dispose(); } catch { }
            }
        }

        public void OnGet(Object<RedisClient> obj)
        {
            if (_pool.Encoding != obj.Value.Encoding) obj.Value.Encoding = _pool.Encoding;
            if (_pool.IsAvailable)
            {
                if (DateTime.Now.Subtract(obj.LastReturnTime).TotalSeconds > 60 || obj.Value.IsConnected == false)
                {
                    try
                    {
                        if (!obj.Value.IsConnected) obj.Value.Connect(_connectTimeout);
                        obj.Value.Ping();
                    }
                    catch
                    {
                        obj.ResetValue();
                    }
                }
            }
        }
#if net40
#else
        async public Task OnGetAsync(Object<RedisClient> obj)
        {
            if (_pool.Encoding != obj.Value.Encoding) obj.Value.Encoding = _pool.Encoding;
            if (_pool.IsAvailable)
            {
                if (DateTime.Now.Subtract(obj.LastReturnTime).TotalSeconds > 60 || obj.Value.IsConnected == false)
                {
                    try
                    {
                        if (!obj.Value.IsConnected) obj.Value.Connect(_connectTimeout);
                        await obj.Value.PingAsync();
                    }
                    catch
                    {
                        obj.ResetValue();
                    }
                }
            }
        }
#endif

        public void OnGetTimeout()
        {

        }

        public void OnReturn(Object<RedisClient> obj)
        {

        }

        public void OnAvailable()
        {
        }
        public void OnUnavailable()
        {
        }

        public static void PrevReheatConnectionPool(ObjectPool<RedisClient> pool, int minPoolSize)
        {
            if (minPoolSize <= 0) minPoolSize = Math.Min(5, pool.Policy.PoolSize);
            if (minPoolSize > pool.Policy.PoolSize) minPoolSize = pool.Policy.PoolSize;
            var initTestOk = true;
            var initStartTime = DateTime.Now;
            var initConns = new ConcurrentBag<Object<RedisClient>>();

            try
            {
                var conn = pool.Get();
                initConns.Add(conn);
                pool.Policy.OnCheckAvailable(conn);
            }
            catch (Exception ex)
            {
                initTestOk = false; //预热一次失败，后面将不进行
                pool.SetUnavailable(ex);
            }
            for (var a = 1; initTestOk && a < minPoolSize; a += 10)
            {
                if (initStartTime.Subtract(DateTime.Now).TotalSeconds > 3) break; //预热耗时超过3秒，退出
                var b = Math.Min(minPoolSize - a, 10); //每10个预热
                var initTasks = new Task[b];
                for (var c = 0; c < b; c++)
                {
                    initTasks[c] = TaskEx.Run(() =>
                    {
                        try
                        {
                            var conn = pool.Get();
                            initConns.Add(conn);
                            pool.Policy.OnCheckAvailable(conn);
                        }
                        catch
                        {
                            initTestOk = false;  //有失败，下一组退出预热
                        }
                    });
                }
                Task.WaitAll(initTasks);
            }
            while (initConns.TryTake(out var conn)) pool.Return(conn);
        }
    }
}
