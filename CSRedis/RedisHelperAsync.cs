using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

partial class RedisHelper {
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
	public static Task<T> CacheShellAsync<T>(string key, int timeoutSeconds, Func<Task<T>> getDataAsync, Func<T, string> serialize = null, Func<string, T> deserialize = null) =>
		Instance.CacheShellAsync(key, timeoutSeconds, getDataAsync, serialize ?? new Func<T, string>(value => Serialize(value)), deserialize ?? new Func<string, T>(data => (T) Deserialize(data, typeof(T))));
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
	public static Task<T> CacheShellAsync<T>(string key, string field, int timeoutSeconds, Func<Task<T>> getDataAsync, Func<(T, long), string> serialize = null, Func<string, (T, long)> deserialize = null) =>
		Instance.CacheShellAsync(key, field, timeoutSeconds, getDataAsync, serialize ?? new Func<(T, long), string>(value => Serialize(value)), deserialize ?? new Func<string, (T, long)>(data => ((T, long)) Deserialize(data, typeof((T, long)))));

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
	public static Task<T[]> CacheShellAsync<T>(string key, string[] fields, int timeoutSeconds, Func<string[], Task<(string, T)[]>> getDataAsync, Func<(T, long), string> serialize = null, Func<string, (T, long)> deserialize = null) =>
		Instance.CacheShellAsync(key, fields, timeoutSeconds, getDataAsync, serialize ?? new Func<(T, long), string>(value => Serialize(value)), deserialize ?? new Func<string, (T, long)>(data => ((T, long))Deserialize(data, typeof((T, long)))));

