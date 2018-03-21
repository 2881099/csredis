using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace CSRedis {
	/// <summary>
	/// Connection链接池
	/// </summary>
	public partial class ConnectionPool {

		public List<RedisConnection2> AllConnections = new List<RedisConnection2>();
		public Queue<RedisConnection2> FreeConnections = new Queue<RedisConnection2>();
		public Queue<ManualResetEventSlim> GetConnectionQueue = new Queue<ManualResetEventSlim>();
		public Queue<TaskCompletionSource<RedisConnection2>> GetConnectionAsyncQueue = new Queue<TaskCompletionSource<RedisConnection2>>();
		private static object _lock = new object();
		private static object _lock_GetConnectionQueue = new object();
		private string _ip;
		private int _port, _poolsize;
		public event EventHandler Connected;

		public ConnectionPool(string ip, int port, int poolsize = 50) {
			_ip = ip;
			_port = port;
			_poolsize = poolsize;
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
					conn.Client = new RedisClient(new IPEndPoint(ips[0], _port));
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
			conn.ThreadId = Thread.CurrentThread.ManagedThreadId;
			conn.LastActive = DateTime.Now;
			Interlocked.Increment(ref conn.UseSum);
			if (conn.Client.IsConnected == false)
				try {
					conn.Client.Ping();
				} catch {
					var ips = Dns.GetHostAddresses(_ip);
					if (ips.Length == 0) throw new Exception($"无法解析“{_ip}”");
					conn.Client = new RedisClient(new IPEndPoint(ips[0], _port));
					conn.Client.Connected += Connected;
				}
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
			conn.ThreadId = Thread.CurrentThread.ManagedThreadId;
			conn.LastActive = DateTime.Now;
			Interlocked.Increment(ref conn.UseSum);
			if (conn.Client.IsConnected == false)
				try {
					conn.Client.Ping();
				} catch {
					var ips = Dns.GetHostAddresses(_ip);
					if (ips.Length == 0) throw new Exception($"无法解析“{_ip}”");
					conn.Client = new RedisClient(new IPEndPoint(ips[0], _port));
					conn.Client.Connected += Connected;
				}
			return conn;
		}

		public void ReleaseConnection(RedisConnection2 conn) {
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