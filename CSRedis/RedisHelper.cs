using CSRedis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

public abstract partial class RedisHelper {
	private static CSRedisClient _instance;
	/// <summary>
	/// CSRedisClient 静态实例，使用前请初始化
	/// RedisHelper.Initialization(
	///		csredis: new CSRedis.CSRedisClient(\"127.0.0.1:6379,pass=123,defaultDatabase=13,poolsize=50,ssl=false,writeBuffer=10240,prefix=key前辍\"), 
	///		serialize: value => Newtonsoft.Json.JsonConvert.SerializeObject(value), 
	///		deserialize: (data, type) => Newtonsoft.Json.JsonConvert.DeserializeObject(data, type))
	/// </summary>
	public static CSRedisClient Instance {
		get {
			if (_instance == null) throw new Exception("使用前请初始化 RedisHelper.Initialization(new CSRedis.CSRedisClient(\"127.0.0.1:6379,pass=123,defaultDatabase=13,poolsize=50,ssl=false,writeBuffer=10240,prefix=key前辍\");");
			return _instance;
		}
	}
	private static Func<object, string> _serialize;
	private static Func<string, Type, object> _deserialize;
	private static Func<object, string> Serialize {
		get {
			if (_serialize == null) throw new Exception("使用前请初始化 RedisHelper.Initialization");
			return _serialize;
		}
	}
	private static Func<string, Type, object> Deserialize {
		get {
			if (_deserialize == null) throw new Exception("使用前请初始化 RedisHelper.Initialization");
			return _deserialize;
		}
	}
	public static Dictionary<string, ConnectionPool> ClusterNodes => Instance.ClusterNodes;
	private static DateTime dt1970 = new DateTime(1970, 1, 1);

	/// <summary>
	/// 初始化csredis静态访问类
	/// RedisHelper.Initialization(
	///		csredis: new CSRedis.CSRedisClient(\"127.0.0.1:6379,pass=123,defaultDatabase=13,poolsize=50,ssl=false,writeBuffer=10240,prefix=key前辍\"), 
	///		serialize: value => Newtonsoft.Json.JsonConvert.SerializeObject(value), 
	///		deserialize: (data, type) => Newtonsoft.Json.JsonConvert.DeserializeObject(data, type))
	/// </summary>
	/// <param name="csredis"></param>
	/// <param name="serialize"></param>
	/// <param name="deserialize"></param>
	public static void Initialization(CSRedisClient csredis, Func<object, string> serialize, Func<string, Type, object> deserialize) {
		_instance = csredis;
		_serialize = serialize;
		_deserialize = deserialize;
	}

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
	public static T CacheShell<T>(string key, int timeoutSeconds, Func<T> getData, Func<T, string> serialize = null, Func<string, T> deserialize = null) =>
		Instance.CacheShell(key, timeoutSeconds, getData, serialize ?? new Func<T, string>(value => Serialize(value)), deserialize ?? new Func<string, T>(data => (T) Deserialize(data, typeof(T))));
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
	public static T CacheShell<T>(string key, string field, int timeoutSeconds, Func<T> getData, Func<(T, long), string> serialize = null, Func<string, (T, long)> deserialize = null) =>
		Instance.CacheShell(key, field, timeoutSeconds, getData, serialize ?? new Func<(T, long), string>(value => Serialize(value)), deserialize ?? new Func<string, (T, long)>(data => ((T, long)) Deserialize(data, typeof((T, long)))));

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
	public static T[] CacheShell<T>(string key, string[] fields, int timeoutSeconds, Func<string[], (string, T)[]> getData, Func<(T, long), string> serialize = null, Func<string, (T, long)> deserialize = null) =>
		Instance.CacheShell(key, fields, timeoutSeconds, getData, serialize ?? new Func<(T, long), string>(value => Serialize(value)), deserialize ?? new Func<string, (T, long)>(data => ((T, long))Deserialize(data, typeof((T, long)))));