	/// <summary>
	/// 设置指定 key 的值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="value">字符串值</param>
	/// <param name="expireSeconds">过期(秒单位)</param>
	/// <param name="exists">Nx, Xx</param>
	/// <returns></returns>
	public static Task<bool> SetAsync(string key, string value, int expireSeconds = -1, CSRedisExistence? exists = null) => Instance.SetAsync(key, value, expireSeconds, exists);
	/// <summary>
	/// 设置指定 key 的值(字节流)
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="value">字节流</param>
	/// <param name="expireSeconds">过期(秒单位)</param>
	/// <param name="exists">Nx, Xx</param>
	/// <returns></returns>
	public static Task<bool> SetBytesAsync(string key, byte[] value, int expireSeconds = -1, CSRedisExistence? exists = null) => Instance.SetBytesAsync(key, value, expireSeconds, exists);
	/// <summary>
	/// 只有在 key 不存在时设置 key 的值。
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="value">字符串值</param>
	/// <returns></returns>
	public static Task<bool> SetNxAsync(string key, string value) => Instance.SetNxAsync(key, value);
	/// <summary>
	/// 同时设置一个或多个 key-value 对。
	/// </summary>
	/// <param name="keyValues">key1 value1 [key2 value2]</param>
	/// <returns></returns>
	public static Task<bool> MSetAsync(params string[] keyValues) => Instance.MSetAsync(keyValues);
	/// <summary>
	/// 同时设置一个或多个 key-value 对，当且仅当所有给定 key 都不存在。警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="keyValues">key1 value1 [key2 value2]</param>
	/// <returns></returns>
	public static Task<bool> MSetNxAsync(params string[] keyValues) => Instance.MSetNxAsync(keyValues);
	/// <summary>
	/// 获取指定 key 的值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<string> GetAsync(string key) => Instance.GetAsync(key);
	/// <summary>
	/// 获取多个指定 key 的值(数组)
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<string[]> MGetAsync(params string[] key) => Instance.MGetAsync(key);
	/// <summary>
	/// 获取多个指定 key 的值(数组)
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<string[]> GetStringsAsync(params string[] key) => Instance.MGetAsync(key);
	/// <summary>
	/// 获取指定 key 的值(字节流)
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<byte[]> GetBytesAsync(string key) => Instance.GetBytesAsync(key);
	/// <summary>
	/// 用于在 key 存在时删除 key
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> RemoveAsync(params string[] key) => Instance.RemoveAsync(key);
	/// <summary>
	/// 检查给定 key 是否存在
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<bool> ExistsAsync(string key) => Instance.ExistsAsync(key);
	/// <summary>
	/// 将 key 所储存的值加上给定的增量值（increment）
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="value">增量值(默认=1)</param>
	/// <returns></returns>
	public static Task<long> IncrementAsync(string key, long value = 1) => Instance.IncrementAsync(key, value);
	/// <summary>
	/// 为给定 key 设置过期时间
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="expire">过期时间</param>
	/// <returns></returns>
	public static Task<bool> ExpireAsync(string key, TimeSpan expire) => Instance.ExpireAsync(key, expire);
	/// <summary>
	/// 以秒为单位，返回给定 key 的剩余生存时间
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> TtlAsync(string key) => Instance.TtlAsync(key);
	/// <summary>
	/// 执行脚本
	/// </summary>
	/// <param name="script">脚本</param>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="args">参数</param>
	/// <returns></returns>
	public static Task<object> EvalAsync(string script, string key, params object[] args) => Instance.EvalAsync(script, key, args);
	/// <summary>
	/// 查找所有符合给定模式( pattern)的 key
	/// </summary>
	/// <param name="pattern">如：runoob*</param>
	/// <returns></returns>
	public static Task<string[]> KeysAsync(string pattern) => Instance.KeysAsync(pattern);
	/// <summary>
	/// Redis Publish 命令用于将信息发送到指定的频道
	/// </summary>
	/// <param name="channel">频道名</param>
	/// <param name="data">消息文本</param>
	/// <returns></returns>
	public static Task<long> PublishAsync(string channel, string data) => Instance.PublishAsync(channel, data);
	#region Hash 操作
	/// <summary>
	/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="keyValues">field1 value1 [field2 value2]</param>
	/// <returns></returns>
	public static Task<string> HashSetAsync(string key, params object[] keyValues) => Instance.HashSetAsync(key, keyValues);
	/// <summary>
	/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="expire">过期时间</param>
	/// <param name="keyValues">field1 value1 [field2 value2]</param>
	/// <returns></returns>
	public static Task<string> HashSetExpireAsync(string key, TimeSpan expire, params object[] keyValues) => Instance.HashSetExpireAsync(key, expire, keyValues);
	/// <summary>
	/// 只有在字段 field 不存在时，设置哈希表字段的值。
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="field">字段</param>
	/// <param name="value">值</param>
	/// <returns></returns>
	public static Task<bool> HashSetNxAsync(string key, string field, object value) => Instance.HashSetNxAsync(key, field, value);
	/// <summary>
	/// 获取存储在哈希表中指定字段的值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="field">字段</param>
	/// <returns></returns>
	public static Task<string> HashGetAsync(string key, string field) => Instance.HashGetAsync(key, field);
	/// <summary>
	/// 获取存储在哈希表中多个字段的值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="fields">字段</param>
	/// <returns></returns>
	public static Task<string[]> HashMGetAsync(string key, params string[] fields) => Instance.HashMGetAsync(key, fields);
	/// <summary>
	/// 为哈希表 key 中的指定字段的整数值加上增量 increment
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="field">字段</param>
	/// <param name="value">增量值(默认=1)</param>
	/// <returns></returns>
	public static Task<long> HashIncrementAsync(string key, string field, long value = 1) => Instance.HashIncrementAsync(key, field, value);
	/// <summary>
	/// 为哈希表 key 中的指定字段的整数值加上增量 increment
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="field">字段</param>
	/// <param name="value">增量值(默认=1)</param>
	/// <returns></returns>
	public static Task<double> HashIncrementFloatAsync(string key, string field, double value = 1) => Instance.HashIncrementFloatAsync(key, field, value);
	/// <summary>
	/// 删除一个或多个哈希表字段
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="fields">字段</param>
	/// <returns></returns>
	public static Task<long> HashDeleteAsync(string key, params string[] fields) => Instance.HashDeleteAsync(key, fields);
	/// <summary>
	/// 查看哈希表 key 中，指定的字段是否存在
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="field">字段</param>
	/// <returns></returns>
	public static Task<bool> HashExistsAsync(string key, string field) => Instance.HashExistsAsync(key, field);
	/// <summary>
	/// 获取哈希表中字段的数量
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> HashLengthAsync(string key) => Instance.HashLengthAsync(key);
	/// <summary>
	/// 获取在哈希表中指定 key 的所有字段和值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<Dictionary<string, string>> HashGetAllAsync(string key) => Instance.HashGetAllAsync(key);
	/// <summary>
	/// 获取所有哈希表中的字段
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<string[]> HashKeysAsync(string key) => Instance.HashKeysAsync(key);
	/// <summary>
	/// 获取哈希表中所有值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<string[]> HashValsAsync(string key) => Instance.HashValsAsync(key);
	#endregion

