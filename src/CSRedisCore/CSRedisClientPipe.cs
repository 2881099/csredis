using CSRedis.Internal.ObjectPool;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CSRedis
{

    public partial class CSRedisClientPipe<TObject> : IDisposable
    {
        private CSRedisClient rds;
        private ConcurrentDictionary<string, RedisClientPool> Nodes => rds.Nodes;
        private bool IsMultiNode => rds.IsMultiNode;
        private Func<string, string> NodeRuleRaw => rds.NodeRuleRaw;
        private ConcurrentDictionary<string, (List<int> indexes, Object<RedisClient> conn)> Conns = new ConcurrentDictionary<string, (List<int> indexes, Object<RedisClient> conn)>();
        private Queue<Func<object, object>> Parsers = new Queue<Func<object, object>>();
        private static object ConnsLock = new object();
        /// <summary>
        /// 执行命令数量
        /// </summary>
        public int Counter => Parsers.Count;

        internal CSRedisClientPipe(CSRedisClient csredis)
        {
            rds = csredis;
        }
        private CSRedisClientPipe(CSRedisClient csredis, ConcurrentDictionary<string, (List<int> indexes, Object<RedisClient> conn)> conns, Queue<Func<object, object>> parsers)
        {
            this.rds = csredis;
            this.Conns = conns;
            this.Parsers = parsers;
        }

        /// <summary>
        /// 提交批命令
        /// </summary>
        /// <returns></returns>
        public object[] EndPipe()
        {
            var ret = new object[Parsers.Count];
            Exception ex = null;
            try
            {
                foreach (var conn in Conns.Values)
                {
                    try
                    {
                        object[] tmp = tmp = conn.conn.Value.EndPipe();
                        for (var a = 0; a < tmp.Length; a++)
                        {
                            var retIdx = conn.indexes[a];
                            ret[retIdx] = tmp[a];
                        }
                    }
                    catch (Exception ex2)
                    {
                        ex = ex2;
                    }
                }
            }
            finally
            {
                foreach (var conn in Conns.Values)
                    (conn.conn.Pool as RedisClientPool).Return(conn.conn, ex);
            }
            for (var b = 0; b < ret.Length; b++)
            {
                var parse = Parsers.Dequeue();
                if (parse != null) ret[b] = parse(ret[b]);
            }
            Conns.Clear();
            return ret;
        }

        /// <summary>
        /// 提交批命令
        /// </summary>
        public void Dispose()
        {
            this.EndPipe();
        }

        private CSRedisClientPipe<TReturn> PipeCommand<TReturn>(string key, Func<Object<RedisClient>, string, TReturn> handle) => PipeCommand<TReturn>(key, handle, null);
        private CSRedisClientPipe<TReturn> PipeCommand<TReturn>(string key, Func<Object<RedisClient>, string, TReturn> handle, Func<object, object> parser)
        {
            if (string.IsNullOrEmpty(key)) throw new Exception("key 不可为空或null");
            var nodeKey = NodeRuleRaw == null || Nodes.Count == 1 ? Nodes.Keys.First() : NodeRuleRaw(key);
            if (Nodes.TryGetValue(nodeKey, out var pool) == false) Nodes.TryGetValue(nodeKey = Nodes.Keys.First(), out pool);

            try
            {
                if (Conns.TryGetValue(pool.Key, out var conn) == false)
                {
                    conn = (new List<int>(), pool.Get());
                    bool isStartPipe = false;
                    lock (ConnsLock)
                    {
                        if (Conns.TryAdd(pool.Key, conn) == false)
                        {
                            pool.Return(conn.conn);
                            Conns.TryGetValue(pool.Key, out conn);
                        }
                        else
                        {
                            isStartPipe = true;
                        }
                    }
                    if (isStartPipe)
                    {
                        conn.conn.Value.StartPipe();
                    }
                }
                key = string.Concat(pool.Prefix, key);
                handle(conn.conn, key);
                conn.indexes.Add(Parsers.Count);
                Parsers.Enqueue(parser);
            }
            catch (Exception ex)
            {
                foreach (var conn in Conns.Values)
                    (conn.conn.Pool as RedisClientPool).Return(conn.conn, ex);
                throw ex;
            }
            if (typeof(TReturn) == typeof(TObject)) return this as CSRedisClientPipe<TReturn>;// return (CSRedisClientPipe<TReturn>)Convert.ChangeType(this, typeof(CSRedisClientPipe<TReturn>));
                                                                                              //this._isDisposed = true;
            return new CSRedisClientPipe<TReturn>(rds, this.Conns, this.Parsers);
        }

        #region Script
        /// <summary>
        /// 执行脚本
        /// </summary>
        /// <param name="script">Lua 脚本</param>
        /// <param name="key">用于定位分区节点，不含prefix前辍</param>
        /// <param name="args">参数</param>
        /// <returns></returns>
        public CSRedisClientPipe<object> Eval(string script, string key, params object[] args) => PipeCommand(key, (c, k) => c.Value.Eval(script, new[] { k }, args?.Select(z => rds.SerializeRedisValueInternal(z)).ToArray()));
        /// <summary>
        /// 执行脚本
        /// </summary>
        /// <param name="sha1">脚本缓存的sha1</param>
        /// <param name="key">用于定位分区节点，不含prefix前辍</param>
        /// <param name="args">参数</param>
        /// <returns></returns>
        public CSRedisClientPipe<object> EvalSHA(string sha1, string key, params object[] args) => PipeCommand(key, (c, k) => c.Value.EvalSHA(sha1, new[] { k }, args?.Select(z => rds.SerializeRedisValueInternal(z)).ToArray()));
        #endregion

        #region Pub/Sub
        /// <summary>
        /// 用于将信息发送到指定分区节点的频道，最终消息发布格式：1|message
        /// </summary>
        /// <param name="channel">频道名</param>
        /// <param name="message">消息文本</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> Publish(string channel, string message)
        {
            var msgid = rds.HIncrBy("csredisclient:Publish:msgid", channel, 1);
            return PipeCommand(channel, (c, k) => c.Value.Publish(channel, $"{msgid}|{message}"));
        }
        /// <summary>
        /// 用于将信息发送到指定分区节点的频道，与 Publish 方法不同，不返回消息id头，即 1|
        /// </summary>
        /// <param name="channel">频道名</param>
        /// <param name="message">消息文本</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> PublishNoneMessageId(string channel, string message) => PipeCommand(channel, (c, k) => c.Value.Publish(channel, message));
        #endregion

        #region HyperLogLog
        /// <summary>
        /// 添加指定元素到 HyperLogLog
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="elements">元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> PfAdd<T>(string key, params T[] elements) => PipeCommand(key, (c, k) => c.Value.PfAdd(k, elements?.Select(z => rds.SerializeRedisValueInternal(z)).ToArray()));
        /// <summary>
        /// 返回给定 HyperLogLog 的基数估算值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> PfCount(string key) => PipeCommand(key, (c, k) => c.Value.PfCount(k));
        #endregion

        #region Sorted Set
        /// <summary>
        /// [redis-server 5.0.0] 删除并返回有序集合key中的最多count个具有最高得分的成员。如未指定，count的默认值为1。指定一个大于有序集合的基数的count不会产生错误。 当返回多个元素时候，得分最高的元素将是第一个元素，然后是分数较低的元素。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public CSRedisClientPipe<(string member, decimal score)[]> ZPopMax(string key, long count) =>
            PipeCommand(key, (c, k) => { c.Value.ZPopMax(k, count); return default((string, decimal)[]); }, obj =>
            ((Tuple<string, decimal>[])obj).Select(a => (a.Item1, a.Item2)).ToArray());
        /// <summary>
        /// [redis-server 5.0.0] 删除并返回有序集合key中的最多count个具有最高得分的成员。如未指定，count的默认值为1。指定一个大于有序集合的基数的count不会产生错误。 当返回多个元素时候，得分最高的元素将是第一个元素，然后是分数较低的元素。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public CSRedisClientPipe<(T member, decimal score)[]> ZPopMax<T>(string key, long count) =>
            PipeCommand(key, (c, k) => { c.Value.ZPopMaxBytes(k, count); return default((T member, decimal score)[]); }, obj => rds.DeserializeRedisValueTuple1Internal<T, decimal>((Tuple<byte[], decimal>[])obj));
        /// <summary>
        /// [redis-server 5.0.0] 删除并返回有序集合key中的最多count个具有最低得分的成员。如未指定，count的默认值为1。指定一个大于有序集合的基数的count不会产生错误。 当返回多个元素时候，得分最低的元素将是第一个元素，然后是分数较高的元素。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public CSRedisClientPipe<(string member, decimal score)[]> ZPopMin(string key, long count) =>
            PipeCommand(key, (c, k) => { c.Value.ZPopMin(k, count); return default((string, decimal)[]); }, obj =>
            ((Tuple<string, decimal>[])obj).Select(a => (a.Item1, a.Item2)).ToArray());
        /// <summary>
        /// [redis-server 5.0.0] 删除并返回有序集合key中的最多count个具有最低得分的成员。如未指定，count的默认值为1。指定一个大于有序集合的基数的count不会产生错误。 当返回多个元素时候，得分最低的元素将是第一个元素，然后是分数较高的元素。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public CSRedisClientPipe<(T member, decimal score)[]> ZPopMin<T>(string key, long count) =>
            PipeCommand(key, (c, k) => { c.Value.ZPopMinBytes(k, count); return default((T member, decimal score)[]); }, obj => rds.DeserializeRedisValueTuple1Internal<T, decimal>((Tuple<byte[], decimal>[])obj));

        /// <summary>
        /// 向有序集合添加一个或多个成员，或者更新已存在成员的分数
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="scoreMembers">一个或多个成员分数</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> ZAdd(string key, params (decimal, object)[] scoreMembers)
        {
            if (scoreMembers == null || scoreMembers.Any() == false) throw new Exception("scoreMembers 参数不可为空");
            var ms = scoreMembers.Select(a => new Tuple<decimal, object>(a.Item1, rds.SerializeRedisValueInternal(a.Item2))).ToArray();
            return PipeCommand(key, (c, k) => c.Value.ZAdd(k, ms));
        }
        /// <summary>
        /// 获取有序集合的成员数量
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> ZCard(string key) => PipeCommand(key, (c, k) => c.Value.ZCard(k));
        /// <summary>
        /// 计算在有序集合中指定区间分数的成员数量
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> ZCount(string key, decimal min, decimal max) => PipeCommand(key, (c, k) => c.Value.ZCount(k, min == decimal.MinValue ? "-inf" : min.ToString(), max == decimal.MaxValue ? "+inf" : max.ToString()));
        /// <summary>
        /// 计算在有序集合中指定区间分数的成员数量
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> ZCount(string key, string min, string max) => PipeCommand(key, (c, k) => c.Value.ZCount(k, min, max));
        /// <summary>
        /// 有序集合中对指定成员的分数加上增量 increment
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member">成员</param>
        /// <param name="increment">增量值(默认=1)</param>
        /// <returns></returns>
        public CSRedisClientPipe<decimal> ZIncrBy(string key, string member, decimal increment = 1) => PipeCommand(key, (c, k) => c.Value.ZIncrBy(k, increment, member));

        /// <summary>
        /// 计算给定的一个或多个有序集的交集，将结果集存储在新的有序集合 destination 中
        /// </summary>
        /// <param name="destination">新的有序集合，不含prefix前辍</param>
        /// <param name="weights">使用 WEIGHTS 选项，你可以为 每个 给定有序集 分别 指定一个乘法因子。如果没有指定 WEIGHTS 选项，乘法因子默认设置为 1 。</param>
        /// <param name="aggregate">Sum | Min | Max</param>
        /// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> ZInterStore(string destination, decimal[] weights, RedisAggregate aggregate, params string[] keys)
        {
            if (keys == null || keys.Length == 0) throw new Exception("keys 参数不可为空");
            if (weights != null && weights.Length != keys.Length) throw new Exception("weights 和 keys 参数长度必须相同");
            if (IsMultiNode) throw new Exception("ZInterStore 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(destination, (c, k) => c.Value.ZInterStore(k, weights, aggregate, keys.Select(z => prefix + z).ToArray()));
        }

        /// <summary>
        /// 通过索引区间返回有序集合成指定区间内的成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> ZRange(string key, long start, long stop) => PipeCommand(key, (c, k) => c.Value.ZRange(k, start, stop, false));
        /// <summary>
        /// 通过索引区间返回有序集合成指定区间内的成员
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> ZRange<T>(string key, long start, long stop) =>
            PipeCommand(key, (c, k) => { c.Value.ZRangeBytes(k, start, stop, false); return default(T[]); }, obj => rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));
        /// <summary>
        /// 通过索引区间返回有序集合成指定区间内的成员和分数
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<(string member, decimal score)[]> ZRangeWithScores(string key, long start, long stop) =>
            PipeCommand(key, (c, k) => { c.Value.ZRangeWithScores(k, start, stop); return default((string, decimal)[]); }, obj =>
            ((Tuple<string, decimal>[])obj).Select(a => (a.Item1, a.Item2)).ToArray());
        /// <summary>
        /// 通过索引区间返回有序集合成指定区间内的成员和分数
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<(T member, decimal score)[]> ZRangeWithScores<T>(string key, long start, long stop) =>
            PipeCommand(key, (c, k) => { c.Value.ZRangeBytesWithScores(k, start, stop); return default((T member, decimal score)[]); }, obj => rds.DeserializeRedisValueTuple1Internal<T, decimal>((Tuple<byte[], decimal>[])obj));

        /// <summary>
        /// 通过分数返回有序集合指定区间内的成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> ZRangeByScore(string key, decimal min, decimal max, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => c.Value.ZRangeByScore(k, min == decimal.MinValue ? "-inf" : min.ToString(), max == decimal.MaxValue ? "+inf" : max.ToString(), false, offset, count));
        /// <summary>
        /// 通过分数返回有序集合指定区间内的成员
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> ZRangeByScore<T>(string key, decimal min, decimal max, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => { c.Value.ZRangeBytesByScore(k, min == decimal.MinValue ? "-inf" : min.ToString(), max == decimal.MaxValue ? "+inf" : max.ToString(), false, offset, count); return default(T[]); }, obj =>
            rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));
        /// <summary>
        /// 通过分数返回有序集合指定区间内的成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> ZRangeByScore(string key, string min, string max, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => c.Value.ZRangeByScore(k, min, max, false, offset, count));
        /// <summary>
        /// 通过分数返回有序集合指定区间内的成员
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> ZRangeByScore<T>(string key, string min, string max, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => { c.Value.ZRangeBytesByScore(k, min, max, false, offset, count); return default(T[]); }, obj =>
            rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));

        /// <summary>
        /// 通过分数返回有序集合指定区间内的成员和分数
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<(string member, decimal score)[]> ZRangeByScoreWithScores(string key, decimal min, decimal max, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => { c.Value.ZRangeByScoreWithScores(k, min == decimal.MinValue ? "-inf" : min.ToString(), max == decimal.MaxValue ? "+inf" : max.ToString(), offset, count); return default((string, decimal)[]); }, obj =>
            ((Tuple<string, decimal>[])obj).Select(z => (z.Item1, z.Item2)));
        /// <summary>
        /// 通过分数返回有序集合指定区间内的成员和分数
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<(T member, decimal score)[]> ZRangeByScoreWithScores<T>(string key, decimal min, decimal max, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => { c.Value.ZRangeBytesByScoreWithScores(k, min == decimal.MinValue ? "-inf" : min.ToString(), max == decimal.MaxValue ? "+inf" : max.ToString(), offset, count); return default((T, decimal)[]); }, obj =>
            rds.DeserializeRedisValueTuple1Internal<T, decimal>((Tuple<byte[], decimal>[])obj));
        /// <summary>
        /// 通过分数返回有序集合指定区间内的成员和分数
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<(string member, decimal score)[]> ZRangeByScoreWithScores(string key, string min, string max, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => c.Value.ZRangeByScoreWithScores(k, min, max, offset, count).Select(z => (z.Item1, z.Item2)).ToArray());
        /// <summary>
        /// 通过分数返回有序集合指定区间内的成员和分数
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<(T member, decimal score)[]> ZRangeByScoreWithScores<T>(string key, string min, string max, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => { c.Value.ZRangeBytesByScoreWithScores(k, min, max, offset, count); return default((T, decimal)[]); }, obj =>
            rds.DeserializeRedisValueTuple1Internal<T, decimal>((Tuple<byte[], decimal>[])obj));

        /// <summary>
        /// 返回有序集合中指定成员的索引
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member">成员</param>
        /// <returns></returns>
        public CSRedisClientPipe<long?> ZRank(string key, object member) => PipeCommand(key, (c, k) => c.Value.ZRank(k, rds.SerializeRedisValueInternal(member)));
        /// <summary>
        /// 移除有序集合中的一个或多个成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member">一个或多个成员</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> ZRem<T>(string key, params T[] member)
        {
            if (member == null || member.Any() == false) throw new Exception("member 参数不可为空");
            return PipeCommand(key, (c, k) => c.Value.ZRem(k, member?.Select(z => rds.SerializeRedisValueInternal(z)).ToArray()));
        }
        /// <summary>
        /// 移除有序集合中给定的排名区间的所有成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> ZRemRangeByRank(string key, long start, long stop) => PipeCommand(key, (c, k) => c.Value.ZRemRangeByRank(k, start, stop));
        /// <summary>
        /// 移除有序集合中给定的分数区间的所有成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> ZRemRangeByScore(string key, decimal min, decimal max) => PipeCommand(key, (c, k) => c.Value.ZRemRangeByScore(k, min == decimal.MinValue ? "-inf" : min.ToString(), max == decimal.MaxValue ? "+inf" : max.ToString()));
        /// <summary>
        /// 移除有序集合中给定的分数区间的所有成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> ZRemRangeByScore(string key, string min, string max) => PipeCommand(key, (c, k) => c.Value.ZRemRangeByScore(k, min, max));

        /// <summary>
        /// 返回有序集中指定区间内的成员，通过索引，分数从高到底
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> ZRevRange(string key, long start, long stop) => PipeCommand(key, (c, k) => c.Value.ZRevRange(k, start, stop, false));
        /// <summary>
        /// 返回有序集中指定区间内的成员，通过索引，分数从高到底
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> ZRevRange<T>(string key, long start, long stop) =>
            PipeCommand(key, (c, k) => { c.Value.ZRevRangeBytes(k, start, stop, false); return default(T[]); }, obj =>
             rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));
        /// <summary>
        /// 返回有序集中指定区间内的成员和分数，通过索引，分数从高到底
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<(string member, decimal score)[]> ZRevRangeWithScores(string key, long start, long stop) =>
            PipeCommand(key, (c, k) => { c.Value.ZRevRangeWithScores(k, start, stop); return default((string, decimal)[]); }, obj =>
            ((Tuple<string, decimal>[])obj).Select(a => (a.Item1, a.Item2)).ToArray());
        /// <summary>
        /// 返回有序集中指定区间内的成员和分数，通过索引，分数从高到底
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<(T member, decimal score)[]> ZRevRangeWithScores<T>(string key, long start, long stop) =>
            PipeCommand(key, (c, k) => { c.Value.ZRevRangeBytesWithScores(k, start, stop); return default((T, decimal)[]); }, obj =>
            rds.DeserializeRedisValueTuple1Internal<T, decimal>((Tuple<byte[], decimal>[])obj));

        /// <summary>
        /// 返回有序集中指定分数区间内的成员，分数从高到低排序
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> ZRevRangeByScore(string key, decimal max, decimal min, long? count = null, long? offset = 0) =>
            PipeCommand(key, (c, k) => c.Value.ZRevRangeByScore(k, max == decimal.MaxValue ? "+inf" : max.ToString(), min == decimal.MinValue ? "-inf" : min.ToString(), false, offset, count));
        /// <summary>
        /// 返回有序集中指定分数区间内的成员，分数从高到低排序
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> ZRevRangeByScore<T>(string key, decimal max, decimal min, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => { c.Value.ZRevRangeBytesByScore(k, max == decimal.MaxValue ? "+inf" : max.ToString(), min == decimal.MinValue ? "-inf" : min.ToString(), false, offset, count); return default(T[]); }, obj =>
            rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));
        /// <summary>
        /// 返回有序集中指定分数区间内的成员，分数从高到低排序
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> ZRevRangeByScore(string key, string max, string min, long? count = null, long? offset = 0) =>
            PipeCommand(key, (c, k) => c.Value.ZRevRangeByScore(k, max, min, false, offset, count));
        /// <summary>
        /// 返回有序集中指定分数区间内的成员，分数从高到低排序
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> ZRevRangeByScore<T>(string key, string max, string min, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => { c.Value.ZRevRangeBytesByScore(k, max, min, false, offset, count); return default(T[]); }, obj =>
            rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));

        /// <summary>
        /// 返回有序集中指定分数区间内的成员和分数，分数从高到低排序
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<(string member, decimal score)[]> ZRevRangeByScoreWithScores(string key, decimal max, decimal min, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => { c.Value.ZRevRangeByScoreWithScores(k, max == decimal.MaxValue ? "+inf" : max.ToString(), min == decimal.MinValue ? "-inf" : min.ToString(), offset, count); return default((string member, decimal score)[]); }, obj =>
            ((Tuple<string, decimal>[])obj).Select(z => (z.Item1, z.Item2)).ToArray());
        /// <summary>
        /// 返回有序集中指定分数区间内的成员和分数，分数从高到低排序
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<(T member, decimal score)[]> ZRevRangeByScoreWithScores<T>(string key, decimal max, decimal min, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => { c.Value.ZRevRangeBytesByScoreWithScores(k, max == decimal.MaxValue ? "+inf" : max.ToString(), min == decimal.MinValue ? "-inf" : min.ToString(), offset, count); return default((T member, decimal score)[]); }, obj =>
            rds.DeserializeRedisValueTuple1Internal<T, decimal>((Tuple<byte[], decimal>[])obj));
        /// <summary>
        /// 返回有序集中指定分数区间内的成员和分数，分数从高到低排序
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<(string member, decimal score)[]> ZRevRangeByScoreWithScores(string key, string max, string min, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => { c.Value.ZRevRangeByScoreWithScores(k, max, min, offset, count); return default((string, decimal)[]); }, obj =>
            ((Tuple<string, decimal>[])obj).Select(z => (z.Item1, z.Item2)).ToArray());
        /// <summary>
        /// 返回有序集中指定分数区间内的成员和分数，分数从高到低排序
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<(T member, decimal score)[]> ZRevRangeByScoreWithScores<T>(string key, string max, string min, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => { c.Value.ZRevRangeBytesByScoreWithScores(k, max, min, offset, count); return default((T, decimal)[]); }, obj =>
            rds.DeserializeRedisValueTuple1Internal<T, decimal>((Tuple<byte[], decimal>[])obj));

        /// <summary>
        /// 返回有序集合中指定成员的排名，有序集成员按分数值递减(从大到小)排序
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member">成员</param>
        /// <returns></returns>
        public CSRedisClientPipe<long?> ZRevRank(string key, object member) => PipeCommand(key, (c, k) => c.Value.ZRevRank(k, rds.SerializeRedisValueInternal(member)));
        /// <summary>
        /// 返回有序集中，成员的分数值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member">成员</param>
        /// <returns></returns>
        public CSRedisClientPipe<decimal?> ZScore(string key, object member) => PipeCommand(key, (c, k) => c.Value.ZScore(k, rds.SerializeRedisValueInternal(member)));

        /// <summary>
        /// 计算给定的一个或多个有序集的并集，将结果集存储在新的有序集合 destination 中
        /// </summary>
        /// <param name="destination">新的有序集合，不含prefix前辍</param>
        /// <param name="weights">使用 WEIGHTS 选项，你可以为 每个 给定有序集 分别 指定一个乘法因子。如果没有指定 WEIGHTS 选项，乘法因子默认设置为 1 。</param>
        /// <param name="aggregate">Sum | Min | Max</param>
        /// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> ZUnionStore(string destination, decimal[] weights, RedisAggregate aggregate, params string[] keys)
        {
            if (keys == null || keys.Length == 0) throw new Exception("keys 参数不可为空");
            if (weights != null && weights.Length != keys.Length) throw new Exception("weights 和 keys 参数长度必须相同");
            if (IsMultiNode) throw new Exception("ZUnionStore 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(destination, (c, k) => c.Value.ZUnionStore(k, weights, aggregate, keys.Select(z => prefix + z).ToArray()));
        }

        /// <summary>
        /// 迭代有序集合中的元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public CSRedisClientPipe<RedisScan<(string member, decimal score)>> ZScan(string key, long cursor, string pattern = null, long? count = null) =>
            PipeCommand(key, (c, k) => { c.Value.ZScan(k, cursor, pattern, count); return default(RedisScan<(string, decimal)>); }, obj =>
            {
                var scan = (RedisScan<Tuple<string, decimal>>)obj;
                return new RedisScan<(string, decimal)>(scan.Cursor, scan.Items.Select(z => (z.Item1, z.Item2)).ToArray());
            });
        /// <summary>
        /// 迭代有序集合中的元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public CSRedisClientPipe<RedisScan<(T member, decimal score)>> ZScan<T>(string key, long cursor, string pattern = null, long? count = null) =>
            PipeCommand(key, (c, k) => { c.Value.ZScanBytes(k, cursor, pattern, count); return default(RedisScan<(T, decimal)>); }, obj =>
            {
                var scan = (RedisScan<Tuple<byte[], decimal>>)obj;
                return new RedisScan<(T, decimal)>(scan.Cursor, rds.DeserializeRedisValueTuple1Internal<T, decimal>(scan.Items));
            });

        /// <summary>
        /// 当有序集合的所有成员都具有相同的分值时，有序集合的元素会根据成员的字典序来进行排序，这个命令可以返回给定的有序集合键 key 中，值介于 min 和 max 之间的成员。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
        /// <param name="max">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> ZRangeByLex(string key, string min, string max, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => c.Value.ZRangeByLex(k, min, max, offset, count));
        /// <summary>
        /// 当有序集合的所有成员都具有相同的分值时，有序集合的元素会根据成员的字典序来进行排序，这个命令可以返回给定的有序集合键 key 中，值介于 min 和 max 之间的成员。
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
        /// <param name="max">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> ZRangeByLex<T>(string key, string min, string max, long? count = null, long offset = 0) =>
            PipeCommand(key, (c, k) => { c.Value.ZRangeBytesByLex(k, min, max, offset, count); return default(T[]); }, obj => rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));

        /// <summary>
        /// 当有序集合的所有成员都具有相同的分值时，有序集合的元素会根据成员的字典序来进行排序，这个命令可以返回给定的有序集合键 key 中，值介于 min 和 max 之间的成员。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
        /// <param name="max">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> ZRemRangeByLex(string key, string min, string max) =>
            PipeCommand(key, (c, k) => c.Value.ZRemRangeByLex(k, min, max));
        /// <summary>
        /// 当有序集合的所有成员都具有相同的分值时，有序集合的元素会根据成员的字典序来进行排序，这个命令可以返回给定的有序集合键 key 中，值介于 min 和 max 之间的成员。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
        /// <param name="max">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> ZLexCount(string key, string min, string max) =>
            PipeCommand(key, (c, k) => c.Value.ZLexCount(k, min, max));
        #endregion

        #region Set
        /// <summary>
        /// 向集合添加一个或多个成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="members">一个或多个成员</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> SAdd<T>(string key, params T[] members)
        {
            if (members == null || members.Any() == false) throw new Exception("members 参数不可为空");
            return PipeCommand(key, (c, k) => c.Value.SAdd(k, members?.Select(z => rds.SerializeRedisValueInternal(z)).ToArray()));
        }
        /// <summary>
        /// 获取集合的成员数
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> SCard(string key) => PipeCommand(key, (c, k) => c.Value.SCard(k));
        /// <summary>
        /// 返回给定所有集合的差集
        /// </summary>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> SDiff(params string[] keys)
        {
            if (keys == null || keys.Length == 0) throw new Exception("keys 参数不可为空");
            if (IsMultiNode) throw new Exception("SDiff 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(keys.First(), (c, k) => c.Value.SDiff(keys.Select(z => prefix + z).ToArray()));
        }
        /// <summary>
        /// 返回给定所有集合的差集
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> SDiff<T>(params string[] keys)
        {
            if (keys == null || keys.Length == 0) throw new Exception("keys 参数不可为空");
            if (IsMultiNode) throw new Exception("SDiff<T> 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(keys.First(), (c, k) => { c.Value.SDiffBytes(keys.Select(z => prefix + z).ToArray()); return default(T[]); }, obj =>
            rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));
        }
        /// <summary>
        /// 返回给定所有集合的差集并存储在 destination 中
        /// </summary>
        /// <param name="destination">新的无序集合，不含prefix前辍</param>
        /// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> SDiffStore(string destination, params string[] keys)
        {
            if (string.IsNullOrEmpty(destination)) throw new Exception("destination 参数不可为空");
            if (keys == null || keys.Length == 0) throw new Exception("keys 参数不可为空");
            if (IsMultiNode) throw new Exception("SDiffStore 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(destination, (c, k) => c.Value.SDiffStore(k, keys.Select(z => prefix + z).ToArray()));
        }
        /// <summary>
        /// 返回给定所有集合的交集
        /// </summary>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> SInter(params string[] keys)
        {
            if (keys == null || keys.Length == 0) throw new Exception("keys 参数不可为空");
            if (IsMultiNode) throw new Exception("SInter 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(keys.First(), (c, k) => c.Value.SInter(keys.Select(z => prefix + z).ToArray()));
        }
        /// <summary>
        /// 返回给定所有集合的交集
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> SInter<T>(params string[] keys)
        {
            if (keys == null || keys.Length == 0) throw new Exception("keys 参数不可为空");
            if (IsMultiNode) throw new Exception("SInter<T> 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(keys.First(), (c, k) => { c.Value.SInterBytes(keys.Select(z => prefix + z).ToArray()); return default(T[]); }, obj =>
            rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));
        }
        /// <summary>
        /// 返回给定所有集合的交集并存储在 destination 中
        /// </summary>
        /// <param name="destination">新的无序集合，不含prefix前辍</param>
        /// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> SInterStore(string destination, params string[] keys)
        {
            if (string.IsNullOrEmpty(destination)) throw new Exception("destination 参数不可为空");
            if (keys == null || keys.Length == 0) throw new Exception("keys 参数不可为空");
            if (IsMultiNode) throw new Exception("SInterStore 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(destination, (c, k) => c.Value.SInterStore(k, keys.Select(z => prefix + z).ToArray()));
        }
        /// <summary>
        /// 判断 member 元素是否是集合 key 的成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member">成员</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> SIsMember(string key, object member) => PipeCommand(key, (c, k) => c.Value.SIsMember(k, rds.SerializeRedisValueInternal(member)));
        /// <summary>
        /// 返回集合中的所有成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> SMembers(string key) => PipeCommand(key, (c, k) => c.Value.SMembers(k));
        /// <summary>
        /// 返回集合中的所有成员
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> SMembers<T>(string key) => PipeCommand(key, (c, k) => { c.Value.SMembersBytes(k); return default(T[]); }, obj => rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));
        /// <summary>
        /// 将 member 元素从 source 集合移动到 destination 集合
        /// </summary>
        /// <param name="source">无序集合key，不含prefix前辍</param>
        /// <param name="destination">目标无序集合key，不含prefix前辍</param>
        /// <param name="member">成员</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> SMove(string source, string destination, object member)
        {
            if (IsMultiNode) throw new Exception("SMove 管道命令，在分区模式下不可用");
            return PipeCommand(source, (c, k) => c.Value.SMove(k, (c.Pool as RedisClientPool)?.Prefix + destination, rds.SerializeRedisValueInternal(member)));
        }
        /// <summary>
        /// 移除并返回集合中的一个随机元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<string> SPop(string key) => PipeCommand(key, (c, k) => c.Value.SPop(k));
        /// <summary>
        /// 移除并返回集合中的一个随机元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<T> SPop<T>(string key) => PipeCommand(key, (c, k) => { c.Value.SPopBytes(k); return default(T); }, obj => rds.DeserializeRedisValueInternal<T>((byte[])obj));
        /// <summary>
        /// 返回集合中的一个随机元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<string> SRandMember(string key) => PipeCommand(key, (c, k) => c.Value.SRandMember(k));
        /// <summary>
        /// 返回集合中的一个随机元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<T> SRandMember<T>(string key) => PipeCommand(key, (c, k) => { c.Value.SRandMemberBytes(k); return default(T); }, obj => rds.DeserializeRedisValueInternal<T>((byte[])obj));
        /// <summary>
        /// 返回集合中一个或多个随机数的元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">返回个数</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> SRandMembers(string key, int count = 1) => PipeCommand(key, (c, k) => c.Value.SRandMembers(k, count));
        /// <summary>
        /// 返回集合中一个或多个随机数的元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">返回个数</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> SRandMembers<T>(string key, int count = 1) => PipeCommand(key, (c, k) => { c.Value.SRandMembersBytes(k, count); return default(T[]); }, obj => rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));
        /// <summary>
        /// 移除集合中一个或多个成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="members">一个或多个成员</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> SRem<T>(string key, params T[] members)
        {
            if (members == null || members.Any() == false) throw new Exception("members 参数不可为空");
            return PipeCommand(key, (c, k) => c.Value.SRem(k, members?.Select(z => rds.SerializeRedisValueInternal(z)).ToArray()));
        }
        /// <summary>
        /// 返回所有给定集合的并集
        /// </summary>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> SUnion(params string[] keys)
        {
            if (keys == null || keys.Length == 0) throw new Exception("keys 参数不可为空");
            if (IsMultiNode) throw new Exception("SUnion 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(keys.First(), (c, k) => c.Value.SUnion(keys.Select(z => prefix + z).ToArray()));
        }
        /// <summary>
        /// 返回所有给定集合的并集
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> SUnion<T>(params string[] keys)
        {
            if (keys == null || keys.Length == 0) throw new Exception("keys 参数不可为空");
            if (IsMultiNode) throw new Exception("SUnion<T> 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(keys.First(), (c, k) => { c.Value.SUnionBytes(keys.Select(z => prefix + z).ToArray()); return default(T[]); }, obj =>
            rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));
        }
        /// <summary>
        /// 所有给定集合的并集存储在 destination 集合中
        /// </summary>
        /// <param name="destination">新的无序集合，不含prefix前辍</param>
        /// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> SUnionStore(string destination, params string[] keys)
        {
            if (string.IsNullOrEmpty(destination)) throw new Exception("destination 参数不可为空");
            if (keys == null || keys.Length == 0) throw new Exception("keys 参数不可为空");
            if (IsMultiNode) throw new Exception("SUnionStore 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(destination, (c, k) => c.Value.SUnionStore(k, keys.Select(z => prefix + z).ToArray()));
        }
        /// <summary>
        /// 迭代集合中的元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public CSRedisClientPipe<RedisScan<string>> SScan(string key, long cursor, string pattern = null, long? count = null) =>
            PipeCommand(key, (c, k) => c.Value.SScan(k, cursor, pattern, count));
        /// <summary>
        /// 迭代集合中的元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public CSRedisClientPipe<RedisScan<T>> SScan<T>(string key, long cursor, string pattern = null, long? count = null) =>
            PipeCommand(key, (c, k) => { c.Value.SScanBytes(k, cursor, pattern, count); return default(RedisScan<T>); }, obj =>
            {
                var scan = (RedisScan<byte[]>)obj;
                return new RedisScan<T>(scan.Cursor, rds.DeserializeRedisValueArrayInternal<T>(scan.Items));
            });
        #endregion

        #region List
        /// <summary>
        /// 通过索引获取列表中的元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="index">索引</param>
        /// <returns></returns>
        public CSRedisClientPipe<string> LIndex(string key, long index) => PipeCommand(key, (c, k) => c.Value.LIndex(k, index));
        /// <summary>
        /// 通过索引获取列表中的元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="index">索引</param>
        /// <returns></returns>
        public CSRedisClientPipe<T> LIndex<T>(string key, long index) => PipeCommand(key, (c, k) => { c.Value.LIndexBytes(k, index); return default(T); }, obj => rds.DeserializeRedisValueInternal<T>((byte[])obj));
        /// <summary>
        /// 在列表中的元素前面插入元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="pivot">列表的元素</param>
        /// <param name="value">新元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> LInsertBefore(string key, string pivot, object value) => PipeCommand(key, (c, k) => c.Value.LInsert(k, RedisInsert.Before, pivot, rds.SerializeRedisValueInternal(value)));
        /// <summary>
        /// 在列表中的元素后面插入元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="pivot">列表的元素</param>
        /// <param name="value">新元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> LInsertAfter(string key, string pivot, object value) => PipeCommand(key, (c, k) => c.Value.LInsert(k, RedisInsert.After, pivot, rds.SerializeRedisValueInternal(value)));
        /// <summary>
        /// 获取列表长度
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> LLen(string key) => PipeCommand(key, (c, k) => c.Value.LLen(k));
        /// <summary>
        /// 移出并获取列表的第一个元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<string> LPop(string key) => PipeCommand(key, (c, k) => c.Value.LPop(k));
        /// <summary>
        /// 移出并获取列表的第一个元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<T> LPop<T>(string key) => PipeCommand(key, (c, k) => { c.Value.LPopBytes(k); return default(T); }, obj => rds.DeserializeRedisValueInternal<T>((byte[])obj));
        /// <summary>
        /// 将一个或多个值插入到列表头部
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">一个或多个值</param>
        /// <returns>执行 LPUSH 命令后，列表的长度</returns>
        public CSRedisClientPipe<long> LPush<T>(string key, params T[] value)
        {
            if (value == null || value.Any() == false) throw new Exception("value 参数不可为空"); ;
            return PipeCommand(key, (c, k) => c.Value.LPush(k, value?.Select(z => rds.SerializeRedisValueInternal(z)).ToArray()));
        }
        /// <summary>
        /// 将一个值插入到已存在的列表头部
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">值</param>
        /// <returns>执行 LPUSHX 命令后，列表的长度。</returns>
        public CSRedisClientPipe<long> LPushX(string key, object value) => PipeCommand(key, (c, k) => c.Value.LPushX(k, rds.SerializeRedisValueInternal(value)));
        /// <summary>
        /// 获取列表指定范围内的元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> LRange(string key, long start, long stop) => PipeCommand(key, (c, k) => c.Value.LRange(k, start, stop));
        /// <summary>
        /// 获取列表指定范围内的元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> LRange<T>(string key, long start, long stop) => PipeCommand(key, (c, k) => { c.Value.LRangeBytes(k, start, stop); return default(T[]); }, obj => rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));
        /// <summary>
        /// 根据参数 count 的值，移除列表中与参数 value 相等的元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">移除的数量，大于0时从表头删除数量count，小于0时从表尾删除数量-count，等于0移除所有</param>
        /// <param name="value">元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> LRem(string key, long count, object value) => PipeCommand(key, (c, k) => c.Value.LRem(k, count, rds.SerializeRedisValueInternal(value)));
        /// <summary>
        /// 通过索引设置列表元素的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="index">索引</param>
        /// <param name="value">值</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> LSet(string key, long index, object value) => PipeCommand(key, (c, k) =>
        {
            c.Value.LSet(k, index, rds.SerializeRedisValueInternal(value));
            return false;
        }, ret => ret?.ToString() == "OK");
        /// <summary>
        /// 对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> LTrim(string key, long start, long stop) => PipeCommand(key, (c, k) =>
        {
            c.Value.LTrim(k, start, stop);
            return false;
        }, ret => ret?.ToString() == "OK");
        /// <summary>
        /// 移除并获取列表最后一个元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<string> RPop(string key) => PipeCommand(key, (c, k) => c.Value.RPop(k));
        /// <summary>
        /// 移除并获取列表最后一个元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<T> RPop<T>(string key) => PipeCommand(key, (c, k) => { c.Value.RPopBytes(k); return default(T); }, obj => rds.DeserializeRedisValueInternal<T>((byte[])obj));
        /// <summary>
        /// 将列表 source 中的最后一个元素(尾元素)弹出，并返回给客户端。
        /// 将 source 弹出的元素插入到列表 destination ，作为 destination 列表的的头元素。
        /// </summary>
        /// <param name="source">源key，不含prefix前辍</param>
        /// <param name="destination">目标key，不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<string> RPopLPush(string source, string destination)
        {
            if (IsMultiNode) throw new Exception("RPopLPush 管道命令，在分区模式下不可用");
            return PipeCommand(source, (c, k) => c.Value.RPopLPush(k, (c.Pool as RedisClientPool)?.Prefix + destination));
        }
        /// <summary>
        /// 将列表 source 中的最后一个元素(尾元素)弹出，并返回给客户端。
        /// 将 source 弹出的元素插入到列表 destination ，作为 destination 列表的的头元素。
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="source">源key，不含prefix前辍</param>
        /// <param name="destination">目标key，不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<T> RPopLPush<T>(string source, string destination)
        {
            if (IsMultiNode) throw new Exception("RPopLPush<T> 管道命令，在分区模式下不可用");
            return PipeCommand(source, (c, k) => { c.Value.RPopBytesLPush(k, (c.Pool as RedisClientPool)?.Prefix + destination); return default(T); }, obj =>
                rds.DeserializeRedisValueInternal<T>((byte[])obj));
        }
        /// <summary>
        /// 在列表中添加一个或多个值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">一个或多个值</param>
        /// <returns>执行 RPUSH 命令后，列表的长度</returns>
        public CSRedisClientPipe<long> RPush<T>(string key, params T[] value)
        {
            if (value == null || value.Any() == false) throw new Exception("value 参数不可为空"); ;
            return PipeCommand(key, (c, k) => c.Value.RPush(k, value?.Select(z => rds.SerializeRedisValueInternal(z)).ToArray()));
        }
        /// <summary>
        /// 为已存在的列表添加值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">一个或多个值</param>
        /// <returns>执行 RPUSHX 命令后，列表的长度</returns>
        public CSRedisClientPipe<long> RPushX(string key, object value) => PipeCommand(key, (c, k) => c.Value.RPushX(k, rds.SerializeRedisValueInternal(value)));
        #endregion

        #region Hash
        /// <summary>
        /// [redis-server 3.2.0] 返回hash指定field的value的字符串长度，如果hash或者field不存在，返回0.
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> HStrLen(string key, string field) => PipeCommand(key, (c, k) => c.Value.HStrLen(k, field));

        /// <summary>
        /// 删除一个或多个哈希表字段
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="fields">字段</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> HDel(string key, params string[] fields)
        {
            if (fields == null || fields.Any() == false) throw new Exception("fields 参数不可为空");
            return PipeCommand(key, (c, k) => c.Value.HDel(k, fields));
        }
        /// <summary>
        /// 查看哈希表 key 中，指定的字段是否存在
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> HExists(string key, string field) => PipeCommand(key, (c, k) => c.Value.HExists(k, field));
        /// <summary>
        /// 获取存储在哈希表中指定字段的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <returns></returns>
        public CSRedisClientPipe<string> HGet(string key, string field) => PipeCommand(key, (c, k) => c.Value.HGet(k, field));
        /// <summary>
        /// 获取存储在哈希表中指定字段的值
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <returns></returns>
        public CSRedisClientPipe<T> HGet<T>(string key, string field) => PipeCommand(key, (c, k) => { c.Value.HGetBytes(k, field); return default(T); }, obj => rds.DeserializeRedisValueInternal<T>((byte[])obj));
        /// <summary>
        /// 获取在哈希表中指定 key 的所有字段和值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<Dictionary<string, string>> HGetAll(string key) => PipeCommand(key, (c, k) => c.Value.HGetAll(k));
        /// <summary>
        /// 获取在哈希表中指定 key 的所有字段和值
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<Dictionary<string, T>> HGetAll<T>(string key) => PipeCommand(key, (c, k) => { c.Value.HGetAllBytes(k); return default(Dictionary<string, T>); }, obj =>
            rds.DeserializeRedisValueDictionaryInternal<string, T>((Dictionary<string, byte[]>)obj));
        /// <summary>
        /// 为哈希表 key 中的指定字段的整数值加上增量 increment
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <param name="value">增量值(默认=1)</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> HIncrBy(string key, string field, long value = 1) => PipeCommand(key, (c, k) => c.Value.HIncrBy(k, field, value));
        /// <summary>
        /// 为哈希表 key 中的指定字段的整数值加上增量 increment
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <param name="value">增量值(默认=1)</param>
        /// <returns></returns>
        public CSRedisClientPipe<decimal> HIncrByFloat(string key, string field, decimal value) => PipeCommand(key, (c, k) => c.Value.HIncrByFloat(k, field, value));
        /// <summary>
        /// 获取所有哈希表中的字段
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> HKeys(string key) => PipeCommand(key, (c, k) => c.Value.HKeys(k));
        /// <summary>
        /// 获取哈希表中字段的数量
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> HLen(string key) => PipeCommand(key, (c, k) => c.Value.HLen(k));
        /// <summary>
        /// 获取存储在哈希表中多个字段的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="fields">字段</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> HMGet(string key, params string[] fields)
        {
            if (fields == null || fields.Any() == false) throw new Exception("fields 参数不可为空");
            return PipeCommand(key, (c, k) => c.Value.HMGet(k, fields));
        }
        /// <summary>
        /// 获取存储在哈希表中多个字段的值
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="fields">一个或多个字段</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> HMGet<T>(string key, params string[] fields)
        {
            if (fields == null || fields.Any() == false) throw new Exception("fields 参数不可为空");
            return PipeCommand(key, (c, k) => { c.Value.HMGetBytes(k, fields); return default(T[]); }, obj => rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));
        }
        /// <summary>
        /// 同时将多个 field-value (域-值)对设置到哈希表 key 中
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="keyValues">key1 value1 [key2 value2]</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> HMSet(string key, params object[] keyValues)
        {
            if (keyValues == null || keyValues.Any() == false) throw new Exception("keyValues 参数不可为空");
            if (keyValues.Length % 2 != 0) throw new Exception("keyValues 参数是键值对，不应该出现奇数(数量)，请检查使用姿势。");
            var parms = new List<object>();
            for (var a = 0; a < keyValues.Length; a += 2)
            {
                var k = string.Concat(keyValues[a]);
                var v = keyValues[a + 1];
                if (string.IsNullOrEmpty(k)) throw new Exception("keyValues 参数是键值对，并且 key 不可为空");
                parms.Add(k);
                parms.Add(rds.SerializeRedisValueInternal(v));
            }
            return PipeCommand(key, (c, k) => { c.Value.HMSet(k, parms.ToArray()); return false; }, obj => obj?.ToString() == "OK");
        }

        /// <summary>
        /// 将哈希表 key 中的字段 field 的值设为 value
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <param name="value">值</param>
        /// <returns>如果字段是哈希表中的一个新建字段，并且值设置成功，返回true。如果哈希表中域字段已经存在且旧值已被新值覆盖，返回false。</returns>
        public CSRedisClientPipe<bool> HSet(string key, string field, object value) => PipeCommand(key, (c, k) => c.Value.HSet(k, field, rds.SerializeRedisValueInternal(value)));
        /// <summary>
        /// 只有在字段 field 不存在时，设置哈希表字段的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <param name="value">值(string 或 byte[])</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> HSetNx(string key, string field, object value) => PipeCommand(key, (c, k) => c.Value.HSetNx(k, field, rds.SerializeRedisValueInternal(value)));
        /// <summary>
        /// 获取哈希表中所有值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> HVals(string key) => PipeCommand(key, (c, k) => c.Value.HVals(k));
        /// <summary>
        /// 获取哈希表中所有值
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<T[]> HVals<T>(string key) => PipeCommand(key, (c, k) => { c.Value.HValsBytes(k); return default(T[]); }, obj => rds.DeserializeRedisValueArrayInternal<T>((byte[][])obj));
        /// <summary>
        /// 迭代哈希表中的键值对
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public CSRedisClientPipe<RedisScan<(string field, string value)>> HScan(string key, long cursor, string pattern = null, long? count = null) =>
            PipeCommand(key, (c, k) => { c.Value.HScan(k, cursor, pattern, count); return default(RedisScan<(string, string)>); }, obj =>
            {
                var scan = (RedisScan<Tuple<string, string>>)obj;
                return new RedisScan<(string, string)>(scan.Cursor, scan.Items.Select(z => (z.Item1, z.Item2)).ToArray());
            });
        /// <summary>
        /// 迭代哈希表中的键值对
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public CSRedisClientPipe<RedisScan<(string field, T value)>> HScan<T>(string key, long cursor, string pattern = null, long? count = null) =>
            PipeCommand(key, (c, k) => { c.Value.HScanBytes(k, cursor, pattern, count); return default(RedisScan<(string, T)>); }, obj =>
            {
                var scan = (RedisScan<Tuple<string, byte[]>>)obj;
                return new RedisScan<(string, T)>(scan.Cursor, scan.Items.Select(z => (z.Item1, rds.DeserializeRedisValueInternal<T>(z.Item2))).ToArray());
            });
        #endregion

        #region String
        /// <summary>
        /// 如果 key 已经存在并且是一个字符串， APPEND 命令将指定的 value 追加到该 key 原来值（value）的末尾
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">字符串</param>
        /// <returns>追加指定值之后， key 中字符串的长度</returns>
        public CSRedisClientPipe<long> Append(string key, object value) => PipeCommand(key, (c, k) => c.Value.Append(k, rds.SerializeRedisValueInternal(value)));
        /// <summary>
        /// 计算给定位置被设置为 1 的比特位的数量
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置</param>
        /// <param name="end">结束位置</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> BitCount(string key, long start, long end) => PipeCommand(key, (c, k) => c.Value.BitCount(k, start, end));
        /// <summary>
        /// 对一个或多个保存二进制位的字符串 key 进行位元操作，并将结果保存到 destkey 上
        /// </summary>
        /// <param name="op">And | Or | XOr | Not</param>
        /// <param name="destKey">不含prefix前辍</param>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns>保存到 destkey 的长度，和输入 key 中最长的长度相等</returns>
        public CSRedisClientPipe<long> BitOp(RedisBitOp op, string destKey, params string[] keys)
        {
            if (string.IsNullOrEmpty(destKey)) throw new Exception("destKey 不能为空");
            if (keys == null || keys.Length == 0) throw new Exception("keys 不能为空");
            if (IsMultiNode) throw new Exception("BitOp 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(destKey, (c, k) => c.Value.BitOp(op, k, keys.Select(z => prefix + z).ToArray()));
        }
        /// <summary>
        /// 对 key 所储存的值，查找范围内第一个被设置为1或者0的bit位
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="bit">查找值</param>
        /// <param name="start">开始位置，-1是最后一个，-2是倒数第二个</param>
        /// <param name="end">结果位置，-1是最后一个，-2是倒数第二个</param>
        /// <returns>返回范围内第一个被设置为1或者0的bit位</returns>
        public CSRedisClientPipe<long> BitPos(string key, bool bit, long? start = null, long? end = null) => PipeCommand(key, (c, k) => c.Value.BitPos(k, bit, start, end));
        /// <summary>
        /// 获取指定 key 的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<string> Get(string key) => PipeCommand(key, (c, k) => c.Value.Get(k));
        /// <summary>
        /// 获取指定 key 的值
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<T> Get<T>(string key) => PipeCommand(key, (c, k) => { c.Value.GetBytes(k); return default(T); }, obj => rds.DeserializeRedisValueInternal<T>((byte[])obj));
        /// <summary>
        /// 对 key 所储存的值，获取指定偏移量上的位(bit)
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="offset">偏移量</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> GetBit(string key, uint offset) => PipeCommand(key, (c, k) => c.Value.GetBit(k, offset));
        /// <summary>
        /// 返回 key 中字符串值的子字符
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="end">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<string> GetRange(string key, long start, long end) => PipeCommand(key, (c, k) => c.Value.GetRange(k, start, end));
        /// <summary>
        /// 返回 key 中字符串值的子字符
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="end">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public CSRedisClientPipe<T> GetRange<T>(string key, long start, long end) => PipeCommand(key, (c, k) => { c.Value.GetRangeBytes(k, start, end); return default(T); }, obj => rds.DeserializeRedisValueInternal<T>((byte[])obj));
        /// <summary>
        /// 将给定 key 的值设为 value ，并返回 key 的旧值(old value)
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">字符串</param>
        /// <returns></returns>
        public CSRedisClientPipe<string> GetSet(string key, object value) => PipeCommand(key, (c, k) => c.Value.GetSet(k, rds.SerializeRedisValueInternal(value)));
        /// <summary>
        /// 将给定 key 的值设为 value ，并返回 key 的旧值(old value)
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">值</param>
        /// <returns></returns>
        public CSRedisClientPipe<T> GetSet<T>(string key, object value) => PipeCommand(key, (c, k) => { c.Value.GetSetBytes(k, rds.SerializeRedisValueInternal(value)); return default(T); }, obj => rds.DeserializeRedisValueInternal<T>((byte[])obj));
        /// <summary>
        /// 将 key 所储存的值加上给定的增量值（increment）
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">增量值(默认=1)</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> IncrBy(string key, long value = 1) => PipeCommand(key, (c, k) => c.Value.IncrBy(k, value));
        /// <summary>
        /// 将 key 所储存的值加上给定的浮点增量值（increment）
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">增量值(默认=1)</param>
        /// <returns></returns>
        public CSRedisClientPipe<decimal> IncrBy(string key, decimal value) => PipeCommand(key, (c, k) => c.Value.IncrByFloat(k, value));
        /// <summary>
        /// 获取多个指定 key 的值(数组)
        /// </summary>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> MGet(params string[] keys)
        {
            if (keys == null || keys.Length == 0) throw new Exception("keys 不能为空");
            if (IsMultiNode) throw new Exception("MGet 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(keys.First(), (c, k) => c.Value.MGet(keys.Select(z => prefix + z).ToArray()));
        }
        /// <summary>
        /// 设置指定 key 的值，所有写入参数object都支持string | byte[] | 数值 | 对象
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">值</param>
        /// <param name="expireSeconds">过期(秒单位)</param>
        /// <param name="exists">Nx, Xx</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> Set(string key, object value, int expireSeconds = -1, RedisExistence? exists = null)
        {
            object redisValule = rds.SerializeRedisValueInternal(value);
            if (expireSeconds <= 0 && exists == null) return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule); return false; }, obj => obj?.ToString() == "OK");
            if (expireSeconds <= 0 && exists != null) return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule, null, exists); return false; }, obj => obj?.ToString() == "OK");
            if (expireSeconds > 0 && exists == null) return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule, expireSeconds, null); return false; }, obj => obj?.ToString() == "OK");
            if (expireSeconds > 0 && exists != null) return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule, expireSeconds, exists); return false; }, obj => obj?.ToString() == "OK");
            return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule); return false; }, obj => obj?.ToString() == "OK");
        }
        public CSRedisClientPipe<bool> Set(string key, object value, TimeSpan expire, RedisExistence? exists = null)
        {
            object redisValule = rds.SerializeRedisValueInternal(value);
            if (expire <= TimeSpan.Zero && exists == null) return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule); return false; }, obj => obj?.ToString() == "OK");
            if (expire <= TimeSpan.Zero && exists != null) return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule, null, exists); return false; }, obj => obj?.ToString() == "OK");
            if (expire > TimeSpan.Zero && exists == null) return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule, expire, null); return false; }, obj => obj?.ToString() == "OK");
            if (expire > TimeSpan.Zero && exists != null) return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule, expire, exists); return false; }, obj => obj?.ToString() == "OK");
            return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule); return false; }, obj => obj?.ToString() == "OK");
        }
        /// <summary>
        /// 对 key 所储存的字符串值，设置或清除指定偏移量上的位(bit)
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="offset">偏移量</param>
        /// <param name="value">值</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> SetBit(string key, uint offset, bool value) => PipeCommand(key, (c, k) => c.Value.SetBit(k, offset, value));
        /// <summary>
        /// 只有在 key 不存在时设置 key 的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">值</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> SetNx(string key, object value) => PipeCommand(key, (c, k) => c.Value.SetNx(k, rds.SerializeRedisValueInternal(value)));
        /// <summary>
        /// 用 value 参数覆写给定 key 所储存的字符串值，从偏移量 offset 开始
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="offset">偏移量</param>
        /// <param name="value">值</param>
        /// <returns>被修改后的字符串长度</returns>
        public CSRedisClientPipe<long> SetRange(string key, uint offset, object value) => PipeCommand(key, (c, k) => c.Value.SetRange(k, offset, rds.SerializeRedisValueInternal(value)));
        /// <summary>
        /// 返回 key 所储存的字符串值的长度
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> StrLen(string key) => PipeCommand(key, (c, k) => c.Value.StrLen(k));
        #endregion

        #region Key
        /// <summary>
        /// [redis-server 3.2.1] 修改指定key(s) 最后访问时间 若key不存在，不做操作
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> Touch(string key) => PipeCommand(key, (c, k) => c.Value.Touch(k));
        /// <summary>
        /// [redis-server 4.0.0] Delete a key, 该命令和DEL十分相似：删除指定的key(s),若key不存在则该key被跳过。但是，相比DEL会产生阻塞，该命令会在另一个线程中回收内存，因此它是非阻塞的。 这也是该命令名字的由来：仅将keys从keyspace元数据中删除，真正的删除会在后续异步操作。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> UnLink(string key) => PipeCommand(key, (c, k) => c.Value.UnLink(k));
        /// <summary>
        /// 用于在 key 存在时删除 key
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> Del(string key) => PipeCommand(key, (c, k) => c.Value.Del(k));
        /// <summary>
        /// 序列化给定 key ，并返回被序列化的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<byte[]> Dump(string key) => PipeCommand(key, (c, k) => c.Value.Dump(k));
        /// <summary>
        /// 检查给定 key 是否存在
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> Exists(string key) => PipeCommand(key, (c, k) => c.Value.Exists(k));
        /// <summary>
        /// 为给定 key 设置过期时间
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="seconds">过期秒数</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> Expire(string key, int seconds) => PipeCommand(key, (c, k) => c.Value.Expire(k, seconds));
        /// <summary>
        /// 为给定 key 设置过期时间
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="expire">过期时间</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> Expire(string key, TimeSpan expire) => PipeCommand(key, (c, k) => c.Value.Expire(k, expire));
        /// <summary>
        /// 为给定 key 设置过期时间
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="expire">过期时间</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> ExpireAt(string key, DateTime expire) => PipeCommand(key, (c, k) => c.Value.ExpireAt(k, expire));
        /// <summary>
        /// 查找所有分区节点中符合给定模式(pattern)的 key
        /// </summary>
        /// <param name="pattern">如：runoob*</param>
        /// <returns></returns>
        public CSRedisClientPipe<string[]> Keys(string pattern)
        {
            if (IsMultiNode) throw new Exception("SInterStore 管道命令，在分区模式下不可用");
            return PipeCommand("Keys", (c, k) => c.Value.Keys(pattern));
        }
        /// <summary>
        /// 将当前数据库的 key 移动到给定的数据库 db 当中
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="database">数据库</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> Move(string key, int database) => PipeCommand(key, (c, k) => c.Value.Move(k, database));
        /// <summary>
        /// 该返回给定 key 锁储存的值所使用的内部表示(representation)
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<string> ObjectEncoding(string key) => PipeCommand(key, (c, k) => c.Value.ObjectEncoding(k));
        /// <summary>
        /// 该返回给定 key 引用所储存的值的次数。此命令主要用于除错
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long?> ObjectRefCount(string key) => PipeCommand(key, (c, k) => c.Value.Object(RedisObjectSubCommand.RefCount, k));
        /// <summary>
        /// 返回给定 key 自储存以来的空转时间(idle， 没有被读取也没有被写入)，以秒为单位
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long?> ObjectIdleTime(string key) => PipeCommand(key, (c, k) => c.Value.Object(RedisObjectSubCommand.IdleTime, k));
        /// <summary>
        /// 移除 key 的过期时间，key 将持久保持
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> Persist(string key) => PipeCommand(key, (c, k) => c.Value.Persist(k));
        /// <summary>
        /// 为给定 key 设置过期时间（毫秒）
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="milliseconds">过期毫秒数</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> PExpire(string key, int milliseconds) => PipeCommand(key, (c, k) => c.Value.PExpire(k, milliseconds));
        /// <summary>
        /// 为给定 key 设置过期时间（毫秒）
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="expire">过期时间</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> PExpire(string key, TimeSpan expire) => PipeCommand(key, (c, k) => c.Value.PExpire(k, expire));
        /// <summary>
        /// 为给定 key 设置过期时间（毫秒）
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="expire">过期时间</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> PExpireAt(string key, DateTime expire) => PipeCommand(key, (c, k) => c.Value.PExpireAt(k, expire));
        /// <summary>
        /// 以毫秒为单位返回 key 的剩余的过期时间
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> PTtl(string key) => PipeCommand(key, (c, k) => c.Value.PTtl(k));
        /// <summary>
        /// 从所有节点中随机返回一个 key
        /// </summary>
        /// <returns>返回的 key 如果包含 prefix前辍，则会去除后返回</returns>
        public CSRedisClientPipe<string> RandomKey()
        {
            var prefix = "";
            return PipeCommand(Guid.NewGuid().ToString(), (c, k) =>
            {
                c.Value.RandomKey();
                prefix = (c.Pool as RedisClientPool).Prefix;
                return prefix;
            }, obj =>
            {
                var rk = obj?.ToString();
                if (string.IsNullOrEmpty(prefix) == false && rk.StartsWith(prefix)) return rk.Substring(prefix.Length);
                return rk;
            });
        }
        /// <summary>
        /// 修改 key 的名称
        /// </summary>
        /// <param name="key">旧名称，不含prefix前辍</param>
        /// <param name="newKey">新名称，不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> Rename(string key, string newKey)
        {
            if (IsMultiNode) throw new Exception("Rename 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(key, (c, k) => { c.Value.Rename(k, prefix + newKey); return false; }, obj => obj?.ToString() == "OK");
        }
        /// <summary>
        /// 修改 key 的名称
        /// </summary>
        /// <param name="key">旧名称，不含prefix前辍</param>
        /// <param name="newKey">新名称，不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> RenameNx(string key, string newKey)
        {
            if (IsMultiNode) throw new Exception("RenameNx 管道命令，在分区模式下不可用");
            var prefix = Nodes.First().Value.Prefix;
            return PipeCommand(key, (c, k) => { c.Value.RenameNx(k, prefix + newKey); return false; }, obj => obj?.ToString() == "OK");
        }
        /// <summary>
        /// 反序列化给定的序列化值，并将它和给定的 key 关联
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="serializedValue">序列化值</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> Restore(string key, byte[] serializedValue) => PipeCommand(key, (c, k) => { c.Value.Restore(k, 0, serializedValue); return false; }, obj => obj?.ToString() == "OK");
        /// <summary>
        /// 反序列化给定的序列化值，并将它和给定的 key 关联
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="ttlMilliseconds">毫秒为单位为 key 设置生存时间</param>
        /// <param name="serializedValue">序列化值</param>
        /// <returns></returns>
        public CSRedisClientPipe<bool> Restore(string key, long ttlMilliseconds, byte[] serializedValue) => PipeCommand(key, (c, k) => { c.Value.Restore(k, ttlMilliseconds, serializedValue); return false; }, obj => obj?.ToString() == "OK");
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
        public CSRedisClientPipe<string[]> Sort(string key, long? count = null, long offset = 0, string by = null, RedisSortDir? dir = null, bool? isAlpha = null, params string[] get)
        {
            if (IsMultiNode) throw new Exception("Sort 管道命令，在分区模式下不可用");
            return PipeCommand(key, (c, k) => c.Value.Sort(k, offset, count, by, dir, isAlpha, get));
        }
        /// <summary>
        /// 保存给定列表、集合、有序集合 key 中经过排序的元素，参数资料：http://doc.redisfans.com/key/sort.html
        /// </summary>
        /// <param name="key">列表、集合、有序集合，不含prefix前辍</param>
        /// <param name="destination">目标key，不含prefix前辍</param>
        /// <param name="offset">偏移量</param>
        /// <param name="count">数量</param>
        /// <param name="by">排序字段</param>
        /// <param name="dir">排序方式</param>
        /// <param name="isAlpha">对字符串或数字进行排序</param>
        /// <param name="get">根据排序的结果来取出相应的键值</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> SortAndStore(string key, string destination, long? count = null, long offset = 0, string by = null, RedisSortDir? dir = null, bool? isAlpha = null, params string[] get)
        {
            if (IsMultiNode) throw new Exception("SortAndStore 管道命令，在分区模式下不可用");
            return PipeCommand(key, (c, k) => c.Value.SortAndStore(k, (c.Pool as RedisClientPool)?.Prefix + destination, offset, count, by, dir, isAlpha, get));
        }
        /// <summary>
        /// 以秒为单位，返回给定 key 的剩余生存时间
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<long> Ttl(string key) => PipeCommand(key, (c, k) => c.Value.Ttl(k));
        /// <summary>
        /// 返回 key 所储存的值的类型
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public CSRedisClientPipe<KeyType> Type(string key) => PipeCommand(key, (c, k) => { c.Value.Type(k); return KeyType.None; }, obj => Enum.TryParse(obj?.ToString(), true, out KeyType tryenum) ? tryenum : KeyType.None);
        /// <summary>
        /// 迭代当前数据库中的数据库键
        /// </summary>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public CSRedisClientPipe<RedisScan<string>> Scan(long cursor, string pattern = null, long? count = null)
        {
            if (IsMultiNode) throw new Exception("Scan 管道命令，在分区模式下不可用");
            return PipeCommand("Scan", (c, k) => c.Value.Scan(cursor, pattern, count));
        }
        /// <summary>
        /// 迭代当前数据库中的数据库键
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public CSRedisClientPipe<RedisScan<T>> Scan<T>(string key, long cursor, string pattern = null, long? count = null)
        {
            if (IsMultiNode) throw new Exception("Scan<T> 管道命令，在分区模式下不可用");
            return PipeCommand("Scan<T>", (c, k) =>
            {
                c.Value.ScanBytes(cursor, pattern, count); return default(RedisScan<T>);
            }, obj =>
            {
                var scan = (RedisScan<byte[]>)obj;
                return new RedisScan<T>(scan.Cursor, rds.DeserializeRedisValueArrayInternal<T>(scan.Items));
            });
        }
        #endregion
    }
}