	/// <summary>
	/// 设置指定 key 的值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="value">字符串值</param>
	/// <param name="expireSeconds">过期(秒单位)</param>
	/// <param name="exists">Nx, Xx</param>
	/// <returns></returns>
	public static bool Set(string key, string value, int expireSeconds = -1, CSRedisExistence? exists = null) => Instance.Set(key, value, expireSeconds, exists);
	/// <summary>
	/// 设置指定 key 的值(字节流)
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="value">字节流</param>
	/// <param name="expireSeconds">过期(秒单位)</param>
	/// <param name="exists">Nx, Xx</param>
	/// <returns></returns>
	public static bool SetBytes(string key, byte[] value, int expireSeconds = -1, CSRedisExistence? exists = null) => Instance.SetBytes(key, value, expireSeconds, exists);
	/// <summary>
	/// 获取指定 key 的值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static string Get(string key) => Instance.Get(key);
	/// <summary>
	/// 获取多个指定 key 的值(数组)
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static string[] MGet(params string[] key) => Instance.MGet(key);
	/// <summary>
	/// 获取多个指定 key 的值(数组)
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static string[] GetStrings(params string[] key) => Instance.GetStrings(key);
	/// <summary>
	/// 获取指定 key 的值(字节流)
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static byte[] GetBytes(string key) => Instance.GetBytes(key);
	/// <summary>
	/// 用于在 key 存在时删除 key
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static long Remove(params string[] key) => Instance.Remove(key);
	/// <summary>
	/// 检查给定 key 是否存在
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static bool Exists(string key) => Instance.Exists(key);
	/// <summary>
	/// 将 key 所储存的值加上给定的增量值（increment）
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="value">增量值(默认=1)</param>
	/// <returns></returns>
	public static long Increment(string key, long value = 1) => Instance.Increment(key, value);
	/// <summary>
	/// 为给定 key 设置过期时间
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="expire">过期时间</param>
	/// <returns></returns>
	public static bool Expire(string key, TimeSpan expire) => Instance.Expire(key, expire);
	/// <summary>
	/// 以秒为单位，返回给定 key 的剩余生存时间
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static long Ttl(string key) => Instance.Ttl(key);
	/// <summary>
	/// 执行脚本
	/// </summary>
	/// <param name="script">脚本</param>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="args">参数</param>
	/// <returns></returns>
	public static object Eval(string script, string key, params object[] args) => Instance.Eval(script, key, args);
	/// <summary>
	/// 查找所有符合给定模式( pattern)的 key
	/// </summary>
	/// <param name="pattern">如：runoob*</param>
	/// <returns></returns>
	public static string[] Keys(string pattern) => Instance.Keys(pattern);
	/// <summary>
	/// Redis Publish 命令用于将信息发送到指定的频道
	/// </summary>
	/// <param name="channel">频道名</param>
	/// <param name="data">消息文本</param>
	/// <returns></returns>
	public static long Publish(string channel, string data) => Instance.Publish(channel, data);
	/// <summary>
	/// 订阅，根据集群规则，Subscribe(("chan1", msg => Console.WriteLine(msg.Body)), ("chan2", msg => Console.WriteLine(msg.Body)))，注意：redis服务重启无法重连
	/// </summary>
	/// <param name="channels">频道</param>
	public static void Subscribe(params (string, Action<CSRedisClient.SubscribeMessageEventArgs>)[] channels) => Instance.Subscribe(channels);
	/// <summary>
	/// 模糊订阅，订阅所有集群节点(同条消息只处理一次），PSubscribe(new [] { "chan1*", "chan2*" }, msg => Console.WriteLine(msg.Body))，注意：redis服务重启无法重连
	/// </summary>
	/// <param name="channelPatterns">模糊频道</param>
	public static void PSubscribe(string[] channelPatterns, Action<CSRedisClient.PSubscribePMessageEventArgs> pmessage) => Instance.PSubscribe(channelPatterns, pmessage);
	#region Hash 操作
	/// <summary>
	/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="keyValues">field1 value1 [field2 value2]</param>
	/// <returns></returns>
	public static string HashSet(string key, params object[] keyValues) => Instance.HashSet(key, keyValues);
	/// <summary>
	/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="expire">过期时间</param>
	/// <param name="keyValues">field1 value1 [field2 value2]</param>
	/// <returns></returns>
	public static string HashSetExpire(string key, TimeSpan expire, params object[] keyValues) => Instance.HashSetExpire(key, expire, keyValues);
	/// <summary>
	/// 获取存储在哈希表中指定字段的值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="field">字段</param>
	/// <returns></returns>
	public static string HashGet(string key, string field) => Instance.HashGet(key, field);
	/// <summary>
	/// 获取存储在哈希表中多个字段的值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="fields">字段</param>
	/// <returns></returns>
	public static string[] HashMGet(string key, params string[] fields) => Instance.HashMGet(key, fields);
	/// <summary>
	/// 为哈希表 key 中的指定字段的整数值加上增量 increment
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="field">字段</param>
	/// <param name="value">增量值(默认=1)</param>
	/// <returns></returns>
	public static long HashIncrement(string key, string field, long value = 1) => Instance.HashIncrement(key, field, value);
	/// <summary>
	/// 为哈希表 key 中的指定字段的整数值加上增量 increment
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="field">字段</param>
	/// <param name="value">增量值(默认=1)</param>
	/// <returns></returns>
	public static double HashIncrementFloat(string key, string field, double value = 1) => Instance.HashIncrementFloat(key, field, value);
	/// <summary>
	/// 删除一个或多个哈希表字段
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="fields">字段</param>
	/// <returns></returns>
	public static long HashDelete(string key, params string[] fields) => Instance.HashDelete(key, fields);
	/// <summary>
	/// 查看哈希表 key 中，指定的字段是否存在
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="field">字段</param>
	/// <returns></returns>
	public static bool HashExists(string key, string field) => Instance.HashExists(key, field);
	/// <summary>
	/// 获取哈希表中字段的数量
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static long HashLength(string key) => Instance.HashLength(key);
	/// <summary>
	/// 获取在哈希表中指定 key 的所有字段和值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static Dictionary<string, string> HashGetAll(string key) => Instance.HashGetAll(key);
	/// <summary>
	/// 获取所有哈希表中的字段
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static string[] HashKeys(string key) => Instance.HashKeys(key);
	/// <summary>
	/// 获取哈希表中所有值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static string[] HashVals(string key) => Instance.HashVals(key);
	#endregion