	#region List 操作
	/// <summary>
	/// 它是 LPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BLPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="timeOut">超时(秒)</param>
	/// <param name="keys">一个或多个列表，不含prefix前辍</param>
	/// <returns></returns>
	public static Task<string> BLPopAsync(int timeOut, params string[] keys) => Instance.BLPopAsync(timeOut, keys);
	/// <summary>
	/// 它是 LPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BLPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="timeOut"></param>
	/// <param name="keys"></param>
	/// <returns></returns>
	public static Task<(string key, string value)?> BLPopWithKeyAsync(int timeOut, params string[] keys) => Instance.BLPopWithKeyAsync(timeOut, keys);
	/// <summary>
	/// 它是 RPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BRPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="timeOut">超时(秒)</param>
	/// <param name="keys">一个或多个列表，不含prefix前辍</param>
	/// <returns></returns>
	public static Task<string> BRPopAsync(int timeOut, params string[] keys) => Instance.BRPopAsync(timeOut, keys);
	/// <summary>
	/// 它是 RPOP 命令的阻塞版本，当给定列表内没有任何元素可供弹出的时候，连接将被 BRPOP 命令阻塞，直到等待超时或发现可弹出元素为止，超时返回null。警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="timeOut">超时(秒)</param>
	/// <param name="keys">一个或多个列表，不含prefix前辍</param>
	/// <returns></returns>
	public static Task<(string key, string value)?> BRPopWithKeyAsync(int timeOut, params string[] keys) => Instance.BRPopWithKeyAsync(timeOut, keys);
	/// <summary>
	/// 通过索引获取列表中的元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="index">索引</param>
	/// <returns></returns>
	public static Task<string> LIndexAsync(string key, long index) => Instance.LIndexAsync(key, index);
	/// <summary>
	/// 在列表的元素前面插入元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="pivot">列表的元素</param>
	/// <param name="value">新元素</param>
	/// <returns></returns>
	public static Task<long> LInsertBeforeAsync(string key, string pivot, string value) => Instance.LInsertBeforeAsync(key, pivot, value);
	/// <summary>
	/// 在列表的元素后面插入元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="pivot">列表的元素</param>
	/// <param name="value">新元素</param>
	/// <returns></returns>
	public static Task<long> LInsertAfterAsync(string key, string pivot, string value) => Instance.LInsertAfterAsync(key, pivot, value);
	/// <summary>
	/// 获取列表长度
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> LLenAsync(string key) => Instance.LLenAsync(key);
	/// <summary>
	/// 移出并获取列表的第一个元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<string> LPopAsync(string key) => Instance.LPopAsync(key);
	/// <summary>
	/// 移除并获取列表最后一个元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<string> RPopAsync(string key) => Instance.RPopAsync(key);
	/// <summary>
	/// 将一个或多个值插入到列表头部
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="value">一个或多个值</param>
	/// <returns></returns>
	public static Task<long> LPushAsync(string key, params string[] value) => Instance.LPushAsync(key, value);
	/// <summary>
	/// 在列表中添加一个或多个值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="value">一个或多个值</param>
	/// <returns></returns>
	public static Task<long> RPushAsync(string key, params string[] value) => Instance.RPushAsync(key, value);
	/// <summary>
	/// 获取列表指定范围内的元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <returns></returns>
	public static Task<string[]> LRangAsync(string key, long start, long stop) => Instance.LRangAsync(key, start, stop);
	/// <summary>
	/// 根据参数 count 的值，移除列表中与参数 value 相等的元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="count">移除的数量，大于0时从表头删除数量count，小于0时从表尾删除数量-count，等于0移除所有</param>
	/// <param name="value">元素</param>
	/// <returns></returns>
	public static Task<long> LRemAsync(string key, long count, string value) => Instance.LRemAsync(key, count, value);
	/// <summary>
	/// 通过索引设置列表元素的值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="index">索引</param>
	/// <param name="value">值</param>
	/// <returns></returns>
	public static Task<bool> LSetAsync(string key, long index, string value) => Instance.LSetAsync(key, index, value);
	/// <summary>
	/// 对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <returns></returns>
	public static Task<bool> LTrimAsync(string key, long start, long stop) => Instance.LTrimAsync(key, start, stop);
	#endregion

