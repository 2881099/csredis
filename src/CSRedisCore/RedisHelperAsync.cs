using CSRedis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#if net40
#else
partial class RedisHelper<TMark>
{

    #region 缓存壳
    /// <summary>
    /// 缓存壳
    /// </summary>
    /// <typeparam name="T">缓存类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="timeoutSeconds">缓存秒数</param>
    /// <param name="getDataAsync">获取源数据的函数</param>
    /// <returns></returns>
    public static Task<T> CacheShellAsync<T>(string key, int timeoutSeconds, Func<Task<T>> getDataAsync) => Instance.CacheShellAsync(key, timeoutSeconds, getDataAsync);
    /// <summary>
    /// 缓存壳(哈希表)
    /// </summary>
    /// <typeparam name="T">缓存类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="field">字段</param>
    /// <param name="timeoutSeconds">缓存秒数</param>
    /// <param name="getDataAsync">获取源数据的函数</param>
    /// <returns></returns>
    public static Task<T> CacheShellAsync<T>(string key, string field, int timeoutSeconds, Func<Task<T>> getDataAsync) => Instance.CacheShellAsync(key, field, timeoutSeconds, getDataAsync);
    /// <summary>
    /// 缓存壳(哈希表)，将 fields 每个元素存储到单独的缓存片，实现最大化复用
    /// </summary>
    /// <typeparam name="T">缓存类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="fields">字段</param>
    /// <param name="timeoutSeconds">缓存秒数</param>
    /// <param name="getDataAsync">获取源数据的函数，输入参数是没有缓存的 fields，返回值应该是 (field, value)[]</param>
    /// <returns></returns>
    public static Task<(string key, T value)[]> CacheShellAsync<T>(string key, string[] fields, int timeoutSeconds, Func<string[], Task<(string, T)[]>> getDataAsync) => Instance.CacheShellAsync(key, fields, timeoutSeconds, getDataAsync);
    #endregion

    #region 连接命令
    /// <summary>
    /// 打印字符串
    /// </summary>
    /// <param name="nodeKey">分区key</param>
    /// <param name="message">消息</param>
    /// <returns></returns>
    public static Task<string> EchoAsync(string nodeKey, string message) => Instance.EchoAsync(nodeKey, message);
    /// <summary>
    /// 打印字符串
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns></returns>
    public static Task<string> EchoAsync(string message) => Instance.EchoAsync(message);
    /// <summary>
    /// 查看服务是否运行
    /// </summary>
    /// <param name="nodeKey">分区key</param>
    /// <returns></returns>
    public static Task<bool> PingAsync(string nodeKey) => Instance.PingAsync(nodeKey);
    /// <summary>
    /// 查看服务是否运行
    /// </summary>
    /// <returns></returns>
    public static Task<bool> PingAsync() => Instance.PingAsync();
    #endregion

    #region Script
    /// <summary>
    /// 执行脚本
    /// </summary>
    /// <param name="script">Lua 脚本</param>
    /// <param name="key">用于定位分区节点，不含prefix前辍</param>
    /// <param name="args">参数</param>
    /// <returns></returns>
    public static Task<object> EvalAsync(string script, string key, params object[] args) => Instance.EvalAsync(script, key, args);
    /// <summary>
    /// 执行脚本
    /// </summary>
    /// <param name="sha1">脚本缓存的sha1</param>
    /// <param name="key">用于定位分区节点，不含prefix前辍</param>
    /// <param name="args">参数</param>
    /// <returns></returns>
    public static Task<object> EvalSHAAsync(string sha1, string key, params object[] args) => Instance.EvalSHAAsync(sha1, key, args);
    /// <summary>
    /// 校验所有分区节点中，脚本是否已经缓存。任何分区节点未缓存sha1，都返回false。
    /// </summary>
    /// <param name="sha1">脚本缓存的sha1</param>
    /// <returns></returns>
    public static Task<bool[]> ScriptExistsAsync(params string[] sha1) => Instance.ScriptExistsAsync(sha1);
    /// <summary>
    /// 清除所有分区节点中，所有 Lua 脚本缓存
    /// </summary>
    public static Task ScriptFlushAsync() => Instance.ScriptFlushAsync();
    /// <summary>
    /// 杀死所有分区节点中，当前正在运行的 Lua 脚本
    /// </summary>
    public static Task ScriptKillAsync() => Instance.ScriptKillAsync();
    /// <summary>
    /// 在所有分区节点中，缓存脚本后返回 sha1（同样的脚本在任何服务器，缓存后的 sha1 都是相同的）
    /// </summary>
    /// <param name="script">Lua 脚本</param>
    /// <returns></returns>
    public static Task<string> ScriptLoadAsync(string script) => Instance.ScriptLoadAsync(script);
    #endregion

    #region Pub/Sub
    /// <summary>
    /// 用于将信息发送到指定分区节点的频道，最终消息发布格式：1|message
    /// </summary>
    /// <param name="channel">频道名</param>
    /// <param name="message">消息文本</param>
    /// <returns></returns>
    public static Task<long> PublishAsync(string channel, string message) => Instance.PublishAsync(channel, message);
    /// <summary>
    /// 用于将信息发送到指定分区节点的频道，与 Publish 方法不同，不返回消息id头，即 1|
    /// </summary>
    /// <param name="channel">频道名</param>
    /// <param name="message">消息文本</param>
    /// <returns></returns>
    public static Task<long> PublishNoneMessageIdAsync(string channel, string message) => Instance.PublishNoneMessageIdAsync(channel, message);
    /// <summary>
    /// 查看所有订阅频道
    /// </summary>
    /// <param name="pattern"></param>
    /// <returns></returns>
    public static Task<string[]> PubSubChannelsAsync(string pattern) => Instance.PubSubChannelsAsync(pattern);
    /// <summary>
    /// 查看所有模糊订阅端的数量
    /// </summary>
    /// <returns></returns>
    [Obsolete("分区模式下，其他客户端的模糊订阅可能不会返回")]
    public static Task<long> PubSubNumPatAsync() => Instance.PubSubNumPatAsync();
    /// <summary>
    /// 查看所有订阅端的数量
    /// </summary>
    /// <param name="channels">频道</param>
    /// <returns></returns>
    [Obsolete("分区模式下，其他客户端的订阅可能不会返回")]
    public static Task<Dictionary<string, long>> PubSubNumSubAsync(params string[] channels) => Instance.PubSubNumSubAsync(channels);
    #endregion

    #region HyperLogLog
    /// <summary>
    /// 添加指定元素到 HyperLogLog
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="elements">元素</param>
    /// <returns></returns>
    public static Task<bool> PfAddAsync<T>(string key, params T[] elements) => Instance.PfAddAsync(key, elements);
    /// <summary>
    /// 返回给定 HyperLogLog 的基数估算值
    /// </summary>
    /// <param name="keys">不含prefix前辍</param>
    /// <returns></returns>
    [Obsolete("分区模式下，若keys分散在多个分区节点时，将报错")]
    public static Task<long> PfCountAsync(params string[] keys) => Instance.PfCountAsync(keys);
    /// <summary>
    /// 将多个 HyperLogLog 合并为一个 HyperLogLog
    /// </summary>
    /// <param name="destKey">新的 HyperLogLog，不含prefix前辍</param>
    /// <param name="sourceKeys">源 HyperLogLog，不含prefix前辍</param>
    /// <returns></returns>
    [Obsolete("分区模式下，若keys分散在多个分区节点时，将报错")]
    public static Task<bool> PfMergeAsync(string destKey, params string[] sourceKeys) => Instance.PfMergeAsync(destKey, sourceKeys);
    #endregion