	#region List 操作
	/// <summary>
	/// 通过索引获取列表中的元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="index">索引</param>
	/// <returns></returns>
	public static string LIndex(string key, long index) => Instance.LIndex(key, index);
	/// <summary>
	/// 在列表的元素前面插入元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="pivot">列表的元素</param>
	/// <param name="value">新元素</param>
	/// <returns></returns>
	public static long LInsertBefore(string key, string pivot, string value) => Instance.LInsertBefore(key, pivot, value);
	/// <summary>
	/// 在列表的元素后面插入元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="pivot">列表的元素</param>
	/// <param name="value">新元素</param>
	/// <returns></returns>
	public static long LInsertAfter(string key, string pivot, string value) => Instance.LInsertAfter(key, pivot, value);
	/// <summary>
	/// 获取列表长度
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static long LLen(string key) => Instance.LLen(key);
	/// <summary>
	/// 移出并获取列表的第一个元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static string LPop(string key) => Instance.LPop(key);
	/// <summary>
	/// 移除并获取列表最后一个元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static string RPop(string key) => Instance.RPop(key);
	/// <summary>
	/// 将一个或多个值插入到列表头部
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="value">一个或多个值</param>
	/// <returns></returns>
	public static long LPush(string key, params string[] value) => Instance.LPush(key, value);
	/// <summary>
	/// 在列表中添加一个或多个值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="value">一个或多个值</param>
	/// <returns></returns>
	public static long RPush(string key, params string[] value) => Instance.RPush(key, value);
	/// <summary>
	/// 获取列表指定范围内的元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <returns></returns>
	public static string[] LRang(string key, long start, long stop) => Instance.LRang(key, start, stop);
	/// <summary>
	/// 根据参数 count 的值，移除列表中与参数 value 相等的元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="count">移除的数量，大于0时从表头删除数量count，小于0时从表尾删除数量-count，等于0移除所有</param>
	/// <param name="value">元素</param>
	/// <returns></returns>
	public static long LRem(string key, long count, string value) => Instance.LRem(key, count, value);
	/// <summary>
	/// 通过索引设置列表元素的值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="index">索引</param>
	/// <param name="value">值</param>
	/// <returns></returns>
	public static bool LSet(string key, long index, string value) => Instance.LSet(key, index, value);
	/// <summary>
	/// 对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <returns></returns>
	public static bool LTrim(string key, long start, long stop) => Instance.LTrim(key, start, stop);
	#endregion