	#region Set 操作
	/// <summary>
	/// 向集合添加一个或多个成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="members">一个或多个成员</param>
	/// <returns></returns>
	public static Task<long> SAddAsync(string key, params string[] members) => Instance.SAddAsync(key, members);
	/// <summary>
	/// 获取集合的成员数
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> SCardAsync(string key) => Instance.SCardAsync(key);
	/// <summary>
	/// 返回给定所有集合的差集，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="keys">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<string[]> SDiffAsync(params string[] keys) => Instance.SDiffAsync(keys);
	/// <summary>
	/// 返回给定所有集合的差集并存储在 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> SDiffStoreAsync(string destinationKey, params string[] keys) => Instance.SDiffStoreAsync(destinationKey, keys);
	/// <summary>
	/// 返回给定所有集合的交集，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="keys">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<string[]> SInterAsync(params string[] keys) => Instance.SInterAsync(keys);
	/// <summary>
	/// 返回给定所有集合的交集并存储在 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> SInterStoreAsync(string destinationKey, params string[] keys) => Instance.SInterStoreAsync(destinationKey, keys);
	/// <summary>
	/// 返回集合中的所有成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<string[]> SMembersAsync(string key) => Instance.SMembersAsync(key);
	/// <summary>
	/// 将 member 元素从 source 集合移动到 destination 集合
	/// </summary>
	/// <param name="sourceKey">无序集合key，不含prefix前辍</param>
	/// <param name="destinationKey">目标无序集合key，不含prefix前辍</param>
	/// <param name="member">成员</param>
	/// <returns></returns>
	public static Task<bool> SMoveAsync(string sourceKey, string destinationKey, string member) => Instance.SMoveAsync(sourceKey, destinationKey, member);
	/// <summary>
	/// 移除并返回集合中的一个随机元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<string> SPopAsync(string key) => Instance.SPopAsync(key);
	/// <summary>
	/// 返回集合中一个或多个随机数的元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="count">返回个数</param>
	/// <returns></returns>
	public static Task<string[]> SRandMemberAsync(string key, int count = 1) => Instance.SRandMemberAsync(key, count);
	/// <summary>
	/// 移除集合中一个或多个成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="members">一个或多个成员</param>
	/// <returns></returns>
	public static Task<long> SRemAsync(string key, params string[] members) => Instance.SRemAsync(key, members);
	/// <summary>
	/// 返回所有给定集合的并集，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="keys">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<string[]> SUnionAsync(params string[] keys) => Instance.SUnionAsync(keys);
	/// <summary>
	/// 所有给定集合的并集存储在 destination 集合中，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> SUnionStoreAsync(string destinationKey, params string[] keys) => Instance.SUnionStoreAsync(destinationKey, keys);
	#endregion

	#region Sorted Set 操作
	/// <summary>
	/// 向有序集合添加一个或多个成员，或者更新已存在成员的分数
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="memberScores">一个或多个成员分数</param>
	/// <returns></returns>
	public static Task<long> ZAddAsync(string key, params (double, string)[] memberScores) => Instance.ZAddAsync(key, memberScores);
	/// <summary>
	/// 获取有序集合的成员数量
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> ZCardAsync(string key) => Instance.ZCardAsync(key);
	/// <summary>
	/// 计算在有序集合中指定区间分数的成员数量
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="min">分数最小值</param>
	/// <param name="max">分数最大值</param>
	/// <returns></returns>
	public static Task<long> ZCountAsync(string key, double min, double max) => Instance.ZCountAsync(key, min, max);
	/// <summary>
	/// 有序集合中对指定成员的分数加上增量 increment
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="memeber">成员</param>
	/// <param name="increment">增量值(默认=1)</param>
	/// <returns></returns>
	public static Task<double> ZIncrByAsync(string key, string memeber, double increment = 1) => Instance.ZIncrByAsync(key, memeber, increment);

