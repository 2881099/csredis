using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace CSRedis {
	public partial class CSRedisClient {
		public string Name { get; set; }
		public ConnectionPool Pool { get; protected set; }

		public CSRedisClient(string ip, int port = 6379, string pass = "", int poolsize = 50, int database = 0, string name = "") {
			this.Name = name;
			this.Pool = new ConnectionPool(ip, port, poolsize);
			this.Pool.Connected += (s, o) => {
				RedisClient rc = s as RedisClient;
				if (!string.IsNullOrEmpty(pass)) rc.Auth(pass);
				if (database > 0) rc.Select(database);
			};
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
			var cacheValue = this.Get(key);
			if (cacheValue != null) {
				try {
					return deserialize(cacheValue);
				} catch {
					this.Remove(key);
					throw;
				}
			}
			var ret = getData();
			this.Set(key, serialize(ret));
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
			var cacheValue = this.HashGet(key, field);
			if (!string.IsNullOrEmpty(cacheValue)) {
				try {
					var value = deserialize(cacheValue);
					if (DateTime.Now.Subtract(dt1970.AddSeconds(value.Item2)).TotalSeconds <= timeoutSeconds) return value.Item1;
				} catch {
					this.HashDelete(key, field);
					throw;
				}
			}
			var ret = getData();
			this.HashSet(key, field, serialize((ret, (long) DateTime.Now.Subtract(dt1970).TotalSeconds)));
			return ret;
		}

		/// <summary>
		/// 设置指定 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字符串值</param>
		/// <param name="expireSeconds">过期(秒单位)</param>
		/// <returns></returns>
		public bool Set(string key, string value, int expireSeconds = -1) {
			key = string.Concat(Name, key);
			using(var conn = Pool.GetConnection()) {
				if (expireSeconds > 0)
					return conn.Client.Set(key, value, TimeSpan.FromSeconds(expireSeconds)) == "OK";
				else
					return conn.Client.Set(key, value) == "OK";
			}
		}
		/// <summary>
		/// 设置指定 key 的值(字节流)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字节流</param>
		/// <param name="expireSeconds">过期(秒单位)</param>
		/// <returns></returns>
		public bool SetBytes(string key, byte[] value, int expireSeconds = -1) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				if (expireSeconds > 0)
					return conn.Client.Set(key, value, TimeSpan.FromSeconds(expireSeconds)) == "OK";
				else
					return conn.Client.Set(key, value) == "OK";
			}
		}
		/// <summary>
		/// 获取指定 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string Get(string key) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.Get(key);
			}
		}
		/// <summary>
		/// 获取多个指定 key 的值(数组)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string[] GetStrings(params string[] key) {
			if (key == null || key.Length == 0) return new string[0];
			string[] rkeys = new string[key.Length];
			for (int a = 0; a < key.Length; a++) rkeys[a] = string.Concat(Name, key[a]);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.MGet(rkeys);
			}
		}
		/// <summary>
		/// 获取指定 key 的值(字节流)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public byte[] GetBytes(string key) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.GetBytes(key);
			}
		}
		/// <summary>
		/// 用于在 key 存在时删除 key
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long Remove(params string[] key) {
			if (key == null || key.Length == 0) return 0;
			string[] rkeys = new string[key.Length];
			for (int a = 0; a < key.Length; a++) rkeys[a] = string.Concat(Name, key[a]);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.Del(rkeys);
			}
		}
		/// <summary>
		/// 检查给定 key 是否存在
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public bool Exists(string key) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.Exists(key);
			}
		}
		/// <summary>
		/// 将 key 所储存的值加上给定的增量值（increment）
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public long Increment(string key, long value = 1) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.IncrBy(key, value);
			}
		}
		/// <summary>
		/// 为给定 key 设置过期时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="expire">过期时间</param>
		/// <returns></returns>
		public bool Expire(string key, TimeSpan expire) {
			if (expire <= TimeSpan.Zero) return false;
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.Expire(key, expire);
			}
		}
		/// <summary>
		/// 以秒为单位，返回给定 key 的剩余生存时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long Ttl(string key) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.Ttl(key);
			}
		}
		/// <summary>
		/// 执行脚本
		/// </summary>
		/// <param name="script">脚本</param>
		/// <param name="keys">不含prefix前辍</param>
		/// <param name="args">参数</param>
		/// <returns></returns>
		public object Eval(string script, string[] keys, params string[] args) {
			string[] rkeys = new string[keys.Length];
			for (int a = 0; a < keys.Length; a++) rkeys[a] = string.Concat(Name, keys[a]);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.Eval(script, rkeys, args);
			}
		}
		/// <summary>
		/// 查找所有符合给定模式( pattern)的 key
		/// </summary>
		/// <param name="pattern">如：runoob*</param>
		/// <returns></returns>
		public string[] Keys(string pattern) {
			using (var conn = Pool.GetConnection()) {
				return conn.Client.Keys(pattern);
			}
		}
		/// <summary>
		/// Redis Publish 命令用于将信息发送到指定的频道
		/// </summary>
		/// <param name="channel">频道名</param>
		/// <param name="data">消息文本</param>
		/// <returns></returns>
		public long Publish(string channel, string data) {
			using (var conn = Pool.GetConnection()) {
				return conn.Client.Publish(channel, data);
			}
		}
		#region Hash 操作
		/// <summary>
		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="keyValues">field1 value1 [field2 value2]</param>
		/// <returns></returns>
		public string HashSet(string key, params object[] keyValues) {
			return HashSetExpire(key, TimeSpan.Zero, keyValues);
		}
		/// <summary>
		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="expire">过期时间</param>
		/// <param name="keyValues">field1 value1 [field2 value2]</param>
		/// <returns></returns>
		public string HashSetExpire(string key, TimeSpan expire, params object[] keyValues) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				var ret = conn.Client.HMSet(key, keyValues.Select(a => string.Concat(a)).ToArray());
				if (expire > TimeSpan.Zero) conn.Client.Expire(key, expire);
				return ret;
			}
		}
		/// <summary>
		/// 获取存储在哈希表中指定字段的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		public string HashGet(string key, string field) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.HGet(key, field);
			}
		}
		/// <summary>
		/// 获取存储在哈希表中多个字段的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="fields">字段</param>
		/// <returns></returns>
		public string[] HashMGet(string key, params string[] fields) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.HMGet(key, fields);
			}
		}
		/// <summary>
		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public long HashIncrement(string key, string field, long value = 1) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.HIncrBy(key, field, value);
			}
		}
		/// <summary>
		/// 删除一个或多个哈希表字段
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="fields">字段</param>
		/// <returns></returns>
		public long HashDelete(string key, params string[] fields) {
			if (fields == null || fields.Length == 0) return 0;
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.HDel(key, fields);
			}
		}
		/// <summary>
		/// 查看哈希表 key 中，指定的字段是否存在
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		public bool HashExists(string key, string field) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.HExists(key, field);
			}
		}
		/// <summary>
		/// 获取哈希表中字段的数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long HashLength(string key) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.HLen(key);
			}
		}
		/// <summary>
		/// 获取在哈希表中指定 key 的所有字段和值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public Dictionary<string, string> HashGetAll(string key) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.HGetAll(key);
			}
		}
		/// <summary>
		/// 获取所有哈希表中的字段
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string[] HashKeys(string key) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.HKeys(key);
			}
		}
		/// <summary>
		/// 获取哈希表中所有值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string[] HashVals(string key) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.HVals(key);
			}
		}
		#endregion

		#region List 操作
		/// <summary>
		/// 通过索引获取列表中的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="index">索引</param>
		/// <returns></returns>
		public string LIndex(string key, long index) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.LIndex(key, index);
			}
		}
		/// <summary>
		/// 在列表的元素前面插入元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="pivot">列表的元素</param>
		/// <param name="value">新元素</param>
		/// <returns></returns>
		public long LInsertBefore(string key, string pivot, string value) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.LInsert(key, RedisInsert.Before, pivot, value);
			}
		}
		/// <summary>
		/// 在列表的元素后面插入元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="pivot">列表的元素</param>
		/// <param name="value">新元素</param>
		/// <returns></returns>
		public long LInsertAfter(string key, string pivot, string value) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.LInsert(key, RedisInsert.After, pivot, value);
			}
		}
		/// <summary>
		/// 获取列表长度
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long LLen(string key) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.LLen(key);
			}
		}
		/// <summary>
		/// 移出并获取列表的第一个元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string LPop(string key) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.LPop(key);
			}
		}
		/// <summary>
		/// 移除并获取列表最后一个元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public string RPop(string key) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.RPop(key);
			}
		}
		/// <summary>
		/// 将一个或多个值插入到列表头部
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">一个或多个值</param>
		/// <returns></returns>
		public long LPush(string key, string[] value) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.LPush(key, value);
			}
		}
		/// <summary>
		/// 在列表中添加一个或多个值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">一个或多个值</param>
		/// <returns></returns>
		public long RPush(string key, string[] value) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.RPush(key, value);
			}
		}
		/// <summary>
		/// 获取列表指定范围内的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public string[] LRang(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.LRange(key, start, stop);
			}
		}
		/// <summary>
		/// 根据参数 count 的值，移除列表中与参数 value 相等的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="count">移除的数量，大于0时从表头删除数量count，小于0时从表尾删除数量-count，等于0移除所有</param>
		/// <param name="value">元素</param>
		/// <returns></returns>
		public long LRem(string key, long count, string value) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.LRem(key, count, value);
			}
		}
		/// <summary>
		/// 通过索引设置列表元素的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="index">索引</param>
		/// <param name="value">值</param>
		/// <returns></returns>
		public bool LSet(string key, long index, string value) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.LSet(key, index, value) == "OK";
			}
		}
		/// <summary>
		/// 对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public bool LTrim(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.LTrim(key, start, stop) == "OK";
			}
		}
		#endregion

		#region Sorted Set 操作
		/// <summary>
		/// 向有序集合添加一个或多个成员，或者更新已存在成员的分数
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="memberScores">一个或多个成员分数</param>
		/// <returns></returns>
		public long ZAdd(string key, params (double, string)[] memberScores) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZAdd<double, string>(key, memberScores.Select(a => new Tuple<double, string>(a.Item1, a.Item2)).ToArray());
			}
		}
		/// <summary>
		/// 获取有序集合的成员数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public long ZCard(string key) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZCard(key);
			}
		}
		/// <summary>
		/// 计算在有序集合中指定区间分数的成员数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="min">分数最小值</param>
		/// <param name="max">分数最大值</param>
		/// <returns></returns>
		public long ZCount(string key, double min, double max) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZCount(key, min, max);
			}
		}
		/// <summary>
		/// 有序集合中对指定成员的分数加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="memeber">成员</param>
		/// <param name="increment">增量值(默认=1)</param>
		/// <returns></returns>
		public double ZIncrBy(string key, string memeber, double increment = 1) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZIncrBy(key, increment, memeber);
			}
		}

		#region 多个有序集合 交集
		/// <summary>
		/// 计算给定的一个或多个有序集的最大值交集，将结果集存储在新的有序集合 destinationKey 中
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long ZInterStoreMax(string destinationKey, params string[] keys) {
			return ZInterStore(destinationKey, RedisAggregate.Max, keys);
		}
		/// <summary>
		/// 计算给定的一个或多个有序集的最小值交集，将结果集存储在新的有序集合 destinationKey 中
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long ZInterStoreMin(string destinationKey, params string[] keys) {
			return ZInterStore(destinationKey, RedisAggregate.Min, keys);
		}
		/// <summary>
		/// 计算给定的一个或多个有序集的合值交集，将结果集存储在新的有序集合 destinationKey 中
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long ZInterStoreSum(string destinationKey, params string[] keys) {
			return ZInterStore(destinationKey, RedisAggregate.Sum, keys);
		}
		private long ZInterStore(string destinationKey, RedisAggregate aggregate, params string[] keys) {
			destinationKey = string.Concat(Name, destinationKey);
			string[] rkeys = new string[keys.Length];
			for (int a = 0; a < keys.Length; a++) rkeys[a] = string.Concat(Name, keys[a]);
			if (rkeys.Length == 0) return 0;
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZInterStore(destinationKey, null, aggregate, rkeys);
			}
		}
		#endregion

		#region 多个有序集合 并集
		/// <summary>
		/// 计算给定的一个或多个有序集的最大值并集，将该并集(结果集)储存到 destination
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long ZUnionStoreMax(string destinationKey, params string[] keys) {
			return ZUnionStore(destinationKey, RedisAggregate.Max, keys);
		}
		/// <summary>
		/// 计算给定的一个或多个有序集的最小值并集，将该并集(结果集)储存到 destination
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long ZUnionStoreMin(string destinationKey, params string[] keys) {
			return ZUnionStore(destinationKey, RedisAggregate.Min, keys);
		}
		/// <summary>
		/// 计算给定的一个或多个有序集的合值并集，将该并集(结果集)储存到 destination
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		public long ZUnionStoreSum(string destinationKey, params string[] keys) {
			return ZUnionStore(destinationKey, RedisAggregate.Sum, keys);
		}
		private long ZUnionStore(string destinationKey, RedisAggregate aggregate, params string[] keys) {
			destinationKey = string.Concat(Name, destinationKey);
			string[] rkeys = new string[keys.Length];
			for (int a = 0; a < keys.Length; a++) rkeys[a] = string.Concat(Name, keys[a]);
			if (rkeys.Length == 0) return 0;
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZUnionStore(destinationKey, null, aggregate, rkeys);
			}
		}
		#endregion

		/// <summary>
		/// 通过索引区间返回有序集合成指定区间内的成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public string[] ZRange(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZRange(key, start, stop, false);
			}
		}
		/// <summary>
		/// 通过分数返回有序集合指定区间内的成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public string[] ZRangeByScore(string key, double minScore, double maxScore, long? limit = null, long offset = 0) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZRangeByScore(key, minScore, maxScore, false, false, false, offset, limit);
			}
		}
		/// <summary>
		/// 返回有序集合中指定成员的索引
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public long? ZRank(string key, string member) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZRank(key, member);
			}
		}
		/// <summary>
		/// 移除有序集合中的一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">一个或多个成员</param>
		/// <returns></returns>
		public long ZRem(string key, params string[] member) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZRem(key, member);
			}
		}
		/// <summary>
		/// 移除有序集合中给定的排名区间的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public long ZRemRangeByRank(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZRemRangeByRank(key, start, stop);
			}
		}
		/// <summary>
		/// 移除有序集合中给定的分数区间的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <returns></returns>
		public long ZRemRangeByScore(string key, double minScore, double maxScore) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZRemRangeByScore(key, minScore, maxScore);
			}
		}
		/// <summary>
		/// 返回有序集中指定区间内的成员，通过索引，分数从高到底
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public string[] ZRevRange(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZRevRange(key, start, stop, false);
			}
		}
		/// <summary>
		/// 返回有序集中指定分数区间内的成员，分数从高到低排序
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public string[] ZRevRangeByScore(string key, double maxScore, double minScore, long? limit = null, long? offset = null) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZRevRangeByScore(key, maxScore, minScore, false, false, false, offset, limit);
			}
		}
		/// <summary>
		/// 返回有序集合中指定成员的排名，有序集成员按分数值递减(从大到小)排序
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public long? ZRevRank(string key, string member) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZRevRank(key, member);
			}
		}
		/// <summary>
		/// 返回有序集中，成员的分数值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public double? ZScore(string key, string member) {
			key = string.Concat(Name, key);
			using (var conn = Pool.GetConnection()) {
				return conn.Client.ZScore(key, member);
			}
		}
		#endregion

	}
}