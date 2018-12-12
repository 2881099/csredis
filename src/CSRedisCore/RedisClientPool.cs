using SafeObjectPool;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

namespace CSRedis {
	public class RedisClientPool : ObjectPool<RedisClient> {

		public RedisClientPool(string name, string connectionString, Action<RedisClient> onConnected) : base(null) {
			_policy = new RedisClientPoolPolicy {
				_pool = this
			};
			_policy.Connected += (s, o) => {
				RedisClient rc = s as RedisClient;
				if (!string.IsNullOrEmpty(_policy._password)) rc.Auth(_policy._password);
				if (_policy._database > 0) rc.Select(_policy._database);
				onConnected(s as RedisClient);
			};
			this.Policy = _policy;
			_policy.ConnectionString = connectionString;
		}

		[Obsolete("方法已更名 pool.Get")]
		public Object<RedisClient> GetConnection() => this.Get();
		[Obsolete("方法已更名 pool.GetAsync")]
		public Task<Object<RedisClient>> GetConnectionAsync() => this.GetAsync();
		[Obsolete("方法已更名 pool.Return")]
		public void ReleaseConnection(Object<RedisClient> conn, bool isReset = false) => this.Return(conn, isReset);

		public void Return(Object<RedisClient> obj, Exception exception, bool isRecreate = false) {
			if (exception != null) {
				try {
					try {
						obj.Value.Ping();

						var fcolor = Console.ForegroundColor;
						Console.WriteLine($"");
						Console.ForegroundColor = ConsoleColor.DarkYellow;
						Console.WriteLine($"csreids 错误【{Policy.Name}】：{exception.Message} {exception.StackTrace}");
						Console.ForegroundColor = fcolor;
						Console.WriteLine($"");
					} catch {
						obj.ResetValue();
						obj.Value.Ping();
					}
				} catch(Exception ex) {
					base.SetUnavailable(ex);
				}
			}
			base.Return(obj, isRecreate);
		}

		internal RedisClientPoolPolicy _policy;
		public string Key => _policy.Key;
		public string Prefix => _policy.Prefix;
		public Encoding Encoding { get; set; } = new UTF8Encoding(false);

		internal int AutoStartPipeCommitCount { get; set; } = 10;
		internal int AutoStartPipeCommitTimeout { get; set; } = 1000;
	}

	public class RedisClientPoolPolicy : IPolicy<RedisClient> {

		internal RedisClientPool _pool;
		internal int _port = 6379, _database = 0, _writebuffer = 10240, _tryit = 0;
		internal string _ip = "127.0.0.1", _password = "", _clientname = "";
		internal bool _ssl = false, _preheat = true;
		internal string Key => $"{_ip}:{_port}/{_database}";
		internal string Prefix { get; set; }
		public event EventHandler Connected;

		public string Name { get => Key; set { throw new Exception("RedisClientPoolPolicy 不提供设置 Name 属性值。"); } }
		public int PoolSize { get; set; } = 50;
		public TimeSpan SyncGetTimeout { get; set; } = TimeSpan.FromSeconds(10);
		public int AsyncGetCapacity { get; set; } = 100000;
		public bool IsThrowGetTimeoutException { get; set; } = true;
		public int CheckAvailableInterval { get; set; } = 5;
		

		private string _connectionString;
		public string ConnectionString {
			get => _connectionString;
			set {
				_connectionString = value;
				if (string.IsNullOrEmpty(_connectionString)) return;

				//支持密码中带有逗号，将原有 split(',') 改成以下处理方式
				var vs = Regex.Split(_connectionString, @"\,([\w \t\r\n]+)=", RegexOptions.Multiline);

				var host = vs[0].Split(':');
				_ip = string.IsNullOrEmpty(host[0].Trim()) == false ? host[0].Trim() : "127.0.0.1";
				if (host.Length < 2 || int.TryParse(host[1].Trim(), out _port) == false) _port = 6379;

				for (var a = 1; a < vs.Length; a += 2) {
					var kv = new[] { vs[a].ToLower().Trim(), vs[a + 1] };
					if (kv[0] == "password") _password = kv.Length > 1 ? kv[1] : "";
					else if (kv[0] == "prefix") Prefix = kv.Length > 1 ? kv[1] : "";
					else if (kv[0] == "defaultdatabase") _database = int.TryParse(kv.Length > 1 ? kv[1].Trim() : "0", out _database) ? _database : 0;
					else if (kv[0] == "poolsize") PoolSize = int.TryParse(kv.Length > 1 ? kv[1].Trim() : "0", out var poolsize) == false || poolsize <= 0 ? 50 : poolsize;
					else if (kv[0] == "ssl") _ssl = kv.Length > 1 ? kv[1].ToLower().Trim() == "true" : false;
					else if (kv[0] == "writebuffer") _writebuffer = int.TryParse(kv.Length > 1 ? kv[1].Trim() : "10240", out _writebuffer) ? _writebuffer : 10240;
					else if (kv[0] == "preheat") _preheat = kv.Length > 1 ? kv[1].ToLower().Trim() == "true" : false;
					else if (kv[0] == "name") _clientname = kv.Length > 1 ? kv[1] : "";
					else if (kv[0] == "tryit") _tryit = int.TryParse(kv.Length > 1 ? kv[1].Trim() : "0", out _tryit) ? _tryit : 0;
				}

				if (_preheat) {
					var initConns = new Object<RedisClient>[PoolSize];
					for (var a = 0; a < PoolSize; a++) try { initConns[a] = _pool.Get(); initConns[a].Value.Ping(); } catch { break; } //预热失败一次就退出
					foreach (var conn in initConns) _pool.Return(conn);
				}
			}
		}

		public bool OnCheckAvailable(Object<RedisClient> obj) {
			obj.ResetValue();
			return obj.Value.Ping() == "PONG";
		}

		public RedisClient OnCreate() {
			var ips = Dns.GetHostAddresses(_ip);
			if (ips.Length == 0) throw new Exception($"无法解析“{_ip}”");
			var client = new RedisClient(new IPEndPoint(ips[0], _port), _ssl, 100, _writebuffer);
			client.Connected += (s, o) => {
				Connected(s, o);
				if (!string.IsNullOrEmpty(_clientname)) client.ClientSetName(_clientname);
			};
			return client;
		}

		public void OnDestroy(RedisClient obj) {
			if (obj != null) {
				if (obj.IsConnected) try { obj.Quit(); } catch { }
				try { obj.Dispose(); } catch { }
			}
		}

		public void OnGet(Object<RedisClient> obj) {
			if (_pool.Encoding != obj.Value.Encoding) obj.Value.Encoding = _pool.Encoding;
			if (_pool.IsAvailable) {
				if (DateTime.Now.Subtract(obj.LastReturnTime).TotalSeconds > 60 || obj.Value.IsConnected == false) {
					try {
						obj.Value.Ping();
					} catch {
						obj.ResetValue();
					}
				}
			}
		}

		async public Task OnGetAsync(Object<RedisClient> obj) {
			if (_pool.Encoding != obj.Value.Encoding) obj.Value.Encoding = _pool.Encoding;
			if (_pool.IsAvailable) {
				if (DateTime.Now.Subtract(obj.LastReturnTime).TotalSeconds > 60 || obj.Value.IsConnected == false) {
					try {
						await obj.Value.PingAsync();
					} catch {
						obj.ResetValue();
					}
				}
			}
		}

		public void OnGetTimeout() {
			
		}

		public void OnReturn(Object<RedisClient> obj) {
			
		}

		public void OnAvailable() {
		}
		public void OnUnavailable() {
		}
	}
}
