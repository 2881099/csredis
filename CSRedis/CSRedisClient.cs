using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CSRedis {
	public partial class CSRedisClient {
		public Dictionary<string, ConnectionPool> ClusterNodes { get; } = new Dictionary<string, ConnectionPool>();
		internal List<string> ClusterKeys;
		internal Func<string, string> ClusterRule;

		/// <summary>
		/// 创建redis访问类
		/// </summary>
		/// <param name="connectionString">127.0.0.1[:6379],password=123456,defaultDatabase=13,poolsize=50,ssl=false,writeBuffer=10240,prefix=key前辍</param>
		public CSRedisClient(string connectionString) : this(null, connectionString) { }
		/// <summary>
		/// 创建redis访问集群类，通过 KeyRule 对 key 进行分区，连接对应的 connectionString
		/// </summary>
		/// <param name="clusterRule">按key分区规则，返回值格式：127.0.0.1:6379/13，默认方案(null)：取key哈希与节点数取模</param>
		/// <param name="connectionStrings">127.0.0.1[:6379],password=123456,defaultDatabase=13,poolsize=50,ssl=false,writeBuffer=10240,prefix=key前辍</param>
		public CSRedisClient(Func<string, string> clusterRule, params string[] connectionStrings) {
			ClusterRule = clusterRule;
			if (ClusterRule == null) ClusterRule = key => {
				var idx = Math.Abs(string.Concat(key).GetHashCode()) % ClusterNodes.Count;
				return idx < 0 || idx >= ClusterKeys.Count ? ClusterKeys.First() : ClusterKeys[idx];
			};
			if (connectionStrings == null || connectionStrings.Any() == false) throw new Exception("Redis ConnectionString 未设置");
			foreach (var connectionString in connectionStrings) {
				var pool = new ConnectionPool();
				pool.ConnectionString = connectionString;
				pool.Connected += (s, o) => {
					RedisClient rc = s as RedisClient;
				};
				if (ClusterNodes.ContainsKey(pool.ClusterKey)) throw new Exception($"ClusterName: {pool.ClusterKey} 重复，请检查");
				ClusterNodes.Add(pool.ClusterKey, pool);
			}
			ClusterKeys = ClusterNodes.Keys.ToList();
		}

		private DateTime dt1970 = new DateTime(1970, 1, 1);
		/// <summary>
		/// 缓存壳
		/// </summary>
		/// <typeparam name="T">缓存类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="timeoutSeconds">缓存秒数</param>
		/// <param name="getData">获取源数据的函数</param>
		/// <param name="serialize">序列化函数</param>
		/// <param name="deserialize">反序列化函数</param>
		/// <returns></returns>
		public T CacheShell<T>(string key, int timeoutSeconds, Func<T> getData, Func<T, string> serialize, Func<string, T> deserialize) {
			if (timeoutSeconds <= 0) return getData();
			var cacheValue = Get(key);
			if (cacheValue != null) {
				try {
					return deserialize(cacheValue);
				} catch {
					Remove(key);
					throw;
				}
			}
			var ret = getData();
			Set(key, serialize(ret), timeoutSeconds);
			return ret;
		}
		/// <summary>
		/// 缓存壳(哈希表)
		/// </summary>
		/// <typeparam name="T">缓存类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <param name="timeoutSeconds">缓存秒数</param>
		/// <param name="getData">获取源数据的函数</param>
		/// <param name="serialize">序列化函数</param>
		/// <param name="deserialize">反序列化函数</param>
		/// <returns></returns>
		public T CacheShell<T>(string key, string field, int timeoutSeconds, Func<T> getData, Func<(T, long), string> serialize, Func<string, (T, long)> deserialize) {
			if (timeoutSeconds <= 0) return getData();
			var cacheValue = HashGet(key, field);
			if (cacheValue != null) {
				try {
					var value = deserialize(cacheValue);
					if (DateTime.Now.Subtract(dt1970.AddSeconds(value.Item2)).TotalSeconds <= timeoutSeconds) return value.Item1;
				} catch {
					HashDelete(key, field);
					throw;
				}
			}
			var ret = getData();
			HashSet(key, field, serialize((ret, (long) DateTime.Now.Subtract(dt1970).TotalSeconds)));
			return ret;
		}
		/// <summary>
		/// 缓存壳(哈希表)，将 fields 每个元素存储到单独的缓存片，实现最大化复用
		/// </summary>
		/// <typeparam name="T">缓存类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="fields">字段</param>
		/// <param name="timeoutSeconds">缓存秒数</param>
		/// <param name="getData">获取源数据的函数，输入参数是没有缓存的 fields，返回值应该是 (field, value)[]</param>
		/// <param name="serialize">序列化函数</param>
		/// <param name="deserialize">反序列化函数</param>
		/// <returns></returns>
		public T[] CacheShell<T>(string key, string[] fields, int timeoutSeconds, Func<string[], (string, T)[]> getData, Func<(T, long), string> serialize, Func<string, (T, long)> deserialize) {
			fields = fields?.Distinct().ToArray();
			if (fields == null || fields.Length == 0) return new T[0];
			if (timeoutSeconds <= 0) return getData(fields).Select(a => a.Item2).ToArray();

			var ret = new T[fields.Length];
			var cacheValue = HashMGet(key, fields);
			var fieldsMGet = new Dictionary<string, int>();

			for (var a = 0; a < ret.Length; a++) {
				if (cacheValue[a] != null) {
					try {
						var value = deserialize(cacheValue[a]);
						if (DateTime.Now.Subtract(dt1970.AddSeconds(value.Item2)).TotalSeconds <= timeoutSeconds) {
							ret[a] = value.Item1;
							continue;
						}
					} catch {
						HashDelete(key, fields[a]);
						throw;
					}
				}
				fieldsMGet.Add(fields[a], a);
			}

			if (fieldsMGet.Any()) {
				var getDataIntput = fieldsMGet.Keys.ToArray();
				var data = getData(getDataIntput);
				var mset = new object[fieldsMGet.Count * 2];
				var msetIndex = 0;
				foreach (var d in data) {
					if (fieldsMGet.ContainsKey(d.Item1) == false) throw new Exception($"使用 CacheShell 请确认 getData 返回值 (string, T)[] 中的 Item1 值: {d.Item1} 存在于 输入参数: {string.Join(",", getDataIntput)}");
					ret[fieldsMGet[d.Item1]] = d.Item2;
					mset[msetIndex++] = d.Item1;
					mset[msetIndex++] = serialize((d.Item2, (long)DateTime.Now.Subtract(dt1970).TotalSeconds));
					fieldsMGet.Remove(d.Item1);
				}
				foreach (var fieldNull in fieldsMGet.Keys) {
					ret[fieldsMGet[fieldNull]] = default(T);
					mset[msetIndex++] = fieldNull;
					mset[msetIndex++] = serialize((default(T), (long)DateTime.Now.Subtract(dt1970).TotalSeconds));
				}
				if (mset.Any()) HashSet(key, mset);
			}
			return ret;
		}

		#region 集群方式 Execute
		private T ExecuteScalar<T>(string key, Func<RedisClient, string, T> hander) {
			if (key == null) return default(T);
			var pool = ClusterRule == null || ClusterNodes.Count == 1 ? ClusterNodes.First().Value : (ClusterNodes.TryGetValue(ClusterRule(key), out var b) ? b : ClusterNodes.First().Value);
			key = string.Concat(pool.Prefix, key);
			using (var conn = pool.GetConnection()) {
				return hander(conn.Client, key);
			}
		}
		private T[] ExeucteArray<T>(string[] key, Func<RedisClient, string[], T[]> hander) {
			if (key == null || key.Any() == false) return new T[0];
			if (ClusterRule == null || ClusterNodes.Count == 1) {
				var pool = ClusterNodes.First().Value;
				var keys = key.Select(a => string.Concat(pool.Prefix, a)).ToArray();
				using (var conn = pool.GetConnection()) {
					return hander(conn.Client, keys);
				}
			}
			var rules = new Dictionary<string, List<(string, int)>>();
			for (var a = 0; a < key.Length; a++) {
				var rule = ClusterRule(key[a]);
				if (rules.ContainsKey(rule)) rules[rule].Add((key[a], a));
				else rules.Add(rule, new List<(string, int)> { (key[a], a) });
			}
			T[] ret = new T[key.Length];
			foreach (var r in rules) {
				var pool = ClusterNodes.TryGetValue(r.Key, out var b) ? b : ClusterNodes.First().Value;
				var keys = r.Value.Select(a => string.Concat(pool.Prefix, a.Item1)).ToArray();
				using (var conn = pool.GetConnection()) {
					var vals = hander(conn.Client, keys);
					for (var z = 0; z < r.Value.Count; z++) {
						ret[r.Value[z].Item2] = vals == null || z >= vals.Length ? default(T) : vals[z];
					}
				}
			}
			return ret;
		}
		private long ExecuteNonQuery(string[] key, Func<RedisClient, string[], long> hander) {
			if (key == null || key.Any() == false) return 0;
			if (ClusterRule == null || ClusterNodes.Count == 1) {
				var pool = ClusterNodes.First().Value;
				var keys = key.Select(a => string.Concat(pool.Prefix, a)).ToArray();
				using (var conn = pool.GetConnection()) {
					return hander(conn.Client, keys);
				}
			}
			var rules = new Dictionary<string, List<string>>();
			for (var a = 0; a < key.Length; a++) {
				var rule = ClusterRule(key[a]);
				if (rules.ContainsKey(rule)) rules[rule].Add(key[a]);
				else rules.Add(rule, new List<string> { key[a] });
			}
			long affrows = 0;
			foreach (var r in rules) {
				var pool = ClusterNodes.TryGetValue(r.Key, out var b) ? b : ClusterNodes.First().Value;
				var keys = r.Value.Select(a => string.Concat(pool.Prefix, a)).ToArray();
				using (var conn = pool.GetConnection()) {
					affrows += hander(conn.Client, keys);
				}
			}
			return affrows;
		}
		#endregion

		/// <summary>
		/// 创建管道传输
		/// </summary>
		/// <param name="handler"></param>
		/// <returns></returns>
		public object[] StartPipe(Action<CSRedisClientPipe> handler) {
			if (handler == null) return new object[0];
			var pipe = new CSRedisClientPipe(this);
			handler(pipe);
			return pipe.EndPipe();
		}

		/// <summary>
		/// 创建管道传输，打包提交如：RedisHelper.StartPipe().Set("a", "1").HashSet("b", "f", "2").EndPipe();
		/// </summary>
		/// <returns></returns>
		[Obsolete("警告：本方法必须有 EndPipe() 提交，否则会造成连接池耗尽。")]
		public CSRedisClientPipe StartPipe() {
			return new CSRedisClientPipe(this);
		}

		/// <summary>
		/// 设置指定 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字符串值</param>
		/// <param name="expireSeconds">过期(秒单位)</param>
		/// <param name="exists">Nx, Xx</param>
		/// <returns></returns>
		public bool Set(string key, string value, int expireSeconds = -1, CSRedisExistence? exists = null) => ExecuteScalar(key, (c, k) => expireSeconds > 0 || exists != null ? c.Set(k, value, expireSeconds > 0 ? new int?(expireSeconds) : null, exists == CSRedisExistence.Nx ? new RedisExistence?(RedisExistence.Nx) : (exists == CSRedisExistence.Xx ? new RedisExistence?(RedisExistence.Xx) : null)) : c.Set(k, value)) == "OK";
		/// <summary>
		/// 设置指定 key 的值(字节流)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字节流</param>
		/// <param name="expireSeconds">过期(秒单位)</param>
		/// <param name="exists">Nx, Xx</param>
		/// <returns></returns>
		public bool SetBytes(string key, byte[] value, int expireSeconds = -1, CSRedisExistence? exists = null) => ExecuteScalar(key, (c, k) => expireSeconds > 0 || exists != null ? c.Set(k, value, expireSeconds > 0 ? new int?(expireSeconds) : null, exists == CSRedisExistence.Nx ? new RedisExistence?(RedisExistence.Nx) : (exists == CSRedisExistence.Xx ? new RedisExistence?(RedisExistence.Xx) : null)) : c.Set(k, value)) == "OK";
		/// <summary>
		/// 获取指定 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string Get(string key) => ExecuteScalar(key, (c, k) => c.Get(k));
		/// <summary>
		/// 获取多个指定 key 的值(数组)
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public string[] GetStrings(params string[] keys) => ExeucteArray(keys, (c, k) => c.MGet(k));
		/// <summary>
		/// 获取多个指定 key 的值(数组)
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public string[] MGet(params string[] keys) => ExeucteArray(keys, (c, k) => c.MGet(k));
		/// <summary>
		/// 获取指定 key 的值(字节流)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public byte[] GetBytes(string key) => ExecuteScalar(key, (c, k) => c.GetBytes(k));
		/// <summary>
		/// 用于在 key 存在时删除 key
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long Remove(params string[] key) => ExecuteNonQuery(key, (c, k) => c.Del(k));
		/// <summary>
		/// 检查给定 key 是否存在
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public bool Exists(string key) => ExecuteScalar(key, (c, k) => c.Exists(k));
		/// <summary>
		/// 将 key 所储存的值加上给定的增量值（increment）
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public long Increment(string key, long value = 1) => ExecuteScalar(key, (c, k) => c.IncrBy(k, value));
		/// <summary>
		/// 为给定 key 设置过期时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="expire">过期时间</param>
		/// <returns></returns>
		public bool Expire(string key, TimeSpan expire) => ExecuteScalar(key, (c, k) => c.Expire(k, expire));
		/// <summary>
		/// 以秒为单位，返回给定 key 的剩余生存时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long Ttl(string key) => ExecuteScalar(key, (c, k) => c.Ttl(k));
		/// <summary>
		/// 执行脚本
		/// </summary>
		/// <param name="script">脚本</param>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="args">参数</param>
		/// <returns></returns>
		public object Eval(string script, string key, params object[] args) => ExecuteScalar(key, (c, k) => c.Eval(script, new[] { k }, args));
		/// <summary>
		/// 查找所有集群中符合给定模式(pattern)的 key
		/// </summary>
		/// <param name="pattern">如：runoob*</param>
		/// <returns></returns>
		public string[] Keys(string pattern) {
			List<string> ret = new List<string>();
			foreach (var pool in ClusterNodes)
				using (var conn = pool.Value.GetConnection()) {
					ret.AddRange(conn.Client.Keys(pattern));
				}
			return ret.ToArray();
		}
		/// <summary>
		/// Redis Publish 命令用于将信息发送到指定群集节点的频道
		/// </summary>
		/// <param name="channel">频道名</param>
		/// <param name="data">消息文本</param>
		/// <returns></returns>
		public long Publish(string channel, string data) {
			var msgid = HashIncrement("CSRedisPublishMsgId", channel, 1);
			return ExecuteScalar(channel, (c, k) => c.Publish(channel, $"{msgid}|{data}"));
		}
		private Dictionary<string, List<RedisConnection2>> _subscrsDic = new Dictionary<string, List<RedisConnection2>>();
		private object _subscrsDic_lock = new object();
		/// <summary>
		/// 订阅，根据集群规则返回RedisClient数组，Subscribe(("chan1", msg => Console.WriteLine(msg.Body)), ("chan2", msg => Console.WriteLine(msg.Body)))，注意：redis服务重启无法重连
		/// </summary>
		/// <param name="channels">频道</param>
		public IRedisClient[] Subscribe(params (string, Action<SubscribeMessageEventArgs>)[] channels) {
			var chans = channels.Select(a => a.Item1).Distinct().ToArray();
			var onmessages = channels.ToDictionary(a => a.Item1, b => b.Item2);
			var subscrKey = string.Join("SpLiT", chans);

			EventHandler<RedisMonitorEventArgs> MonitorReceived = (a, b) => {
			};
			EventHandler<RedisSubscriptionReceivedEventArgs> SubscriptionReceived = (a, b) => {
				try {
					if (b.Message.Type == "message" && onmessages.TryGetValue(b.Message.Channel, out var action)) {
						var msgidIdx = b.Message.Body.IndexOf('|');
						if (msgidIdx != -1 && long.TryParse(b.Message.Body.Substring(0, msgidIdx), out var trylong))
							action(new SubscribeMessageEventArgs {
								MessageId = trylong,
								Body = b.Message.Body.Substring(msgidIdx + 1),
								Channel = b.Message.Channel
							});
						else action(new SubscribeMessageEventArgs {
							MessageId = 0,
							Body = b.Message.Body,
							Channel = b.Message.Channel
						});
					}
				} catch (Exception ex) {
					Console.WriteLine($"订阅方法出错(channel:{b.Message.Channel})：{ex.Message}\r\n{ex.StackTrace}");
				}
			};
			
			lock (_subscrsDic_lock)
				if (_subscrsDic.TryGetValue(subscrKey, out var subscrs2)) {
					foreach (var subscr in subscrs2) {
						try {
							subscr.Client.MonitorReceived -= MonitorReceived;
							subscr.Client.SubscriptionReceived -= SubscriptionReceived;
							subscr.Client.Unsubscribe();
							//subscr.Client.Quit();
							//subscr.Client.ClientKill();
							subscr.Client.Dispose();
						} catch {
						}
						subscr.Pool.ReleaseConnection(subscr, true);
					}
					_subscrsDic.Remove(subscrKey);
				}

			var rules = new Dictionary<string, List<string>>();
			for (var a = 0; a < chans.Length; a++) {
				var rule = ClusterRule(chans[a]);
				if (rules.ContainsKey(rule)) rules[rule].Add(chans[a]);
				else rules.Add(rule, new List<string> { chans[a] });
			}
			var subscrs = new List<RedisConnection2>();
			foreach (var r in rules) {
				var pool = ClusterNodes.TryGetValue(r.Key, out var p) ? p : ClusterNodes.First().Value;
				var subscr = pool.GetConnection();
				subscr.Client.MonitorReceived += MonitorReceived;
				subscr.Client.SubscriptionReceived += SubscriptionReceived;
				subscrs.Add(subscr);

				Task.Run(() => {
					try {
						subscr.Client.Subscribe(r.Value.ToArray());
					} catch (Exception ex) {
						var bgcolor = Console.BackgroundColor;
						var forecolor = Console.ForegroundColor;
						Console.BackgroundColor = ConsoleColor.Yellow;
						Console.ForegroundColor = ConsoleColor.Red;
						Console.Write($"订阅出错(channel:{string.Join(",", chans)}：{ex.Message}，5秒后重连。。。");
						Console.BackgroundColor = bgcolor;
						Console.ForegroundColor = forecolor;
						Console.WriteLine();
						Thread.CurrentThread.Join(1000 * 5);
						Subscribe(channels);
					}
				});
			}
			lock (_subscrsDic_lock)
				_subscrsDic.Add(subscrKey, subscrs);

			return subscrs.Select(a => a.Client).ToArray();
		}
		public class SubscribeMessageEventArgs {
			/// <summary>
			/// 频道的消息id
			/// </summary>
			public long MessageId { get; set; }
			/// <summary>
			/// 频道
			/// </summary>
			public string Channel { get; set; }
			/// <summary>
			/// 接收到的内容
			/// </summary>
			public string Body { get; set; }
		}
		/// <summary>
		/// 模糊订阅，订阅所有集群节点(同条消息只处理一次），返回数组RedisClient PSubscribe(new [] { "chan1*", "chan2*" }, msg => Console.WriteLine(msg.Body))，注意：redis服务重启无法重连
		/// </summary>
		/// <param name="channelPatterns">模糊频道</param>
		public IRedisClient[] PSubscribe(string[] channelPatterns, Action<PSubscribePMessageEventArgs> pmessage) {
			var chans = channelPatterns.Distinct().ToArray();
			var subscrKey = string.Join("pSpLiT", chans);

			EventHandler<RedisMonitorEventArgs> MonitorReceived = (a, b) => {
			};
			EventHandler<RedisSubscriptionReceivedEventArgs> SubscriptionReceived = (a, b) => {
				try {
					if (b.Message.Type == "pmessage" && pmessage != null) {
						var msgidIdx = b.Message.Body.IndexOf('|');
						if (msgidIdx != -1 && long.TryParse(b.Message.Body.Substring(0, msgidIdx), out var trylong)) {
							var readed = Eval($@"
ARGV[1] = redis.call('HGET', KEYS[1], '{b.Message.Channel}')
if ARGV[1] ~= ARGV[2] then
  redis.call('HSET', KEYS[1], '{b.Message.Channel}', ARGV[2])
  return 1
end
return 0", $"CSRedisPSubscribe{subscrKey}", "", trylong.ToString());
							if (readed?.ToString() == "1")
								pmessage(new PSubscribePMessageEventArgs {
									Body = b.Message.Body.Substring(msgidIdx + 1),
									Channel = b.Message.Channel,
									MessageId = trylong,
									Pattern = b.Message.Pattern
								});
							//else
							//	Console.WriteLine($"消息被处理过：id:{trylong} channel:{b.Message.Channel} pattern:{b.Message.Pattern} body:{b.Message.Body.Substring(msgidIdx + 1)}");
						} else
							pmessage(new PSubscribePMessageEventArgs {
								Body = b.Message.Body,
								Channel = b.Message.Channel,
								MessageId = 0,
								Pattern = b.Message.Pattern
							});
					}
				} catch (Exception ex) {
					Console.WriteLine($"模糊订阅方法出错(channel:{b.Message.Channel})：{ex.Message}\r\n{ex.StackTrace}");
				}
			};
			
			lock (_subscrsDic_lock)
				if (_subscrsDic.TryGetValue(subscrKey, out var subscrs2)) {
					foreach (var subscr in subscrs2) {
						try {
							subscr.Client.MonitorReceived -= MonitorReceived;
							subscr.Client.SubscriptionReceived -= SubscriptionReceived;
							subscr.Client.PUnsubscribe();
							//subscr.Client.Quit();
							//subscr.Client.ClientKill();
							subscr.Client.Dispose();
						} catch {
						}
						subscr.Pool.ReleaseConnection(subscr, true);
					}
					_subscrsDic.Remove(subscrKey);
				}

			var subscrs = new List<RedisConnection2>();
			foreach (var pool in ClusterNodes) {
				var subscr = pool.Value.GetConnection();
				subscr.Client.MonitorReceived += MonitorReceived;
				subscr.Client.SubscriptionReceived += SubscriptionReceived;
				subscrs.Add(subscr);

				Task.Run(() => {
					try {
						subscr.Client.PSubscribe(chans);
					} catch (Exception ex) {
						var bgcolor = Console.BackgroundColor;
						var forecolor = Console.ForegroundColor;
						Console.BackgroundColor = ConsoleColor.Yellow;
						Console.ForegroundColor = ConsoleColor.Red;
						Console.Write($"模糊订阅出错(channel:{string.Join(",", chans)}：{ex.Message}，5秒后重连。。。");
						Console.BackgroundColor = bgcolor;
						Console.ForegroundColor = forecolor;
						Console.WriteLine();
						Thread.CurrentThread.Join(1000 * 5);
						PSubscribe(channelPatterns, pmessage);
					}
				});
			}
			lock (_subscrsDic_lock)
				_subscrsDic.Add(subscrKey, subscrs);

			return subscrs.Select(a => a.Client).ToArray();
		}
		public class PSubscribePMessageEventArgs : SubscribeMessageEventArgs {
			/// <summary>
			/// 匹配模式
			/// </summary>
			public string Pattern { get; set; }
		}

		#region Hash 操作
		/// <summary>
		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="keyValues">field1 value1 [field2 value2]</param>
		/// <returns></returns>
		public string HashSet(string key, params object[] keyValues) => HashSetExpire(key, TimeSpan.Zero, keyValues);
		/// <summary>
		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="expire">过期时间</param>
		/// <param name="keyValues">field1 value1 [field2 value2]</param>
		/// <returns></returns>
		public string HashSetExpire(string key, TimeSpan expire, params object[] keyValues) {
			if (keyValues == null || keyValues.Any() == false) return null;
			if (expire > TimeSpan.Zero) {
				var lua = "ARGV[1] = redis.call('HMSET', KEYS[1]";
				var argv = new List<object>();
				for (int a = 0, argvIdx = 3; a < keyValues.Length; a += 2, argvIdx++) {
					lua += ", '" + (keyValues[a]?.ToString().Replace("'", "\\'")) + "', ARGV[" + argvIdx + "]";
					argv.Add(keyValues[a + 1]);
				}
				lua += @") redis.call('EXPIRE', KEYS[1], ARGV[2]) return ARGV[1]";
				argv.InsertRange(0, new object[] { "", (long) expire.TotalSeconds });
				return Eval(lua, key, argv.ToArray())?.ToString();
			}
			return ExecuteScalar(key, (c, k) => c.HMSet(k, keyValues));
		}
		/// <summary>
		/// 获取存储在哈希表中指定字段的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		public string HashGet(string key, string field) => ExecuteScalar(key, (c, k) => c.HGet(k, field));
		/// <summary>
		/// 获取存储在哈希表中多个字段的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="fields">字段</param>
		/// <returns></returns>
		public string[] HashMGet(string key, params string[] fields) => ExecuteScalar(key, (c, k) => c.HMGet(k, fields));
		/// <summary>
		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public long HashIncrement(string key, string field, long value = 1) => ExecuteScalar(key, (c, k) => c.HIncrBy(k, field, value));
		/// <summary>
		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public double HashIncrementFloat(string key, string field, double value = 1) => ExecuteScalar(key, (c, k) => c.HIncrByFloat(k, field, value));
		/// <summary>
		/// 删除一个或多个哈希表字段
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="fields">字段</param>
		/// <returns></returns>
		public long HashDelete(string key, params string[] fields) => fields == null || fields.Any() == false ? 0 : ExecuteScalar(key, (c, k) => c.HDel(k, fields));
		/// <summary>
		/// 查看哈希表 key 中，指定的字段是否存在
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		public bool HashExists(string key, string field) => ExecuteScalar(key, (c, k) => c.HExists(k, field));
		/// <summary>
		/// 获取哈希表中字段的数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long HashLength(string key) => ExecuteScalar(key, (c, k) => c.HLen(k));
		/// <summary>
		/// 获取在哈希表中指定 key 的所有字段和值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Dictionary<string, string> HashGetAll(string key) => ExecuteScalar(key, (c, k) => c.HGetAll(k));
		/// <summary>
		/// 获取所有哈希表中的字段
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string[] HashKeys(string key) => ExecuteScalar(key, (c, k) => c.HKeys(k));
		/// <summary>
		/// 获取哈希表中所有值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string[] HashVals(string key) => ExecuteScalar(key, (c, k) => c.HVals(k));
		#endregion

		#region List 操作
		/// <summary>
		/// 它是 LPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BLPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="timeOut">超时(秒)</param>
		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
		/// <returns></returns>
		public string BLPop(int timeOut, params string[] keys) => ClusterNodesNotSupport(keys, null, (c, k) => c.BLPop(timeOut, k));
		/// <summary>
		/// 它是 LPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BLPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="timeOut">超时(秒)</param>
		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
		/// <returns></returns>
		public (string key, string value)? BLPopWithKey(int timeOut, params string[] keys) {
			string[] rkeys = null;
			var tuple = ClusterNodesNotSupport(keys, null, (c, k) => c.BLPopWithKey(timeOut, rkeys = k));
			if (tuple == null) return null;
			var key = tuple.Item1;
			for (var a = 0; a < rkeys.Length; a++)
				if (rkeys[a] == tuple.Item1) {
					key = keys[a];
					break;
				}
			return (key, tuple.Item2);
		}
		/// <summary>
		/// 它是 RPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BRPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="timeOut">超时(秒)</param>
		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
		/// <returns></returns>
		public string BRPop(int timeOut, params string[] keys) => ClusterNodesNotSupport(keys, null, (c, k) => c.BRPop(timeOut, k));
		/// <summary>
		/// 它是 RPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BRPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="timeOut">超时(秒)</param>
		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
		/// <returns></returns>
		public (string key, string value)? BRPopWithKey(int timeOut, params string[] keys) {
			string[] rkeys = null;
			var tuple = ClusterNodesNotSupport(keys, null, (c, k) => c.BRPopWithKey(timeOut, rkeys = k));
			if (tuple == null) return null;
			var key = tuple.Item1;
			for (var a = 0; a < rkeys.Length; a++)
				if (rkeys[a] == tuple.Item1) {
					key = keys[a];
					break;
				}
			return (key, tuple.Item2);
		}
		/// <summary>
		/// 通过索引获取列表中的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="index">索引</param>
		/// <returns></returns>
		public string LIndex(string key, long index) => ExecuteScalar(key, (c, k) => c.LIndex(k, index));
		/// <summary>
		/// 在列表的元素前面插入元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="pivot">列表的元素</param>
		/// <param name="value">新元素</param>
		/// <returns></returns>
		public long LInsertBefore(string key, string pivot, string value) => ExecuteScalar(key, (c, k) => c.LInsert(k, RedisInsert.Before, pivot, value));
		/// <summary>
		/// 在列表的元素后面插入元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="pivot">列表的元素</param>
		/// <param name="value">新元素</param>
		/// <returns></returns>
		public long LInsertAfter(string key, string pivot, string value) => ExecuteScalar(key, (c, k) => c.LInsert(k, RedisInsert.After, pivot, value));
		/// <summary>
		/// 获取列表长度
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long LLen(string key) => ExecuteScalar(key, (c, k) => c.LLen(k));
		/// <summary>
		/// 移出并获取列表的第一个元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string LPop(string key) => ExecuteScalar(key, (c, k) => c.LPop(k));
		/// <summary>
		/// 移除并获取列表最后一个元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string RPop(string key) => ExecuteScalar(key, (c, k) => c.RPop(k));
		/// <summary>
		/// 将一个或多个值插入到列表头部
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">一个或多个值</param>
		/// <returns></returns>
		public long LPush(string key, params string[] value) => value == null || value.Any() == false ? 0 : ExecuteScalar(key, (c, k) => c.LPush(k, value));
		/// <summary>
		/// 在列表中添加一个或多个值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">一个或多个值</param>
		/// <returns></returns>
		public long RPush(string key, params string[] value) => value == null || value.Any() == false ? 0 : ExecuteScalar(key, (c, k) => c.RPush(k, value));
		/// <summary>
		/// 获取列表指定范围内的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public string[] LRang(string key, long start, long stop) => ExecuteScalar(key, (c, k) => c.LRange(k, start, stop));
		/// <summary>
		/// 根据参数 count 的值，移除列表中与参数 value 相等的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="count">移除的数量，大于0时从表头删除数量count，小于0时从表尾删除数量-count，等于0移除所有</param>
		/// <param name="value">元素</param>
		/// <returns></returns>
		public long LRem(string key, long count, string value) => ExecuteScalar(key, (c, k) => c.LRem(k, count, value));
		/// <summary>
		/// 通过索引设置列表元素的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="index">索引</param>
		/// <param name="value">值</param>
		/// <returns></returns>
		public bool LSet(string key, long index, string value) => ExecuteScalar(key, (c, k) => c.LSet(k, index, value)) == "OK";
		/// <summary>
		/// 对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public bool LTrim(string key, long start, long stop) => ExecuteScalar(key, (c, k) => c.LTrim(k, start, stop)) == "OK";
		#endregion

		#region Set 操作
		/// <summary>
		/// 向集合添加一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="members">一个或多个成员</param>
		/// <returns></returns>
		public long SAdd(string key, params string[] members) {
			if (members == null || members.Any() == false) return 0;
			return ExecuteScalar(key, (c, k) => c.SAdd(k, members));
		}
		/// <summary>
		/// 获取集合的成员数
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long SCard(string key) => ExecuteScalar(key, (c, k) => c.SCard(k));
		/// <summary>
		/// 返回给定所有集合的差集，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public string[] SDiff(params string[] keys) => ClusterNodesNotSupport(keys, new string[0], (c, k) => c.SDiff(k));
		/// <summary>
		/// 返回给定所有集合的差集并存储在 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long SDiffStore(string destinationKey, params string[] keys) => ClusterNodesNotSupport(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.SDiffStore(k.First(), k.Where((ki, kj) => kj > 0).ToArray()));
		/// <summary>
		/// 返回给定所有集合的交集，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public string[] SInter(params string[] keys) => ClusterNodesNotSupport(keys, new string[0], (c, k) => c.SInter(k));
		/// <summary>
		/// 返回给定所有集合的交集并存储在 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long SInterStore(string destinationKey, params string[] keys) => ClusterNodesNotSupport(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.SInterStore(k.First(), k.Where((ki, kj) => kj > 0).ToArray()));
		/// <summary>
		/// 返回集合中的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string[] SMembers(string key) => ExecuteScalar(key, (c, k) => c.SMembers(k));
		/// <summary>
		/// 将 member 元素从 source 集合移动到 destination 集合
		/// </summary>
		/// <param name="sourceKey">无序集合key，不含prefix前辍</param>
		/// <param name="destinationKey">目标无序集合key，不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public bool SMove(string sourceKey, string destinationKey, string member) {
			string rule = string.Empty;
			if (ClusterNodes.Count > 1) {
				var rule1 = ClusterRule(sourceKey);
				var rule2 = ClusterRule(destinationKey);
				if (rule1 != rule2) {
					if (SRem(sourceKey, member) <= 0) return false;
					return SAdd(destinationKey, member) > 0;
				}
				rule = rule1;
			}
			var pool = ClusterNodes.TryGetValue(rule, out var b) ? b : ClusterNodes.First().Value;
			var key1 = string.Concat(pool.Prefix, sourceKey);
			var key2 = string.Concat(pool.Prefix, destinationKey);
			using (var conn = pool.GetConnection()) {
				return conn.Client.SMove(key1, key2, member);
			}
		}
		/// <summary>
		/// 移除并返回集合中的一个随机元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string SPop(string key) => ExecuteScalar(key, (c, k) => c.SPop(k));
		/// <summary>
		/// 返回集合中一个或多个随机数的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="count">返回个数</param>
		/// <returns></returns>
		public string[] SRandMember(string key, int count = 1) => ExecuteScalar(key, (c, k) => c.SRandMember(k, count));
		/// <summary>
		/// 移除集合中一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="members">一个或多个成员</param>
		/// <returns></returns>
		public long SRem(string key, params string[] members) {
			if (members == null || members.Any() == false) return 0;
			return ExecuteScalar(key, (c, k) => c.SRem(k, members));
		}
		/// <summary>
		/// 返回所有给定集合的并集，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public string[] SUnion(params string[] keys) => ClusterNodesNotSupport(keys, new string[0], (c, k) => c.SUnion(k));
		/// <summary>
		/// 所有给定集合的并集存储在 destination 集合中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long SUnionStore(string destinationKey, params string[] keys) => ClusterNodesNotSupport(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.SUnionStore(k.First(), k.Where((ki, kj) => kj > 0).ToArray()));
		/// <summary>
		/// 迭代集合中的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="cursor">位置</param>
		/// <param name="pattern">模式</param>
		/// <param name="count">数量</param>
		/// <returns></returns>
		public RedisScan<string> SScan(string key, int cursor, string pattern = null, int? count = null) => ExecuteScalar(key, (c, k) => c.SScan(k, cursor, pattern, count));
		#endregion

		private T ClusterNodesNotSupport<T>(string[] keys, T defaultValue, Func<RedisClient, string[], T> callback) {
			if (keys == null || keys.Any() == false) return defaultValue;
			var rules = ClusterNodes.Count > 1 ? keys.Select(a => ClusterRule(a)).Distinct() : new[] { ClusterNodes.FirstOrDefault().Key };
			if (rules.Count() > 1) throw new Exception("由于开启了群集模式，keys 分散在多个节点，无法使用此功能");
			var pool = ClusterNodes.TryGetValue(rules.First(), out var b) ? b : ClusterNodes.First().Value;
			string[] rkeys = new string[keys.Length];
			for (int a = 0; a < keys.Length; a++) rkeys[a] = string.Concat(pool.Prefix, keys[a]);
			if (rkeys.Length == 0) return defaultValue;
			using (var conn = pool.GetConnection()) {
				return callback(conn.Client, rkeys);
			}
		}

		#region Sorted Set 操作
		/// <summary>
		/// 向有序集合添加一个或多个成员，或者更新已存在成员的分数
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="memberScores">一个或多个成员分数</param>
		/// <returns></returns>
		public long ZAdd(string key, params (double, string)[] memberScores) {
			if (memberScores == null || memberScores.Any() == false) return 0;
			var ms = memberScores.Select(a => new Tuple<double, string>(a.Item1, a.Item2)).ToArray();
			return ExecuteScalar(key, (c, k) => c.ZAdd<double, string>(k, ms));
		}
		/// <summary>
		/// 获取有序集合的成员数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long ZCard(string key) => ExecuteScalar(key, (c, k) => c.ZCard(k));
		/// <summary>
		/// 计算在有序集合中指定区间分数的成员数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="min">分数最小值</param>
		/// <param name="max">分数最大值</param>
		/// <returns></returns>
		public long ZCount(string key, double min, double max) => ExecuteScalar(key, (c, k) => c.ZCount(k, min, max));
		/// <summary>
		/// 有序集合中对指定成员的分数加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="memeber">成员</param>
		/// <param name="increment">增量值(默认=1)</param>
		/// <returns></returns>
		public double ZIncrBy(string key, string memeber, double increment = 1) => ExecuteScalar(key, (c, k) => c.ZIncrBy(k, increment, memeber));

		#region 多个有序集合 交集
		/// <summary>
		/// 计算给定的一个或多个有序集的最大值交集，将结果集存储在新的有序集合 destinationKey 中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long ZInterStoreMax(string destinationKey, params string[] keys) => ZInterStore(destinationKey, RedisAggregate.Max, keys);
		/// <summary>
		/// 计算给定的一个或多个有序集的最小值交集，将结果集存储在新的有序集合 destinationKey 中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long ZInterStoreMin(string destinationKey, params string[] keys) => ZInterStore(destinationKey, RedisAggregate.Min, keys);
		/// <summary>
		/// 计算给定的一个或多个有序集的合值交集，将结果集存储在新的有序集合 destinationKey 中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long ZInterStoreSum(string destinationKey, params string[] keys) => ZInterStore(destinationKey, RedisAggregate.Sum, keys);
		private long ZInterStore(string destinationKey, RedisAggregate aggregate, params string[] keys) => ClusterNodesNotSupport(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.ZInterStore(k.First(), null, aggregate, k.Where((ki, kj) => kj > 0).ToArray()));

		#endregion

		#region 多个有序集合 并集
		/// <summary>
		/// 计算给定的一个或多个有序集的最大值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long ZUnionStoreMax(string destinationKey, params string[] keys) => ZUnionStore(destinationKey, RedisAggregate.Max, keys);
		/// <summary>
		/// 计算给定的一个或多个有序集的最小值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long ZUnionStoreMin(string destinationKey, params string[] keys) => ZUnionStore(destinationKey, RedisAggregate.Min, keys);
		/// <summary>
		/// 计算给定的一个或多个有序集的合值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long ZUnionStoreSum(string destinationKey, params string[] keys) => ZUnionStore(destinationKey, RedisAggregate.Sum, keys);
		private long ZUnionStore(string destinationKey, RedisAggregate aggregate, params string[] keys) => ClusterNodesNotSupport(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.ZUnionStore(k.First(), null, aggregate, k.Where((ki, kj) => kj > 0).ToArray()));
		#endregion

		/// <summary>
		/// 通过索引区间返回有序集合成指定区间内的成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public string[] ZRange(string key, long start, long stop) => ExecuteScalar(key, (c, k) => c.ZRange(k, start, stop, false));
		/// <summary>
		/// 通过分数返回有序集合指定区间内的成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public string[] ZRangeByScore(string key, double minScore, double maxScore, long? limit = null, long offset = 0) => ExecuteScalar(key, (c, k) => c.ZRangeByScore(k, minScore, maxScore, false, false, false, offset, limit));
		/// <summary>
		/// 返回有序集合中指定成员的索引
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public long? ZRank(string key, string member) => ExecuteScalar(key, (c, k) => c.ZRank(k, member));
		/// <summary>
		/// 移除有序集合中的一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">一个或多个成员</param>
		/// <returns></returns>
		public long ZRem(string key, params string[] member) => ExecuteScalar(key, (c, k) => c.ZRem(k, member));
		/// <summary>
		/// 移除有序集合中给定的排名区间的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public long ZRemRangeByRank(string key, long start, long stop) => ExecuteScalar(key, (c, k) => c.ZRemRangeByRank(k, start, stop));
		/// <summary>
		/// 移除有序集合中给定的分数区间的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <returns></returns>
		public long ZRemRangeByScore(string key, double minScore, double maxScore) => ExecuteScalar(key, (c, k) => c.ZRemRangeByScore(k, minScore, maxScore));
		/// <summary>
		/// 返回有序集中指定区间内的成员，通过索引，分数从高到底
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public string[] ZRevRange(string key, long start, long stop) => ExecuteScalar(key, (c, k) => c.ZRevRange(k, start, stop, false));
		/// <summary>
		/// 返回有序集中指定分数区间内的成员，分数从高到低排序
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public string[] ZRevRangeByScore(string key, double maxScore, double minScore, long? limit = null, long? offset = 0) => ExecuteScalar(key, (c, k) => c.ZRevRangeByScore(k, maxScore, minScore, false, false, false, offset, limit));
		/// <summary>
		/// 返回有序集合中指定成员的排名，有序集成员按分数值递减(从大到小)排序
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public long? ZRevRank(string key, string member) => ExecuteScalar(key, (c, k) => c.ZRevRank(k, member));
		/// <summary>
		/// 返回有序集中，成员的分数值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public double? ZScore(string key, string member) => ExecuteScalar(key, (c, k) => c.ZScore(k, member));
		#endregion
	}
}

public enum CSRedisExistence {
	/// <summary>
	/// Only set the key if it does not already exist
	/// </summary>
	Nx,

	/// <summary>
	/// Only set the key if it already exists
	/// </summary>
	Xx,
}