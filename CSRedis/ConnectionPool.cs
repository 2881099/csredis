using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CSRedis {
	/// <summary>
	/// Connection链接池
	/// </summary>
	public partial class ConnectionPool {

		private int _poolsize = 50, _port = 6379, _database = 0, _writebuffer = 10240;
		private string _ip = "127.0.0.1", _password = "";
		private bool _ssl = false;
		internal string ClusterKey => $"{_ip}:{_port}/{_database}";
		internal string Prefix { get; set; }
		public List<RedisConnection2> AllConnections = new List<RedisConnection2>();
		public Queue<RedisConnection2> FreeConnections = new Queue<RedisConnection2>();
		public Queue<ManualResetEventSlim> GetConnectionQueue = new Queue<ManualResetEventSlim>();
		public Queue<TaskCompletionSource<RedisConnection2>> GetConnectionAsyncQueue = new Queue<TaskCompletionSource<RedisConnection2>>();
		private static object _lock = new object();
		private static object _lock_GetConnectionQueue = new object();
		public event EventHandler Connected;
		private string _connectionString;
		public string ConnectionString {
			get { return _connectionString; }
			set {
				_connectionString = value;
				if (string.IsNullOrEmpty(_connectionString)) return;
				var vs = _connectionString.Split(',');
				foreach(var v in vs) {
					if (v.IndexOf('=') == -1) {
						var host = v.Split(':');
						_ip = string.IsNullOrEmpty(host[0].Trim()) == false ? host[0].Trim() : "127.0.0.1";
						if (host.Length < 2 || int.TryParse(host[1].Trim(), out _port) == false) _port = 6379;
						continue;
					}
					var kv = v.Split(new[] { '=' }, 2);
					if (kv[0].ToLower().Trim() == "password") _password = kv.Length > 1 ? kv[1] : "";
					else if (kv[0].ToLower().Trim() == "prefix") Prefix = kv.Length > 1 ? kv[1] : "";
					else if (kv[0].ToLower().Trim() == "defaultdatabase") _database = int.TryParse(kv.Length > 1 ? kv[1] : "0", out _database) ? _database : 0;
					else if (kv[0].ToLower().Trim() == "poolsize") _poolsize = int.TryParse(kv.Length > 1 ? kv[1] : "0", out _poolsize) ? _poolsize : 50;
					else if (kv[0].ToLower().Trim() == "ssl") _ssl = kv.Length > 1 ? kv[1] == "true" : false;
					else if (kv[0].ToLower().Trim() == "writebuffer") _writebuffer = int.TryParse(kv.Length > 1 ? kv[1] : "10240", out _writebuffer) ? _writebuffer : 10240;
				}
				if (_poolsize <= 0) _poolsize = 50;
				var initConns = new RedisConnection2[_poolsize];
				for (var a = 0; a < _poolsize; a++) initConns[a] = GetFreeConnection();
				foreach (var conn in initConns) ReleaseConnection(conn);
			}
		}
		public ConnectionPool() {
			Connected += (s, o) => {
				RedisClient rc = s as RedisClient;
				if (!string.IsNullOrEmpty(_password)) rc.Auth(_password);
				if (_database > 0) rc.Select(_database);
			};
		}

		private RedisConnection2 GetFreeConnection() {
			RedisConnection2 conn = null;
			if (FreeConnections.Count > 0)
				lock (_lock)
					if (FreeConnections.Count > 0)
						conn = FreeConnections.Dequeue();
			if (conn == null && AllConnections.Count < _poolsize) {
				lock (_lock)
					if (AllConnections.Count < _poolsize) {
						conn = new RedisConnection2();
						AllConnections.Add(conn);
					}
				if (conn != null) {
					conn.Pool = this;
					var ips = Dns.GetHostAddresses(_ip);
					if (ips.Length == 0) throw new Exception($"无法解析“{_ip}”");
					conn.Client = new RedisClient(new IPEndPoint(ips[0], _port), _ssl, 1000, _writebuffer);
					conn.Client.Connected += Connected;
				}
			}
			return conn;
		}
		public RedisConnection2 GetConnection () {
			var conn = GetFreeConnection();
			if (conn == null) {
				ManualResetEventSlim wait = new ManualResetEventSlim(false);
				lock (_lock_GetConnectionQueue)
					GetConnectionQueue.Enqueue(wait);
				if (wait.Wait(TimeSpan.FromSeconds(10)))
					return GetConnection();
				throw new Exception("CSRedis.ConnectionPool.GetConnection 连接池获取超时（10秒）");
			}
			if (conn.Client.IsConnected == false || DateTime.Now.Subtract(conn.LastActive).TotalSeconds > 60)
				try {
					conn.Client.Ping();
				} catch {
					var ips = Dns.GetHostAddresses(_ip);
					if (ips.Length == 0) throw new Exception($"无法解析“{_ip}”");
					conn.Client = new RedisClient(new IPEndPoint(ips[0], _port));
					conn.Client.Connected += Connected;
				}
			conn.ThreadId = Thread.CurrentThread.ManagedThreadId;
			conn.LastActive = DateTime.Now;
			Interlocked.Increment(ref conn.UseSum);
			return conn;
		}
		async public Task<RedisConnection2> GetConnectionAsync() {
			var conn = GetFreeConnection();
			if (conn == null) {
				TaskCompletionSource<RedisConnection2> tcs = new TaskCompletionSource<RedisConnection2>();
				lock (_lock_GetConnectionQueue)
					GetConnectionAsyncQueue.Enqueue(tcs);
				conn = await tcs.Task;
			}
			if (conn.Client.IsConnected == false || DateTime.Now.Subtract(conn.LastActive).TotalSeconds > 60)
				try {
					await conn.Client.PingAsync();
				} catch {
					var ips = Dns.GetHostAddresses(_ip);
					if (ips.Length == 0) throw new Exception($"无法解析“{_ip}”");
					conn.Client = new RedisClient(new IPEndPoint(ips[0], _port));
					conn.Client.Connected += Connected;
				}
			conn.ThreadId = Thread.CurrentThread.ManagedThreadId;
			conn.LastActive = DateTime.Now;
			Interlocked.Increment(ref conn.UseSum);
			return conn;
		}

		public void ReleaseConnection(RedisConnection2 conn, bool isReset = false) {
			if (isReset) {
				try {
					conn.Client.Quit();
				} catch { }
				var ips = Dns.GetHostAddresses(_ip);
				if (ips.Length == 0) throw new Exception($"无法解析“{_ip}”");
				conn.Client = new RedisClient(new IPEndPoint(ips[0], _port), _ssl, 1000, _writebuffer);
				conn.Client.Connected += Connected;
			}
			lock (_lock)
				FreeConnections.Enqueue(conn);

			bool isAsync = false;
			if (GetConnectionAsyncQueue.Count > 0) {
				TaskCompletionSource<RedisConnection2> tcs = null;
				lock (_lock_GetConnectionQueue)
					if (GetConnectionAsyncQueue.Count > 0)
						tcs = GetConnectionAsyncQueue.Dequeue();
				if (isAsync = (tcs != null)) tcs.SetResult(GetConnectionAsync().Result);
			}
			if (isAsync == false && GetConnectionQueue.Count > 0) {
				ManualResetEventSlim wait = null;
				lock (_lock_GetConnectionQueue)
					if (GetConnectionQueue.Count > 0)
						wait = GetConnectionQueue.Dequeue();
				if (wait != null) wait.Set();
			}
		}
	}

	public class RedisConnection2 : IDisposable {
		public RedisClient Client;
		public DateTime LastActive;
		public long UseSum;
		internal int ThreadId;
		internal ConnectionPool Pool;

		public void Dispose() {
			if (Pool != null) Pool.ReleaseConnection(this);
		}
	}
}