	#region 多个有序集合 交集
	/// <summary>
	/// 计算给定的一个或多个有序集的最大值交集，将结果集存储在新的有序集合 destinationKey 中，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> ZInterStoreMaxAsync(string destinationKey, params string[] keys) => Instance.ZInterStoreMaxAsync(destinationKey, keys);
	/// <summary>
	/// 计算给定的一个或多个有序集的最小值交集，将结果集存储在新的有序集合 destinationKey 中，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> ZInterStoreMinAsync(string destinationKey, params string[] keys) => Instance.ZInterStoreMinAsync(destinationKey, keys);
	/// <summary>
	/// 计算给定的一个或多个有序集的合值交集，将结果集存储在新的有序集合 destinationKey 中，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> ZInterStoreSumAsync(string destinationKey, params string[] keys) => Instance.ZInterStoreSumAsync(destinationKey, keys);
	#endregion

	#region 多个有序集合 并集
	/// <summary>
	/// 计算给定的一个或多个有序集的最大值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> ZUnionStoreMaxAsync(string destinationKey, params string[] keys) => Instance.ZUnionStoreMaxAsync(destinationKey, keys);
	/// <summary>
	/// 计算给定的一个或多个有序集的最小值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> ZUnionStoreMinAsync(string destinationKey, params string[] keys) => Instance.ZUnionStoreMinAsync(destinationKey, keys);
	/// <summary>
	/// 计算给定的一个或多个有序集的合值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static Task<long> ZUnionStoreSumAsync(string destinationKey, params string[] keys) => Instance.ZUnionStoreSumAsync(destinationKey, keys);
	#endregion

	/// <summary>
	/// 通过索引区间返回有序集合成指定区间内的成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <returns></returns>
	public static Task<string[]> ZRangeAsync(string key, long start, long stop) => Instance.ZRangeAsync(key, start, stop);
	/// <summary>
	/// 通过分数返回有序集合指定区间内的成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="minScore">最小分数</param>
	/// <param name="maxScore">最大分数</param>
	/// <param name="limit">返回多少成员</param>
	/// <param name="offset">返回条件偏移位置</param>
	/// <returns></returns>
	public static Task<string[]> ZRangeByScoreAsync(string key, double minScore, double maxScore, long? limit = null, long offset = 0) => Instance.ZRangeByScoreAsync(key, minScore, maxScore, limit, offset);
	/// <summary>
	/// 返回有序集合中指定成员的索引
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="member">成员</param>
	/// <returns></returns>
	public static Task<long?> ZRankAsync(string key, string member) => Instance.ZRankAsync(key, member);
	/// <summary>
	/// 移除有序集合中的一个或多个成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="member">一个或多个成员</param>
	/// <returns></returns>
	public static Task<long> ZRemAsync(string key, params string[] member) => Instance.ZRemAsync(key, member);
	/// <summary>
	/// 移除有序集合中给定的排名区间的所有成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <returns></returns>
	public static Task<long> ZRemRangeByRankAsync(string key, long start, long stop) => Instance.ZRemRangeByRankAsync(key, start, stop);
	/// <summary>
	/// 移除有序集合中给定的分数区间的所有成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="minScore">最小分数</param>
	/// <param name="maxScore">最大分数</param>
	/// <returns></returns>
	public static Task<long> ZRemRangeByScoreAsync(string key, double minScore, double maxScore) => Instance.ZRemRangeByScoreAsync(key, minScore, maxScore);
	/// <summary>
	/// 返回有序集中指定区间内的成员，通过索引，分数从高到底
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <returns></returns>
	public static Task<string[]> ZRevRangeAsync(string key, long start, long stop) => Instance.ZRevRangeAsync(key, start, stop);
	/// <summary>
	/// 返回有序集中指定分数区间内的成员，分数从高到低排序
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="minScore">最小分数</param>
	/// <param name="maxScore">最大分数</param>
	/// <param name="limit">返回多少成员</param>
	/// <param name="offset">返回条件偏移位置</param>
	/// <returns></returns>
	public static Task<string[]> ZRevRangeByScoreAsync(string key, double maxScore, double minScore, long? limit = null, long? offset = null) => Instance.ZRevRangeByScoreAsync(key, maxScore, minScore, limit, offset);
	/// <summary>
	/// 返回有序集合中指定成员的排名，有序集成员按分数值递减(从大到小)排序
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="member">成员</param>
	/// <returns></returns>
	public static Task<long?> ZRevRankAsync(string key, string member) => Instance.ZRevRankAsync(key, member);
	/// <summary>
	/// 返回有序集中，成员的分数值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="member">成员</param>
	/// <returns></returns>
	public static Task<double?> ZScoreAsync(string key, string member) => Instance.ZScoreAsync(key, member);
	#endregion

}