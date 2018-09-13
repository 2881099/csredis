using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CSRedis {
	partial class CSRedisClient {
		/// <summary>
		/// 缓存壳
		/// </summary>
		/// <typeparam name="T">缓存类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="timeoutSeconds">缓存秒数</param>
		/// <param name="getDataAsync">获取源数据的函数</param>
		/// <param name="serialize">序列化函数</param>
		/// <param name="deserialize">反序列化函数</param>
		/// <returns></returns>
		async public Task<T> CacheShellAsync<T>(string key, int timeoutSeconds, Func<Task<T>> getDataAsync, Func<T, string> serialize, Func<string, T> deserialize) {
			if (timeoutSeconds <= 0) return await getDataAsync();
			var cacheValue = await GetAsync(key);
			if (cacheValue != null) {
				try {
					return deserialize(cacheValue);
				} catch {
					await RemoveAsync(key);
					throw;
				}
			}
			var ret = await getDataAsync();
			await SetAsync(key, serialize(ret), timeoutSeconds);
			return ret;
		}
		/// <summary>
		/// 缓存壳(哈希表)
		/// </summary>
		/// <typeparam name="T">缓存类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <param name="timeoutSeconds">缓存秒数</param>
		/// <param name="getDataAsync">获取源数据的函数</param>
		/// <param name="serialize">序列化函数</param>
		/// <param name="deserialize">反序列化函数</param>
		/// <returns></returns>
		async public Task<T> CacheShellAsync<T>(string key, string field, int timeoutSeconds, Func<Task<T>> getDataAsync, Func<(T, long), string> serialize, Func<string, (T, long)> deserialize) {
			if (timeoutSeconds <= 0) return await getDataAsync();
			var cacheValue = await HashGetAsync(key, field);
			if (cacheValue != null) {
				try {
					var value = deserialize(cacheValue);
					if (DateTime.Now.Subtract(dt1970.AddSeconds(value.Item2)).TotalSeconds <= timeoutSeconds) return value.Item1;
				} catch {
					await HashDeleteAsync(key, field);
					throw;
				}
			}
			var ret = await getDataAsync();
			await HashSetAsync(key, field, serialize((ret, (long)DateTime.Now.Subtract(dt1970).TotalSeconds)));
			return ret;
		}
		/// <summary>
		/// 缓存壳(哈希表)，将 fields 每个元素存储到单独的缓存片，实现最大化复用
		/// </summary>
		/// <typeparam name="T">缓存类型</typeparam>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="fields">字段</param>
		/// <param name="timeoutSeconds">缓存秒数</param>
		/// <param name="getDataAsync">获取源数据的函数，输入参数是没有缓存的 fields，返回值应该是 (field, value)[]</param>
		/// <param name="serialize">序列化函数</param>
		/// <param name="deserialize">反序列化函数</param>
		/// <returns></returns>
		async public Task<T[]> CacheShellAsync<T>(string key, string[] fields, int timeoutSeconds, Func<string[], Task<(string, T)[]>> getDataAsync, Func<(T, long), string> serialize, Func<string, (T, long)> deserialize) {
			fields = fields?.Distinct().ToArray();
			if (fields == null || fields.Length == 0) return new T[0];
			if (timeoutSeconds <= 0) return (await getDataAsync(fields)).Select(a => a.Item2).ToArray();

			var ret = new T[fields.Length];
			var cacheValue = await HashMGetAsync(key, fields);
			var fieldsMGet = new Dictionary<string, int>();

			for (var a = 0; a < cacheValue.Length; a++) {
				if (cacheValue[a] != null) {
					try {
						var value = deserialize(cacheValue[a]);
						if (DateTime.Now.Subtract(dt1970.AddSeconds(value.Item2)).TotalSeconds <= timeoutSeconds) {
							ret[a] = value.Item1;
							continue;
						}
					} catch {
						await HashDeleteAsync(key, fields[a]);
						throw;
					}
				}
				fieldsMGet.Add(fields[a], a);
			}

			if (fieldsMGet.Any()) {
				var getDataIntput = fieldsMGet.Keys.ToArray();
				var data = await getDataAsync(getDataIntput);
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
				if (mset.Any()) await HashSetAsync(key, mset);
			}
			return ret.ToArray();
		}

		#region 集群方式 Execute
		async private Task<T> ExecuteScalarAsync<T>(string key, Func<RedisClient, string, Task<T>> hander) {
			if (key == null) return default(T);
			var pool = _clusterRule == null || ClusterNodes.Count == 1 ? ClusterNodes.First().Value : (ClusterNodes.TryGetValue(_clusterRule(key), out var b) ? b : ClusterNodes.First().Value);
			key = string.Concat(pool.Prefix, key);
			using (var conn = await pool.GetConnectionAsync()) {
				return await hander(conn.Client, key);
			}
		}
		async private Task<T[]> ExeucteArrayAsync<T>(string[] key, Func<RedisClient, string[], Task<T[]>> hander) {
			if (key == null || key.Any() == false) return new T[0];
			if (_clusterRule == null || ClusterNodes.Count == 1) {
				var pool = ClusterNodes.First().Value;
				var keys = key.Select(a => string.Concat(pool.Prefix, a)).ToArray();
				using (var conn = await pool.GetConnectionAsync()) {
					return await hander(conn.Client, keys);
				}
			}
			var rules = new Dictionary<string, List<(string, int)>>();
			for (var a = 0; a < key.Length; a++) {
				var rule = _clusterRule(key[a]);
				if (rules.ContainsKey(rule)) rules[rule].Add((key[a], a));
				else rules.Add(rule, new List<(string, int)> { (key[a], a) });
			}
			T[] ret = new T[key.Length];
			foreach (var r in rules) {
				var pool = ClusterNodes.TryGetValue(r.Key, out var b) ? b : ClusterNodes.First().Value;
				var keys = r.Value.Select(a => string.Concat(pool.Prefix, a.Item1)).ToArray();
				using (var conn = await pool.GetConnectionAsync()) {
					var vals = await hander(conn.Client, keys);
					for (var z = 0; z < r.Value.Count; z++) {
						ret[r.Value[z].Item2] = vals == null || z >= vals.Length ? default(T) : vals[z];
					}
				}
			}
			return ret;
		}
		async private Task<long> ExecuteNonQueryAsync(string[] key, Func<RedisClient, string[], Task<long>> hander) {
			if (key == null || key.Any() == false) return 0;
			if (_clusterRule == null || ClusterNodes.Count == 1) {
				var pool = ClusterNodes.First().Value;
				var keys = key.Select(a => string.Concat(pool.Prefix, a)).ToArray();
				using (var conn = await pool.GetConnectionAsync()) {
					return await hander(conn.Client, keys);
				}
			}
			var rules = new Dictionary<string, List<string>>();
			for (var a = 0; a < key.Length; a++) {
				var rule = _clusterRule(key[a]);
				if (rules.ContainsKey(rule)) rules[rule].Add(key[a]);
				else rules.Add(rule, new List<string> { key[a] });
			}
			long affrows = 0;
			foreach (var r in rules) {
				var pool = ClusterNodes.TryGetValue(r.Key, out var b) ? b : ClusterNodes.First().Value;
				var keys = r.Value.Select(a => string.Concat(pool.Prefix, a)).ToArray();
				using (var conn = await pool.GetConnectionAsync()) {
					affrows += await hander(conn.Client, keys);
				}
			}
			return affrows;
		}
		#endregion

		/// <summary>
		/// 设置指定 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字符串值</param>
		/// <param name="expireSeconds">过期(秒单位)</param>
		/// <param name="exists">Nx, Xx</param>
		/// <returns></returns>
		async public Task<bool> SetAsync(string key, string value, int expireSeconds = -1, CSRedisExistence? exists = null) => await ExecuteScalarAsync(key, (c, k) => expireSeconds > 0 || exists != null ? c.SetAsync(k, value, expireSeconds > 0 ? new int?(expireSeconds) : null, exists == CSRedisExistence.Nx ? new RedisExistence?(RedisExistence.Nx) : (exists == CSRedisExistence.Xx ? new RedisExistence?(RedisExistence.Xx) : null)) : c.SetAsync(k, value)) == "OK";
		/// <summary>
		/// 设置指定 key 的值(字节流)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字节流</param>
		/// <param name="expireSeconds">过期(秒单位)</param>
		/// <param name="exists">Nx, Xx</param>
		/// <returns></returns>
		async public Task<bool> SetBytesAsync(string key, byte[] value, int expireSeconds = -1, CSRedisExistence? exists = null) => await ExecuteScalarAsync(key, (c, k) => expireSeconds > 0 || exists != null ? c.SetAsync(k, value, expireSeconds > 0 ? new int?(expireSeconds) : null, exists == CSRedisExistence.Nx ? new RedisExistence?(RedisExistence.Nx) : (exists == CSRedisExistence.Xx ? new RedisExistence?(RedisExistence.Xx) : null)) : c.SetAsync(k, value)) == "OK";
		/// <summary>
		/// 获取指定 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<string> GetAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.GetAsync(k));
		/// <summary>
		/// 获取多个指定 key 的值(数组)
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public Task<string[]> MGetAsync(params string[] keys) => ExeucteArrayAsync(keys, (c, k) => c.MGetAsync(k));
		/// <summary>
		/// 获取多个指定 key 的值(数组)
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public Task<string[]> GetStringsAsync(params string[] keys) => ExeucteArrayAsync(keys, (c, k) => c.MGetAsync(k));
		/// <summary>
		/// 获取指定 key 的值(字节流)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<byte[]> GetBytesAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.GetBytesAsync(k));
		/// <summary>
		/// 用于在 key 存在时删除 key
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> RemoveAsync(params string[] key) => ExecuteNonQueryAsync(key, (c, k) => c.DelAsync(k));
		/// <summary>
		/// 检查给定 key 是否存在
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<bool> ExistsAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.ExistsAsync(k));
		/// <summary>
		/// 将 key 所储存的值加上给定的增量值（increment）
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public Task<long> IncrementAsync(string key, long value = 1) => ExecuteScalarAsync(key, (c, k) => c.IncrByAsync(k, value));
		/// <summary>
		/// 为给定 key 设置过期时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="expire">过期时间</param>
		/// <returns></returns>
		public Task<bool> ExpireAsync(string key, TimeSpan expire) => ExecuteScalarAsync(key, (c, k) => c.ExpireAsync(k, expire));
		/// <summary>
		/// 以秒为单位，返回给定 key 的剩余生存时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> TtlAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.TtlAsync(k));
		/// <summary>
		/// 执行脚本
		/// </summary>
		/// <param name="script">脚本</param>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="args">参数</param>
		/// <returns></returns>
		public Task<object> EvalAsync(string script, string key, params object[] args) => ExecuteScalarAsync(key, (c, k) => c.EvalAsync(script, new[] { k }, args));
		/// <summary>
		/// 查找所有集群中符合给定模式(pattern)的 key
		/// </summary>
		/// <param name="pattern">如：runoob*</param>
		/// <returns></returns>
		async public Task<string[]> KeysAsync(string pattern) {
			List<string> ret = new List<string>();
			foreach (var pool in ClusterNodes)
				using (var conn = await pool.Value.GetConnectionAsync()) {
					ret.AddRange(await conn.Client.KeysAsync(pattern));
				}
			return ret.ToArray();
		}
		/// <summary>
		/// Redis Publish 命令用于将信息发送到指定群集节点的频道
		/// </summary>
		/// <param name="channel">频道名</param>
		/// <param name="data">消息文本</param>
		/// <returns></returns>
		async public Task<long> PublishAsync(string channel, string data) {
			var msgid = await HashIncrementAsync("CSRedisPublishMsgId", channel, 1);
			return await ExecuteScalarAsync(channel, (c, k) => c.PublishAsync(channel, $"{msgid}|{data}"));
		}

		#region Hash 操作
		/// <summary>
		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="keyValues">field1 value1 [field2 value2]</param>
		/// <returns></returns>
		public Task<string> HashSetAsync(string key, params object[] keyValues) => HashSetExpireAsync(key, TimeSpan.Zero, keyValues);
		/// <summary>
		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="expire">过期时间</param>
		/// <param name="keyValues">field1 value1 [field2 value2]</param>
		/// <returns></returns>
		async public Task<string> HashSetExpireAsync(string key, TimeSpan expire, params object[] keyValues) {
			if (keyValues == null || keyValues.Any() == false) return null;
			if (expire > TimeSpan.Zero) {
				var lua = "ARGV[1] = redis.call('HMSET', KEYS[1]";
				var argv = new List<object>();
				for (int a = 0, argvIdx = 3; a < keyValues.Length; a += 2, argvIdx++) {
					lua += ", '" + (keyValues[a]?.ToString().Replace("'", "\\'")) + "', ARGV[" + argvIdx + "]";
					argv.Add(keyValues[a + 1]);
				}
				lua += @") redis.call('EXPIRE', KEYS[1], ARGV[2]) return ARGV[1]";
				argv.InsertRange(0, new object[] { "", (long)expire.TotalSeconds });
				return (await EvalAsync(lua, key, argv.ToArray()))?.ToString();
			}
			return await ExecuteScalarAsync(key, (c, k) => c.HMSetAsync(k, keyValues));
		}
		/// <summary>
		/// 获取存储在哈希表中指定字段的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		public Task<string> HashGetAsync(string key, string field) => ExecuteScalarAsync(key, (c, k) => c.HGetAsync(k, field));
		/// <summary>
		/// 获取存储在哈希表中多个字段的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="fields">字段</param>
		/// <returns></returns>
		public Task<string[]> HashMGetAsync(string key, params string[] fields) => ExecuteScalarAsync(key, (c, k) => c.HMGetAsync(k, fields));
		/// <summary>
		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public Task<long> HashIncrementAsync(string key, string field, long value = 1) => ExecuteScalarAsync(key, (c, k) => c.HIncrByAsync(k, field, value));
		/// <summary>
		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public Task<double> HashIncrementFloatAsync(string key, string field, double value = 1) => ExecuteScalarAsync(key, (c, k) => c.HIncrByFloatAsync(k, field, value));
		/// <summary>
		/// 删除一个或多个哈希表字段
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="fields">字段</param>
		/// <returns></returns>
		async public Task<long> HashDeleteAsync(string key, params string[] fields) => fields == null || fields.Any() == false ? 0 : await ExecuteScalarAsync(key, (c, k) => c.HDelAsync(k, fields));
		/// <summary>
		/// 查看哈希表 key 中，指定的字段是否存在
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		public Task<bool> HashExistsAsync(string key, string field) => ExecuteScalarAsync(key, (c, k) => c.HExistsAsync(k, field));
		/// <summary>
		/// 获取哈希表中字段的数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> HashLengthAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.HLenAsync(k));
		/// <summary>
		/// 获取在哈希表中指定 key 的所有字段和值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<Dictionary<string, string>> HashGetAllAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.HGetAllAsync(k));
		/// <summary>
		/// 获取所有哈希表中的字段
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<string[]> HashKeysAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.HKeysAsync(k));
		/// <summary>
		/// 获取哈希表中所有值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<string[]> HashValsAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.HValsAsync(k));
		#endregion

		#region List 操作
		/// <summary>
		/// 它是 LPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BLPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="timeOut">超时(秒)</param>
		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
		/// <returns></returns>
		public Task<string> BLPopAsync(int timeOut, params string[] keys) => ClusterNodesNotSupportAsync(keys, null, (c, k) => c.BLPopAsync(timeOut, k));
		/// <summary>
		/// 它是 LPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BLPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="timeOut">超时(秒)</param>
		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
		/// <returns></returns>
		async public Task<(string key, string value)?> BLPopWithKeyAsync(int timeOut, params string[] keys) {
			string[] rkeys = null;
			var tuple = await ClusterNodesNotSupportAsync(keys, null, (c, k) => c.BLPopWithKeyAsync(timeOut, rkeys = k));
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
		public Task<string> BRPopAsync(int timeOut, params string[] keys) => ClusterNodesNotSupportAsync(keys, null, (c, k) => c.BRPopAsync(timeOut, k));
		/// <summary>
		/// 它是 RPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BRPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="timeOut">超时(秒)</param>
		/// <param name="keys">一个或多个列表，不含prefix前辍</param>
		/// <returns></returns>
		async public Task<(string key, string value)?> BRPopWithKeyAsync(int timeOut, params string[] keys) {
			string[] rkeys = null;
			var tuple = await ClusterNodesNotSupportAsync(keys, null, (c, k) => c.BRPopWithKeyAsync(timeOut, rkeys = k));
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
		public Task<string> LIndexAsync(string key, long index) => ExecuteScalarAsync(key, (c, k) => c.LIndexAsync(k, index));
		/// <summary>
		/// 在列表的元素前面插入元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="pivot">列表的元素</param>
		/// <param name="value">新元素</param>
		/// <returns></returns>
		public Task<long> LInsertBeforeAsync(string key, string pivot, string value) => ExecuteScalarAsync(key, (c, k) => c.LInsertAsync(k, RedisInsert.Before, pivot, value));
		/// <summary>
		/// 在列表的元素后面插入元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="pivot">列表的元素</param>
		/// <param name="value">新元素</param>
		/// <returns></returns>
		public Task<long> LInsertAfterAsync(string key, string pivot, string value) => ExecuteScalarAsync(key, (c, k) => c.LInsertAsync(k, RedisInsert.After, pivot, value));
		/// <summary>
		/// 获取列表长度
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> LLenAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.LLenAsync(k));
		/// <summary>
		/// 移出并获取列表的第一个元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<string> LPopAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.LPopAsync(k));
		/// <summary>
		/// 移除并获取列表最后一个元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<string> RPopAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.RPopAsync(k));
		/// <summary>
		/// 将一个或多个值插入到列表头部
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">一个或多个值</param>
		/// <returns></returns>
		async public Task<long> LPushAsync(string key, params string[] value) => value == null || value.Any() == false ? 0 : await ExecuteScalarAsync(key, (c, k) => c.LPushAsync(k, value));
		/// <summary>
		/// 在列表中添加一个或多个值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">一个或多个值</param>
		/// <returns></returns>
		async public Task<long> RPushAsync(string key, params string[] value) => value == null || value.Any() == false ? 0 : await ExecuteScalarAsync(key, (c, k) => c.RPushAsync(k, value));
		/// <summary>
		/// 获取列表指定范围内的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public Task<string[]> LRangAsync(string key, long start, long stop) => ExecuteScalarAsync(key, (c, k) => c.LRangeAsync(k, start, stop));
		/// <summary>
		/// 根据参数 count 的值，移除列表中与参数 value 相等的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="count">移除的数量，大于0时从表头删除数量count，小于0时从表尾删除数量-count，等于0移除所有</param>
		/// <param name="value">元素</param>
		/// <returns></returns>
		public Task<long> LRemAsync(string key, long count, string value) => ExecuteScalarAsync(key, (c, k) => c.LRemAsync(k, count, value));
		/// <summary>
		/// 通过索引设置列表元素的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="index">索引</param>
		/// <param name="value">值</param>
		/// <returns></returns>
		async public Task<bool> LSetAsync(string key, long index, string value) => await ExecuteScalarAsync(key, (c, k) => c.LSetAsync(k, index, value)) == "OK";
		/// <summary>
		/// 对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		async public Task<bool> LTrimAsync(string key, long start, long stop) => await ExecuteScalarAsync(key, (c, k) => c.LTrimAsync(k, start, stop)) == "OK";
		#endregion

		#region Set 操作
		/// <summary>
		/// 向集合添加一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="members">一个或多个成员</param>
		/// <returns></returns>
		async public Task<long> SAddAsync(string key, params string[] members) {
			if (members == null || members.Any() == false) return 0;
			return await ExecuteScalarAsync(key, (c, k) => c.SAddAsync(k, members));
		}
		/// <summary>
		/// 获取集合的成员数
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> SCardAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.SCardAsync(k));
		/// <summary>
		/// 返回给定所有集合的差集，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public Task<string[]> SDiffAsync(params string[] keys) => ClusterNodesNotSupportAsync(keys, new string[0], (c, k) => c.SDiffAsync(k));
		/// <summary>
		/// 返回给定所有集合的差集并存储在 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> SDiffStoreAsync(string destinationKey, params string[] keys) => ClusterNodesNotSupportAsync(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.SDiffStoreAsync(k.First(), k.Where((ki, kj) => kj > 0).ToArray()));
		/// <summary>
		/// 返回给定所有集合的交集，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public Task<string[]> SInterAsync(params string[] keys) => ClusterNodesNotSupportAsync(keys, new string[0], (c, k) => c.SInterAsync(k));
		/// <summary>
		/// 返回给定所有集合的交集并存储在 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> SInterStoreAsync(string destinationKey, params string[] keys) => ClusterNodesNotSupportAsync(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.SInterStoreAsync(k.First(), k.Where((ki, kj) => kj > 0).ToArray()));
		/// <summary>
		/// 返回集合中的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<string[]> SMembersAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.SMembersAsync(k));
		/// <summary>
		/// 将 member 元素从 source 集合移动到 destination 集合
		/// </summary>
		/// <param name="sourceKey">无序集合key，不含prefix前辍</param>
		/// <param name="destinationKey">目标无序集合key，不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		async public Task<bool> SMoveAsync(string sourceKey, string destinationKey, string member) {
			string rule = string.Empty;
			if (ClusterNodes.Count > 1) {
				var rule1 = _clusterRule(sourceKey);
				var rule2 = _clusterRule(destinationKey);
				if (rule1 != rule2) {
					if (await SRemAsync(sourceKey, member) <= 0) return false;
					return await SAddAsync(destinationKey, member) > 0;
				}
				rule = rule1;
			}
			var pool = ClusterNodes.TryGetValue(rule, out var b) ? b : ClusterNodes.First().Value;
			var key1 = string.Concat(pool.Prefix, sourceKey);
			var key2 = string.Concat(pool.Prefix, destinationKey);
			using (var conn = await pool.GetConnectionAsync()) {
				return await conn.Client.SMoveAsync(key1, key2, member);
			}
		}
		/// <summary>
		/// 移除并返回集合中的一个随机元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<string> SPopAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.SPopAsync(k));
		/// <summary>
		/// 返回集合中一个或多个随机数的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="count">返回个数</param>
		/// <returns></returns>
		public Task<string[]> SRandMemberAsync(string key, int count = 1) => ExecuteScalarAsync(key, (c, k) => c.SRandMemberAsync(k, count));
		/// <summary>
		/// 移除集合中一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="members">一个或多个成员</param>
		/// <returns></returns>
		async public Task<long> SRemAsync(string key, params string[] members) {
			if (members == null || members.Any() == false) return 0;
			return await ExecuteScalarAsync(key, (c, k) => c.SRemAsync(k, members));
		}
		/// <summary>
		/// 返回所有给定集合的并集，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public Task<string[]> SUnionAsync(params string[] keys) => ClusterNodesNotSupportAsync(keys, new string[0], (c, k) => c.SUnionAsync(k));
		/// <summary>
		/// 所有给定集合的并集存储在 destination 集合中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> SUnionStoreAsync(string destinationKey, params string[] keys) => ClusterNodesNotSupportAsync(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.SUnionStoreAsync(k.First(), k.Where((ki, kj) => kj > 0).ToArray()));
		#endregion

		async private Task<T> ClusterNodesNotSupportAsync<T>(string[] keys, T defaultValue, Func<RedisClient, string[], Task<T>> callbackAsync) {
			if (keys == null || keys.Any() == false) return defaultValue;
			var rules = ClusterNodes.Count > 1 ? keys.Select(a => _clusterRule(a)).Distinct() : new[] { ClusterNodes.FirstOrDefault().Key };
			if (rules.Count() > 1) throw new Exception("由于开启了群集模式，keys 分散在多个节点，无法使用此功能");
			var pool = ClusterNodes.TryGetValue(rules.First(), out var b) ? b : ClusterNodes.First().Value;
			string[] rkeys = new string[keys.Length];
			for (int a = 0; a < keys.Length; a++) rkeys[a] = string.Concat(pool.Prefix, keys[a]);
			if (rkeys.Length == 0) return defaultValue;
			using (var conn = await pool.GetConnectionAsync()) {
				return await callbackAsync(conn.Client, rkeys);
			}
		}

		#region Sorted Set 操作
		/// <summary>
		/// 向有序集合添加一个或多个成员，或者更新已存在成员的分数
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="memberScores">一个或多个成员分数</param>
		/// <returns></returns>
		async public Task<long> ZAddAsync(string key, params (double, string)[] memberScores) {
			if (memberScores == null || memberScores.Any() == false) return 0;
			var ms = memberScores.Select(a => new Tuple<double, string>(a.Item1, a.Item2)).ToArray();
			return await ExecuteScalarAsync(key, (c, k) => c.ZAddAsync<double, string>(k, ms));
		}
		/// <summary>
		/// 获取有序集合的成员数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> ZCardAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.ZCardAsync(k));
		/// <summary>
		/// 计算在有序集合中指定区间分数的成员数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="min">分数最小值</param>
		/// <param name="max">分数最大值</param>
		/// <returns></returns>
		public Task<long> ZCountAsync(string key, double min, double max) => ExecuteScalarAsync(key, (c, k) => c.ZCountAsync(k, min, max));
		/// <summary>
		/// 有序集合中对指定成员的分数加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="memeber">成员</param>
		/// <param name="increment">增量值(默认=1)</param>
		/// <returns></returns>
		public Task<double> ZIncrByAsync(string key, string memeber, double increment = 1) => ExecuteScalarAsync(key, (c, k) => c.ZIncrByAsync(k, increment, memeber));

		#region 多个有序集合 交集
		/// <summary>
		/// 计算给定的一个或多个有序集的最大值交集，将结果集存储在新的有序集合 destinationKey 中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> ZInterStoreMaxAsync(string destinationKey, params string[] keys) => ZInterStoreAsync(destinationKey, RedisAggregate.Max, keys);
		/// <summary>
		/// 计算给定的一个或多个有序集的最小值交集，将结果集存储在新的有序集合 destinationKey 中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> ZInterStoreMinAsync(string destinationKey, params string[] keys) => ZInterStoreAsync(destinationKey, RedisAggregate.Min, keys);
		/// <summary>
		/// 计算给定的一个或多个有序集的合值交集，将结果集存储在新的有序集合 destinationKey 中，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> ZInterStoreSumAsync(string destinationKey, params string[] keys) => ZInterStoreAsync(destinationKey, RedisAggregate.Sum, keys);
		private Task<long> ZInterStoreAsync(string destinationKey, RedisAggregate aggregate, params string[] keys) => ClusterNodesNotSupportAsync(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.ZInterStoreAsync(k.First(), null, aggregate, k.Where((ki, kj) => kj > 0).ToArray()));
		#endregion

		#region 多个有序集合 并集
		/// <summary>
		/// 计算给定的一个或多个有序集的最大值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> ZUnionStoreMaxAsync(string destinationKey, params string[] keys) => ZUnionStoreAsync(destinationKey, RedisAggregate.Max, keys);
		/// <summary>
		/// 计算给定的一个或多个有序集的最小值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> ZUnionStoreMinAsync(string destinationKey, params string[] keys) => ZUnionStoreAsync(destinationKey, RedisAggregate.Min, keys);
		/// <summary>
		/// 计算给定的一个或多个有序集的合值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> ZUnionStoreSumAsync(string destinationKey, params string[] keys) => ZUnionStoreAsync(destinationKey, RedisAggregate.Sum, keys);
		private Task<long> ZUnionStoreAsync(string destinationKey, RedisAggregate aggregate, params string[] keys) => ClusterNodesNotSupportAsync(new[] { destinationKey }.Concat(keys).ToArray(), 0, (c, k) => c.ZUnionStoreAsync(k.First(), null, aggregate, k.Where((ki, kj) => kj > 0).ToArray()));
		#endregion

		/// <summary>
		/// 通过索引区间返回有序集合成指定区间内的成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public Task<string[]> ZRangeAsync(string key, long start, long stop) => ExecuteScalarAsync(key, (c, k) => c.ZRangeAsync(k, start, stop, false));
		/// <summary>
		/// 通过分数返回有序集合指定区间内的成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public Task<string[]> ZRangeByScoreAsync(string key, double minScore, double maxScore, long? limit = null, long offset = 0) => ExecuteScalarAsync(key, (c, k) => c.ZRangeByScoreAsync(k, minScore, maxScore, false, false, false, offset, limit));
		/// <summary>
		/// 返回有序集合中指定成员的索引
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public Task<long?> ZRankAsync(string key, string member) => ExecuteScalarAsync(key, (c, k) => c.ZRankAsync(k, member));
		/// <summary>
		/// 移除有序集合中的一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">一个或多个成员</param>
		/// <returns></returns>
		public Task<long> ZRemAsync(string key, params string[] member) => ExecuteScalarAsync(key, (c, k) => c.ZRemAsync(k, member));
		/// <summary>
		/// 移除有序集合中给定的排名区间的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public Task<long> ZRemRangeByRankAsync(string key, long start, long stop) => ExecuteScalarAsync(key, (c, k) => c.ZRemRangeByRankAsync(k, start, stop));
		/// <summary>
		/// 移除有序集合中给定的分数区间的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <returns></returns>
		public Task<long> ZRemRangeByScoreAsync(string key, double minScore, double maxScore) => ExecuteScalarAsync(key, (c, k) => c.ZRemRangeByScoreAsync(k, minScore, maxScore));
		/// <summary>
		/// 返回有序集中指定区间内的成员，通过索引，分数从高到底
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public Task<string[]> ZRevRangeAsync(string key, long start, long stop) => ExecuteScalarAsync(key, (c, k) => c.ZRevRangeAsync(k, start, stop, false));
		/// <summary>
		/// 返回有序集中指定分数区间内的成员，分数从高到低排序
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public Task<string[]> ZRevRangeByScoreAsync(string key, double maxScore, double minScore, long? limit = null, long? offset = 0) => ExecuteScalarAsync(key, (c, k) => c.ZRevRangeByScoreAsync(k, maxScore, minScore, false, false, false, offset, limit));
		/// <summary>
		/// 返回有序集合中指定成员的排名，有序集成员按分数值递减(从大到小)排序
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public Task<long?> ZRevRankAsync(string key, string member) => ExecuteScalarAsync(key, (c, k) => c.ZRevRankAsync(k, member));
		/// <summary>
		/// 返回有序集中，成员的分数值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public Task<double?> ZScoreAsync(string key, string member) => ExecuteScalarAsync(key, (c, k) => c.ZScoreAsync(k, member));
		#endregion
	}
}