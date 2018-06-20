using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace CSRedis {
	public partial class QuickHelperBase {
		public static string Name { get; set; }
		public static ConnectionPool Instance { get; protected set; }

		private static DateTime dt1970 = new DateTime(1970, 1, 1);
		private static Random rnd = new Random();
		private static readonly int __staticMachine = ((0x00ffffff & Environment.MachineName.GetHashCode()) +
#if NETSTANDARD1_5 || NETSTANDARD1_6
			1
#else
            AppDomain.CurrentDomain.Id
#endif
			) & 0x00ffffff;
		private static readonly int __staticPid = Process.GetCurrentProcess().Id;
		private static int __staticIncrement = rnd.Next();
		/// <summary>
		/// 生成类似Mongodb的ObjectId有序、不重复Guid
		/// </summary>
		/// <returns></returns>
		public static Guid NewMongodbId() {
			var now = DateTime.Now;
			var uninxtime = (int)now.Subtract(dt1970).TotalSeconds;
			int increment = Interlocked.Increment(ref __staticIncrement) & 0x00ffffff;
			var rand = rnd.Next(0, int.MaxValue);
			var guid = $"{uninxtime.ToString("x8").PadLeft(8, '0')}{__staticMachine.ToString("x8").PadLeft(8, '0').Substring(2, 6)}{__staticPid.ToString("x8").PadLeft(8, '0').Substring(6, 2)}{increment.ToString("x8").PadLeft(8, '0')}{rand.ToString("x8").PadLeft(8, '0')}";
			return Guid.Parse(guid);
			//var value = HashIncrement("NewMongodbIdyyyyMMdd", now.ToString("HH:mm"), 1);
			//if (value == 1) Expire("NewMongodbIdyyyyMMdd", TimeSpan.FromHours(24));
			//var rand = rnd.Next(0, int.MaxValue);
			////e8f35037-887d-4f64-8355-f96e02e71807
			//var guid = $"{uninxtime.ToString("x8").PadLeft(8, '0')}{rand.ToString("x8").PadLeft(8, '0')}{value.ToString("x8").PadLeft(16, '0')}";
			//return Guid.Parse(guid);
			
		}
		/// <summary>
		/// 缓存壳
		/// </summary>
		/// <typeparam name="T">缓存类型</typeparam>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="timeoutSeconds">缓存秒数</param>
		/// <param name="getData">获取源数据的函数</param>
		/// <param name="serialize">序列化函数</param>
		/// <param name="deserialize">反序列化函数</param>
		/// <returns></returns>
		public static T Cache<T>(string key, int timeoutSeconds, Func<T> getData, Func<T, string> serialize, Func<string, T> deserialize) {
			if (timeoutSeconds <= 0) return getData();
			var cacheValue = Get(key);
			if (!string.IsNullOrEmpty(cacheValue)) {
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
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="field">字段</param>
		/// <param name="timeoutSeconds">缓存秒数</param>
		/// <param name="getData">获取源数据的函数</param>
		/// <param name="serialize">序列化函数</param>
		/// <param name="deserialize">反序列化函数</param>
		/// <returns></returns>
		public static T Cache<T>(string key, string field, long timeoutSeconds, Func<T> getData, Func<(T, DateTime), string> serialize, Func<string, (T, DateTime)> deserialize) {
			if (timeoutSeconds <= 0) return getData();
			var cacheValue = HashGet(key, field);
			if (!string.IsNullOrEmpty(cacheValue)) {
				try {
					var value = deserialize(cacheValue);
					if (DateTime.Now.Subtract(value.Item2).TotalSeconds <= timeoutSeconds) return value.Item1;
				} catch {
					HashDelete(key, field);
					throw;
				}
			}
			var ret = getData();
			HashSet(key, field, serialize((ret, DateTime.Now)));
			return ret;
		}
		/// <summary>
		/// 设置指定 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="value">字符串值</param>
		/// <param name="expireSeconds">过期(秒单位)</param>
		/// <returns></returns>
		public static bool Set(string key, string value, int expireSeconds = -1) {
			key = string.Concat(Name, key);
			using(var conn = Instance.GetConnection()) {
				if (expireSeconds > 0)
					return conn.Client.Set(key, value, TimeSpan.FromSeconds(expireSeconds)) == "OK";
				else
					return conn.Client.Set(key, value) == "OK";
			}
		}
		/// <summary>
		/// 设置指定 key 的值(字节流)
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="value">字节流</param>
		/// <param name="expireSeconds">过期(秒单位)</param>
		/// <returns></returns>
		public static bool SetBytes(string key, byte[] value, int expireSeconds = -1) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				if (expireSeconds > 0)
					return conn.Client.Set(key, value, TimeSpan.FromSeconds(expireSeconds)) == "OK";
				else
					return conn.Client.Set(key, value) == "OK";
			}
		}
		/// <summary>
		/// 获取指定 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static string Get(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.Get(key);
			}
		}
		/// <summary>
		/// 获取多个指定 key 的值(数组)
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static string[] GetStrings(params string[] key) {
			if (key == null || key.Length == 0) return new string[0];
			string[] rkeys = new string[key.Length];
			for (int a = 0; a < key.Length; a++) rkeys[a] = string.Concat(Name, key[a]);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.MGet(rkeys);
			}
		}
		/// <summary>
		/// 获取指定 key 的值(字节流)
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static byte[] GetBytes(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.GetBytes(key);
			}
		}
		/// <summary>
		/// 用于在 key 存在时删除 key
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static long Remove(params string[] key) {
			if (key == null || key.Length == 0) return 0;
			string[] rkeys = new string[key.Length];
			for (int a = 0; a < key.Length; a++) rkeys[a] = string.Concat(Name, key[a]);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.Del(rkeys);
			}
		}
		/// <summary>
		/// 检查给定 key 是否存在
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static bool Exists(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.Exists(key);
			}
		}
		/// <summary>
		/// 将 key 所储存的值加上给定的增量值（increment）
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public static long Increment(string key, long value = 1) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.IncrBy(key, value);
			}
		}
		/// <summary>
		/// 为给定 key 设置过期时间
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="expire">过期时间</param>
		/// <returns></returns>
		public static bool Expire(string key, TimeSpan expire) {
			if (expire <= TimeSpan.Zero) return false;
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.Expire(key, expire);
			}
		}
		/// <summary>
		/// 以秒为单位，返回给定 key 的剩余生存时间
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static long Ttl(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.Ttl(key);
			}
		}
		/// <summary>
		/// 查找所有符合给定模式( pattern)的 key
		/// </summary>
		/// <param name="pattern">如：runoob*</param>
		/// <returns></returns>
		public static string[] Keys(string pattern) {
			using (var conn = Instance.GetConnection()) {
				return conn.Client.Keys(pattern);
			}
		}
		/// <summary>
		/// Redis Publish 命令用于将信息发送到指定的频道
		/// </summary>
		/// <param name="channel">频道名</param>
		/// <param name="data">消息文本</param>
		/// <returns></returns>
		public static long Publish(string channel, string data) {
			using (var conn = Instance.GetConnection()) {
				return conn.Client.Publish(channel, data);
			}
		}
		#region Hash 操作
		/// <summary>
		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="keyValues">field1 value1 [field2 value2]</param>
		/// <returns></returns>
		public static string HashSet(string key, params object[] keyValues) {
			return HashSetExpire(key, TimeSpan.Zero, keyValues);
		}
		/// <summary>
		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="expire">过期时间</param>
		/// <param name="keyValues">field1 value1 [field2 value2]</param>
		/// <returns></returns>
		public static string HashSetExpire(string key, TimeSpan expire, params object[] keyValues) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				var ret = conn.Client.HMSet(key, keyValues.Select(a => string.Concat(a)).ToArray());
				if (expire > TimeSpan.Zero) conn.Client.Expire(key, expire);
				return ret;
			}
		}
		/// <summary>
		/// 获取存储在哈希表中指定字段的值
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		public static string HashGet(string key, string field) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HGet(key, field);
			}
		}
		/// <summary>
		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="field">字段</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public static long HashIncrement(string key, string field, long value = 1) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HIncrBy(key, field, value);
			}
		}
		/// <summary>
		/// 删除一个或多个哈希表字段
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="fields">字段</param>
		/// <returns></returns>
		public static long HashDelete(string key, params string[] fields) {
			if (fields == null || fields.Length == 0) return 0;
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HDel(key, fields);
			}
		}
		/// <summary>
		/// 查看哈希表 key 中，指定的字段是否存在
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		public static bool HashExists(string key, string field) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HExists(key, field);
			}
		}
		/// <summary>
		/// 获取哈希表中字段的数量
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static long HashLength(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HLen(key);
			}
		}
		/// <summary>
		/// 获取在哈希表中指定 key 的所有字段和值
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static Dictionary<string, string> HashGetAll(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HGetAll(key);
			}
		}
		/// <summary>
		/// 获取所有哈希表中的字段
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static string[] HashKeys(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HKeys(key);
			}
		}
		/// <summary>
		/// 获取哈希表中所有值
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static string[] HashVals(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HVals(key);
			}
		}
		#endregion

		#region List 操作
		/// <summary>
		/// 通过索引获取列表中的元素
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="index">索引</param>
		/// <returns></returns>
		public static string LIndex(string key, long index) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.LIndex(key, index);
			}
		}
		/// <summary>
		/// 在列表的元素前面插入元素
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="pivot">列表的元素</param>
		/// <param name="value">新元素</param>
		/// <returns></returns>
		public static long LInsertBefore(string key, string pivot, string value) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.LInsert(key, RedisInsert.Before, pivot, value);
			}
		}
		/// <summary>
		/// 在列表的元素后面插入元素
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="pivot">列表的元素</param>
		/// <param name="value">新元素</param>
		/// <returns></returns>
		public static long LInsertAfter(string key, string pivot, string value) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.LInsert(key, RedisInsert.After, pivot, value);
			}
		}
		/// <summary>
		/// 获取列表长度
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static long LLen(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.LLen(key);
			}
		}
		/// <summary>
		/// 移出并获取列表的第一个元素
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static string LPop(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.LPop(key);
			}
		}
		/// <summary>
		/// 移除并获取列表最后一个元素
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static string RPop(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.RPop(key);
			}
		}
		/// <summary>
		/// 将一个或多个值插入到列表头部
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="value">一个或多个值</param>
		/// <returns></returns>
		public static long LPush(string key, string[] value) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.LPush(key, value);
			}
		}
		/// <summary>
		/// 在列表中添加一个或多个值
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="value">一个或多个值</param>
		/// <returns></returns>
		public static long RPush(string key, string[] value) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.RPush(key, value);
			}
		}
		/// <summary>
		/// 获取列表指定范围内的元素
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public static string[] LRang(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.LRange(key, start, stop);
			}
		}
		/// <summary>
		/// 根据参数 count 的值，移除列表中与参数 value 相等的元素
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="count">移除的数量，大于0时从表头删除数量count，小于0时从表尾删除数量-count，等于0移除所有</param>
		/// <param name="value">元素</param>
		/// <returns></returns>
		public static long LRem(string key, long count, string value) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.LRem(key, count, value);
			}
		}
		/// <summary>
		/// 通过索引设置列表元素的值
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="index">索引</param>
		/// <param name="value">值</param>
		/// <returns></returns>
		public static bool LSet(string key, long index, string value) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.LSet(key, index, value) == "OK";
			}
		}
		/// <summary>
		/// 对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public static bool LTrim(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.LTrim(key, start, stop) == "OK";
			}
		}
		#endregion

		#region Sorted Set 操作
		/// <summary>
		/// 向有序集合添加一个或多个成员，或者更新已存在成员的分数
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="memberScores">一个或多个成员分数</param>
		/// <returns></returns>
		public static long ZAdd(string key, params (double, string)[] memberScores) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZAdd<double, string>(key, memberScores.Select(a => new Tuple<double, string>(a.Item1, a.Item2)).ToArray());
			}
		}
		/// <summary>
		/// 获取有序集合的成员数量
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static long ZCard(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZCard(key);
			}
		}
		/// <summary>
		/// 计算在有序集合中指定区间分数的成员数量
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="min">分数最小值</param>
		/// <param name="max">分数最大值</param>
		/// <returns></returns>
		public static long ZCount(string key, double min, double max) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZCount(key, min, max);
			}
		}
		/// <summary>
		/// 有序集合中对指定成员的分数加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="memeber">成员</param>
		/// <param name="increment">增量值(默认=1)</param>
		/// <returns></returns>
		public static double ZIncrBy(string key, string memeber, double increment = 1) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZIncrBy(key, increment, memeber);
			}
		}

		#region 多个有序集合 交集
		/// <summary>
		/// 计算给定的一个或多个有序集的最大值交集，将结果集存储在新的有序集合 destinationKey 中
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍RedisHelper.Name</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static long ZInterStoreMax(string destinationKey, params string[] keys) {
			return ZInterStore(destinationKey, RedisAggregate.Max, keys);
		}
		/// <summary>
		/// 计算给定的一个或多个有序集的最小值交集，将结果集存储在新的有序集合 destinationKey 中
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍RedisHelper.Name</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static long ZInterStoreMin(string destinationKey, params string[] keys) {
			return ZInterStore(destinationKey, RedisAggregate.Min, keys);
		}
		/// <summary>
		/// 计算给定的一个或多个有序集的合值交集，将结果集存储在新的有序集合 destinationKey 中
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍RedisHelper.Name</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static long ZInterStoreSum(string destinationKey, params string[] keys) {
			return ZInterStore(destinationKey, RedisAggregate.Sum, keys);
		}
		private static long ZInterStore(string destinationKey, RedisAggregate aggregate, params string[] keys) {
			destinationKey = string.Concat(Name, destinationKey);
			string[] rkeys = new string[keys.Length];
			for (int a = 0; a < keys.Length; a++) rkeys[a] = string.Concat(Name, keys[a]);
			if (rkeys.Length == 0) return 0;
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZInterStore(destinationKey, null, aggregate, rkeys);
			}
		}
		#endregion

		#region 多个有序集合 并集
		/// <summary>
		/// 计算给定的一个或多个有序集的最大值并集，将该并集(结果集)储存到 destination
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍RedisHelper.Name</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static long ZUnionStoreMax(string destinationKey, params string[] keys) {
			return ZUnionStore(destinationKey, RedisAggregate.Max, keys);
		}
		/// <summary>
		/// 计算给定的一个或多个有序集的最小值并集，将该并集(结果集)储存到 destination
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍RedisHelper.Name</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static long ZUnionStoreMin(string destinationKey, params string[] keys) {
			return ZUnionStore(destinationKey, RedisAggregate.Min, keys);
		}
		/// <summary>
		/// 计算给定的一个或多个有序集的合值并集，将该并集(结果集)储存到 destination
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍RedisHelper.Name</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍RedisHelper.Name</param>
		/// <returns></returns>
		public static long ZUnionStoreSum(string destinationKey, params string[] keys) {
			return ZUnionStore(destinationKey, RedisAggregate.Sum, keys);
		}
		private static long ZUnionStore(string destinationKey, RedisAggregate aggregate, params string[] keys) {
			destinationKey = string.Concat(Name, destinationKey);
			string[] rkeys = new string[keys.Length];
			for (int a = 0; a < keys.Length; a++) rkeys[a] = string.Concat(Name, keys[a]);
			if (rkeys.Length == 0) return 0;
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZUnionStore(destinationKey, null, aggregate, rkeys);
			}
		}
		#endregion

		/// <summary>
		/// 通过索引区间返回有序集合成指定区间内的成员
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public static string[] ZRange(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZRange(key, start, stop, false);
			}
		}
		/// <summary>
		/// 通过分数返回有序集合指定区间内的成员
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public static string[] ZRangeByScore(string key, double minScore, double maxScore, long? limit = null, long offset = 0) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZRangeByScore(key, minScore, maxScore, false, false, false, offset, limit);
			}
		}
		/// <summary>
		/// 返回有序集合中指定成员的索引
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public static long? ZRank(string key, string member) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZRank(key, member);
			}
		}
		/// <summary>
		/// 移除有序集合中的一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="member">一个或多个成员</param>
		/// <returns></returns>
		public static long ZRem(string key, params string[] member) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZRem(key, member);
			}
		}
		/// <summary>
		/// 移除有序集合中给定的排名区间的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public static long ZRemRangeByRank(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZRemRangeByRank(key, start, stop);
			}
		}
		/// <summary>
		/// 移除有序集合中给定的分数区间的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <returns></returns>
		public static long ZRemRangeByScore(string key, double minScore, double maxScore) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZRemRangeByScore(key, minScore, maxScore);
			}
		}
		/// <summary>
		/// 返回有序集中指定区间内的成员，通过索引，分数从高到底
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public static string[] ZRevRange(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZRevRange(key, start, stop, false);
			}
		}
		/// <summary>
		/// 返回有序集中指定分数区间内的成员，分数从高到低排序
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public static string[] ZRevRangeByScore(string key, double maxScore, double minScore, long? limit = null, long? offset = null) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZRevRangeByScore(key, maxScore, minScore, false, false, false, offset, limit);
			}
		}
		/// <summary>
		/// 返回有序集合中指定成员的排名，有序集成员按分数值递减(从大到小)排序
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public static long? ZRevRank(string key, string member) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZRevRank(key, member);
			}
		}
		/// <summary>
		/// 返回有序集中，成员的分数值
		/// </summary>
		/// <param name="key">不含prefix前辍RedisHelper.Name</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public static double? ZScore(string key, string member) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.ZScore(key, member);
			}
		}
		#endregion

	}
}