	#region Set 操作
	/// <summary>
	/// 向集合添加一个或多个成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="members">一个或多个成员</param>
	/// <returns></returns>
	public static long SAdd(string key, params string[] members) => Instance.SAdd(key, members);
	/// <summary>
	/// 获取集合的成员数
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static long SCard(string key) => Instance.SCard(key);
	/// <summary>
	/// 返回给定所有集合的差集，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="keys">不含prefix前辍</param>
	/// <returns></returns>
	public static string[] SDiff(params string[] keys) => Instance.SDiff(keys);
	/// <summary>
	/// 返回给定所有集合的差集并存储在 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static long SDiffStore(string destinationKey, params string[] keys) => Instance.SDiffStore(destinationKey, keys);
	/// <summary>
	/// 返回给定所有集合的交集，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="keys">不含prefix前辍</param>
	/// <returns></returns>
	public static string[] SInter(params string[] keys) => Instance.SInter(keys);
	/// <summary>
	/// 返回给定所有集合的交集并存储在 destination 中，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static long SInterStore(string destinationKey, params string[] keys) => Instance.SInterStore(destinationKey, keys);
	/// <summary>
	/// 返回集合中的所有成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static string[] SMembers(string key) => Instance.SMembers(key);
	/// <summary>
	/// 将 member 元素从 source 集合移动到 destination 集合
	/// </summary>
	/// <param name="sourceKey">无序集合key，不含prefix前辍</param>
	/// <param name="destinationKey">目标无序集合key，不含prefix前辍</param>
	/// <param name="member">成员</param>
	/// <returns></returns>
	public static bool SMove(string sourceKey, string destinationKey, string member) => Instance.SMove(sourceKey, destinationKey, member);
	/// <summary>
	/// 移除并返回集合中的一个随机元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static string SPop(string key) => Instance.SPop(key);
	/// <summary>
	/// 返回集合中一个或多个随机数的元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="count">返回个数</param>
	/// <returns></returns>
	public static string[] SRandMember(string key, int count = 1) => Instance.SRandMember(key, count);
	/// <summary>
	/// 移除集合中一个或多个成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="members">一个或多个成员</param>
	/// <returns></returns>
	public static long SRem(string key, params string[] members) => Instance.SRem(key, members);
	/// <summary>
	/// 返回所有给定集合的并集，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="keys">不含prefix前辍</param>
	/// <returns></returns>
	public static string[] SUnion(params string[] keys) => Instance.SUnion(keys);
	/// <summary>
	/// 所有给定集合的并集存储在 destination 集合中，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的无序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static long SUnionStore(string destinationKey, params string[] keys) => Instance.SUnionStore(destinationKey, keys);
	/// <summary>
	/// 迭代集合中的元素
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="cursor">位置</param>
	/// <param name="pattern">模式</param>
	/// <param name="count">数量</param>
	/// <returns></returns>
	public static RedisScan<string> SScan(string key, int cursor, string pattern = null, int? count = null) => Instance.SScan(key, cursor, pattern, count);
	#endregion

	#region Sorted Set 操作
	/// <summary>
	/// 向有序集合添加一个或多个成员，或者更新已存在成员的分数
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="memberScores">一个或多个成员分数</param>
	/// <returns></returns>
	public static long ZAdd(string key, params (double, string)[] memberScores) => Instance.ZAdd(key, memberScores);
	/// <summary>
	/// 获取有序集合的成员数量
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <returns></returns>
	public static long ZCard(string key) => Instance.ZCard(key);
	/// <summary>
	/// 计算在有序集合中指定区间分数的成员数量
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="min">分数最小值</param>
	/// <param name="max">分数最大值</param>
	/// <returns></returns>
	public static long ZCount(string key, double min, double max) => Instance.ZCount(key, min, max);
	/// <summary>
	/// 有序集合中对指定成员的分数加上增量 increment
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="memeber">成员</param>
	/// <param name="increment">增量值(默认=1)</param>
	/// <returns></returns>
	public static double ZIncrBy(string key, string memeber, double increment = 1) => Instance.ZIncrBy(key, memeber, increment);