    #region Sorted Set
    /// <summary>
    /// 向有序集合添加一个或多个成员，或者更新已存在成员的分数
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="scoreMembers">一个或多个成员分数</param>
    /// <returns></returns>
    public static Task<long> ZAddAsync(string key, params (decimal, object)[] scoreMembers) => Instance.ZAddAsync(key, scoreMembers);
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
    /// <param name="min">分数最小值 decimal.MinValue 1</param>
    /// <param name="max">分数最大值 decimal.MaxValue 10</param>
    /// <returns></returns>
    public static Task<long> ZCountAsync(string key, decimal min, decimal max) => Instance.ZCountAsync(key, min, max);
    /// <summary>
    /// 计算在有序集合中指定区间分数的成员数量
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="min">分数最小值 -inf (1 1</param>
    /// <param name="max">分数最大值 +inf (10 10</param>
    /// <returns></returns>
    public static Task<long> ZCountAsync(string key, string min, string max) => Instance.ZCountAsync(key, min, max);
    /// <summary>
    /// 有序集合中对指定成员的分数加上增量 increment
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="member">成员</param>
    /// <param name="increment">增量值(默认=1)</param>
    /// <returns></returns>
    public static Task<decimal> ZIncrByAsync(string key, string member, decimal increment = 1) => Instance.ZIncrByAsync(key, member, increment);

    /// <summary>
    /// 计算给定的一个或多个有序集的交集，将结果集存储在新的有序集合 destination 中
    /// </summary>
    /// <param name="destination">新的有序集合，不含prefix前辍</param>
    /// <param name="weights">使用 WEIGHTS 选项，你可以为 每个 给定有序集 分别 指定一个乘法因子。如果没有指定 WEIGHTS 选项，乘法因子默认设置为 1 。</param>
    /// <param name="aggregate">Sum | Min | Max</param>
    /// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
    /// <returns></returns>
    public static Task<long> ZInterStoreAsync(string destination, decimal[] weights, RedisAggregate aggregate, params string[] keys) => Instance.ZInterStoreAsync(destination, weights, aggregate, keys);

    /// <summary>
    /// 通过索引区间返回有序集合成指定区间内的成员
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <returns></returns>
    public static Task<string[]> ZRangeAsync(string key, long start, long stop) => Instance.ZRangeAsync(key, start, stop);
    /// <summary>
    /// 通过索引区间返回有序集合成指定区间内的成员
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <returns></returns>
    public static Task<T[]> ZRangeAsync<T>(string key, long start, long stop) => Instance.ZRangeAsync<T>(key, start, stop);
    /// <summary>
    /// 通过索引区间返回有序集合成指定区间内的成员和分数
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <returns></returns>
    public static Task<(string member, decimal score)[]> ZRangeWithScoresAsync(string key, long start, long stop) => Instance.ZRangeWithScoresAsync(key, start, stop);
    /// <summary>
    /// 通过索引区间返回有序集合成指定区间内的成员和分数
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <returns></returns>
    public static Task<(T member, decimal score)[]> ZRangeWithScoresAsync<T>(string key, long start, long stop) => Instance.ZRangeWithScoresAsync<T>(key, start, stop);

    /// <summary>
    /// 通过分数返回有序集合指定区间内的成员
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="min">分数最小值 decimal.MinValue 1</param>
    /// <param name="max">分数最大值 decimal.MaxValue 10</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<string[]> ZRangeByScoreAsync(string key, decimal min, decimal max, long? limit = null, long offset = 0) => Instance.ZRangeByScoreAsync(key, min, max, limit, offset);
    /// <summary>
    /// 通过分数返回有序集合指定区间内的成员
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="min">分数最小值 decimal.MinValue 1</param>
    /// <param name="max">分数最大值 decimal.MaxValue 10</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<T[]> ZRangeByScoreAsync<T>(string key, decimal min, decimal max, long? limit = null, long offset = 0) => Instance.ZRangeByScoreAsync<T>(key, min, max, limit, offset);
    /// <summary>
    /// 通过分数返回有序集合指定区间内的成员
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="min">分数最小值 -inf (1 1</param>
    /// <param name="max">分数最大值 +inf (10 10</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<string[]> ZRangeByScoreAsync(string key, string min, string max, long? limit = null, long offset = 0) => Instance.ZRangeByScoreAsync(key, min, max, limit, offset);
    /// <summary>
    /// 通过分数返回有序集合指定区间内的成员
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="min">分数最小值 -inf (1 1</param>
    /// <param name="max">分数最大值 +inf (10 10</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<T[]> ZRangeByScoreAsync<T>(string key, string min, string max, long? limit = null, long offset = 0) => Instance.ZRangeByScoreAsync<T>(key, min, max, limit, offset);

    /// <summary>
    /// 通过分数返回有序集合指定区间内的成员和分数
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="min">分数最小值 decimal.MinValue 1</param>
    /// <param name="max">分数最大值 decimal.MaxValue 10</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<(string member, decimal score)[]> ZRangeByScoreWithScoresAsync(string key, decimal min, decimal max, long? limit = null, long offset = 0) =>
           Instance.ZRangeByScoreWithScoresAsync(key, min, max, limit, offset);
    /// <summary>
    /// 通过分数返回有序集合指定区间内的成员和分数
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="min">分数最小值 decimal.MinValue 1</param>
    /// <param name="max">分数最大值 decimal.MaxValue 10</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<(T member, decimal score)[]> ZRangeByScoreWithScoresAsync<T>(string key, decimal min, decimal max, long? limit = null, long offset = 0) =>
           Instance.ZRangeByScoreWithScoresAsync<T>(key, min, max, limit, offset);
    /// <summary>
    /// 通过分数返回有序集合指定区间内的成员和分数
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="min">分数最小值 -inf (1 1</param>
    /// <param name="max">分数最大值 +inf (10 10</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<(string member, decimal score)[]> ZRangeByScoreWithScoresAsync(string key, string min, string max, long? limit = null, long offset = 0) =>
           Instance.ZRangeByScoreWithScoresAsync(key, min, max, limit, offset);
    /// <summary>
    /// 通过分数返回有序集合指定区间内的成员和分数
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="min">分数最小值 -inf (1 1</param>
    /// <param name="max">分数最大值 +inf (10 10</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<(T member, decimal score)[]> ZRangeByScoreWithScoresAsync<T>(string key, string min, string max, long? limit = null, long offset = 0) =>
           Instance.ZRangeByScoreWithScoresAsync<T>(key, min, max, limit, offset);

    /// <summary>
    /// 返回有序集合中指定成员的索引
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="member">成员</param>
    /// <returns></returns>
    public static Task<long?> ZRankAsync(string key, object member) => Instance.ZRankAsync(key, member);
    /// <summary>
    /// 移除有序集合中的一个或多个成员
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="member">一个或多个成员</param>
    /// <returns></returns>
    public static Task<long> ZRemAsync<T>(string key, params T[] member) => Instance.ZRemAsync(key, member);
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
    /// <param name="min">分数最小值 decimal.MinValue 1</param>
    /// <param name="max">分数最大值 decimal.MaxValue 10</param>
    /// <returns></returns>
    public static Task<long> ZRemRangeByScoreAsync(string key, decimal min, decimal max) => Instance.ZRemRangeByScoreAsync(key, min, max);
    /// <summary>
    /// 移除有序集合中给定的分数区间的所有成员
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="min">分数最小值 -inf (1 1</param>
    /// <param name="max">分数最大值 +inf (10 10</param>
    /// <returns></returns>
    public static Task<long> ZRemRangeByScoreAsync(string key, string min, string max) => Instance.ZRemRangeByScoreAsync(key, min, max);

    /// <summary>
    /// 返回有序集中指定区间内的成员，通过索引，分数从高到底
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <returns></returns>
    public static Task<string[]> ZRevRangeAsync(string key, long start, long stop) => Instance.ZRevRangeAsync(key, start, stop);
    /// <summary>
    /// 返回有序集中指定区间内的成员，通过索引，分数从高到底
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <returns></returns>
    public static Task<T[]> ZRevRangeAsync<T>(string key, long start, long stop) => Instance.ZRevRangeAsync<T>(key, start, stop);
    /// <summary>
    /// 返回有序集中指定区间内的成员和分数，通过索引，分数从高到底
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <returns></returns>
    public static Task<(string member, decimal score)[]> ZRevRangeWithScoresAsync(string key, long start, long stop) => Instance.ZRevRangeWithScoresAsync(key, start, stop);
    /// <summary>
    /// 返回有序集中指定区间内的成员和分数，通过索引，分数从高到底
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <returns></returns>
    public static Task<(T member, decimal score)[]> ZRevRangeWithScoresAsync<T>(string key, long start, long stop) => Instance.ZRevRangeWithScoresAsync<T>(key, start, stop);

    /// <summary>
    /// 返回有序集中指定分数区间内的成员，分数从高到低排序
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="max">分数最大值 decimal.MaxValue 10</param>
    /// <param name="min">分数最小值 decimal.MinValue 1</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<string[]> ZRevRangeByScoreAsync(string key, decimal max, decimal min, long? limit = null, long? offset = 0) => Instance.ZRevRangeByScoreAsync(key, max, min, limit, offset);
    /// <summary>
    /// 返回有序集中指定分数区间内的成员，分数从高到低排序
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="max">分数最大值 decimal.MaxValue 10</param>
    /// <param name="min">分数最小值 decimal.MinValue 1</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<T[]> ZRevRangeByScoreAsync<T>(string key, decimal max, decimal min, long? limit = null, long offset = 0) => Instance.ZRevRangeByScoreAsync<T>(key, max, min, limit, offset);
    /// <summary>
    /// 返回有序集中指定分数区间内的成员，分数从高到低排序
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="max">分数最大值 +inf (10 10</param>
    /// <param name="min">分数最小值 -inf (1 1</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<string[]> ZRevRangeByScoreAsync(string key, string max, string min, long? limit = null, long? offset = 0) => Instance.ZRevRangeByScoreAsync(key, max, min, limit, offset);
    /// <summary>
    /// 返回有序集中指定分数区间内的成员，分数从高到低排序
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="max">分数最大值 +inf (10 10</param>
    /// <param name="min">分数最小值 -inf (1 1</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<T[]> ZRevRangeByScoreAsync<T>(string key, string max, string min, long? limit = null, long offset = 0) => Instance.ZRevRangeByScoreAsync<T>(key, max, min, limit, offset);

    /// <summary>
    /// 返回有序集中指定分数区间内的成员和分数，分数从高到低排序
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="max">分数最大值 decimal.MaxValue 10</param>
    /// <param name="min">分数最小值 decimal.MinValue 1</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<(string member, decimal score)[]> ZRevRangeByScoreWithScoresAsync(string key, decimal max, decimal min, long? limit = null, long offset = 0) =>
           Instance.ZRevRangeByScoreWithScoresAsync(key, max, min, limit, offset);
    /// <summary>
    /// 返回有序集中指定分数区间内的成员和分数，分数从高到低排序
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="max">分数最大值 decimal.MaxValue 10</param>
    /// <param name="min">分数最小值 decimal.MinValue 1</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<(T member, decimal score)[]> ZRevRangeByScoreWithScoresAsync<T>(string key, decimal max, decimal min, long? limit = null, long offset = 0) =>
           Instance.ZRevRangeByScoreWithScoresAsync<T>(key, max, min, limit, offset);
    /// <summary>
    /// 返回有序集中指定分数区间内的成员和分数，分数从高到低排序
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="max">分数最大值 +inf (10 10</param>
    /// <param name="min">分数最小值 -inf (1 1</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<(string member, decimal score)[]> ZRevRangeByScoreWithScoresAsync(string key, string max, string min, long? limit = null, long offset = 0) =>
           Instance.ZRevRangeByScoreWithScoresAsync(key, max, min, limit, offset);
    /// <summary>
    /// 返回有序集中指定分数区间内的成员和分数，分数从高到低排序
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="max">分数最大值 +inf (10 10</param>
    /// <param name="min">分数最小值 -inf (1 1</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<(T member, decimal score)[]> ZRevRangeByScoreWithScoresAsync<T>(string key, string max, string min, long? limit = null, long offset = 0) =>
           Instance.ZRevRangeByScoreWithScoresAsync<T>(key, max, min, limit, offset);

    /// <summary>
    /// 返回有序集合中指定成员的排名，有序集成员按分数值递减(从大到小)排序
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="member">成员</param>
    /// <returns></returns>
    public static Task<long?> ZRevRankAsync(string key, object member) => Instance.ZRevRankAsync(key, member);
    /// <summary>
    /// 返回有序集中，成员的分数值
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="member">成员</param>
    /// <returns></returns>
    public static Task<decimal?> ZScoreAsync(string key, object member) => Instance.ZScoreAsync(key, member);

    /// <summary>
    /// 计算给定的一个或多个有序集的并集，将结果集存储在新的有序集合 destination 中
    /// </summary>
    /// <param name="destination">新的有序集合，不含prefix前辍</param>
    /// <param name="weights">使用 WEIGHTS 选项，你可以为 每个 给定有序集 分别 指定一个乘法因子。如果没有指定 WEIGHTS 选项，乘法因子默认设置为 1 。</param>
    /// <param name="aggregate">Sum | Min | Max</param>
    /// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
    /// <returns></returns>
    public static Task<long> ZUnionStoreAsync(string destination, decimal[] weights, RedisAggregate aggregate, params string[] keys) => Instance.ZUnionStoreAsync(destination, weights, aggregate, keys);

    /// <summary>
    /// 迭代有序集合中的元素
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="cursor">位置</param>
    /// <param name="pattern">模式</param>
    /// <param name="count">数量</param>
    /// <returns></returns>
    public static Task<RedisScan<(string member, decimal score)>> ZScanAsync(string key, long cursor, string pattern = null, long? count = null) =>
           Instance.ZScanAsync(key, cursor, pattern, count);
    /// <summary>
    /// 迭代有序集合中的元素
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="cursor">位置</param>
    /// <param name="pattern">模式</param>
    /// <param name="count">数量</param>
    /// <returns></returns>
    public static Task<RedisScan<(T member, decimal score)>> ZScanAsync<T>(string key, long cursor, string pattern = null, long? count = null) =>
           Instance.ZScanAsync<T>(key, cursor, pattern, count);

    /// <summary>
    /// 当有序集合的所有成员都具有相同的分值时，有序集合的元素会根据成员的字典序来进行排序，这个命令可以返回给定的有序集合键 key 中，值介于 min 和 max 之间的成员。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="min">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
    /// <param name="max">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<string[]> ZRangeByLexAsync(string key, string min, string max, long? limit = null, long offset = 0) =>
           Instance.ZRangeByLexAsync(key, min, max, limit, offset);
    /// <summary>
    /// 当有序集合的所有成员都具有相同的分值时，有序集合的元素会根据成员的字典序来进行排序，这个命令可以返回给定的有序集合键 key 中，值介于 min 和 max 之间的成员。
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="min">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
    /// <param name="max">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
    /// <param name="limit">返回多少成员</param>
    /// <param name="offset">返回条件偏移位置</param>
    /// <returns></returns>
    public static Task<T[]> ZRangeByLexAsync<T>(string key, string min, string max, long? limit = null, long offset = 0) =>
           Instance.ZRangeByLexAsync<T>(key, min, max, limit, offset);

    /// <summary>
    /// 当有序集合的所有成员都具有相同的分值时，有序集合的元素会根据成员的字典序来进行排序，这个命令可以返回给定的有序集合键 key 中，值介于 min 和 max 之间的成员。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="min">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
    /// <param name="max">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
    /// <returns></returns>
    public static Task<long> ZRemRangeByLexAsync(string key, string min, string max) =>
           Instance.ZRemRangeByLexAsync(key, min, max);
    /// <summary>
    /// 当有序集合的所有成员都具有相同的分值时，有序集合的元素会根据成员的字典序来进行排序，这个命令可以返回给定的有序集合键 key 中，值介于 min 和 max 之间的成员。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="min">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
    /// <param name="max">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
    /// <returns></returns>
    public static Task<long> ZLexCountAsync(string key, string min, string max) =>
           Instance.ZLexCountAsync(key, min, max);
    #endregion

    #region Set
    /// <summary>
    /// 向集合添加一个或多个成员
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="members">一个或多个成员</param>
    /// <returns></returns>
    public static Task<long> SAddAsync<T>(string key, params T[] members) => Instance.SAddAsync(key, members);
    /// <summary>
    /// 获取集合的成员数
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<long> SCardAsync(string key) => Instance.SCardAsync(key);
    /// <summary>
    /// 返回给定所有集合的差集
    /// </summary>
    /// <param name="keys">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<string[]> SDiffAsync(params string[] keys) => Instance.SDiffAsync(keys);
    /// <summary>
    /// 返回给定所有集合的差集
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="keys">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<T[]> SDiffAsync<T>(params string[] keys) => Instance.SDiffAsync<T>(keys);
    /// <summary>
    /// 返回给定所有集合的差集并存储在 destination 中
    /// </summary>
    /// <param name="destination">新的无序集合，不含prefix前辍</param>
    /// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
    /// <returns></returns>
    public static Task<long> SDiffStoreAsync(string destination, params string[] keys) => Instance.SDiffStoreAsync(destination, keys);
    /// <summary>
    /// 返回给定所有集合的交集
    /// </summary>
    /// <param name="keys">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<string[]> SInterAsync(params string[] keys) => Instance.SInterAsync(keys);
    /// <summary>
    /// 返回给定所有集合的交集
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="keys">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<T[]> SInterAsync<T>(params string[] keys) => Instance.SInterAsync<T>(keys);
    /// <summary>
    /// 返回给定所有集合的交集并存储在 destination 中
    /// </summary>
    /// <param name="destination">新的无序集合，不含prefix前辍</param>
    /// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
    /// <returns></returns>
    public static Task<long> SInterStoreAsync(string destination, params string[] keys) => Instance.SInterStoreAsync(destination, keys);
    /// <summary>
    /// 判断 member 元素是否是集合 key 的成员
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="member">成员</param>
    /// <returns></returns>
    public static Task<bool> SIsMemberAsync(string key, object member) => Instance.SIsMemberAsync(key, member);
    /// <summary>
    /// 返回集合中的所有成员
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<string[]> SMembersAsync(string key) => Instance.SMembersAsync(key);
    /// <summary>
    /// 返回集合中的所有成员
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<T[]> SMembersAsync<T>(string key) => Instance.SMembersAsync<T>(key);
    /// <summary>
    /// 将 member 元素从 source 集合移动到 destination 集合
    /// </summary>
    /// <param name="source">无序集合key，不含prefix前辍</param>
    /// <param name="destination">目标无序集合key，不含prefix前辍</param>
    /// <param name="member">成员</param>
    /// <returns></returns>
    public static Task<bool> SMoveAsync(string source, string destination, object member) => Instance.SMoveAsync(source, destination, member);
    /// <summary>
    /// 移除并返回集合中的一个随机元素
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<string> SPopAsync(string key) => Instance.SPopAsync(key);
    /// <summary>
    /// 移除并返回集合中的一个随机元素
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<T> SPopAsync<T>(string key) => Instance.SPopAsync<T>(key);
    /// <summary>
    /// [redis-server 3.2] 移除并返回集合中的一个或多个随机元素
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="count">移除并返回的个数</param>
    /// <returns></returns>
    public static Task<string[]> SPopAsync(string key, long count) => Instance.SPopAsync(key, count);
    /// <summary>
    /// [redis-server 3.2] 移除并返回集合中的一个或多个随机元素
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="count">移除并返回的个数</param>
    /// <returns></returns>
    public static Task<T[]> SPopAsync<T>(string key, long count) => Instance.SPopAsync<T>(key, count);
    /// <summary>
    /// 返回集合中的一个随机元素
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<string> SRandMemberAsync(string key) => Instance.SRandMemberAsync(key);
    /// <summary>
    /// 返回集合中的一个随机元素
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<T> SRandMemberAsync<T>(string key) => Instance.SRandMemberAsync<T>(key);
    /// <summary>
    /// 返回集合中一个或多个随机数的元素
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="count">返回个数</param>
    /// <returns></returns>
    public static Task<string[]> SRandMembersAsync(string key, int count = 1) => Instance.SRandMembersAsync(key, count);
    /// <summary>
    /// 返回集合中一个或多个随机数的元素
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="count">返回个数</param>
    /// <returns></returns>
    public static Task<T[]> SRandMembersAsync<T>(string key, int count = 1) => Instance.SRandMembersAsync<T>(key, count);
    /// <summary>
    /// 移除集合中一个或多个成员
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="members">一个或多个成员</param>
    /// <returns></returns>
    public static Task<long> SRemAsync<T>(string key, params T[] members) => Instance.SRemAsync(key, members);
    /// <summary>
    /// 返回所有给定集合的并集
    /// </summary>
    /// <param name="keys">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<string[]> SUnionAsync(params string[] keys) => Instance.SUnionAsync(keys);
    /// <summary>
    /// 返回所有给定集合的并集
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="keys">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<T[]> SUnionAsync<T>(params string[] keys) => Instance.SUnionAsync<T>(keys);
    /// <summary>
    /// 所有给定集合的并集存储在 destination 集合中
    /// </summary>
    /// <param name="destination">新的无序集合，不含prefix前辍</param>
    /// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
    /// <returns></returns>
    public static Task<long> SUnionStoreAsync(string destination, params string[] keys) => Instance.SUnionStoreAsync(destination, keys);
    /// <summary>
    /// 迭代集合中的元素
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="cursor">位置</param>
    /// <param name="pattern">模式</param>
    /// <param name="count">数量</param>
    /// <returns></returns>
    public static Task<RedisScan<string>> SScanAsync(string key, long cursor, string pattern = null, long? count = null) => Instance.SScanAsync(key, cursor, pattern, count);
    /// <summary>
    /// 迭代集合中的元素
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="cursor">位置</param>
    /// <param name="pattern">模式</param>
    /// <param name="count">数量</param>
    /// <returns></returns>
    public static Task<RedisScan<T>> SScanAsync<T>(string key, long cursor, string pattern = null, long? count = null) => Instance.SScanAsync<T>(key, cursor, pattern, count);
    #endregion

    #region List
    /// <summary>
    /// 通过索引获取列表中的元素
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="index">索引</param>
    /// <returns></returns>
    public static Task<string> LIndexAsync(string key, long index) => Instance.LIndexAsync(key, index);
    /// <summary>
    /// 通过索引获取列表中的元素
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="index">索引</param>
    /// <returns></returns>
    public static Task<T> LIndexAsync<T>(string key, long index) => Instance.LIndexAsync<T>(key, index);
    /// <summary>
    /// 在列表中的元素前面插入元素
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="pivot">列表的元素</param>
    /// <param name="value">新元素</param>
    /// <returns></returns>
    public static Task<long> LInsertBeforeAsync(string key, object pivot, object value) => Instance.LInsertBeforeAsync(key, pivot, value);
    /// <summary>
    /// 在列表中的元素后面插入元素
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="pivot">列表的元素</param>
    /// <param name="value">新元素</param>
    /// <returns></returns>
    public static Task<long> LInsertAfterAsync(string key, object pivot, object value) => Instance.LInsertAfterAsync(key, pivot, value);
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
    /// 移出并获取列表的第一个元素
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<T> LPopAsync<T>(string key) => Instance.LPopAsync<T>(key);
    /// <summary>
    /// 将一个或多个值插入到列表头部
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="value">一个或多个值</param>
    /// <returns>执行 LPUSH 命令后，列表的长度</returns>
    public static Task<long> LPushAsync<T>(string key, params T[] value) => Instance.LPushAsync(key, value);
    /// <summary>
    /// 将一个值插入到已存在的列表头部
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="value">值</param>
    /// <returns>执行 LPUSHX 命令后，列表的长度。</returns>
    public static Task<long> LPushXAsync(string key, object value) => Instance.LPushXAsync(key, value);
    /// <summary>
    /// 获取列表指定范围内的元素
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <returns></returns>
    public static Task<string[]> LRangeAsync(string key, long start, long stop) => Instance.LRangeAsync(key, start, stop);
    /// <summary>
    /// 获取列表指定范围内的元素
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <returns></returns>
    public static Task<T[]> LRangeAsync<T>(string key, long start, long stop) => Instance.LRangeAsync<T>(key, start, stop);
    /// <summary>
    /// 根据参数 count 的值，移除列表中与参数 value 相等的元素
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="count">移除的数量，大于0时从表头删除数量count，小于0时从表尾删除数量-count，等于0移除所有</param>
    /// <param name="value">元素</param>
    /// <returns></returns>
    public static Task<long> LRemAsync(string key, long count, object value) => Instance.LRemAsync(key, count, value);
    /// <summary>
    /// 通过索引设置列表元素的值
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="index">索引</param>
    /// <param name="value">值</param>
    /// <returns></returns>
    public static Task<bool> LSetAsync(string key, long index, object value) => Instance.LSetAsync(key, index, value);
    /// <summary>
    /// 对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <returns></returns>
    public static Task<bool> LTrimAsync(string key, long start, long stop) => Instance.LTrimAsync(key, start, stop);
    /// <summary>
    /// 移除并获取列表最后一个元素
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<string> RPopAsync(string key) => Instance.RPopAsync(key);
    /// <summary>
    /// 移除并获取列表最后一个元素
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<T> RPopAsync<T>(string key) => Instance.RPopAsync<T>(key);
    /// <summary>
    /// 将列表 source 中的最后一个元素(尾元素)弹出，并返回给客户端。
    /// 将 source 弹出的元素插入到列表 destination ，作为 destination 列表的的头元素。
    /// </summary>
    /// <param name="source">源key，不含prefix前辍</param>
    /// <param name="destination">目标key，不含prefix前辍</param>
    /// <returns></returns>
    public static Task<string> RPopLPushAsync(string source, string destination) => Instance.RPopLPushAsync(source, destination);
    /// <summary>
    /// 将列表 source 中的最后一个元素(尾元素)弹出，并返回给客户端。
    /// 将 source 弹出的元素插入到列表 destination ，作为 destination 列表的的头元素。
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="source">源key，不含prefix前辍</param>
    /// <param name="destination">目标key，不含prefix前辍</param>
    /// <returns></returns>
    public static Task<T> RPopLPushAsync<T>(string source, string destination) => Instance.RPopLPushAsync<T>(source, destination);
    /// <summary>
    /// 在列表中添加一个或多个值
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="value">一个或多个值</param>
    /// <returns>执行 RPUSH 命令后，列表的长度</returns>
    public static Task<long> RPushAsync<T>(string key, params T[] value) => Instance.RPushAsync(key, value);
    /// <summary>
    /// 为已存在的列表添加值
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="value">一个或多个值</param>
    /// <returns>执行 RPUSHX 命令后，列表的长度</returns>
    public static Task<long> RPushXAsync(string key, object value) => Instance.RPushAsync(key, value);
    #endregion

    #region Hash
    /// <summary>
    /// 删除一个或多个哈希表字段
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="fields">字段</param>
    /// <returns></returns>
    public static Task<long> HDelAsync(string key, params string[] fields) => Instance.HDelAsync(key, fields);
    /// <summary>
    /// 查看哈希表 key 中，指定的字段是否存在
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="field">字段</param>
    /// <returns></returns>
    public static Task<bool> HExistsAsync(string key, string field) => Instance.HExistsAsync(key, field);
    /// <summary>
    /// 获取存储在哈希表中指定字段的值
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="field">字段</param>
    /// <returns></returns>
    public static Task<string> HGetAsync(string key, string field) => Instance.HGetAsync(key, field);
    /// <summary>
    /// 获取存储在哈希表中指定字段的值
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="field">字段</param>
    /// <returns></returns>
    public static Task<T> HGetAsync<T>(string key, string field) => Instance.HGetAsync<T>(key, field);
    /// <summary>
    /// 获取在哈希表中指定 key 的所有字段和值
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<Dictionary<string, string>> HGetAllAsync(string key) => Instance.HGetAllAsync(key);
    /// <summary>
    /// 获取在哈希表中指定 key 的所有字段和值
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<Dictionary<string, T>> HGetAllAsync<T>(string key) => Instance.HGetAllAsync<T>(key);
    /// <summary>
    /// 为哈希表 key 中的指定字段的整数值加上增量 increment
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="field">字段</param>
    /// <param name="value">增量值(默认=1)</param>
    /// <returns></returns>
    public static Task<long> HIncrByAsync(string key, string field, long value = 1) => Instance.HIncrByAsync(key, field, value);
    /// <summary>
    /// 为哈希表 key 中的指定字段的整数值加上增量 increment
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="field">字段</param>
    /// <param name="value">增量值(默认=1)</param>
    /// <returns></returns>
    public static Task<decimal> HIncrByFloatAsync(string key, string field, decimal value = 1) => Instance.HIncrByFloatAsync(key, field, value);
    /// <summary>
    /// 获取所有哈希表中的字段
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<string[]> HKeysAsync(string key) => Instance.HKeysAsync(key);
    /// <summary>
    /// 获取哈希表中字段的数量
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<long> HLenAsync(string key) => Instance.HLenAsync(key);
    /// <summary>
    /// 获取存储在哈希表中多个字段的值
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="fields">字段</param>
    /// <returns></returns>
    public static Task<string[]> HMGetAsync(string key, params string[] fields) => Instance.HMGetAsync(key, fields);
    /// <summary>
    /// 获取存储在哈希表中多个字段的值
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="fields">一个或多个字段</param>
    /// <returns></returns>
    public static Task<T[]> HMGetAsync<T>(string key, params string[] fields) => Instance.HMGetAsync<T>(key, fields);
    /// <summary>
    /// 同时将多个 field-value (域-值)对设置到哈希表 key 中
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="keyValues">key1 value1 [key2 value2]</param>
    /// <returns></returns>
    public static Task<bool> HMSetAsync(string key, params object[] keyValues) => Instance.HMSetAsync(key, keyValues);
    /// <summary>
    /// 将哈希表 key 中的字段 field 的值设为 value
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="field">字段</param>
    /// <param name="value">值</param>
    /// <returns>如果字段是哈希表中的一个新建字段，并且值设置成功，返回true。如果哈希表中域字段已经存在且旧值已被新值覆盖，返回false。</returns>
    public static Task<bool> HSetAsync(string key, string field, object value) => Instance.HSetAsync(key, field, value);
    /// <summary>
    /// 只有在字段 field 不存在时，设置哈希表字段的值
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="field">字段</param>
    /// <param name="value">值(string 或 byte[])</param>
    /// <returns></returns>
    public static Task<bool> HSetNxAsync(string key, string field, object value) => Instance.HSetNxAsync(key, field, value);
    /// <summary>
    /// 获取哈希表中所有值
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<string[]> HValsAsync(string key) => Instance.HValsAsync(key);
    /// <summary>
    /// 获取哈希表中所有值
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<T[]> HValsAsync<T>(string key) => Instance.HValsAsync<T>(key);
    /// <summary>
    /// 迭代哈希表中的键值对
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="cursor">位置</param>
    /// <param name="pattern">模式</param>
    /// <param name="count">数量</param>
    /// <returns></returns>
    public static Task<RedisScan<(string field, string value)>> HScanAsync(string key, long cursor, string pattern = null, long? count = null) =>
           Instance.HScanAsync(key, cursor, pattern, count);
    /// <summary>
    /// 迭代哈希表中的键值对
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="cursor">位置</param>
    /// <param name="pattern">模式</param>
    /// <param name="count">数量</param>
    /// <returns></returns>
    public static Task<RedisScan<(string field, T value)>> HScanAsync<T>(string key, long cursor, string pattern = null, long? count = null) =>
           Instance.HScanAsync<T>(key, cursor, pattern, count);

    /// <summary>
    /// [redis-server 5.0.0] 删除并返回有序集合key中的最多count个具有最高得分的成员。如未指定，count的默认值为1。指定一个大于有序集合的基数的count不会产生错误。 当返回多个元素时候，得分最高的元素将是第一个元素，然后是分数较低的元素。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="count">数量</param>
    /// <returns></returns>
    public static Task<(string member, decimal score)[]> ZPopMaxAsync(string key, long count) =>
        Instance.ZPopMaxAsync(key, count);

    /// <summary>
    /// [redis-server 5.0.0] 删除并返回有序集合key中的最多count个具有最高得分的成员。如未指定，count的默认值为1。指定一个大于有序集合的基数的count不会产生错误。 当返回多个元素时候，得分最高的元素将是第一个元素，然后是分数较低的元素。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="count">数量</param>
    /// <returns></returns>
    public static Task<(T member, decimal score)[]> ZPopMaxAsync<T>(string key, long count) =>
        Instance.ZPopMaxAsync<T>(key, count);

    /// <summary>
    /// [redis-server 5.0.0] 删除并返回有序集合key中的最多count个具有最低得分的成员。如未指定，count的默认值为1。指定一个大于有序集合的基数的count不会产生错误。 当返回多个元素时候，得分最低的元素将是第一个元素，然后是分数较高的元素。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="count">数量</param>
    /// <returns></returns>
    public static Task<(string member, decimal score)[]> ZPopMinAsync(string key, long count) =>
        Instance.ZPopMinAsync(key, count);

    /// <summary>
    /// [redis-server 5.0.0] 删除并返回有序集合key中的最多count个具有最低得分的成员。如未指定，count的默认值为1。指定一个大于有序集合的基数的count不会产生错误。 当返回多个元素时候，得分最低的元素将是第一个元素，然后是分数较高的元素。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="count">数量</param>
    /// <returns></returns>
    public static Task<(T member, decimal score)[]> ZPopMinAsync<T>(string key, long count) =>
        Instance.ZPopMinAsync<T>(key, count);
    #endregion

    #region String
    /// <summary>
    /// 如果 key 已经存在并且是一个字符串， APPEND 命令将指定的 value 追加到该 key 原来值（value）的末尾
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="value">字符串</param>
    /// <returns>追加指定值之后， key 中字符串的长度</returns>
    public static Task<long> AppendAsync(string key, object value) => Instance.AppendAsync(key, value);
    /// <summary>
    /// 计算给定位置被设置为 1 的比特位的数量
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="start">开始位置</param>
    /// <param name="end">结束位置</param>
    /// <returns></returns>
    public static Task<long> BitCountAsync(string key, long start, long end) => Instance.BitCountAsync(key, start, end);
    /// <summary>
    /// 对一个或多个保存二进制位的字符串 key 进行位元操作，并将结果保存到 destkey 上
    /// </summary>
    /// <param name="op">And | Or | XOr | Not</param>
    /// <param name="destKey">不含prefix前辍</param>
    /// <param name="keys">不含prefix前辍</param>
    /// <returns>保存到 destkey 的长度，和输入 key 中最长的长度相等</returns>
    public static Task<long> BitOpAsync(RedisBitOp op, string destKey, params string[] keys) => Instance.BitOpAsync(op, destKey, keys);
    /// <summary>
    /// 对 key 所储存的值，查找范围内第一个被设置为1或者0的bit位
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="bit">查找值</param>
    /// <param name="start">开始位置，-1是最后一个，-2是倒数第二个</param>
    /// <param name="end">结果位置，-1是最后一个，-2是倒数第二个</param>
    /// <returns>返回范围内第一个被设置为1或者0的bit位</returns>
    public static Task<long> BitPosAsync(string key, bool bit, long? start = null, long? end = null) => Instance.BitPosAsync(key, bit, start, end);
    /// <summary>
    /// 获取指定 key 的值
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<string> GetAsync(string key) => Instance.GetAsync(key);
    /// <summary>
    /// 获取指定 key 的值
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<T> GetAsync<T>(string key) => Instance.GetAsync<T>(key);
    /// <summary>
    /// 对 key 所储存的值，获取指定偏移量上的位(bit)
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="offset">偏移量</param>
    /// <returns></returns>
    public static Task<bool> GetBitAsync(string key, uint offset) => Instance.GetBitAsync(key, offset);
    /// <summary>
    /// 返回 key 中字符串值的子字符
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <param name="end">结束位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <returns></returns>
    public static Task<string> GetRangeAsync(string key, long start, long end) => Instance.GetRangeAsync(key, start, end);
    /// <summary>
    /// 返回 key 中字符串值的子字符
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <param name="end">结束位置，0表示第一个元素，-1表示最后一个元素</param>
    /// <returns></returns>
    public static Task<T> GetRangeAsync<T>(string key, long start, long end) => Instance.GetRangeAsync<T>(key, start, end);
    /// <summary>
    /// 将给定 key 的值设为 value ，并返回 key 的旧值(old value)
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="value">值</param>
    /// <returns></returns>
    public static Task<string> GetSetAsync(string key, object value) => Instance.GetSetAsync(key, value);
    /// <summary>
    /// 将给定 key 的值设为 value ，并返回 key 的旧值(old value)
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="value">值</param>
    /// <returns></returns>
    public static Task<T> GetSetAsync<T>(string key, object value) => Instance.GetSetAsync<T>(key, value);
    /// <summary>
    /// 将 key 所储存的值加上给定的增量值（increment）
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="value">增量值(默认=1)</param>
    /// <returns></returns>
    public static Task<long> IncrByAsync(string key, long value = 1) => Instance.IncrByAsync(key, value);
    /// <summary>
    /// 将 key 所储存的值加上给定的浮点增量值（increment）
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="value">增量值(默认=1)</param>
    /// <returns></returns>
    public static Task<decimal> IncrByFloatAsync(string key, decimal value = 1) => Instance.IncrByFloatAsync(key, value);
    /// <summary>
    /// 获取多个指定 key 的值(数组)
    /// </summary>
    /// <param name="keys">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<string[]> MGetAsync(params string[] keys) => Instance.MGetAsync(keys);
    /// <summary>
    /// 获取多个指定 key 的值(数组)
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="keys">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<T[]> MGetAsync<T>(params string[] keys) => Instance.MGetAsync<T>(keys);
    /// <summary>
    /// 同时设置一个或多个 key-value 对
    /// </summary>
    /// <param name="keyValues">key1 value1 [key2 value2]</param>
    /// <returns></returns>
    public static Task<bool> MSetAsync(params object[] keyValues) => Instance.MSetAsync(keyValues);
    /// <summary>
    /// 同时设置一个或多个 key-value 对，当且仅当所有给定 key 都不存在
    /// </summary>
    /// <param name="keyValues">key1 value1 [key2 value2]</param>
    /// <returns></returns>
    public static Task<bool> MSetNxAsync(params object[] keyValues) => Instance.MSetNxAsync(keyValues);
    /// <summary>
    /// 设置指定 key 的值，所有写入参数object都支持string | byte[] | 数值 | 对象
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="value">值</param>
    /// <param name="expireSeconds">过期(秒单位)</param>
    /// <param name="exists">Nx, Xx</param>
    /// <returns></returns>
    public static Task<bool> SetAsync(string key, object value, int expireSeconds = -1, RedisExistence? exists = null) => Instance.SetAsync(key, value, expireSeconds, exists);
    public static Task<bool> SetAsync(string key, object value, TimeSpan expire, RedisExistence? exists = null) => Instance.SetAsync(key, value, expire, exists);
    /// <summary>
    /// 对 key 所储存的字符串值，设置或清除指定偏移量上的位(bit)
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="offset">偏移量</param>
    /// <param name="value">值</param>
    /// <returns></returns>
    public static Task<bool> SetBitAsync(string key, uint offset, bool value) => Instance.SetBitAsync(key, offset, value);
    /// <summary>
    /// 只有在 key 不存在时设置 key 的值
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="value">值</param>
    /// <returns></returns>
    public static Task<bool> SetNxAsync(string key, object value) => Instance.SetNxAsync(key, value);
    /// <summary>
    /// 用 value 参数覆写给定 key 所储存的字符串值，从偏移量 offset 开始
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="offset">偏移量</param>
    /// <param name="value">值</param>
    /// <returns>被修改后的字符串长度</returns>
    public static Task<long> SetRangeAsync(string key, uint offset, object value) => Instance.SetRangeAsync(key, offset, value);
    /// <summary>
    /// 返回 key 所储存的字符串值的长度
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<long> StrLenAsync(string key) => Instance.StrLenAsync(key);
    #endregion

    #region Key
    /// <summary>
    /// 用于在 key 存在时删除 key
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<long> DelAsync(params string[] key) => Instance.DelAsync(key);
    /// <summary>
    /// 序列化给定 key ，并返回被序列化的值
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<byte[]> DumpAsync(string key) => Instance.DumpAsync(key);
    /// <summary>
    /// 检查给定 key 是否存在
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<bool> ExistsAsync(string key) => Instance.ExistsAsync(key);
    /// <summary>
    /// [redis-server 3.0] 检查给定多个 key 是否存在
    /// </summary>
    /// <param name="keys">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<long> ExistsAsync(string[] keys) => Instance.ExistsAsync(keys);
    /// <summary>
    /// 为给定 key 设置过期时间
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="seconds">过期秒数</param>
    /// <returns></returns>
    public static Task<bool> ExpireAsync(string key, int seconds) => Instance.ExpireAsync(key, seconds);
    /// <summary>
    /// 为给定 key 设置过期时间
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="expire">过期时间</param>
    /// <returns></returns>
    public static Task<bool> ExpireAsync(string key, TimeSpan expire) => Instance.ExpireAsync(key, expire);
    /// <summary>
    /// 为给定 key 设置过期时间
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="expire">过期时间</param>
    /// <returns></returns>
    public static Task<bool> ExpireAtAsync(string key, DateTime expire) => Instance.ExpireAtAsync(key, expire);
    /// <summary>
    /// 查找所有分区节点中符合给定模式(pattern)的 key
    /// </summary>
    /// <param name="pattern">如：runoob*</param>
    /// <returns></returns>
    public static Task<string[]> KeysAsync(string pattern) => Instance.KeysAsync(pattern);
    /// <summary>
    /// 将当前数据库的 key 移动到给定的数据库 db 当中
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="database">数据库</param>
    /// <returns></returns>
    public static Task<bool> MoveAsync(string key, int database) => Instance.MoveAsync(key, database);
    /// <summary>
    /// 该返回给定 key 锁储存的值所使用的内部表示(representation)
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<string> ObjectEncodingAsync(string key) => Instance.ObjectEncodingAsync(key);
    /// <summary>
    /// 该返回给定 key 引用所储存的值的次数。此命令主要用于除错
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<long?> ObjectRefCountAsync(string key) => Instance.ObjectRefCountAsync(key);
    /// <summary>
    /// 返回给定 key 自储存以来的空转时间(idle， 没有被读取也没有被写入)，以秒为单位
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<long?> ObjectIdleTimeAsync(string key) => Instance.ObjectIdleTimeAsync(key);
    /// <summary>
    /// 移除 key 的过期时间，key 将持久保持
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<bool> PersistAsync(string key) => Instance.PersistAsync(key);
    /// <summary>
    /// 为给定 key 设置过期时间（毫秒）
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="milliseconds">过期毫秒数</param>
    /// <returns></returns>
    public static Task<bool> PExpireAsync(string key, int milliseconds) => Instance.PExpireAsync(key, milliseconds);
    /// <summary>
    /// 为给定 key 设置过期时间（毫秒）
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="expire">过期时间</param>
    /// <returns></returns>
    public static Task<bool> PExpireAsync(string key, TimeSpan expire) => Instance.PExpireAsync(key, expire);
    /// <summary>
    /// 为给定 key 设置过期时间（毫秒）
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="expire">过期时间</param>
    /// <returns></returns>
    public static Task<bool> PExpireAtAsync(string key, DateTime expire) => Instance.PExpireAtAsync(key, expire);
    /// <summary>
    /// 以毫秒为单位返回 key 的剩余的过期时间
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<long> PTtlAsync(string key) => Instance.PTtlAsync(key);
    /// <summary>
    /// 从所有节点中随机返回一个 key
    /// </summary>
    /// <returns>返回的 key 如果包含 prefix前辍，则会去除后返回</returns>
    public static Task<string> RandomKeyAsync() => Instance.RandomKeyAsync();
    /// <summary>
    /// 修改 key 的名称
    /// </summary>
    /// <param name="key">旧名称，不含prefix前辍</param>
    /// <param name="newKey">新名称，不含prefix前辍</param>
    /// <returns></returns>
    public static Task<bool> RenameAsync(string key, string newKey) => Instance.RenameAsync(key, newKey);
    /// <summary>
    /// 修改 key 的名称
    /// </summary>
    /// <param name="key">旧名称，不含prefix前辍</param>
    /// <param name="newKey">新名称，不含prefix前辍</param>
    /// <returns></returns>
    public static Task<bool> RenameNxAsync(string key, string newKey) => Instance.RenameNxAsync(key, newKey);
    /// <summary>
    /// 反序列化给定的序列化值，并将它和给定的 key 关联
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="serializedValue">序列化值</param>
    /// <returns></returns>
    public static Task<bool> RestoreAsync(string key, byte[] serializedValue) => Instance.RestoreAsync(key, serializedValue);
    /// <summary>
    /// 反序列化给定的序列化值，并将它和给定的 key 关联
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="ttlMilliseconds">毫秒为单位为 key 设置生存时间</param>
    /// <param name="serializedValue">序列化值</param>
    /// <returns></returns>
    public static Task<bool> RestoreAsync(string key, long ttlMilliseconds, byte[] serializedValue) => Instance.RestoreAsync(key, ttlMilliseconds, serializedValue);
    /// <summary>
    /// 返回给定列表、集合、有序集合 key 中经过排序的元素，参数资料：http://doc.redisfans.com/key/sort.html
    /// </summary>
    /// <param name="key">列表、集合、有序集合，不含prefix前辍</param>
    /// <param name="count">数量</param>
    /// <param name="offset">偏移量</param>
    /// <param name="by">排序字段</param>
    /// <param name="dir">排序方式</param>
    /// <param name="isAlpha">对字符串或数字进行排序</param>
    /// <param name="get">根据排序的结果来取出相应的键值</param>
    /// <returns></returns>
    public static Task<string[]> SortAsync(string key, long? count = null, long offset = 0, string by = null, RedisSortDir? dir = null, bool? isAlpha = null, params string[] get) =>
           Instance.SortAsync(key, count, offset, by, dir, isAlpha, get);
    /// <summary>
    /// 保存给定列表、集合、有序集合 key 中经过排序的元素，参数资料：http://doc.redisfans.com/key/sort.html
    /// </summary>
    /// <param name="key">列表、集合、有序集合，不含prefix前辍</param>
    /// <param name="destination">目标key，不含prefix前辍</param>
    /// <param name="count">数量</param>
    /// <param name="offset">偏移量</param>
    /// <param name="by">排序字段</param>
    /// <param name="dir">排序方式</param>
    /// <param name="isAlpha">对字符串或数字进行排序</param>
    /// <param name="get">根据排序的结果来取出相应的键值</param>
    /// <returns></returns>
    public static Task<long> SortAndStoreAsync(string key, string destination, long? count = null, long offset = 0, string by = null, RedisSortDir? dir = null, bool? isAlpha = null, params string[] get) =>
           Instance.SortAndStoreAsync(key, destination, count, offset, by, dir, isAlpha, get);
    /// <summary>
    /// 以秒为单位，返回给定 key 的剩余生存时间
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<long> TtlAsync(string key) => Instance.TtlAsync(key);
    /// <summary>
    /// 返回 key 所储存的值的类型
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <returns></returns>
    public static Task<KeyType> TypeAsync(string key) => Instance.TypeAsync(key);
    /// <summary>
    /// 迭代当前数据库中的数据库键
    /// </summary>
    /// <param name="cursor">位置</param>
    /// <param name="pattern">模式</param>
    /// <param name="count">数量</param>
    /// <returns></returns>
    public static Task<RedisScan<string>> ScanAsync(long cursor, string pattern = null, long? count = null) => Instance.ScanAsync(cursor, pattern, count);
    /// <summary>
    /// 迭代当前数据库中的数据库键
    /// </summary>
    /// <typeparam name="T">byte[] 或其他类型</typeparam>
    /// <param name="cursor">位置</param>
    /// <param name="pattern">模式</param>
    /// <param name="count">数量</param>
    /// <returns></returns>
    public static Task<RedisScan<T>> ScanAsync<T>(long cursor, string pattern = null, long? count = null) => Instance.ScanAsync<T>(cursor, pattern, count);
    #endregion

    #region Geo redis-server 3.2
    /// <summary>
    /// 将指定的地理空间位置（纬度、经度、成员）添加到指定的key中。这些数据将会存储到sorted set这样的目的是为了方便使用GEORADIUS或者GEORADIUSBYMEMBER命令对数据进行半径查询等操作。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="longitude">经度</param>
    /// <param name="latitude">纬度</param>
    /// <param name="member">成员</param>
    /// <returns>是否成功</returns>
    public static Task<bool> GeoAddAsync(string key, decimal longitude, decimal latitude, object member) => Instance.GeoAddAsync(key, longitude, latitude, member);
    /// <summary>
    /// 将指定的地理空间位置（纬度、经度、成员）添加到指定的key中。这些数据将会存储到sorted set这样的目的是为了方便使用GEORADIUS或者GEORADIUSBYMEMBER命令对数据进行半径查询等操作。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="values">批量添加的值</param>
    /// <returns>添加到sorted set元素的数目，但不包括已更新score的元素。</returns>
    public static Task<long> GeoAddAsync(string key, params (decimal longitude, decimal latitude, object member)[] values) => Instance.GeoAddAsync(key, values);
    /// <summary>
    /// 返回两个给定位置之间的距离。如果两个位置之间的其中一个不存在， 那么命令返回空值。GEODIST 命令在计算距离时会假设地球为完美的球形， 在极限情况下， 这一假设最大会造成 0.5% 的误差。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="member1">成员1</param>
    /// <param name="member2">成员2</param>
    /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    /// <returns>计算出的距离会以双精度浮点数的形式被返回。 如果给定的位置元素不存在， 那么命令返回空值。</returns>
    public static Task<decimal?> GeoDistAsync(string key, object member1, object member2, GeoUnit unit = GeoUnit.m) => Instance.GeoDistAsync(key, member1, member2, unit);
    /// <summary>
    /// 返回一个或多个位置元素的 Geohash 表示。通常使用表示位置的元素使用不同的技术，使用Geohash位置52点整数编码。由于编码和解码过程中所使用的初始最小和最大坐标不同，编码的编码也不同于标准。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="members">多个查询的成员</param>
    /// <returns>一个数组， 数组的每个项都是一个 geohash 。 命令返回的 geohash 的位置与用户给定的位置元素的位置一一对应。</returns>
    public static Task<string[]> GeoHashAsync(string key, object[] members) => Instance.GeoHashAsync(key, members);
    /// <summary>
    /// 从key里返回所有给定位置元素的位置（经度和纬度）。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="members">多个查询的成员</param>
    /// <returns>GEOPOS 命令返回一个数组， 数组中的每个项都由两个元素组成： 第一个元素为给定位置元素的经度， 而第二个元素则为给定位置元素的纬度。当给定的位置元素不存在时， 对应的数组项为空值。</returns>
    public static Task<(decimal longitude, decimal latitude)?[]> GeoPosAsync(string key, object[] members) => Instance.GeoPosAsync(key, members);

    /// <summary>
    /// 以给定的经纬度为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="longitude">经度</param>
    /// <param name="latitude">纬度</param>
    /// <param name="radius">距离</param>
    /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    /// <param name="sorting">排序</param>
    /// <returns></returns>
    public static Task<string[]> GeoRadiusAsync(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
        Instance.GeoRadiusAsync(key, longitude, latitude, radius, unit, count, sorting);
    /// <summary>
    /// 以给定的经纬度为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="longitude">经度</param>
    /// <param name="latitude">纬度</param>
    /// <param name="radius">距离</param>
    /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    /// <param name="sorting">排序</param>
    /// <returns></returns>
    public static Task<T[]> GeoRadiusAsync<T>(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
        Instance.GeoRadiusAsync<T>(key, longitude, latitude, radius, unit, count, sorting);

    /// <summary>
    /// 以给定的经纬度为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含距离）。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="longitude">经度</param>
    /// <param name="latitude">纬度</param>
    /// <param name="radius">距离</param>
    /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    /// <param name="sorting">排序</param>
    /// <returns></returns>
    public static Task<(string member, decimal dist)[]> GeoRadiusWithDistAsync(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
        Instance.GeoRadiusWithDistAsync(key, longitude, latitude, radius, unit, count, sorting);
    /// <summary>
    /// 以给定的经纬度为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含距离）。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="longitude">经度</param>
    /// <param name="latitude">纬度</param>
    /// <param name="radius">距离</param>
    /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    /// <param name="sorting">排序</param>
    /// <returns></returns>
    public static Task<(T member, decimal dist)[]> GeoRadiusWithDistAsync<T>(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
        Instance.GeoRadiusWithDistAsync<T>(key, longitude, latitude, radius, unit, count, sorting);

    ///// <summary>
    ///// 以给定的经纬度为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含经度、纬度）。
    ///// </summary>
    ///// <param name="key">不含prefix前辍</param>
    ///// <param name="longitude">经度</param>
    ///// <param name="latitude">纬度</param>
    ///// <param name="radius">距离</param>
    ///// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    ///// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    ///// <param name="sorting">排序</param>
    ///// <returns></returns>
    //private static Task<(string member, decimal longitude, decimal latitude)[]> GeoRadiusWithCoordAsync(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
    //	Instance.GeoRadiusWithCoordAsync(key, longitude, latitude, radius, unit, count, sorting);
    ///// <summary>
    ///// 以给定的经纬度为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含经度、纬度）。
    ///// </summary>
    ///// <param name="key">不含prefix前辍</param>
    ///// <param name="longitude">经度</param>
    ///// <param name="latitude">纬度</param>
    ///// <param name="radius">距离</param>
    ///// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    ///// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    ///// <param name="sorting">排序</param>
    ///// <returns></returns>
    //private static Task<(T member, decimal longitude, decimal latitude)[]> GeoRadiusWithCoordAsync<T>(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
    //	Instance.GeoRadiusWithCoordAsync<T>(key, longitude, latitude, radius, unit, count, sorting);

    /// <summary>
    /// 以给定的经纬度为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含距离、经度、纬度）。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="longitude">经度</param>
    /// <param name="latitude">纬度</param>
    /// <param name="radius">距离</param>
    /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    /// <param name="sorting">排序</param>
    /// <returns></returns>
    public static Task<(string member, decimal dist, decimal longitude, decimal latitude)[]> GeoRadiusWithDistAndCoordAsync(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
        Instance.GeoRadiusWithDistAndCoordAsync(key, longitude, latitude, radius, unit, count, sorting);
    /// <summary>
    /// 以给定的经纬度为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含距离、经度、纬度）。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="longitude">经度</param>
    /// <param name="latitude">纬度</param>
    /// <param name="radius">距离</param>
    /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    /// <param name="sorting">排序</param>
    /// <returns></returns>
    public static Task<(T member, decimal dist, decimal longitude, decimal latitude)[]> GeoRadiusWithDistAndCoordAsync<T>(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
        Instance.GeoRadiusWithDistAndCoordAsync<T>(key, longitude, latitude, radius, unit, count, sorting);

    /// <summary>
    /// 以给定的成员为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="member">成员</param>
    /// <param name="radius">距离</param>
    /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    /// <param name="sorting">排序</param>
    /// <returns></returns>
    public static Task<string[]> GeoRadiusByMemberAsync(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
        Instance.GeoRadiusByMemberAsync(key, member, radius, unit, count, sorting);
    /// <summary>
    /// 以给定的成员为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="member">成员</param>
    /// <param name="radius">距离</param>
    /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    /// <param name="sorting">排序</param>
    /// <returns></returns>
    public static Task<T[]> GeoRadiusByMemberAsync<T>(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
        Instance.GeoRadiusByMemberAsync<T>(key, member, radius, unit, count, sorting);

    /// <summary>
    /// 以给定的成员为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含距离）。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="member">成员</param>
    /// <param name="radius">距离</param>
    /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    /// <param name="sorting">排序</param>
    /// <returns></returns>
    public static Task<(string member, decimal dist)[]> GeoRadiusByMemberWithDistAsync(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
        Instance.GeoRadiusByMemberWithDistAsync(key, member, radius, unit, count, sorting);
    /// <summary>
    /// 以给定的成员为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含距离）。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="member">成员</param>
    /// <param name="radius">距离</param>
    /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    /// <param name="sorting">排序</param>
    /// <returns></returns>
    public static Task<(T member, decimal dist)[]> GeoRadiusByMemberWithDistAsync<T>(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
        Instance.GeoRadiusByMemberWithDistAsync<T>(key, member, radius, unit, count, sorting);

    ///// <summary>
    ///// 以给定的成员为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含经度、纬度）。
    ///// </summary>
    ///// <param name="key">不含prefix前辍</param>
    ///// <param name="member">成员</param>
    ///// <param name="radius">距离</param>
    ///// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    ///// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    ///// <param name="sorting">排序</param>
    ///// <returns></returns>
    //private static Task<(string member, decimal longitude, decimal latitude)[]> GeoRadiusByMemberWithCoordAsync(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
    //	Instance.GeoRadiusByMemberWithCoordAsync(key, member, radius, unit, count, sorting);
    ///// <summary>
    ///// 以给定的成员为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含经度、纬度）。
    ///// </summary>
    ///// <param name="key">不含prefix前辍</param>
    ///// <param name="member">成员</param>
    ///// <param name="radius">距离</param>
    ///// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    ///// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    ///// <param name="sorting">排序</param>
    ///// <returns></returns>
    //private static Task<(T member, decimal longitude, decimal latitude)[]> GeoRadiusByMemberWithCoordAsync<T>(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
    //	Instance.GeoRadiusByMemberWithCoordAsync<T>(key, member, radius, unit, count, sorting);

    /// <summary>
    /// 以给定的成员为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含距离、经度、纬度）。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="member">成员</param>
    /// <param name="radius">距离</param>
    /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    /// <param name="sorting">排序</param>
    /// <returns></returns>
    public static Task<(string member, decimal dist, decimal longitude, decimal latitude)[]> GeoRadiusByMemberWithDistAndCoordAsync(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
        Instance.GeoRadiusByMemberWithDistAndCoordAsync(key, member, radius, unit, count, sorting);
    /// <summary>
    /// 以给定的成员为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含距离、经度、纬度）。
    /// </summary>
    /// <param name="key">不含prefix前辍</param>
    /// <param name="member">成员</param>
    /// <param name="radius">距离</param>
    /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
    /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
    /// <param name="sorting">排序</param>
    /// <returns></returns>
    public static Task<(T member, decimal dist, decimal longitude, decimal latitude)[]> GeoRadiusByMemberWithDistAndCoordAsync<T>(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
        Instance.GeoRadiusByMemberWithDistAndCoordAsync<T>(key, member, radius, unit, count, sorting);
    #endregion
}
#endif