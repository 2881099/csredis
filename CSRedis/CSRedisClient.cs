using Newtonsoft.Json;
using SafeObjectPool;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CSRedis {
	public partial class CSRedisClient {
		/// <summary>
		/// 按 key 规则分区存储
		/// </summary>
		public Dictionary<string, RedisClientPool> Nodes { get; } = new Dictionary<string, RedisClientPool>();
		internal List<string> NodeKeys;
		internal Func<string, string> NodeRule;

		public JsonSerializerSettings SerializerSettings { get; set; } = new JsonSerializerSettings {
			Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
			DateFormatHandling = DateFormatHandling.IsoDateFormat,
			DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind
		};

		DateTime _dt1970 = new DateTime(1970, 1, 1);
		Random _rnd = new Random();

		internal object[] GetKeyValues(params (string, object)[] keyValues) {
			if (keyValues == null || keyValues.Any() == false) return new object[0];
			var kv = new object[keyValues.Length * 2];
			for (var a = 0; a < keyValues.Length; a++) {
				kv[a * 2] = keyValues[a].Item1;
				kv[a * 2 + 1] = this.SerializeInternal(keyValues[a].Item2);
			}
			return kv;
		}
		internal object SerializeInternal(object value) {
			if (value == null) return null;
			var type = value.GetType();
			var typename = type.ToString().TrimEnd(']');
			if (typename == "System.Byte[]" ||
				typename == "System.String") return value;

			if (type.IsValueType) {
				bool isNullable = typename.StartsWith("System.Nullable`1[");
				var basename = isNullable ? typename.Substring(18) : typename;

				switch (basename) {
					case "System.Boolean": return value.ToString() == "True" ? "1" : "0";
					case "System.Byte": return value.ToString();
					case "System.Char": return value.ToString()[0];
					case "System.Decimal":
					case "System.Double":
					case "System.Single":
					case "System.Int32":
					case "System.Int64":
					case "System.SByte":
					case "System.Int16":
					case "System.UInt32":
					case "System.UInt64":
					case "System.UInt16":
					case "System.DateTime":
					case "System.DateTimeOffset": return value.ToString();
					case "System.TimeSpan": return ((TimeSpan)value).Ticks;
					case "System.Guid": return value.ToString();
				}
			}

			return JsonConvert.SerializeObject(value, this.SerializerSettings);
		}
		internal T DeserializeInternal<T>(byte[] value) {
			if (value == null) return default(T);
			var type = typeof(T);
			var typename = type.ToString().TrimEnd(']');
			if (typename == "System.Byte[]") return (T)Convert.ChangeType(value, type);
			if (typename == "System.String") return (T)Convert.ChangeType(Nodes.First().Value.Encoding.GetString(value), type);

			var valueStr = Nodes.First().Value.Encoding.GetString(value);
			if (string.IsNullOrEmpty(valueStr)) return default(T);
			if (type.IsValueType) {
				bool isNullable = typename.StartsWith("System.Nullable`1[");
				var basename = isNullable ? typename.Substring(18) : typename;

				bool isElse = false;
				object obj = null;
				switch (basename) {
					case "System.Boolean":
						if (valueStr == "1") obj = true;
						else if (valueStr == "0") obj = true;
						break;
					case "System.Byte":
						if (byte.TryParse(valueStr, out var trybyte)) obj = trybyte;
						break;
					case "System.Char":
						if (valueStr.Length > 0) obj = valueStr[0];
						break;
					case "System.Decimal":
						if (Decimal.TryParse(valueStr, out var trydec)) obj = trydec;
						break;
					case "System.Double":
						if (Double.TryParse(valueStr, out var trydb)) obj = trydb;
						break;
					case "System.Single":
						if (Single.TryParse(valueStr, out var trysg)) obj = trysg;
						break;
					case "System.Int32":
						if (Int32.TryParse(valueStr, out var tryint32)) obj = tryint32;
						break;
					case "System.Int64":
						if (Int64.TryParse(valueStr, out var tryint64)) obj = tryint64;
						break;
					case "System.SByte":
						if (SByte.TryParse(valueStr, out var trysb)) obj = trysb;
						break;
					case "System.Int16":
						if (Int16.TryParse(valueStr, out var tryint16)) obj = tryint16;
						break;
					case "System.UInt32":
						if (UInt32.TryParse(valueStr, out var tryuint32)) obj = tryuint32;
						break;
					case "System.UInt64":
						if (UInt64.TryParse(valueStr, out var tryuint64)) obj = tryuint64;
						break;
					case "System.UInt16":
						if (UInt16.TryParse(valueStr, out var tryuint16)) obj = tryuint16;
						break;
					case "System.DateTime":
						if (DateTime.TryParse(valueStr, out var trydt)) obj = trydt;
						break;
					case "System.DateTimeOffset":
						if (DateTimeOffset.TryParse(valueStr, out var trydtos)) obj = trydtos;
						break;
					case "System.TimeSpan":
						if (Int64.TryParse(valueStr, out tryint64)) obj = new TimeSpan(tryint64);
						break;
					case "System.Guid":
						if (Guid.TryParse(valueStr, out var tryguid)) obj = tryguid;
						break;
					default:
						isElse = true;
						break;
				}

				if (isElse) {
					if (obj == null) return default(T);
					return (T)Convert.ChangeType(obj, typeof(T));
				}
			}

			return JsonConvert.DeserializeObject<T>(valueStr, this.SerializerSettings);
		}
		internal T[] DeserializeArrayInternal<T>(byte[][] value) {
			if (value == null) return new T[0];
			var list = new T[value.Length];
			for (var a = 0; a < value.Length; a++) list[a] = this.DeserializeInternal<T>(value[a]);
			return list;
		}
		internal (T1, T2)[] DeserializeTuple1Internal<T1, T2>(Tuple<byte[], T2>[] value) {
			if (value == null) return new(T1, T2)[0];
			var list = new (T1, T2)[value.Length];
			for (var a = 0; a < value.Length; a++) list[a] = (this.DeserializeInternal<T1>(value[a].Item1), value[a].Item2);
			return list;
		}
		internal (T2, T1)[] DeserializeTuple2Internal<T2, T1>(Tuple<T2, byte[]>[] value) {
			if (value == null) return new(T2, T1)[0];
			var list = new(T2, T1)[value.Length];
			for (var a = 0; a < value.Length; a++) list[a] = (value[a].Item1, this.DeserializeInternal<T1>(value[a].Item2));
			return list;
		}

		/// <summary>
		/// 创建redis访问类
		/// </summary>
		/// <param name="connectionString">127.0.0.1[:6379],password=123456,defaultDatabase=13,poolsize=50,ssl=false,writeBuffer=10240,prefix=key前辍</param>
		public CSRedisClient(string connectionString) : this(null, connectionString) { }
		/// <summary>
		/// 创建redis访问分区类，通过 KeyRule 对 key 进行分区，连接对应的 connectionString
		/// </summary>
		/// <param name="NodeRule">按key分区规则，返回值格式：127.0.0.1:6379/13，默认方案(null)：取key哈希与节点数取模</param>
		/// <param name="connectionStrings">127.0.0.1[:6379],password=123456,defaultDatabase=13,poolsize=50,ssl=false,writeBuffer=10240,prefix=key前辍</param>
		public CSRedisClient(Func<string, string> NodeRule, params string[] connectionStrings) {
			this.NodeRule = NodeRule;
			if (this.NodeRule == null) this.NodeRule = key => {
				var idx = Math.Abs(string.Concat(key).GetHashCode()) % Nodes.Count;
				return idx < 0 || idx >= NodeKeys.Count ? NodeKeys.First() : NodeKeys[idx];
			};
			if (connectionStrings == null || connectionStrings.Any() == false) throw new Exception("Redis ConnectionString 未设置");
			foreach (var connectionString in connectionStrings) {
				var pool = new RedisClientPool("", connectionString, client => { });
				if (Nodes.ContainsKey(pool.Key)) throw new Exception($"Node: {pool.Key} 重复，请检查");
				Nodes.Add(pool.Key, pool);
			}
			NodeKeys = Nodes.Keys.ToList();
			this.NodesServerManager = new NodesServerManagerProvider(this);
		}

		T GetConnectionAndExecute<T>(RedisClientPool pool, Func<Object<RedisClient>, T> handle) {
			Object<RedisClient> obj = null;
			Exception ex = null;
			try {
				obj = pool.Get();
				try {
					return handle(obj);
				} catch (Exception ex2) {
					ex = ex2;
					throw ex;
				}
			} finally {
				pool.Return(obj, ex);
			}
		}

		private T NodesNotSupport<T>(string[] keys, T defaultValue, Func<Object<RedisClient>, string[], T> callback) {
			if (keys == null || keys.Any() == false) return defaultValue;
			var rules = Nodes.Count > 1 ? keys.Select(a => NodeRule(a)).Distinct() : new[] { Nodes.FirstOrDefault().Key };
			if (rules.Count() > 1) throw new Exception("由于开启了群集模式，keys 分散在多个节点，无法使用此功能");
			var pool = Nodes.TryGetValue(rules.First(), out var b) ? b : Nodes.First().Value;
			string[] rkeys = new string[keys.Length];
			for (int a = 0; a < keys.Length; a++) rkeys[a] = string.Concat(pool.Prefix, keys[a]);
			if (rkeys.Length == 0) return defaultValue;
			return GetConnectionAndExecute(pool, conn => callback(conn, rkeys));
		}

		RedisClientPool GetNodeOrThrowNotFound(string nodeKey) {
			if (Nodes.ContainsKey(nodeKey) == false) throw new Exception($"找不到群集节点：{nodeKey}");
			return Nodes[nodeKey];
		}

		#region 缓存壳
		/// <summary>
		/// 缓存壳
		/// </summary>
		/// <typeparam name="T">缓存类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="timeoutSeconds">缓存秒数</param>
		/// <param name="getData">获取源数据的函数</param>
		/// <returns></returns>
		public T CacheShell<T>(string key, int timeoutSeconds, Func<T> getData) {
			if (timeoutSeconds <= 0) return getData();
			var cacheValue = Get(key);
			if (cacheValue != null) {
				try {
					return JsonConvert.DeserializeObject<T>(cacheValue, this.SerializerSettings);
				} catch {
					Del(key);
					throw;
				}
			}
			var ret = getData();
			Set(key, JsonConvert.SerializeObject(ret, this.SerializerSettings), timeoutSeconds);
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
		/// <returns></returns>
		public T CacheShell<T>(string key, string field, int timeoutSeconds, Func<T> getData) {
			if (timeoutSeconds <= 0) return getData();
			var cacheValue = HGet(key, field);
			if (cacheValue != null) {
				try {
					var value = JsonConvert.DeserializeObject<(T, long)>(cacheValue, this.SerializerSettings);
					if (DateTime.Now.Subtract(_dt1970.AddSeconds(value.Item2)).TotalSeconds <= timeoutSeconds) return value.Item1;
				} catch {
					HDel(key, field);
					throw;
				}
			}
			var ret = getData();
			HSet(key, field, JsonConvert.SerializeObject((ret, (long)DateTime.Now.Subtract(_dt1970).TotalSeconds), this.SerializerSettings));
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
		/// <returns></returns>
		public T[] CacheShell<T>(string key, string[] fields, int timeoutSeconds, Func<string[], (string, T)[]> getData) {
			fields = fields?.Distinct().ToArray();
			if (fields == null || fields.Length == 0) return new T[0];
			if (timeoutSeconds <= 0) return getData(fields).Select(a => a.Item2).ToArray();

			var ret = new T[fields.Length];
			var cacheValue = HMGet(key, fields);
			var fieldsMGet = new Dictionary<string, int>();

			for (var a = 0; a < ret.Length; a++) {
				if (cacheValue[a] != null) {
					try {
						var value = JsonConvert.DeserializeObject<(T, long)>(cacheValue[a], this.SerializerSettings);
						if (DateTime.Now.Subtract(_dt1970.AddSeconds(value.Item2)).TotalSeconds <= timeoutSeconds) {
							ret[a] = value.Item1;
							continue;
						}
					} catch {
						HDel(key, fields[a]);
						throw;
					}
				}
				fieldsMGet.Add(fields[a], a);
			}

			if (fieldsMGet.Any()) {
				var getDataIntput = fieldsMGet.Keys.ToArray();
				var data = getData(getDataIntput);
				var mset = new(string field, object value)[fieldsMGet.Count];
				var msetIndex = 0;
				foreach (var d in data) {
					if (fieldsMGet.ContainsKey(d.Item1) == false) throw new Exception($"使用 CacheShell 请确认 getData 返回值 (string, T)[] 中的 Item1 值: {d.Item1} 存在于 输入参数: {string.Join(",", getDataIntput)}");
					ret[fieldsMGet[d.Item1]] = d.Item2;
					mset[msetIndex++] = (d.Item1, JsonConvert.SerializeObject((d.Item2, (long)DateTime.Now.Subtract(_dt1970).TotalSeconds), this.SerializerSettings));
					fieldsMGet.Remove(d.Item1);
				}
				foreach (var fieldNull in fieldsMGet.Keys) {
					ret[fieldsMGet[fieldNull]] = default(T);
					mset[msetIndex++] = (fieldNull, JsonConvert.SerializeObject((default(T), (long)DateTime.Now.Subtract(_dt1970).TotalSeconds), this.SerializerSettings));
				}
				if (mset.Any()) HMSet(key, mset);
			}
			return ret;
		}
		#endregion

		#region 分区方式 Execute
		internal T ExecuteScalar<T>(string key, Func<Object<RedisClient>, string, T> hander) {
			if (key == null) return default(T);
			var pool = NodeRule == null || Nodes.Count == 1 ? Nodes.First().Value : (Nodes.TryGetValue(NodeRule(key), out var b) ? b : Nodes.First().Value);
			key = string.Concat(pool.Prefix, key);
			return GetConnectionAndExecute(pool, conn => hander(conn, key));
		}
		internal T[] ExeucteArray<T>(string[] key, Func<Object<RedisClient>, string[], T[]> hander) {
			if (key == null || key.Any() == false) return new T[0];
			if (NodeRule == null || Nodes.Count == 1) {
				var pool = Nodes.First().Value;
				var keys = key.Select(a => string.Concat(pool.Prefix, a)).ToArray();
				return GetConnectionAndExecute(pool, conn => hander(conn, keys));
			}
			var rules = new Dictionary<string, List<(string, int)>>();
			for (var a = 0; a < key.Length; a++) {
				var rule = NodeRule(key[a]);
				if (rules.ContainsKey(rule)) rules[rule].Add((key[a], a));
				else rules.Add(rule, new List<(string, int)> { (key[a], a) });
			}
			T[] ret = new T[key.Length];
			foreach (var r in rules) {
				var pool = Nodes.TryGetValue(r.Key, out var b) ? b : Nodes.First().Value;
				var keys = r.Value.Select(a => string.Concat(pool.Prefix, a.Item1)).ToArray();
				GetConnectionAndExecute(pool, conn => {
					var vals = hander(conn, keys);
					for (var z = 0; z < r.Value.Count; z++) {
						ret[r.Value[z].Item2] = vals == null || z >= vals.Length ? default(T) : vals[z];
					}
					return 0;
				});
			}
			return ret;
		}
		internal long ExecuteNonQuery(string[] key, Func<Object<RedisClient>, string[], long> hander) {
			if (key == null || key.Any() == false) return 0;
			if (NodeRule == null || Nodes.Count == 1) {
				var pool = Nodes.First().Value;
				var keys = key.Select(a => string.Concat(pool.Prefix, a)).ToArray();
				return GetConnectionAndExecute(pool, conn => hander(conn, keys));
			}
			var rules = new Dictionary<string, List<string>>();
			for (var a = 0; a < key.Length; a++) {
				var rule = NodeRule(key[a]);
				if (rules.ContainsKey(rule)) rules[rule].Add(key[a]);
				else rules.Add(rule, new List<string> { key[a] });
			}
			long affrows = 0;
			foreach (var r in rules) {
				var pool = Nodes.TryGetValue(r.Key, out var b) ? b : Nodes.First().Value;
				var keys = r.Value.Select(a => string.Concat(pool.Prefix, a)).ToArray();
				affrows += GetConnectionAndExecute(pool, conn => hander(conn, keys));
			}
			return affrows;
		}
		#endregion

		/// <summary>
		/// 创建管道传输
		/// </summary>
		/// <param name="handler"></param>
		/// <returns></returns>
		public object[] PipeLine(Action<CSRedisClientPipe<string>> handler) {
			if (handler == null) return new object[0];
			var pipe = new CSRedisClientPipe<string>(this);
			handler(pipe);
			return pipe.EndPipe();
		}

		#region 服务器命令
		/// <summary>
		/// 执行所有 Nodes 服务器命令
		/// </summary>
		public NodesServerManagerProvider NodesServerManager { get; set; }
		public partial class NodesServerManagerProvider {
			private CSRedisClient _csredis;

			public NodesServerManagerProvider(CSRedisClient csredis) {
				_csredis = csredis;
			}

			/// <summary>
			/// 异步执行一个 AOF（AppendOnly File） 文件重写操作
			/// </summary>
			/// <returns></returns>
			public (string key, string value)[] BgRewriteAof() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.BgRewriteAof()))).ToArray();
			/// <summary>
			/// 在后台异步保存当前数据库的数据到磁盘
			/// </summary>
			/// <returns></returns>
			public (string key, string value)[] BgSave() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.BgSave()))).ToArray();
			/// <summary>
			/// 关闭客户端连接
			/// </summary>
			/// <param name="ip">ip</param>
			/// <param name="port">端口</param>
			/// <returns></returns>
			public (string key, string value)[] ClientKill(string ip, int port) => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.ClientKill(ip, port)))).ToArray();
			/// <summary>
			/// 关闭客户端连接
			/// </summary>
			/// <param name="addr">ip:port</param>
			/// <param name="id">客户唯一标识</param>
			/// <param name="type">类型：normal | slave | pubsub</param>
			/// <param name="skipMe">跳过自己</param>
			/// <returns></returns>
			public (string key, long value)[] ClientKill(string addr = null, string id = null, ClientKillType? type = null, bool? skipMe = null) => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.ClientKill(addr, id, type?.ToString(), skipMe)))).ToArray();
			public enum ClientKillType { normal, slave, pubsub }
			/// <summary>
			/// 获取连接到服务器的客户端连接列表
			/// </summary>
			/// <returns></returns>
			public (string key, string value)[] ClientList() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.ClientList()))).ToArray();
			/// <summary>
			/// 获取连接的名称
			/// </summary>
			/// <returns></returns>
			public (string key, string value)[] ClientGetName() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.ClientGetName()))).ToArray();
			/// <summary>
			/// 在指定时间内终止运行来自客户端的命令
			/// </summary>
			/// <param name="timeout">阻塞时间</param>
			/// <returns></returns>
			public (string key, string value)[] ClientPause(TimeSpan timeout) => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.ClientPause(timeout)))).ToArray();
			/// <summary>
			/// 设置当前连接的名称
			/// </summary>
			/// <param name="connectionName">连接名称</param>
			/// <returns></returns>
			public (string key, string value)[] ClientSetName(string connectionName) => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.ClientSetName(connectionName)))).ToArray();
			/// <summary>
			/// 返回当前服务器时间
			/// </summary>
			/// <returns></returns>
			public (string key, DateTime value)[] Time() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.Time()))).ToArray();
			/// <summary>
			/// 获取指定配置参数的值
			/// </summary>
			/// <param name="parameter">参数</param>
			/// <returns></returns>
			public (string key, Dictionary<string, string> value)[] ConfigGet(string parameter) => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.ConfigGet(parameter).ToDictionary(z => z.Item1, y => y.Item2)))).ToArray();
			/// <summary>
			/// 对启动 Redis 服务器时所指定的 redis.conf 配置文件进行改写
			/// </summary>
			/// <returns></returns>
			public (string key, string value)[] ConfigRewrite() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.ConfigRewrite()))).ToArray();
			/// <summary>
			/// 修改 redis 配置参数，无需重启
			/// </summary>
			/// <param name="parameter">参数</param>
			/// <param name="value">值</param>
			/// <returns></returns>
			public (string key, string value)[] ConfigSet(string parameter, string value) => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.ConfigSet(parameter, value)))).ToArray();
			/// <summary>
			/// 重置 INFO 命令中的某些统计数据
			/// </summary>
			/// <returns></returns>
			public (string key, string value)[] ConfigResetStat() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.ConfigResetStat()))).ToArray();
			/// <summary>
			/// 返回当前数据库的 key 的数量
			/// </summary>
			/// <returns></returns>
			public (string key, long value)[] DbSize() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.DbSize()))).ToArray();
			/// <summary>
			/// 让 Redis 服务崩溃
			/// </summary>
			/// <returns></returns>
			public (string key, string value)[] DebugSegFault() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.DebugSegFault()))).ToArray();
			/// <summary>
			/// 删除所有数据库的所有key
			/// </summary>
			/// <returns></returns>
			public (string key, string value)[] FlushAll() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.FlushAll()))).ToArray();
			/// <summary>
			/// 删除当前数据库的所有key
			/// </summary>
			/// <returns></returns>
			public (string key, string value)[] FlushDb() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.FlushDb()))).ToArray();
			/// <summary>
			/// 获取 Redis 服务器的各种信息和统计数值
			/// </summary>
			/// <param name="section">部分(all|default|server|clients|memory|persistence|stats|replication|cpu|commandstats|cluster|keyspace)</param>
			/// <returns></returns>
			public (string key, string value)[] Info(InfoSection? section = null) => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.Info(section?.ToString())))).ToArray();
			public enum InfoSection { All, Default, Server, Clients, Memory, Persistence, Stats, Replication, Cpu, Commandstats, Cluster, Keyspace }
			/// <summary>
			/// 返回最近一次 Redis 成功将数据保存到磁盘上的时间
			/// </summary>
			/// <returns></returns>
			public (string key, DateTime value)[] LastSave() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.LastSave()))).ToArray();
			/// <summary>
			/// 实时打印出 Redis 服务器接收到的命令，调试用
			/// </summary>
			/// <param name="onReceived">接收命令</param>
			/// <returns></returns>
			public (string key, string value)[] Monitor(Action<object, object> onReceived) => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => {
				c.Value.MonitorReceived += (s, o) => onReceived?.Invoke(s, o.Message);
				return (a.Key, c.Value.Monitor());
			})).ToArray();
			/// <summary>
			/// 返回主从实例所属的角色
			/// </summary>
			/// <returns></returns>
			public (string key, RedisRole value)[] Role() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.Role()))).ToArray();
			/// <summary>
			/// 同步保存数据到硬盘
			/// </summary>
			/// <returns></returns>
			public (string key, string value)[] Save() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.Save()))).ToArray();
			/// <summary>
			/// 异步保存数据到硬盘，并关闭服务器
			/// </summary>
			/// <param name="isSave">是否保存</param>
			/// <returns></returns>
			public (string key, string value)[] Shutdown(bool isSave = true) => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.Shutdown(isSave)))).ToArray();
			/// <summary>
			/// 将服务器转变为指定服务器的从属服务器(slave server)，如果当前服务器已经是某个主服务器(master server)的从属服务器，那么执行 SLAVEOF host port 将使当前服务器停止对旧主服务器的同步，丢弃旧数据集，转而开始对新主服务器进行同步。
			/// </summary>
			/// <param name="host">主机</param>
			/// <param name="port">端口</param>
			/// <returns></returns>
			public (string key, string value)[] SlaveOf(string host, int port) => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.SlaveOf(host, port)))).ToArray();
			/// <summary>
			/// 从属服务器执行命令 SLAVEOF NO ONE 将使得这个从属服务器关闭复制功能，并从从属服务器转变回主服务器，原来同步所得的数据集不会被丢弃。
			/// </summary>
			/// <returns></returns>
			public (string key, string value)[] SlaveOfNoOne() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.SlaveOfNoOne()))).ToArray();
			/// <summary>
			/// 管理 redis 的慢日志，按数量获取
			/// </summary>
			/// <param name="count">数量</param>
			/// <returns></returns>
			public (string key, RedisSlowLogEntry[] value)[] SlowLogGet(long? count = null) => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.SlowLogGet(count)))).ToArray();
			/// <summary>
			/// 管理 redis 的慢日志，总数量
			/// </summary>
			/// <returns></returns>
			public (string key, long value)[] SlowLogLen() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.SlowLogLen()))).ToArray();
			/// <summary>
			/// 管理 redis 的慢日志，清空
			/// </summary>
			/// <returns></returns>
			public (string key, string value)[] SlowLogReset() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.SlowLogReset()))).ToArray();
			/// <summary>
			/// 用于复制功能(replication)的内部命令
			/// </summary>
			/// <returns></returns>
			public (string key, byte[] value)[] Sync() => _csredis.Nodes.Values.Select(a => _csredis.GetConnectionAndExecute(a, c => (a.Key, c.Value.Sync()))).ToArray();
		}

		/// <summary>
		/// 获取 Nodes 中某节点的服务器命令
		/// </summary>
		/// <param name="nodeKey">节点key</param>
		/// <returns></returns>
		public ServerManagerProvider GetServerManager(string nodeKey) => new ServerManagerProvider(this, GetNodeOrThrowNotFound(nodeKey));
		public partial class ServerManagerProvider {
			private CSRedisClient _csredis;
			private RedisClientPool _pool;

			public ServerManagerProvider(CSRedisClient csredis, RedisClientPool pool) {
				_csredis = csredis;
				_pool = pool;
			}

			/// <summary>
			/// 异步执行一个 AOF（AppendOnly File） 文件重写操作
			/// </summary>
			/// <returns></returns>
			public string BgRewriteAof() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.BgRewriteAof());
			/// <summary>
			/// 在后台异步保存当前数据库的数据到磁盘
			/// </summary>
			/// <returns></returns>
			public string BgSave() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.BgSave());
			/// <summary>
			/// 关闭客户端连接
			/// </summary>
			/// <param name="ip">ip</param>
			/// <param name="port">端口</param>
			/// <returns></returns>
			public string ClientKill(string ip, int port) => _csredis.GetConnectionAndExecute(_pool, c => c.Value.ClientKill(ip, port));
			/// <summary>
			/// 关闭客户端连接
			/// </summary>
			/// <param name="addr">ip:port</param>
			/// <param name="id">客户唯一标识</param>
			/// <param name="type">类型：normal | slave | pubsub</param>
			/// <param name="skipMe">跳过自己</param>
			/// <returns></returns>
			public long ClientKill(string addr = null, string id = null, ClientKillType? type = null, bool? skipMe = null) => _csredis.GetConnectionAndExecute(_pool, c => c.Value.ClientKill(addr, id, type?.ToString(), skipMe));
			public enum ClientKillType { normal, slave, pubsub }
			/// <summary>
			/// 获取连接到服务器的客户端连接列表
			/// </summary>
			/// <returns></returns>
			public string ClientList() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.ClientList());
			/// <summary>
			/// 获取连接的名称
			/// </summary>
			/// <returns></returns>
			public string ClientGetName() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.ClientGetName());
			/// <summary>
			/// 在指定时间内终止运行来自客户端的命令
			/// </summary>
			/// <param name="timeout">阻塞时间</param>
			/// <returns></returns>
			public string ClientPause(TimeSpan timeout) => _csredis.GetConnectionAndExecute(_pool, c => c.Value.ClientPause(timeout));
			/// <summary>
			/// 设置当前连接的名称
			/// </summary>
			/// <param name="connectionName">连接名称</param>
			/// <returns></returns>
			public string ClientSetName(string connectionName) => _csredis.GetConnectionAndExecute(_pool, c => c.Value.ClientSetName(connectionName));
			/// <summary>
			/// 返回当前服务器时间
			/// </summary>
			/// <returns></returns>
			public DateTime Time() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.Time());
			/// <summary>
			/// 获取指定配置参数的值
			/// </summary>
			/// <param name="parameter">参数</param>
			/// <returns></returns>
			public Dictionary<string, string> ConfigGet(string parameter) => _csredis.GetConnectionAndExecute(_pool, c => c.Value.ConfigGet(parameter).ToDictionary(z => z.Item1, y => y.Item2));
			/// <summary>
			/// 对启动 Redis 服务器时所指定的 redis.conf 配置文件进行改写
			/// </summary>
			/// <returns></returns>
			public string ConfigRewrite() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.ConfigRewrite());
			/// <summary>
			/// 修改 redis 配置参数，无需重启
			/// </summary>
			/// <param name="parameter">参数</param>
			/// <param name="value">值</param>
			/// <returns></returns>
			public string ConfigSet(string parameter, string value) => _csredis.GetConnectionAndExecute(_pool, c => c.Value.ConfigSet(parameter, value));
			/// <summary>
			/// 重置 INFO 命令中的某些统计数据
			/// </summary>
			/// <returns></returns>
			public string ConfigResetStat() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.ConfigResetStat());
			/// <summary>
			/// 返回当前数据库的 key 的数量
			/// </summary>
			/// <returns></returns>
			public long DbSize() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.DbSize());
			/// <summary>
			/// 让 Redis 服务崩溃
			/// </summary>
			/// <returns></returns>
			public string DebugSegFault() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.DebugSegFault());
			/// <summary>
			/// 删除所有数据库的所有key
			/// </summary>
			/// <returns></returns>
			public string FlushAll() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.FlushAll());
			/// <summary>
			/// 删除当前数据库的所有key
			/// </summary>
			/// <returns></returns>
			public string FlushDb() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.FlushDb());
			/// <summary>
			/// 获取 Redis 服务器的各种信息和统计数值
			/// </summary>
			/// <param name="section">部分(Server | Clients | Memory | Persistence | Stats | Replication | CPU | Keyspace)</param>
			/// <returns></returns>
			public string Info(InfoSection? section = null) => _csredis.GetConnectionAndExecute(_pool, c => c.Value.Info(section?.ToString()));
			public enum InfoSection { Server, Clients, Memory, Persistence, Stats, Replication, CPU, Keyspace }
			/// <summary>
			/// 返回最近一次 Redis 成功将数据保存到磁盘上的时间
			/// </summary>
			/// <returns></returns>
			public DateTime LastSave() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.LastSave());
			/// <summary>
			/// 实时打印出 Redis 服务器接收到的命令，调试用
			/// </summary>
			/// <param name="onReceived">接收命令</param>
			/// <returns></returns>
			public string Monitor(Action<object, object> onReceived) => _csredis.GetConnectionAndExecute(_pool, c => {
				c.Value.MonitorReceived += (s, o) => onReceived?.Invoke(s, o.Message);
				return c.Value.Monitor();
			});
			/// <summary>
			/// 返回主从实例所属的角色
			/// </summary>
			/// <returns></returns>
			public RedisRole Role() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.Role());
			/// <summary>
			/// 同步保存数据到硬盘
			/// </summary>
			/// <returns></returns>
			public string Save() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.Save());
			/// <summary>
			/// 异步保存数据到硬盘，并关闭服务器
			/// </summary>
			/// <param name="isSave">是否保存</param>
			/// <returns></returns>
			public string Shutdown(bool isSave = true) => _csredis.GetConnectionAndExecute(_pool, c => c.Value.Shutdown(isSave));
			/// <summary>
			/// 将服务器转变为指定服务器的从属服务器(slave server)，如果当前服务器已经是某个主服务器(master server)的从属服务器，那么执行 SLAVEOF host port 将使当前服务器停止对旧主服务器的同步，丢弃旧数据集，转而开始对新主服务器进行同步。
			/// </summary>
			/// <param name="host">主机</param>
			/// <param name="port">端口</param>
			/// <returns></returns>
			public string SlaveOf(string host, int port) => _csredis.GetConnectionAndExecute(_pool, c => c.Value.SlaveOf(host, port));
			/// <summary>
			/// 从属服务器执行命令 SLAVEOF NO ONE 将使得这个从属服务器关闭复制功能，并从从属服务器转变回主服务器，原来同步所得的数据集不会被丢弃。
			/// </summary>
			/// <returns></returns>
			public string SlaveOfNoOne() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.SlaveOfNoOne());
			/// <summary>
			/// 管理 redis 的慢日志，按数量获取
			/// </summary>
			/// <param name="count">数量</param>
			/// <returns></returns>
			public RedisSlowLogEntry[] SlowLogGet(long? count = null) => _csredis.GetConnectionAndExecute(_pool, c => c.Value.SlowLogGet(count));
			/// <summary>
			/// 管理 redis 的慢日志，总数量
			/// </summary>
			/// <returns></returns>
			public long SlowLogLen() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.SlowLogLen());
			/// <summary>
			/// 管理 redis 的慢日志，清空
			/// </summary>
			/// <returns></returns>
			public string SlowLogReset() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.SlowLogReset());
			/// <summary>
			/// 用于复制功能(replication)的内部命令
			/// </summary>
			/// <returns></returns>
			public byte[] Sync() => _csredis.GetConnectionAndExecute(_pool, c => c.Value.Sync());
		}
		#endregion

		#region 连接命令
		/// <summary>
		/// 验证密码是否正确
		/// </summary>
		/// <param name="nodeKey">分区key</param>
		/// <param name="password">密码</param>
		/// <returns></returns>
		[Obsolete("不建议手工执行，连接池自己管理最佳")]
		private bool Auth(string nodeKey, string password) => GetConnectionAndExecute(GetNodeOrThrowNotFound(nodeKey), c => c.Value.Auth(password)) == "OK";
		/// <summary>
		/// 打印字符串
		/// </summary>
		/// <param name="nodeKey">分区key</param>
		/// <param name="message">消息</param>
		/// <returns></returns>
		public string Echo(string nodeKey, string message) => GetConnectionAndExecute(GetNodeOrThrowNotFound(nodeKey), c => c.Value.Echo(message));
		/// <summary>
		/// 查看服务是否运行
		/// </summary>
		/// <param name="nodeKey">分区key</param>
		/// <returns></returns>
		public bool Ping(string nodeKey) => GetConnectionAndExecute(GetNodeOrThrowNotFound(nodeKey), c => c.Value.Ping()) == "PONG";
		/// <summary>
		/// 关闭当前连接
		/// </summary>
		/// <param name="nodeKey">分区key</param>
		/// <returns></returns>
		[Obsolete("不建议手工执行，连接池自己管理最佳")]
		private bool Quit(string nodeKey) => GetConnectionAndExecute(GetNodeOrThrowNotFound(nodeKey), c => c.Value.Quit()) == "OK";
		/// <summary>
		/// 切换到指定的数据库
		/// </summary>
		/// <param name="nodeKey">分区key</param>
		/// <param name="index">数据库</param>
		/// <returns></returns>
		[Obsolete("不建议手工执行，连接池所有连接应该指向同一数据库，若手工修改将导致数据的不一致")]
		private bool Select(string nodeKey, int index) => GetConnectionAndExecute(GetNodeOrThrowNotFound(nodeKey), c => c.Value.Select(index)) == "OK";
		#endregion

		#region 脚本命令
		/// <summary>
		/// 执行脚本
		/// </summary>
		/// <param name="script">脚本</param>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="args">参数</param>
		/// <returns></returns>
		public object Eval(string script, string key, params object[] args) => ExecuteScalar(key, (c, k) => c.Value.Eval(script, new[] { k }, args?.Select(z => this.SerializeInternal(z)).ToArray()));
		#endregion

		#region 发布订阅
		/// <summary>
		/// Redis Publish 命令用于将信息发送到指定群集节点的频道
		/// </summary>
		/// <param name="channel">频道名</param>
		/// <param name="data">消息文本</param>
		/// <returns></returns>
		public long Publish(string channel, string data) {
			var msgid = HIncrBy("CSRedisPublishMsgId", channel, 1);
			return ExecuteScalar(channel, (c, k) => c.Value.Publish(channel, $"{msgid}|{data}"));
		}
		/// <summary>
		/// 查看所有订阅频道
		/// </summary>
		/// <param name="pattern"></param>
		/// <returns></returns>
		public (string key, string[] value)[] PubSubChannels(string pattern) => Nodes.Values.Select(a => GetConnectionAndExecute(a, c => (a.Key, c.Value.PubSubChannels(pattern)))).ToArray();
		/// <summary>
		/// 查看所有模糊订阅端的数量
		/// </summary>
		/// <returns></returns>
		public (string key, long value)[] PubSubNumPat() => Nodes.Values.Select(a => GetConnectionAndExecute(a, c => (a.Key, c.Value.PubSubNumPat()))).ToArray();
		/// <summary>
		/// 查看所有订阅端的数量
		/// </summary>
		/// <param name="channels"></param>
		/// <returns></returns>
		public (string key, Dictionary<string, long> value)[] PubSubNumSub(params string[] channels) => Nodes.Values.Select(a => GetConnectionAndExecute(a, c => (a.Key, c.Value.PubSubNumSub(channels).ToDictionary(z => z.Item1, y => y.Item2)))).ToArray();
		/// <summary>
		/// 订阅，根据分区规则返回SubscribeObject，Subscribe(("chan1", msg => Console.WriteLine(msg.Body)), ("chan2", msg => Console.WriteLine(msg.Body)))
		/// </summary>
		/// <param name="channels">频道和接收器</param>
		/// <returns>返回可停止订阅的对象</returns>
		public SubscribeObject Subscribe(params (string, Action<SubscribeMessageEventArgs>)[] channels) {
			var chans = channels.Select(a => a.Item1).Distinct().ToArray();
			var onmessages = channels.ToDictionary(a => a.Item1, b => b.Item2);

			var rules = new Dictionary<string, List<string>>();
			for (var a = 0; a < chans.Length; a++) {
				var rule = NodeRule(chans[a]);
				if (rules.ContainsKey(rule)) rules[rule].Add(chans[a]);
				else rules.Add(rule, new List<string> { chans[a] });
			}

			List<(string[] keys, Object<RedisClient> conn)> subscrs = new List<(string[] keys, Object<RedisClient> conn)>();
			foreach (var r in rules) {
				var pool = Nodes.TryGetValue(r.Key, out var p) ? p : Nodes.First().Value;
				Task.Run(async () => subscrs.Add((r.Value.ToArray(), await pool.GetAsync()))).Wait();
			}

			var so = new SubscribeObject(this, chans, subscrs.ToArray(), onmessages);
			return so;
		}
		public class SubscribeObject : IDisposable {
			internal CSRedisClient Redis;
			public string[] Channels { get; }
			public (string[] chans, Object<RedisClient> conn)[] Subscrs { get; }
			internal Dictionary<string, Action<SubscribeMessageEventArgs>> OnMessageDic;
			public bool IsUnsubscribed { get; private set; } = true;

			internal SubscribeObject(CSRedisClient redis, string[] channels, (string[] chans, Object<RedisClient> conn)[] subscrs, Dictionary<string, Action<SubscribeMessageEventArgs>> onMessageDic) {
				this.Redis = redis;
				this.Channels = channels;
				this.Subscrs = subscrs;
				this.OnMessageDic = onMessageDic;
				this.IsUnsubscribed = false;

				AppDomain.CurrentDomain.ProcessExit += (s1, e1) => {
					this.Dispose();
				};
				Console.CancelKeyPress += (s1, e1) => {
					this.Dispose();
				};

				foreach (var subscr in this.Subscrs) {
					new Thread(Subscribe).Start(subscr);
				}
			}

			private void Subscribe(object state) {
				var subscr = ((string[] chans, Object<RedisClient> conn))state;
				var pool = subscr.conn.Pool as RedisClientPool;

				EventHandler<RedisMonitorEventArgs> MonitorReceived = (a, b) => {
				};
				EventHandler<RedisSubscriptionReceivedEventArgs> SubscriptionReceived = (a, b) => {
					try {
						if (b.Message.Type == "message" && this.OnMessageDic != null && this.OnMessageDic.TryGetValue(b.Message.Channel, out var action) == true) {
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
						var bgcolor = Console.BackgroundColor;
						var forecolor = Console.ForegroundColor;
						Console.BackgroundColor = ConsoleColor.DarkRed;
						Console.ForegroundColor = ConsoleColor.White;
						Console.Write($"订阅方法执行出错【{pool.Key}】(channels:{string.Join(",", Channels)})/(chans:{string.Join(",", subscr.chans)})：{ex.Message}\r\n{ex.StackTrace}");
						Console.BackgroundColor = bgcolor;
						Console.ForegroundColor = forecolor;
						Console.WriteLine();
					}
				};
				subscr.conn.Value.MonitorReceived += MonitorReceived;
				subscr.conn.Value.SubscriptionReceived += SubscriptionReceived;

				while (IsUnsubscribed == false) {
					try {
						subscr.conn.Value.Ping();

						var bgcolor = Console.BackgroundColor;
						var forecolor = Console.ForegroundColor;
						Console.BackgroundColor = ConsoleColor.DarkGreen;
						Console.ForegroundColor = ConsoleColor.White;
						Console.Write($"正在订阅【{pool.Key}】(channels:{string.Join(",", Channels)})/(chans:{string.Join(",", subscr.chans)})");
						Console.BackgroundColor = bgcolor;
						Console.ForegroundColor = forecolor;
						Console.WriteLine();

						subscr.conn.Value.Subscribe(subscr.chans);

						if (IsUnsubscribed == false) {
							subscr.conn.ResetValue();
							subscr.conn.Value.MonitorReceived += MonitorReceived;
							subscr.conn.Value.SubscriptionReceived += SubscriptionReceived;

							//服务器断开连接 IsConnected == false https://github.com/2881099/csredis/issues/37
							if (subscr.conn.Value.IsConnected == false)
								throw new Exception("redis-server 连接已断开");
						}
					} catch (Exception ex) {
						if (IsUnsubscribed) break;

						var bgcolor = Console.BackgroundColor;
						var forecolor = Console.ForegroundColor;
						Console.BackgroundColor = ConsoleColor.DarkYellow;
						Console.ForegroundColor = ConsoleColor.White;
						Console.Write($"订阅出错【{pool.Key}】(channels:{string.Join(",", Channels)})/(chans:{string.Join(",", subscr.chans)})：{ex.Message}，3秒后重连。。。");
						Console.BackgroundColor = bgcolor;
						Console.ForegroundColor = forecolor;
						Console.WriteLine();
						Thread.CurrentThread.Join(1000 * 3);
					}
				}
			}

			public void Unsubscribe() {
				this.Dispose();
			}

			~SubscribeObject() {
				this.Dispose();
			}

			public void Dispose() {
				this.IsUnsubscribed = true;
				if (this.Subscrs != null) {
					foreach (var subscr in this.Subscrs) {
						try { subscr.conn.Value.Unsubscribe(); } catch { }
						subscr.conn.Pool.Return(subscr.conn, true);
					}
				}
			}
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
		/// 模糊订阅，订阅所有分区节点(同条消息只处理一次），返回SubscribeObject，PSubscribe(new [] { "chan1*", "chan2*" }, msg => Console.WriteLine(msg.Body))
		/// </summary>
		/// <param name="channelPatterns">模糊频道</param>
		/// <param name="pmessage">接收器</param>
		/// <returns>返回可停止模糊订阅的对象</returns>
		public PSubscribeObject PSubscribe(string[] channelPatterns, Action<PSubscribePMessageEventArgs> pmessage) {
			var chans = channelPatterns.Distinct().ToArray();

			List<Object<RedisClient>> redisConnections = new List<Object<RedisClient>>();
			foreach (var pool in Nodes) {
				Task.Run(async () => redisConnections.Add(await pool.Value.GetAsync())).Wait();
			}

			var so = new PSubscribeObject(this, chans, redisConnections.ToArray(), pmessage);
			return so;
		}
		public class PSubscribeObject : IDisposable {
			internal CSRedisClient Redis;
			public string[] Channels { get; }
			internal Action<PSubscribePMessageEventArgs> OnPMessage;
			public Object<RedisClient>[] RedisConnections { get; }
			public bool IsPUnsubscribed { get; private set; } = true;

			internal PSubscribeObject(CSRedisClient redis, string[] channels, Object<RedisClient>[] redisConnections, Action<PSubscribePMessageEventArgs> onPMessage) {
				this.Redis = redis;
				this.Channels = channels;
				this.RedisConnections = redisConnections;
				this.OnPMessage = onPMessage;
				this.IsPUnsubscribed = false;

				AppDomain.CurrentDomain.ProcessExit += (s1, e1) => {
					this.Dispose();
				};
				Console.CancelKeyPress += (s1, e1) => {
					this.Dispose();
				};

				foreach (var conn in this.RedisConnections) {
					new Thread(PSubscribe).Start(conn);
				}
			}

			private void PSubscribe(object state) {
				var conn = (Object<RedisClient>)state;
				var pool = conn.Pool as RedisClientPool;
				var psubscribeKey = string.Join("pSpLiT", Channels);

				EventHandler<RedisMonitorEventArgs> MonitorReceived = (a, b) => {
				};
				EventHandler<RedisSubscriptionReceivedEventArgs> SubscriptionReceived = (a, b) => {
					try {
						if (b.Message.Type == "pmessage" && this.OnPMessage != null) {
							var msgidIdx = b.Message.Body.IndexOf('|');
							if (msgidIdx != -1 && long.TryParse(b.Message.Body.Substring(0, msgidIdx), out var trylong)) {
								var readed = Redis.Eval($@"
ARGV[1] = redis.call('HGET', KEYS[1], '{b.Message.Channel}')
if ARGV[1] ~= ARGV[2] then
  redis.call('HSET', KEYS[1], '{b.Message.Channel}', ARGV[2])
  return 1
end
return 0", $"CSRedisPSubscribe{psubscribeKey}", "", trylong.ToString());
								if (readed?.ToString() == "1")
									this.OnPMessage(new PSubscribePMessageEventArgs {
										Body = b.Message.Body.Substring(msgidIdx + 1),
										Channel = b.Message.Channel,
										MessageId = trylong,
										Pattern = b.Message.Pattern
									});
								//else
								//	Console.WriteLine($"消息被处理过：id:{trylong} channel:{b.Message.Channel} pattern:{b.Message.Pattern} body:{b.Message.Body.Substring(msgidIdx + 1)}");
							} else
								this.OnPMessage(new PSubscribePMessageEventArgs {
									Body = b.Message.Body,
									Channel = b.Message.Channel,
									MessageId = 0,
									Pattern = b.Message.Pattern
								});
						}
					} catch (Exception ex) {
						var bgcolor = Console.BackgroundColor;
						var forecolor = Console.ForegroundColor;
						Console.BackgroundColor = ConsoleColor.DarkRed;
						Console.ForegroundColor = ConsoleColor.White;
						Console.Write($"模糊订阅出错【{pool.Key}】(channels:{string.Join(",", Channels)})：{ex.Message}\r\n{ex.StackTrace}");
						Console.BackgroundColor = bgcolor;
						Console.ForegroundColor = forecolor;
						Console.WriteLine();

					}
				};
				conn.Value.MonitorReceived += MonitorReceived;
				conn.Value.SubscriptionReceived += SubscriptionReceived;

				while (true) {
					try {
						conn.Value.Ping();

						var bgcolor = Console.BackgroundColor;
						var forecolor = Console.ForegroundColor;
						Console.BackgroundColor = ConsoleColor.DarkGreen;
						Console.ForegroundColor = ConsoleColor.White;
						Console.Write($"正在模糊订阅【{pool.Key}】(channels:{string.Join(",", Channels)})");
						Console.BackgroundColor = bgcolor;
						Console.ForegroundColor = forecolor;
						Console.WriteLine();

						conn.Value.PSubscribe(this.Channels);

						if (IsPUnsubscribed == false) {
							conn.ResetValue();
							conn.Value.MonitorReceived += MonitorReceived;
							conn.Value.SubscriptionReceived += SubscriptionReceived;

							//服务器断开连接 IsConnected == false https://github.com/2881099/csredis/issues/37
							if (conn.Value.IsConnected == false)
								throw new Exception("redis-server 连接已断开");
						}
					} catch (Exception ex) {
						if (IsPUnsubscribed) break;

						var bgcolor = Console.BackgroundColor;
						var forecolor = Console.ForegroundColor;
						Console.BackgroundColor = ConsoleColor.DarkYellow;
						Console.ForegroundColor = ConsoleColor.White;
						Console.Write($"模糊订阅出错【{pool.Key}】(channels:{string.Join(",", Channels)})：{ex.Message}，3秒后重连。。。");
						Console.BackgroundColor = bgcolor;
						Console.ForegroundColor = forecolor;
						Console.WriteLine();
						Thread.CurrentThread.Join(1000 * 3);
					}
				}
			}

			public void PUnsubscribe() {
				this.Dispose();
			}

			~PSubscribeObject() {
				this.Dispose();
			}

			public void Dispose() {
				this.IsPUnsubscribed = true;
				if (this.RedisConnections != null) {
					foreach (var conn in this.RedisConnections) {
						try { conn.Value.PUnsubscribe(); } catch { }
						conn.Pool.Return(conn, true);
					}
				}
			}
		}
		public class PSubscribePMessageEventArgs : SubscribeMessageEventArgs {
			/// <summary>
			/// 匹配模式
			/// </summary>
			public string Pattern { get; set; }
		}
		#endregion

		#region HyperLogLog
		/// <summary>
		/// 添加指定元素到 HyperLogLog 中
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="elements">元素</param>
		/// <returns></returns>
		public bool PfAdd(string key, params object[] elements) => elements == null || elements.Any() == false ? false : ExecuteScalar(key, (c, k) => c.Value.PfAdd(k, elements?.Select(z => this.SerializeInternal(z)).ToArray()));
		/// <summary>
		/// 返回给定 HyperLogLog 的基数估算值。警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public long PfCount(params string[] keys) => NodesNotSupport(keys, 0, (c, k) => c.Value.PfCount(k));
		/// <summary>
		/// 将多个 HyperLogLog 合并为一个 HyperLogLog。警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destKey">新的 HyperLogLog，不含prefix前辍</param>
		/// <param name="sourceKeys">源 HyperLogLog，不含prefix前辍</param>
		/// <returns></returns>
		public bool PfMerge(string destKey, params string[] sourceKeys) => NodesNotSupport(new[] { destKey }.Concat(sourceKeys).ToArray(), null, (c, k) => c.Value.PfMerge(k.First(), k.Where((x, y) => y > 0).ToArray())) == "OK";
		#endregion

		#region Sorted Set
		/// <summary>
		/// 向有序集合添加一个或多个成员，或者更新已存在成员的分数
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="memberScores">一个或多个成员分数</param>
		/// <returns></returns>
		public long ZAdd(string key, params (object, double)[] memberScores) {
			if (memberScores == null || memberScores.Any() == false) return 0;
			var ms = memberScores.Select(a => new Tuple<double, object>(a.Item2, this.SerializeInternal(a.Item1))).ToArray();
			return ExecuteScalar(key, (c, k) => c.Value.ZAdd(k, ms));
		}
		/// <summary>
		/// 获取有序集合的成员数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long ZCard(string key) => ExecuteScalar(key, (c, k) => c.Value.ZCard(k));
		/// <summary>
		/// 计算在有序集合中指定区间分数的成员数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="min">分数最小值</param>
		/// <param name="max">分数最大值</param>
		/// <returns></returns>
		public long ZCount(string key, double min, double max) => ExecuteScalar(key, (c, k) => c.Value.ZCount(k, min, max));
		/// <summary>
		/// 有序集合中对指定成员的分数加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="memeber">成员</param>
		/// <param name="increment">增量值(默认=1)</param>
		/// <returns></returns>
		public double ZIncrBy(string key, string memeber, double increment = 1) => ExecuteScalar(key, (c, k) => c.Value.ZIncrBy(k, increment, memeber));

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
		private long ZInterStore(string destinationKey, RedisAggregate aggregate, params string[] keys) => NodesNotSupport(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.Value.ZInterStore(k.First(), null, aggregate, k.Where((ki, kj) => kj > 0).ToArray()));

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
		private long ZUnionStore(string destinationKey, RedisAggregate aggregate, params string[] keys) => NodesNotSupport(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.Value.ZUnionStore(k.First(), null, aggregate, k.Where((ki, kj) => kj > 0).ToArray()));
		#endregion

		/// <summary>
		/// 通过索引区间返回有序集合成指定区间内的成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public string[] ZRange(string key, long start, long stop) => ExecuteScalar(key, (c, k) => c.Value.ZRange(k, start, stop, false));
		/// <summary>
		/// 通过索引区间返回有序集合成指定区间内的成员
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public T[] ZRange<T>(string key, long start, long stop) => this.DeserializeArrayInternal<T>(ExecuteScalar(key, (c, k) => c.Value.ZRangeBytes(k, start, stop, false)));
		/// <summary>
		/// 通过分数返回有序集合指定区间内的成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public string[] ZRangeByScore(string key, double minScore, double maxScore, long? limit = null, long offset = 0) => ExecuteScalar(key, (c, k) => c.Value.ZRangeByScore(k, minScore, maxScore, false, false, false, offset, limit));
		/// <summary>
		/// 通过分数返回有序集合指定区间内的成员
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public T[] ZRangeByScore<T>(string key, double minScore, double maxScore, long? limit = null, long offset = 0) => this.DeserializeArrayInternal<T>(ExecuteScalar(key, (c, k) => c.Value.ZRangeBytesByScore(k, minScore, maxScore, false, false, false, offset, limit)));
		/// <summary>
		/// 通过分数返回有序集合指定区间内的成员和分数
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public (string member, double score)[] ZRangeByScoreWithScores(string key, double minScore, double maxScore, long? limit = null, long offset = 0) =>
			ExecuteScalar(key, (c, k) => c.Value.ZRangeByScoreWithScores(k, minScore, maxScore, false, false, offset, limit).Select(z => (z.Item1, z.Item2)).ToArray());
		/// <summary>
		/// 通过分数返回有序集合指定区间内的成员和分数
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public (T member, double score)[] ZRangeByScoreWithScores<T>(string key, double minScore, double maxScore, long? limit = null, long offset = 0) =>
			this.DeserializeTuple1Internal<T, double>(ExecuteScalar(key, (c, k) => c.Value.ZRangeBytesByScoreWithScores(k, minScore, maxScore, false, false, offset, limit)));
		/// <summary>
		/// 返回有序集合中指定成员的索引
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public long? ZRank(string key, object member) => ExecuteScalar(key, (c, k) => c.Value.ZRank(k, this.SerializeInternal(member)));
		/// <summary>
		/// 移除有序集合中的一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">一个或多个成员</param>
		/// <returns></returns>
		public long ZRem(string key, params object[] member) => member == null || member.Any() == false ? 0 : ExecuteScalar(key, (c, k) => c.Value.ZRem(k, member?.Select(z => this.SerializeInternal(z)).ToArray()));
		/// <summary>
		/// 移除有序集合中给定的排名区间的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public long ZRemRangeByRank(string key, long start, long stop) => ExecuteScalar(key, (c, k) => c.Value.ZRemRangeByRank(k, start, stop));
		/// <summary>
		/// 移除有序集合中给定的分数区间的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <returns></returns>
		public long ZRemRangeByScore(string key, double minScore, double maxScore) => ExecuteScalar(key, (c, k) => c.Value.ZRemRangeByScore(k, minScore, maxScore));
		/// <summary>
		/// 返回有序集中指定区间内的成员，通过索引，分数从高到底
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public string[] ZRevRange(string key, long start, long stop) => ExecuteScalar(key, (c, k) => c.Value.ZRevRange(k, start, stop, false));
		/// <summary>
		/// 返回有序集中指定区间内的成员，通过索引，分数从高到底
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public T[] ZRevRange<T>(string key, long start, long stop) => this.DeserializeArrayInternal<T>(ExecuteScalar(key, (c, k) => c.Value.ZRevRangeBytes(k, start, stop, false)));
		/// <summary>
		/// 返回有序集中指定分数区间内的成员，分数从高到低排序
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public string[] ZRevRangeByScore(string key, double maxScore, double minScore, long? limit = null, long? offset = 0) => ExecuteScalar(key, (c, k) => c.Value.ZRevRangeByScore(k, maxScore, minScore, false, false, false, offset, limit));
		/// <summary>
		/// 返回有序集中指定分数区间内的成员，分数从高到低排序
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public T[] ZRevRangeByScore<T>(string key, double maxScore, double minScore, long? limit = null, long offset = 0) =>
			this.DeserializeArrayInternal<T>(ExecuteScalar(key, (c, k) => c.Value.ZRevRangeBytesByScore(k, maxScore, minScore, false, false, false, offset, limit)));
		/// <summary>
		/// 返回有序集中指定分数区间内的成员和分数，分数从高到低排序
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public (string member, double score)[] ZRevRangeByScoreWithScores(string key, double maxScore, double minScore, long? limit = null, long offset = 0) =>
			ExecuteScalar(key, (c, k) => c.Value.ZRevRangeByScoreWithScores(k, maxScore, minScore, false, false, offset, limit).Select(z => (z.Item1, z.Item2)).ToArray());
		/// <summary>
		/// 返回有序集中指定分数区间内的成员和分数，分数从高到低排序
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public (T member, double score)[] ZRevRangeByScoreWithScores<T>(string key, double maxScore, double minScore, long? limit = null, long offset = 0) =>
			this.DeserializeTuple1Internal<T, double>(ExecuteScalar(key, (c, k) => c.Value.ZRevRangeBytesByScoreWithScores(k, maxScore, minScore, false, false, offset, limit)));
		/// <summary>
		/// 返回有序集合中指定成员的排名，有序集成员按分数值递减(从大到小)排序
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public long? ZRevRank(string key, object member) => ExecuteScalar(key, (c, k) => c.Value.ZRevRank(k, this.SerializeInternal(member)));
		/// <summary>
		/// 返回有序集中，成员的分数值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public double? ZScore(string key, object member) => ExecuteScalar(key, (c, k) => c.Value.ZScore(k, this.SerializeInternal(member)));
		/// <summary>
		/// 迭代有序集合中的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="cursor">位置</param>
		/// <param name="pattern">模式</param>
		/// <param name="count">数量</param>
		/// <returns></returns>
		public RedisScan<(string member, double score)> ZScan(string key, int cursor, string pattern = null, int? count = null) => ExecuteScalar(key, (c, k) => {
			var scan = c.Value.ZScan(k, cursor, pattern, count);
			return new RedisScan<(string, double)>(scan.Cursor, scan.Items.Select(z => (z.Item1, z.Item2)).ToArray());
		});
		/// <summary>
		/// 迭代有序集合中的元素
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="cursor">位置</param>
		/// <param name="pattern">模式</param>
		/// <param name="count">数量</param>
		/// <returns></returns>
		public RedisScan<(T member, double score)> ZScan<T>(string key, int cursor, string pattern = null, int? count = null) => ExecuteScalar(key, (c, k) => {
			var scan = c.Value.ZScanBytes(k, cursor, pattern, count);
			return new RedisScan<(T, double)>(scan.Cursor, this.DeserializeTuple1Internal<T, double>(scan.Items));
		});
		#endregion

		#region Set
		/// <summary>
		/// 向集合添加一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="members">一个或多个成员</param>
		/// <returns></returns>
		public long SAdd(string key, params object[] members) => members == null || members.Any() == false ? 0 : ExecuteScalar(key, (c, k) => c.Value.SAdd(k, members?.Select(z => this.SerializeInternal(z)).ToArray()));
		/// <summary>
		/// 获取集合的成员数
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long SCard(string key) => ExecuteScalar(key, (c, k) => c.Value.SCard(k));
		/// <summary>
		/// 返回给定所有集合的差集，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public string[] SDiff(params string[] keys) => NodesNotSupport(keys, new string[0], (c, k) => c.Value.SDiff(k));
		/// <summary>
		/// 返回给定所有集合的差集，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public T[] SDiff<T>(params string[] keys) => NodesNotSupport(keys, new T[0], (c, k) => this.DeserializeArrayInternal<T>(c.Value.SDiffBytes(k)));
		/// <summary>
		/// 返回给定所有集合的差集并存储在 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long SDiffStore(string destinationKey, params string[] keys) => NodesNotSupport(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.Value.SDiffStore(k.First(), k.Where((ki, kj) => kj > 0).ToArray()));
		/// <summary>
		/// 返回给定所有集合的交集，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public string[] SInter(params string[] keys) => NodesNotSupport(keys, new string[0], (c, k) => c.Value.SInter(k));
		/// <summary>
		/// 返回给定所有集合的交集，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public T[] SInter<T>(params string[] keys) => NodesNotSupport(keys, new T[0], (c, k) => this.DeserializeArrayInternal<T>(c.Value.SInterBytes(k)));
		/// <summary>
		/// 返回给定所有集合的交集并存储在 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long SInterStore(string destinationKey, params string[] keys) => NodesNotSupport(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.Value.SInterStore(k.First(), k.Where((ki, kj) => kj > 0).ToArray()));
		/// <summary>
		/// 判断 member 元素是否是集合 key 的成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public bool SIsMember(string key, object member) => ExecuteScalar(key, (c, k) => c.Value.SIsMember(k, this.SerializeInternal(member)));
		/// <summary>
		/// 返回集合中的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string[] SMembers(string key) => ExecuteScalar(key, (c, k) => c.Value.SMembers(k));
		/// <summary>
		/// 返回集合中的所有成员
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public T[] SMembers<T>(string key) => this.DeserializeArrayInternal<T>(ExecuteScalar(key, (c, k) => c.Value.SMembersBytes(k)));
		/// <summary>
		/// 将 member 元素从 source 集合移动到 destination 集合
		/// </summary>
		/// <param name="sourceKey">无序集合key，不含prefix前辍</param>
		/// <param name="destinationKey">目标无序集合key，不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public bool SMove(string sourceKey, string destinationKey, object member) {
			string rule = string.Empty;
			if (Nodes.Count > 1) {
				var rule1 = NodeRule(sourceKey);
				var rule2 = NodeRule(destinationKey);
				if (rule1 != rule2) {
					if (SRem(sourceKey, member) <= 0) return false;
					return SAdd(destinationKey, member) > 0;
				}
				rule = rule1;
			}
			var pool = Nodes.TryGetValue(rule, out var b) ? b : Nodes.First().Value;
			var key1 = string.Concat(pool.Prefix, sourceKey);
			var key2 = string.Concat(pool.Prefix, destinationKey);
			return GetConnectionAndExecute(pool, conn => conn.Value.SMove(key1, key2, this.SerializeInternal(member)));
		}
		/// <summary>
		/// 移除并返回集合中的一个随机元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string SPop(string key) => ExecuteScalar(key, (c, k) => c.Value.SPop(k));
		/// <summary>
		/// 返回集合中的一个随机元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string SRandMember(string key) => ExecuteScalar(key, (c, k) => c.Value.SRandMember(k));
		/// <summary>
		/// 返回集合中的一个随机元素
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public T SRandMember<T>(string key) => this.DeserializeInternal<T>(ExecuteScalar(key, (c, k) => c.Value.SRandMemberBytes(k)));
		/// <summary>
		/// 返回集合中一个或多个随机数的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="count">返回个数</param>
		/// <returns></returns>
		public string[] SRandMembers(string key, int count = 1) => ExecuteScalar(key, (c, k) => c.Value.SRandMembers(k, count));
		/// <summary>
		/// 返回集合中一个或多个随机数的元素
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="count">返回个数</param>
		/// <returns></returns>
		public T[] SRandMembers<T>(string key, int count = 1) => this.DeserializeArrayInternal<T>(ExecuteScalar(key, (c, k) => c.Value.SRandMembersBytes(k, count)));
		/// <summary>
		/// 移除集合中一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="members">一个或多个成员</param>
		/// <returns></returns>
		public long SRem(string key, params object[] members) => members == null || members.Any() == false ? 0 : ExecuteScalar(key, (c, k) => c.Value.SRem(k, members?.Select(z => this.SerializeInternal(z)).ToArray()));
		/// <summary>
		/// 返回所有给定集合的并集，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public string[] SUnion(params string[] keys) => NodesNotSupport(keys, new string[0], (c, k) => c.Value.SUnion(k));
		/// <summary>
		/// 返回所有给定集合的并集，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public T[] SUnion<T>(params string[] keys) => NodesNotSupport(keys, new T[0], (c, k) => this.DeserializeArrayInternal<T>(c.Value.SUnionBytes(k)));
		/// <summary>
		/// 所有给定集合的并集存储在 destination 集合中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long SUnionStore(string destinationKey, params string[] keys) => NodesNotSupport(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.Value.SUnionStore(k.First(), k.Where((ki, kj) => kj > 0).ToArray()));
		/// <summary>
		/// 迭代集合中的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="cursor">位置</param>
		/// <param name="pattern">模式</param>
		/// <param name="count">数量</param>
		/// <returns></returns>
		public RedisScan<string> SScan(string key, int cursor, string pattern = null, int? count = null) => ExecuteScalar(key, (c, k) => c.Value.SScan(k, cursor, pattern, count));
		/// <summary>
		/// 迭代集合中的元素
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="cursor">位置</param>
		/// <param name="pattern">模式</param>
		/// <param name="count">数量</param>
		/// <returns></returns>
		public RedisScan<T> SScan<T>(string key, int cursor, string pattern = null, int? count = null) => ExecuteScalar(key, (c, k) => {
			var scan = c.Value.SScanBytes(k, cursor, pattern, count);
			return new RedisScan<T>(scan.Cursor, this.DeserializeArrayInternal<T>(scan.Items));
		});
		#endregion

		#region List
		/// <summary>
		/// 它是 LPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BLPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="timeout">超时(秒)</param>
		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
		/// <returns></returns>
		public string BLPop(int timeout, params string[] keys) => NodesNotSupport(keys, null, (c, k) => c.Value.BLPop(timeout, k));
		/// <summary>
		/// 它是 LPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BLPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="timeout">超时(秒)</param>
		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
		/// <returns></returns>
		public (string key, string value)? BLPopWithKey(int timeout, params string[] keys) {
			string[] rkeys = null;
			var tuple = NodesNotSupport(keys, null, (c, k) => c.Value.BLPopWithKey(timeout, rkeys = k));
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
		/// <param name="timeout">超时(秒)</param>
		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
		/// <returns></returns>
		public string BRPop(int timeout, params string[] keys) => NodesNotSupport(keys, null, (c, k) => c.Value.BRPop(timeout, k));
		/// <summary>
		/// 它是 RPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BRPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="timeout">超时(秒)</param>
		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
		/// <returns></returns>
		public (string key, string value)? BRPopWithKey(int timeout, params string[] keys) {
			string[] rkeys = null;
			var tuple = NodesNotSupport(keys, null, (c, k) => c.Value.BRPopWithKey(timeout, rkeys = k));
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
		public string LIndex(string key, long index) => ExecuteScalar(key, (c, k) => c.Value.LIndex(k, index));
		/// <summary>
		/// 在列表的元素前面插入元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="pivot">列表的元素</param>
		/// <param name="value">新元素</param>
		/// <returns></returns>
		public long LInsertBefore(string key, string pivot, object value) => ExecuteScalar(key, (c, k) => c.Value.LInsert(k, RedisInsert.Before, pivot, this.SerializeInternal(value)));
		/// <summary>
		/// 在列表的元素后面插入元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="pivot">列表的元素</param>
		/// <param name="value">新元素</param>
		/// <returns></returns>
		public long LInsertAfter(string key, string pivot, object value) => ExecuteScalar(key, (c, k) => c.Value.LInsert(k, RedisInsert.After, pivot, this.SerializeInternal(value)));
		/// <summary>
		/// 获取列表长度
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long LLen(string key) => ExecuteScalar(key, (c, k) => c.Value.LLen(k));
		/// <summary>
		/// 移出并获取列表的第一个元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string LPop(string key) => ExecuteScalar(key, (c, k) => c.Value.LPop(k));
		/// <summary>
		/// 移除并获取列表最后一个元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string RPop(string key) => ExecuteScalar(key, (c, k) => c.Value.RPop(k));
		/// <summary>
		/// 将一个或多个值插入到列表头部
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">一个或多个值</param>
		/// <returns>执行 LPUSH 命令后，列表的长度</returns>
		public long LPush(string key, params object[] value) => value == null || value.Any() == false ? 0 : ExecuteScalar(key, (c, k) => c.Value.LPush(k, value?.Select(z => this.SerializeInternal(z)).ToArray()));
		/// <summary>
		/// 将一个值插入到已存在的列表头部
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">值</param>
		/// <returns>执行 LPUSHX 命令后，列表的长度。</returns>
		public long LPushX(string key, object value) => ExecuteScalar(key, (c, k) => c.Value.LPushX(k, this.SerializeInternal(value)));
		/// <summary>
		/// 在列表中添加一个或多个值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">一个或多个值</param>
		/// <returns>执行 RPUSH 命令后，列表的长度</returns>
		public long RPush(string key, params object[] value) => value == null || value.Any() == false ? 0 : ExecuteScalar(key, (c, k) => c.Value.RPush(k, value?.Select(z => this.SerializeInternal(z)).ToArray()));
		/// <summary>
		/// 为已存在的列表添加值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">一个或多个值</param>
		/// <returns>执行 RPUSHX 命令后，列表的长度</returns>
		public long RPushX(string key, object value) => ExecuteScalar(key, (c, k) => c.Value.RPushX(k, this.SerializeInternal(value)));
		/// <summary>
		/// 获取列表指定范围内的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public string[] LRang(string key, long start, long stop) => ExecuteScalar(key, (c, k) => c.Value.LRange(k, start, stop));
		/// <summary>
		/// 根据参数 count 的值，移除列表中与参数 value 相等的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="count">移除的数量，大于0时从表头删除数量count，小于0时从表尾删除数量-count，等于0移除所有</param>
		/// <param name="value">元素</param>
		/// <returns></returns>
		public long LRem(string key, long count, object value) => ExecuteScalar(key, (c, k) => c.Value.LRem(k, count, this.SerializeInternal(value)));
		/// <summary>
		/// 通过索引设置列表元素的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="index">索引</param>
		/// <param name="value">值</param>
		/// <returns></returns>
		public bool LSet(string key, long index, object value) => ExecuteScalar(key, (c, k) => c.Value.LSet(k, index, this.SerializeInternal(value))) == "OK";
		/// <summary>
		/// 对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public bool LTrim(string key, long start, long stop) => ExecuteScalar(key, (c, k) => c.Value.LTrim(k, start, stop)) == "OK";
		#endregion

		#region Hash
		/// <summary>
		/// 将哈希表 key 中的字段 field 的值设为 value
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <param name="value">值</param>
		/// <returns>如果字段是哈希表中的一个新建字段，并且值设置成功，返回true。如果哈希表中域字段已经存在且旧值已被新值覆盖，返回false。</returns>
		public bool HSet(string key, string field, object value) => ExecuteScalar(key, (c, k) => c.Value.HSet(k, field, this.SerializeInternal(value)));
		/// <summary>
		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="keyValues">字段-值 元组数组</param>
		/// <returns></returns>
		public bool HMSet(string key, params (string field, object value)[] keyValues) => keyValues == null || keyValues.Any() == false ? false : ExecuteScalar(key, (c, k) => c.Value.HMSet(k, this.GetKeyValues(keyValues))) == "OK";
		/// <summary>
		/// 只有在字段 field 不存在时，设置哈希表字段的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <param name="value">值(string 或 byte[])</param>
		/// <returns></returns>
		public bool HSetNx(string key, string field, object value) => ExecuteScalar(key, (c, k) => c.Value.HSetNx(k, field, this.SerializeInternal(value)));
		/// <summary>
		/// 获取存储在哈希表中指定字段的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		public string HGet(string key, string field) => ExecuteScalar(key, (c, k) => c.Value.HGet(k, field));
		/// <summary>
		/// 获取存储在哈希表中指定字段的值
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		public T HGet<T>(string key, string field) => this.DeserializeInternal<T>(ExecuteScalar(key, (c, k) => c.Value.HGetBytes(k, field)));
		/// <summary>
		/// 获取存储在哈希表中多个字段的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="fields">字段</param>
		/// <returns></returns>
		public string[] HMGet(string key, params string[] fields) => fields == null || fields.Any() == false ? new string[0] : ExecuteScalar(key, (c, k) => c.Value.HMGet(k, fields));
		/// <summary>
		/// 获取存储在哈希表中多个字段的值
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="fields">一个或多个字段</param>
		/// <returns></returns>
		public T[] HMGet<T>(string key, params string[] fields) => fields == null || fields.Any() == false ? new T[0] : this.DeserializeArrayInternal<T>(ExecuteScalar(key, (c, k) => c.Value.HMGetBytes(k, fields)));
		/// <summary>
		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public long HIncrBy(string key, string field, long value = 1) => ExecuteScalar(key, (c, k) => c.Value.HIncrBy(k, field, value));
		/// <summary>
		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public double HIncrByFloat(string key, string field, double value = 1) => ExecuteScalar(key, (c, k) => c.Value.HIncrByFloat(k, field, value));
		/// <summary>
		/// 删除一个或多个哈希表字段
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="fields">字段</param>
		/// <returns></returns>
		public long HDel(string key, params string[] fields) => fields == null || fields.Any() == false ? 0 : ExecuteScalar(key, (c, k) => c.Value.HDel(k, fields));
		/// <summary>
		/// 查看哈希表 key 中，指定的字段是否存在
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		public bool HExists(string key, string field) => ExecuteScalar(key, (c, k) => c.Value.HExists(k, field));
		/// <summary>
		/// 获取哈希表中字段的数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long HLen(string key) => ExecuteScalar(key, (c, k) => c.Value.HLen(k));
		/// <summary>
		/// 获取在哈希表中指定 key 的所有字段和值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Dictionary<string, string> HGetAll(string key) => ExecuteScalar(key, (c, k) => c.Value.HGetAll(k));
		/// <summary>
		/// 获取所有哈希表中的字段
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string[] HKeys(string key) => ExecuteScalar(key, (c, k) => c.Value.HKeys(k));
		/// <summary>
		/// 获取哈希表中所有值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string[] HVals(string key) => ExecuteScalar(key, (c, k) => c.Value.HVals(k));
		/// <summary>
		/// 迭代哈希表中的键值对
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="cursor">位置</param>
		/// <param name="pattern">模式</param>
		/// <param name="count">数量</param>
		/// <returns></returns>
		public RedisScan<(string field, string value)> HScan(string key, int cursor, string pattern = null, int? count = null) => ExecuteScalar(key, (c, k) => {
			var scan = c.Value.HScan(k, cursor, pattern, count);
			return new RedisScan<(string, string)>(scan.Cursor, scan.Items.Select(z => (z.Item1, z.Item2)).ToArray());
		});
		#endregion

		#region String
		/// <summary>
		/// 设置指定 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字符串、byte[]、对象</param>
		/// <param name="expireSeconds">过期(秒单位)</param>
		/// <param name="exists">Nx, Xx</param>
		/// <returns></returns>
		public bool Set(string key, object value, int expireSeconds = -1, RedisExistence? exists = null) {
			object redisValule = this.SerializeInternal(value);
			if (expireSeconds <= 0 && exists == null) return ExecuteScalar(key, (c, k) => c.Value.Set(k, redisValule)) == "OK";
			if (expireSeconds <= 0 && exists != null) return ExecuteScalar(key, (c, k) => c.Value.Set(k, redisValule, null, exists)) == "OK";
			if (expireSeconds > 0 && exists == null) return ExecuteScalar(key, (c, k) => c.Value.Set(k, redisValule, expireSeconds, null)) == "OK";
			if (expireSeconds > 0 && exists != null) return ExecuteScalar(key, (c, k) => c.Value.Set(k, redisValule, expireSeconds, exists)) == "OK";
			return false;
		}
		/// <summary>
		/// 只有在 key 不存在时设置 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字符串、byte[]、对象</param>
		/// <returns></returns>
		public bool SetNx(string key, object value) => ExecuteScalar(key, (c, k) => c.Value.SetNx(k, this.SerializeInternal(value)));
		/// <summary>
		/// 同时设置一个或多个 key-value 对
		/// </summary>
		/// <param name="keyValues">字段-值 元组数组</param>
		/// <returns></returns>
		public bool MSet(params (string key, object value)[] keyValues) => MSetInternal(RedisExistence.Xx, keyValues);
		/// <summary>
		/// 同时设置一个或多个 key-value 对，当且仅当所有给定 key 都不存在。警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="keyValues">字段-值 元组数组</param>
		/// <returns></returns>
		public bool MSetNx(params (string key, object value)[] keyValues) => MSetInternal(RedisExistence.Nx, keyValues);
		internal bool MSetInternal(RedisExistence exists, params (string key, object value)[] keyValues) {
			if (keyValues == null || keyValues.Any() == false) return false;
			var dic = new Dictionary<string, object>();
			foreach (var kv in keyValues) {
				if (dic.ContainsKey(kv.key)) dic[kv.key] = kv.value;
				else dic.Add(kv.key, kv.value);
			}
			Func<Object<RedisClient>, string[], long> handle = (c, k) => {
				var prefix = (c.Pool as RedisClientPool)?.Prefix;
				var parms = new object[k.Length * 2];
				for (var a = 0; a < k.Length; a++) {
					parms[a * 2] = k[a];
					parms[a * 2 + 1] = this.SerializeInternal(dic[string.IsNullOrEmpty(prefix) ? k[a] : k[a].Substring(prefix.Length)]);
				}
				if (exists == RedisExistence.Nx) return c.Value.MSetNx(parms) ? 1 : 0;
				return c.Value.MSet(parms) == "OK" ? 1 : 0;
			};
			if (exists == RedisExistence.Nx) return NodesNotSupport(dic.Keys.ToArray(), 0, handle) > 0;
			return ExecuteNonQuery(dic.Keys.ToArray(), handle) > 0;
		}
		/// <summary>
		/// 获取指定 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string Get(string key) => ExecuteScalar(key, (c, k) => c.Value.Get(k));
		/// <summary>
		/// 获取指定 key 的值
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public T Get<T>(string key) => this.DeserializeInternal<T>(ExecuteScalar(key, (c, k) => c.Value.GetBytes(k)));
		/// <summary>
		/// 获取多个指定 key 的值(数组)
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public string[] MGet(params string[] keys) => ExeucteArray(keys, (c, k) => c.Value.MGet(k));
		/// <summary>
		/// 获取多个指定 key 的值(数组)
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public T[] MGet<T>(params string[] keys) => this.DeserializeArrayInternal<T>(ExeucteArray(keys, (c, k) => c.Value.MGetBytes(k)));
		/// <summary>
		/// 返回 key 中字符串值的子字符
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string GetRange(string key, long start, long end) => ExecuteScalar(key, (c, k) => c.Value.GetRange(k, start, end));
		/// <summary>
		/// 用 value 参数覆写给定 key 所储存的字符串值，从偏移量 offset 开始
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="offset">偏移量</param>
		/// <param name="value">值</param>
		/// <returns>被修改后的字符串长度</returns>
		public long SetRange(string key, uint offset, object value) => ExecuteScalar(key, (c, k) => c.Value.SetRange(k, offset, this.SerializeInternal(value)));
		/// <summary>
		/// 将给定 key 的值设为 value
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字符串</param>
		/// <returns></returns>
		public string GetSet(string key, object value) => ExecuteScalar(key, (c, k) => c.Value.GetSet(k, this.SerializeInternal(value)));
		/// <summary>
		/// 将给定 key 的值设为 value ，并返回 key 的旧值(old value)
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字符串、byte[]、对象</param>
		/// <returns></returns>
		public T GetSet<T>(string key, object value) => this.DeserializeInternal<T>(ExecuteScalar(key, (c, k) => c.Value.GetSetBytes(k, this.SerializeInternal(value))));
		/// <summary>
		/// 对 key 所储存的字符串值，获取指定偏移量上的位(bit)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="offset">偏移量</param>
		/// <returns></returns>
		public bool GetBit(string key, uint offset) => ExecuteScalar(key, (c, k) => c.Value.GetBit(k, offset));
		/// <summary>
		/// 对 key 所储存的字符串值，设置或清除指定偏移量上的位(bit)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="offset">偏移量</param>
		/// <param name="value">值</param>
		/// <returns></returns>
		public bool SetBit(string key, uint offset, bool value) => ExecuteScalar(key, (c, k) => c.Value.SetBit(k, offset, value));
		/// <summary>
		/// 计算给定字符串中，被设置为 1 的比特位的数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置</param>
		/// <param name="end">结束位置</param>
		/// <returns></returns>
		public long BitCount(string key, long start, long end) => ExecuteScalar(key, (c, k) => c.Value.BitCount(k, start, end));
		/// <summary>
		/// 返回 key 所储存的字符串值的长度
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long StrLen(string key) => ExecuteScalar(key, (c, k) => c.Value.StrLen(k));
		/// <summary>
		/// 将 key 所储存的值加上给定的增量值（increment）
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public long IncrBy(string key, long value = 1) => ExecuteScalar(key, (c, k) => c.Value.IncrBy(k, value));
		/// <summary>
		/// 将 key 所储存的值加上给定的浮点增量值（increment）
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public double IncrBy(string key, double value = 1) => ExecuteScalar(key, (c, k) => c.Value.IncrByFloat(k, value));
		/// <summary>
		/// 如果 key 已经存在并且是一个字符串， APPEND 命令将指定的 value 追加到该 key 原来值（value）的末尾
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字符串</param>
		/// <returns>追加指定值之后， key 中字符串的长度</returns>
		public long Append(string key, object value) => ExecuteScalar(key, (c, k) => c.Value.Append(k, this.SerializeInternal(value)));
		#endregion

		#region Key
		/// <summary>
		/// 用于在 key 存在时删除 key
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long Del(params string[] key) => ExecuteNonQuery(key, (c, k) => c.Value.Del(k));
		/// <summary>
		/// 序列化给定 key ，并返回被序列化的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public byte[] Dump(string key) => ExecuteScalar(key, (c, k) => c.Value.Dump(k));
		/// <summary>
		/// 检查给定 key 是否存在
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public bool Exists(string key) => ExecuteScalar(key, (c, k) => c.Value.Exists(k));
		/// <summary>
		/// 为给定 key 设置过期时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="seconds">过期秒数</param>
		/// <returns></returns>
		public bool Expire(string key, int seconds) => ExecuteScalar(key, (c, k) => c.Value.Expire(k, seconds));
		/// <summary>
		/// 为给定 key 设置过期时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="expire">过期时间</param>
		/// <returns></returns>
		public bool Expire(string key, TimeSpan expire) => ExecuteScalar(key, (c, k) => c.Value.Expire(k, expire));
		/// <summary>
		/// 为给定 key 设置过期时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="expire">过期时间</param>
		/// <returns></returns>
		public bool ExpireAt(string key, DateTime expire) => ExecuteScalar(key, (c, k) => c.Value.ExpireAt(k, expire));
		/// <summary>
		/// 查找所有分区节点中符合给定模式(pattern)的 key
		/// </summary>
		/// <param name="pattern">如：runoob*</param>
		/// <returns></returns>
		public string[] Keys(string pattern) {
			List<string> ret = new List<string>();
			foreach (var pool in Nodes)
				ret.AddRange(GetConnectionAndExecute(pool.Value, conn => conn.Value.Keys(pattern)));
			return ret.ToArray();
		}
		/// <summary>
		/// 将当前数据库的 key 移动到给定的数据库 db 当中
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="database">数据库</param>
		/// <returns></returns>
		public bool Move(string key, int database) => ExecuteScalar(key, (c, k) => c.Value.Move(k, database));
		/// <summary>
		/// 该返回给定 key 锁储存的值所使用的内部表示(representation)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string ObjectEncoding(string key) => ExecuteScalar(key, (c, k) => c.Value.ObjectEncoding(k));
		/// <summary>
		/// 该返回给定 key 引用所储存的值的次数。此命令主要用于除错
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long? ObjectRefCount(string key) => ExecuteScalar(key, (c, k) => c.Value.Object(RedisObjectSubCommand.RefCount, k));
		/// <summary>
		/// 返回给定 key 自储存以来的空转时间(idle， 没有被读取也没有被写入)，以秒为单位
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long? ObjectIdleTime(string key) => ExecuteScalar(key, (c, k) => c.Value.Object(RedisObjectSubCommand.IdleTime, k));
		/// <summary>
		/// 移除 key 的过期时间，key 将持久保持
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public bool Persist(string key) => ExecuteScalar(key, (c, k) => c.Value.Persist(k));
		/// <summary>
		/// 为给定 key 设置过期时间（毫秒）
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="milliseconds">过期毫秒数</param>
		/// <returns></returns>
		public bool PExpire(string key, int milliseconds) => ExecuteScalar(key, (c, k) => c.Value.PExpire(k, milliseconds));
		/// <summary>
		/// 为给定 key 设置过期时间（毫秒）
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="expire">过期时间</param>
		/// <returns></returns>
		public bool PExpire(string key, TimeSpan expire) => ExecuteScalar(key, (c, k) => c.Value.PExpire(k, expire));
		/// <summary>
		/// 为给定 key 设置过期时间（毫秒）
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="expire">过期时间</param>
		/// <returns></returns>
		public bool PExpireAt(string key, DateTime expire) => ExecuteScalar(key, (c, k) => c.Value.PExpireAt(k, expire));
		/// <summary>
		/// 以毫秒为单位返回 key 的剩余的过期时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long PTtl(string key) => ExecuteScalar(key, (c, k) => c.Value.PTtl(k));
		/// <summary>
		/// 从所有节点中随机返回一个 key
		/// </summary>
		/// <returns></returns>
		public string RandomKey() => GetConnectionAndExecute(Nodes[NodeKeys[_rnd.Next(0, NodeKeys.Count)]], c => c.Value.RandomKey());
		/// <summary>
		/// 修改 key 的名称
		/// </summary>
		/// <param name="key">旧名称，不含prefix前辍</param>
		/// <param name="newKey">新名称，不含prefix前辍</param>
		/// <returns></returns>
		public bool Rename(string key, string newKey) {
			string rule = string.Empty;
			if (Nodes.Count > 1) {
				var rule1 = NodeRule(key);
				var rule2 = NodeRule(newKey);
				if (rule1 != rule2) {
					var ret = PipeLine(a => a.Get<byte[]>(key).Del(key));
					int.TryParse(ret[1]?.ToString(), out var tryint);
					if (tryint <= 0) return false;
					return Set(newKey, ret[0]);
				}
				rule = rule1;
			}
			var pool = Nodes.TryGetValue(rule, out var b) ? b : Nodes.First().Value;
			var key1 = string.Concat(pool.Prefix, key);
			var key2 = string.Concat(pool.Prefix, newKey);
			return GetConnectionAndExecute(pool, conn => conn.Value.Rename(key1, key2)) == "OK";
		}
		/// <summary>
		/// 修改 key 的名称。警告：群集模式下，若key、newKey分散在多个节点时，将报错
		/// </summary>
		/// <param name="key">旧名称，不含prefix前辍</param>
		/// <param name="newKey">新名称，不含prefix前辍</param>
		/// <returns></returns>
		public bool RenameNx(string key, string newKey) => NodesNotSupport(new[] { key, newKey }, false, (c, k) => c.Value.RenameNx(k.First(), k.Last()));
		/// <summary>
		/// 反序列化给定的序列化值，并将它和给定的 key 关联
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="serializedValue">序列化值</param>
		/// <returns></returns>

		public bool Restore(string key, byte[] serializedValue) => ExecuteScalar(key, (c, k) => c.Value.Restore(k, 0, serializedValue)) == "OK";
		/// <summary>
		/// 反序列化给定的序列化值，并将它和给定的 key 关联
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="ttlMilliseconds">毫秒为单位为 key 设置生存时间</param>
		/// <param name="serializedValue">序列化值</param>
		/// <returns></returns>

		public bool Restore(string key, long ttlMilliseconds, byte[] serializedValue) => ExecuteScalar(key, (c, k) => c.Value.Restore(k, ttlMilliseconds, serializedValue)) == "OK";
		/// <summary>
		/// 返回给定列表、集合、有序集合 key 中经过排序的元素，参数资料：http://doc.redisfans.com/key/sort.html
		/// </summary>
		/// <param name="key">列表、集合、有序集合，不含prefix前辍</param>
		/// <param name="offset">偏移量</param>
		/// <param name="count">数量</param>
		/// <param name="by">排序字段</param>
		/// <param name="dir">排序方式</param>
		/// <param name="isAlpha">对字符串或数字进行排序</param>
		/// <param name="get">根据排序的结果来取出相应的键值</param>
		/// <returns></returns>
		public string[] Sort(string key, long? offset = null, long? count = null, string by = null, RedisSortDir? dir = null, bool? isAlpha = null, params string[] get) {
			if (Nodes.Count > 1) throw new Exception("Sort 功能在多节点分区模式下不可用。");
			return ExecuteScalar(key, (c, k) => c.Value.Sort(k, offset, count, by, dir, isAlpha, get));
		}
		/// <summary>
		/// 保存给定列表、集合、有序集合 key 中经过排序的元素，参数资料：http://doc.redisfans.com/key/sort.html
		/// </summary>
		/// <param name="key">列表、集合、有序集合，不含prefix前辍</param>
		/// <param name="destinationKey">目标key，不含prefix前辍</param>
		/// <param name="offset">偏移量</param>
		/// <param name="count">数量</param>
		/// <param name="by">排序字段</param>
		/// <param name="dir">排序方式</param>
		/// <param name="isAlpha">对字符串或数字进行排序</param>
		/// <param name="get">根据排序的结果来取出相应的键值</param>
		/// <returns></returns>
		public long SortAndStore(string key, string destinationKey, long? offset = null, long? count = null, string by = null, RedisSortDir? dir = null, bool? isAlpha = null, params string[] get) {
			if (Nodes.Count > 1) throw new Exception("SortAndStore 功能在多节点分区模式下不可用。");
			return ExecuteScalar(key, (c, k) => c.Value.SortAndStore(k, (c.Pool as RedisClientPool)?.Prefix + destinationKey, offset, count, by, dir, isAlpha, get));
		}
		/// <summary>
		/// 以秒为单位，返回给定 key 的剩余生存时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long Ttl(string key) => ExecuteScalar(key, (c, k) => c.Value.Ttl(k));
		/// <summary>
		/// 返回 key 所储存的值的类型
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public KeyType Type(string key) => Enum.TryParse(ExecuteScalar(key, (c, k) => c.Value.Type(k)), out KeyType tryenum) ? tryenum : KeyType.Unkown;
		public enum KeyType { Unkown, None, String, List, Set, ZSet, Hash }
		/// <summary>
		/// 迭代当前数据库中的数据库键
		/// </summary>
		/// <param name="cursor">位置</param>
		/// <param name="pattern">模式</param>
		/// <param name="count">数量</param>
		/// <returns></returns>
		public RedisScan<string> Scan(int cursor, string pattern = null, int? count = null) {
			if (Nodes.Count > 1) throw new Exception("Scan 功能在多节点分区模式下不可用。");
			return ExecuteScalar("Scan", (c, k) => c.Value.Scan(cursor, pattern, count));
		}
		/// <summary>
		/// 迭代当前数据库中的数据库键
		/// </summary>
		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="cursor">位置</param>
		/// <param name="pattern">模式</param>
		/// <param name="count">数量</param>
		/// <returns></returns>
		public RedisScan<T> Scan<T>(string key, int cursor, string pattern = null, int? count = null) => ExecuteScalar(key, (c, k) => {
			var scan = c.Value.SScanBytes(k, cursor, pattern, count);
			return new RedisScan<T>(scan.Cursor, this.DeserializeArrayInternal<T>(scan.Items));
		});
		#endregion

		/// <summary>
		/// 开启分布式锁，若超时返回null
		/// </summary>
		/// <param name="name">锁名称</param>
		/// <param name="timeoutSeconds">超时（秒）</param>
		/// <returns></returns>
		public CSRedisClientLock Lock(string name, int timeoutSeconds) {
			name = $"CSRedisClientLock:{name}";
			var startTime = DateTime.Now;
			while (DateTime.Now.Subtract(startTime).TotalSeconds < timeoutSeconds) {
				if (this.SetNx(name, "1") == true) {
					this.Expire(name, TimeSpan.FromSeconds(timeoutSeconds));
					return new CSRedisClientLock { Name = name, _client = this };
				}
				Thread.CurrentThread.Join(3);
			}
			return null;
		}
	}

	public class CSRedisClientLock : IDisposable {

		internal string Name { get; set; }
		internal CSRedisClient _client;

		/// <summary>
		/// 释放分布式锁
		/// </summary>
		public void Unlock() => _client.Del(this.Name);

		public void Dispose() {
			this.Unlock();
		}
	}
}
