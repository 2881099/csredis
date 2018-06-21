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
			var cacheValue = await this.GetAsync(key);
			if (cacheValue != null) {
				try {
					return deserialize(cacheValue);
				} catch {
					await this.RemoveAsync(key);
					throw;
				}
			}
			var ret = await getDataAsync();
			await this.SetAsync(key, serialize(ret));
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
			var cacheValue = await this.HashGetAsync(key, field);
			if (!string.IsNullOrEmpty(cacheValue)) {
				try {
					var value = deserialize(cacheValue);
					if (DateTime.Now.Subtract(dt1970.AddSeconds(value.Item2)).TotalSeconds <= timeoutSeconds) return value.Item1;
				} catch {
					await this.HashDeleteAsync(key, field);
					throw;
				}
			}
			var ret = await getDataAsync();
			await this.HashSetAsync(key, field, serialize((ret, (long) DateTime.Now.Subtract(dt1970).TotalSeconds)));
			return ret;
		}
		/// <summary>
		/// 设置指定 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字符串值</param>
		/// <param name="expireSeconds">过期(秒单位)</param>
		/// <returns></returns>
		async public Task<bool> SetAsync(string key, string value, int expireSeconds = -1) {
			key = string.Concat(Name, key);
			using(var conn = await Instance.GetConnectionAsync()) {
				if (expireSeconds > 0)
					return await conn.Client.SetAsync(key, value, TimeSpan.FromSeconds(expireSeconds)) == "OK";
				else
					return await conn.Client.SetAsync(key, value) == "OK";
			}
		}
		/// <summary>
		/// 设置指定 key 的值(字节流)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字节流</param>
		/// <param name="expireSeconds">过期(秒单位)</param>
		/// <returns></returns>
		async public Task<bool> SetBytesAsync(string key, byte[] value, int expireSeconds = -1) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				if (expireSeconds > 0)
					return await conn.Client.SetAsync(key, value, TimeSpan.FromSeconds(expireSeconds)) == "OK";
				else
					return await conn.Client.SetAsync(key, value) == "OK";
			}
		}
		/// <summary>
		/// 获取指定 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		async public Task<string> GetAsync(string key) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.GetAsync(key);
			}
		}
		/// <summary>
		/// 获取多个指定 key 的值(数组)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		async public Task<string[]> GetStringsAsync(params string[] key) {
			if (key == null || key.Length == 0) return new string[0];
			string[] rkeys = new string[key.Length];
			for (int a = 0; a < key.Length; a++) rkeys[a] = string.Concat(Name, key[a]);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.MGetAsync(rkeys);
			}
		}
		/// <summary>
		/// 获取指定 key 的值(字节流)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		async public Task<byte[]> GetBytesAsync(string key) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.GetBytesAsync(key);
			}
		}
		/// <summary>
		/// 用于在 key 存在时删除 key
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		async public Task<long> RemoveAsync(params string[] key) {
			if (key == null || key.Length == 0) return 0;
			string[] rkeys = new string[key.Length];
			for (int a = 0; a < key.Length; a++) rkeys[a] = string.Concat(Name, key[a]);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.DelAsync(rkeys);
			}
		}
		/// <summary>
		/// 检查给定 key 是否存在
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		async public Task<bool> ExistsAsync(string key) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ExistsAsync(key);
			}
		}
		/// <summary>
		/// 将 key 所储存的值加上给定的增量值（increment）
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		async public Task<long> IncrementAsync(string key, long value = 1) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.IncrByAsync(key, value);
			}
		}
		/// <summary>
		/// 为给定 key 设置过期时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="expire">过期时间</param>
		/// <returns></returns>
		async public Task<bool> ExpireAsync(string key, TimeSpan expire) {
			if (expire <= TimeSpan.Zero) return false;
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ExpireAsync(key, expire);
			}
		}
		/// <summary>
		/// 以秒为单位，返回给定 key 的剩余生存时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		async public Task<long> TtlAsync(string key) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.TtlAsync(key);
			}
		}
		/// <summary>
		/// 查找所有符合给定模式( pattern)的 key
		/// </summary>
		/// <param name="pattern">如：runoob*</param>
		/// <returns></returns>
		async public Task<string[]> KeysAsync(string pattern) {
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.KeysAsync(pattern);
			}
		}
		/// <summary>
		/// Redis Publish 命令用于将信息发送到指定的频道
		/// </summary>
		/// <param name="channel">频道名</param>
		/// <param name="data">消息文本</param>
		/// <returns></returns>
		async public Task<long> PublishAsync(string channel, string data) {
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.PublishAsync(channel, data);
			}
		}
		#region Hash 操作
		/// <summary>
		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="keyValues">field1 value1 [field2 value2]</param>
		/// <returns></returns>
		async public Task<string> HashSetAsync(string key, params object[] keyValues) {
			return await HashSetExpireAsync(key, TimeSpan.Zero, keyValues);
		}
		/// <summary>
		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="expire">过期时间</param>
		/// <param name="keyValues">field1 value1 [field2 value2]</param>
		/// <returns></returns>
		async public Task<string> HashSetExpireAsync(string key, TimeSpan expire, params object[] keyValues) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				var ret = await conn.Client.HMSetAsync(key, keyValues.Select(a => string.Concat(a)).ToArray());
				if (expire > TimeSpan.Zero) await conn.Client.ExpireAsync(key, expire);
				return ret;
			}
		}
		/// <summary>
		/// 获取存储在哈希表中指定字段的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		async public Task<string> HashGetAsync(string key, string field) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.HGetAsync(key, field);
			}
		}
		/// <summary>
		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		async public Task<long> HashIncrementAsync(string key, string field, long value = 1) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.HIncrByAsync(key, field, value);
			}
		}
		/// <summary>
		/// 删除一个或多个哈希表字段
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="fields">字段</param>
		/// <returns></returns>
		async public Task<long> HashDeleteAsync(string key, params string[] fields) {
			if (fields == null || fields.Length == 0) return 0;
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.HDelAsync(key, fields);
			}
		}
		/// <summary>
		/// 查看哈希表 key 中，指定的字段是否存在
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		async public Task<bool> HashExistsAsync(string key, string field) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.HExistsAsync(key, field);
			}
		}
		/// <summary>
		/// 获取哈希表中字段的数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		async public Task<long> HashLengthAsync(string key) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.HLenAsync(key);
			}
		}
		/// <summary>
		/// 获取在哈希表中指定 key 的所有字段和值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		async public Task<Dictionary<string, string>> HashGetAllAsync(string key) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.HGetAllAsync(key);
			}
		}
		/// <summary>
		/// 获取所有哈希表中的字段
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		async public Task<string[]> HashKeysAsync(string key) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.HKeysAsync(key);
			}
		}
		/// <summary>
		/// 获取哈希表中所有值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		async public Task<string[]> HashValsAsync(string key) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.HValsAsync(key);
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
		async public Task<string> LIndexAsync(string key, long index) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.LIndexAsync(key, index);
			}
		}
		/// <summary>
		/// 在列表的元素前面插入元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="pivot">列表的元素</param>
		/// <param name="value">新元素</param>
		/// <returns></returns>
		async public Task<long> LInsertBeforeAsync(string key, string pivot, string value) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.LInsertAsync(key, RedisInsert.Before, pivot, value);
			}
		}
		/// <summary>
		/// 在列表的元素后面插入元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="pivot">列表的元素</param>
		/// <param name="value">新元素</param>
		/// <returns></returns>
		async public Task<long> LInsertAfterAsync(string key, string pivot, string value) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.LInsertAsync(key, RedisInsert.After, pivot, value);
			}
		}
		/// <summary>
		/// 获取列表长度
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		async public Task<long> LLenAsync(string key) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.LLenAsync(key);
			}
		}
		/// <summary>
		/// 移出并获取列表的第一个元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		async public Task<string> LPopAsync(string key) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.LPopAsync(key);
			}
		}
		/// <summary>
		/// 移除并获取列表最后一个元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		async public Task<string> RPopAsync(string key) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.RPopAsync(key);
			}
		}
		/// <summary>
		/// 将一个或多个值插入到列表头部
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">一个或多个值</param>
		/// <returns></returns>
		async public Task<long> LPushAsync(string key, string[] value) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.LPushAsync(key, value);
			}
		}
		/// <summary>
		/// 在列表中添加一个或多个值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">一个或多个值</param>
		/// <returns></returns>
		async public Task<long> RPushAsync(string key, string[] value) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.RPushAsync(key, value);
			}
		}
		/// <summary>
		/// 获取列表指定范围内的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		async public Task<string[]> LRangAsync(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.LRangeAsync(key, start, stop);
			}
		}
		/// <summary>
		/// 根据参数 count 的值，移除列表中与参数 value 相等的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="count">移除的数量，大于0时从表头删除数量count，小于0时从表尾删除数量-count，等于0移除所有</param>
		/// <param name="value">元素</param>
		/// <returns></returns>
		async public Task<long> LRemAsync(string key, long count, string value) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.LRemAsync(key, count, value);
			}
		}
		/// <summary>
		/// 通过索引设置列表元素的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="index">索引</param>
		/// <param name="value">值</param>
		/// <returns></returns>
		async public Task<bool> LSetAsync(string key, long index, string value) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.LSetAsync(key, index, value) == "OK";
			}
		}
		/// <summary>
		/// 对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		async public Task<bool> LTrimAsync(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.LTrimAsync(key, start, stop) == "OK";
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
		async public Task<long> ZAddAsync(string key, params (double, string)[] memberScores) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZAddAsync<double, string>(key, memberScores.Select(a => new Tuple<double, string>(a.Item1, a.Item2)).ToArray());
			}
		}
		/// <summary>
		/// 获取有序集合的成员数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		async public Task<long> ZCardAsync(string key) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZCardAsync(key);
			}
		}
		/// <summary>
		/// 计算在有序集合中指定区间分数的成员数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="min">分数最小值</param>
		/// <param name="max">分数最大值</param>
		/// <returns></returns>
		async public Task<long> ZCountAsync(string key, double min, double max) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZCountAsync(key, min, max);
			}
		}
		/// <summary>
		/// 有序集合中对指定成员的分数加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="memeber">成员</param>
		/// <param name="increment">增量值(默认=1)</param>
		/// <returns></returns>
		async public Task<double> ZIncrByAsync(string key, string memeber, double increment = 1) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZIncrByAsync(key, increment, memeber);
			}
		}

		#region 多个有序集合 交集
		/// <summary>
		/// 计算给定的一个或多个有序集的最大值交集，将结果集存储在新的有序集合 destinationKey 中
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		async public Task<long> ZInterStoreMaxAsync(string destinationKey, params string[] keys) {
			return await ZInterStoreAsync(destinationKey, RedisAggregate.Max, keys);
		}
		/// <summary>
		/// 计算给定的一个或多个有序集的最小值交集，将结果集存储在新的有序集合 destinationKey 中
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		async public Task<long> ZInterStoreMinAsync(string destinationKey, params string[] keys) {
			return await ZInterStoreAsync(destinationKey, RedisAggregate.Min, keys);
		}
		/// <summary>
		/// 计算给定的一个或多个有序集的合值交集，将结果集存储在新的有序集合 destinationKey 中
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		async public Task<long> ZInterStoreSumAsync(string destinationKey, params string[] keys) {
			return await ZInterStoreAsync(destinationKey, RedisAggregate.Sum, keys);
		}
		async private Task<long> ZInterStoreAsync(string destinationKey, RedisAggregate aggregate, params string[] keys) {
			destinationKey = string.Concat(Name, destinationKey);
			string[] rkeys = new string[keys.Length];
			for (int a = 0; a < keys.Length; a++) rkeys[a] = string.Concat(Name, keys[a]);
			if (rkeys.Length == 0) return 0;
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZInterStoreAsync(destinationKey, null, aggregate, rkeys);
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
		async public Task<long> ZUnionStoreMaxAsync(string destinationKey, params string[] keys) {
			return await ZUnionStoreAsync(destinationKey, RedisAggregate.Max, keys);
		}
		/// <summary>
		/// 计算给定的一个或多个有序集的最小值并集，将该并集(结果集)储存到 destination
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		async public Task<long> ZUnionStoreMinAsync(string destinationKey, params string[] keys) {
			return await ZUnionStoreAsync(destinationKey, RedisAggregate.Min, keys);
		}
		/// <summary>
		/// 计算给定的一个或多个有序集的合值并集，将该并集(结果集)储存到 destination
		/// </summary>
		/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
		/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
		/// <returns></returns>
		async public Task<long> ZUnionStoreSumAsync(string destinationKey, params string[] keys) {
			return await ZUnionStoreAsync(destinationKey, RedisAggregate.Sum, keys);
		}
		async private Task<long> ZUnionStoreAsync(string destinationKey, RedisAggregate aggregate, params string[] keys) {
			destinationKey = string.Concat(Name, destinationKey);
			string[] rkeys = new string[keys.Length];
			for (int a = 0; a < keys.Length; a++) rkeys[a] = string.Concat(Name, keys[a]);
			if (rkeys.Length == 0) return 0;
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZUnionStoreAsync(destinationKey, null, aggregate, rkeys);
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
		async public Task<string[]> ZRangeAsync(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZRangeAsync(key, start, stop, false);
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
		async public Task<string[]> ZRangeByScoreAsync(string key, double minScore, double maxScore, long? limit = null, long offset = 0) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZRangeByScoreAsync(key, minScore, maxScore, false, false, false, offset, limit);
			}
		}
		/// <summary>
		/// 返回有序集合中指定成员的索引
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		async public Task<long?> ZRankAsync(string key, string member) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZRankAsync(key, member);
			}
		}
		/// <summary>
		/// 移除有序集合中的一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">一个或多个成员</param>
		/// <returns></returns>
		async public Task<long> ZRemAsync(string key, params string[] member) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZRemAsync(key, member);
			}
		}
		/// <summary>
		/// 移除有序集合中给定的排名区间的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		async public Task<long> ZRemRangeByRankAsync(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZRemRangeByRankAsync(key, start, stop);
			}
		}
		/// <summary>
		/// 移除有序集合中给定的分数区间的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <returns></returns>
		async public Task<long> ZRemRangeByScoreAsync(string key, double minScore, double maxScore) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZRemRangeByScoreAsync(key, minScore, maxScore);
			}
		}
		/// <summary>
		/// 返回有序集中指定区间内的成员，通过索引，分数从高到底
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		async public Task<string[]> ZRevRangeAsync(string key, long start, long stop) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZRevRangeAsync(key, start, stop, false);
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
		async public Task<string[]> ZRevRangeByScoreAsync(string key, double maxScore, double minScore, long? limit = null, long? offset = null) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZRevRangeByScoreAsync(key, maxScore, minScore, false, false, false, offset, limit);
			}
		}
		/// <summary>
		/// 返回有序集合中指定成员的排名，有序集成员按分数值递减(从大到小)排序
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		async public Task<long?> ZRevRankAsync(string key, string member) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZRevRankAsync(key, member);
			}
		}
		/// <summary>
		/// 返回有序集中，成员的分数值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		async public Task<double?> ZScoreAsync(string key, string member) {
			key = string.Concat(Name, key);
			using (var conn = await Instance.GetConnectionAsync()) {
				return await conn.Client.ZScoreAsync(key, member);
			}
		}
		#endregion

	}
}