	#region 多个有序集合 交集
	/// <summary>
	/// 计算给定的一个或多个有序集的最大值交集，将结果集存储在新的有序集合 destinationKey 中，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static long ZInterStoreMax(string destinationKey, params string[] keys) => Instance.ZInterStoreMax(destinationKey, keys);
	/// <summary>
	/// 计算给定的一个或多个有序集的最小值交集，将结果集存储在新的有序集合 destinationKey 中，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static long ZInterStoreMin(string destinationKey, params string[] keys) => Instance.ZInterStoreMin(destinationKey, keys);
	/// <summary>
	/// 计算给定的一个或多个有序集的合值交集，将结果集存储在新的有序集合 destinationKey 中，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static long ZInterStoreSum(string destinationKey, params string[] keys) => Instance.ZInterStoreSum(destinationKey, keys);
	#endregion

	#region 多个有序集合 并集
	/// <summary>
	/// 计算给定的一个或多个有序集的最大值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static long ZUnionStoreMax(string destinationKey, params string[] keys) => Instance.ZUnionStoreMax(destinationKey, keys);
	/// <summary>
	/// 计算给定的一个或多个有序集的最小值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static long ZUnionStoreMin(string destinationKey, params string[] keys) => Instance.ZUnionStoreMin(destinationKey, keys);
	/// <summary>
	/// 计算给定的一个或多个有序集的合值并集，将该并集(结果集)储存到 destination，警告：群集模式下，若keys分散在多个节点时，将报错
	/// </summary>
	/// <param name="destinationKey">新的有序集合，不含prefix前辍</param>
	/// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
	/// <returns></returns>
	public static long ZUnionStoreSum(string destinationKey, params string[] keys) => Instance.ZUnionStoreSum(destinationKey, keys);
	#endregion

	/// <summary>
	/// 通过索引区间返回有序集合成指定区间内的成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <returns></returns>
	public static string[] ZRange(string key, long start, long stop) => Instance.ZRange(key, start, stop);
	/// <summary>
	/// 通过分数返回有序集合指定区间内的成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="minScore">最小分数</param>
	/// <param name="maxScore">最大分数</param>
	/// <param name="limit">返回多少成员</param>
	/// <param name="offset">返回条件偏移位置</param>
	/// <returns></returns>
	public static string[] ZRangeByScore(string key, double minScore, double maxScore, long? limit = null, long offset = 0) => Instance.ZRangeByScore(key, minScore, maxScore, limit, offset);
	/// <summary>
	/// 返回有序集合中指定成员的索引
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="member">成员</param>
	/// <returns></returns>
	public static long? ZRank(string key, string member) => Instance.ZRank(key, member);
	/// <summary>
	/// 移除有序集合中的一个或多个成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="member">一个或多个成员</param>
	/// <returns></returns>
	public static long ZRem(string key, params string[] member) => Instance.ZRem(key, member);
	/// <summary>
	/// 移除有序集合中给定的排名区间的所有成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <returns></returns>
	public static long ZRemRangeByRank(string key, long start, long stop) => Instance.ZRemRangeByRank(key, start, stop);
	/// <summary>
	/// 移除有序集合中给定的分数区间的所有成员
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="minScore">最小分数</param>
	/// <param name="maxScore">最大分数</param>
	/// <returns></returns>
	public static long ZRemRangeByScore(string key, double minScore, double maxScore) => Instance.ZRemRangeByScore(key, minScore, maxScore);
	/// <summary>
	/// 返回有序集中指定区间内的成员，通过索引，分数从高到底
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
	/// <returns></returns>
	public static string[] ZRevRange(string key, long start, long stop) => Instance.ZRevRange(key, start, stop);
	/// <summary>
	/// 返回有序集中指定分数区间内的成员，分数从高到低排序
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="minScore">最小分数</param>
	/// <param name="maxScore">最大分数</param>
	/// <param name="limit">返回多少成员</param>
	/// <param name="offset">返回条件偏移位置</param>
	/// <returns></returns>
	public static string[] ZRevRangeByScore(string key, double maxScore, double minScore, long? limit = null, long? offset = null) => Instance.ZRevRangeByScore(key, maxScore, minScore, limit, offset);
	/// <summary>
	/// 返回有序集合中指定成员的排名，有序集成员按分数值递减(从大到小)排序
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="member">成员</param>
	/// <returns></returns>
	public static long? ZRevRank(string key, string member) => Instance.ZRevRank(key, member);
	/// <summary>
	/// 返回有序集中，成员的分数值
	/// </summary>
	/// <param name="key">不含prefix前辍</param>
	/// <param name="member">成员</param>
	/// <returns></returns>
	public static double? ZScore(string key, string member) => Instance.ZScore(key, member);
	#endregion

}