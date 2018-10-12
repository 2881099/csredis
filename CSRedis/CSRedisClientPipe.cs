//using SafeObjectPool;
//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;

//namespace CSRedis {

//	public partial class CSRedisClientPipe<TTT> : IDisposable {
//		private CSRedisClient redis;
//		private Dictionary<string, RedisClientPool> Nodes => redis.Nodes;
//		private List<string> NodeKeys => redis.NodeKeys;
//		private Func<string, string> NodeRule => redis.NodeRule;
//		private Dictionary<string, (List<int> indexes, Object<RedisClient> conn)> Conns = new Dictionary<string, (List<int> indexes, Object<RedisClient> conn)>();
//		private Queue<Func<object, object>> Parsers = new Queue<Func<object, object>>();
//		/// <summary>
//		/// 执行命令数量
//		/// </summary>
//		public int Counter => Parsers.Count;

//		public CSRedisClientPipe(CSRedisClient csredis) {
//			redis = csredis;
//		}
//		private CSRedisClientPipe(CSRedisClient csredis, Dictionary<string, (List<int> indexes, Object<RedisClient> conn)> conns, Queue<Func<object, object>> parsers) {
//			this.redis = csredis;
//			this.Conns = conns;
//			this.Parsers = parsers;
//		}

//		/// <summary>
//		/// 提交批命令
//		/// </summary>
//		/// <returns></returns>
//		public object[] EndPipe() {
//			var ret = new object[Parsers.Count];
//			Exception ex = null;
//			try {
//				foreach (var conn in Conns) {
//					object[] tmp = tmp = conn.Value.conn.Value.EndPipe();
//					for (var a = 0; a < tmp.Length; a++) {
//						var retIdx = conn.Value.indexes[a];
//						ret[retIdx] = tmp[a];
//					}
//				}
//			} catch (Exception ex2) {
//				ex = ex2;
//				throw ex;
//			} finally {
//				foreach (var conn in Conns)
//					(conn.Value.conn.Pool as RedisClientPool).Return(conn.Value.conn, ex);
//			}
//			for (var b = 0; b < ret.Length; b++) {
//				var parse = Parsers.Dequeue();
//				if (parse != null) ret[b] = parse(ret[b]);
//			}
//			Conns.Clear();
//			return ret;
//		}

//		/// <summary>
//		/// 提交批命令
//		/// </summary>
//		public void Dispose() {
//			this.EndPipe();
//		}

//		private CSRedisClientPipe<TReturn> PipeCommand<TReturn>(string key, Func<Object<RedisClient>, string, TReturn> handle) => PipeCommand<TReturn>(key, handle, null);
//		private CSRedisClientPipe<TReturn> PipeCommand<TReturn>(string key, Func<Object<RedisClient>, string, TReturn> handle, Func<object, object> parser) {
//			if (string.IsNullOrEmpty(key)) throw new Exception("key 不可为空或null");
//			var nodeKey = NodeRule == null || Nodes.Count == 1 ? NodeKeys[0] : NodeRule(key);
//			if (Nodes.TryGetValue(nodeKey, out var pool)) Nodes.TryGetValue(nodeKey = NodeKeys[0], out pool);

//			try {
//				if (Conns.TryGetValue(pool.Key, out var conn) == false) {
//					Conns.Add(pool.Key, conn = (new List<int>(), pool.Get()));
//					conn.conn.Value.StartPipe();
//				}
//				key = string.Concat(pool.Prefix, key);
//				handle(conn.conn, key);
//				conn.indexes.Add(Parsers.Count);
//				Parsers.Enqueue(parser);
//			} catch (Exception ex) {
//				foreach (var conn in Conns)
//					(conn.Value.conn.Pool as RedisClientPool).Return(conn.Value.conn, ex);
//				throw ex;
//			}
//			if (typeof(TReturn).FullName == typeof(TTT).FullName) return this as CSRedisClientPipe<TReturn>;// return (CSRedisClientPipe<TReturn>)Convert.ChangeType(this, typeof(CSRedisClientPipe<TReturn>));
//			return new CSRedisClientPipe<TReturn>(redis, this.Conns, this.Parsers);
//		}

//		#region 脚本命令
//		/// <summary>
//		/// 执行脚本
//		/// </summary>
//		/// <param name="script">脚本</param>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="args">参数</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<object> Eval(string script, string key, params object[] args) => PipeCommand(key, (c, k) => c.Value.Eval(script, new[] { k }, args?.Select(z => redis.SerializeInternal(z)).ToArray()));
//		#endregion

//		#region 发布订阅
//		/// <summary>
//		/// Redis Publish 命令用于将信息发送到指定群集节点的频道
//		/// </summary>
//		/// <param name="channel">频道名</param>
//		/// <param name="data">消息文本</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> Publish(string channel, string data) {
//			var msgid = redis.HIncrBy("CSRedisPublishMsgId", channel, 1);
//			return PipeCommand(channel, (c, k) => c.Value.Publish(channel, $"{msgid}|{data}"));
//		}
//		#endregion

//		#region HyperLogLog
//		/// <summary>
//		/// 添加指定元素到 HyperLogLog 中
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="elements">元素</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> PfAdd(string key, params object[] elements) => PipeCommand(key, (c, k) => c.Value.PfAdd(k, elements?.Select(z => redis.SerializeInternal(z)).ToArray()));
//		/// <summary>
//		/// 返回给定 HyperLogLog 的基数估算值。警告：群集模式下，若keys分散在多个节点时，将报错
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> PfCount(string key) => PipeCommand(key, (c, k) => c.Value.PfCount(k));
//		#endregion

//		#region Sorted Set
//		/// <summary>
//		/// 向有序集合添加一个或多个成员，或者更新已存在成员的分数
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="memberScores">一个或多个成员分数</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> ZAdd(string key, params (object, double)[] memberScores) {
//			if (memberScores == null || memberScores.Any() == false) throw new Exception("memberScores 参数不可为空");
//			var ms = memberScores.Select(a => new Tuple<double, object>(a.Item2, redis.SerializeInternal(a.Item1))).ToArray();
//			return PipeCommand(key, (c, k) => c.Value.ZAdd(k, ms));
//		}
//		/// <summary>
//		/// 获取有序集合的成员数量
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> ZCard(string key) => PipeCommand(key, (c, k) => c.Value.ZCard(k));
//		/// <summary>
//		/// 计算在有序集合中指定区间分数的成员数量
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="min">分数最小值</param>
//		/// <param name="max">分数最大值</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> ZCount(string key, double min, double max) => PipeCommand(key, (c, k) => c.Value.ZCount(k, min, max));
//		/// <summary>
//		/// 有序集合中对指定成员的分数加上增量 increment
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="memeber">成员</param>
//		/// <param name="increment">增量值(默认=1)</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<double> ZIncrBy(string key, string memeber, double increment = 1) => PipeCommand(key, (c, k) => c.Value.ZIncrBy(k, increment, memeber));

//		/// <summary>
//		/// 通过索引区间返回有序集合成指定区间内的成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string[]> ZRange(string key, long start, long stop) => PipeCommand(key, (c, k) => c.Value.ZRange(k, start, stop, false));
//		/// <summary>
//		/// 通过索引区间返回有序集合成指定区间内的成员
//		/// </summary>
//		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<T[]> ZRange<T>(string key, long start, long stop) {
//			var type = typeof(T);
//			if (type.FullName == "System.String") return PipeCommand(key, (c, k) => { c.Value.ZRange(k, start, stop, false); return new T[0]; });
//			if (type.FullName == "System.Byte[]") return PipeCommand(key, (c, k) => { c.Value.ZRangeBytes(k, start, stop, false); return new T[0]; });
//			return PipeCommand(key, (c, k) => { c.Value.ZRange(k, start, stop, false); return new T[0]; }, obj => {
//				var ret = obj as string[];
//				var arr = new T[ret.Length];
//				for (var a = 0; a < ret.Length; a++) arr[a] = (T)redis._deserialize(ret[a], typeof(T));
//				return arr;
//			});
//		}
//		/// <summary>
//		/// 通过分数返回有序集合指定区间内的成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="minScore">最小分数</param>
//		/// <param name="maxScore">最大分数</param>
//		/// <param name="limit">返回多少成员</param>
//		/// <param name="offset">返回条件偏移位置</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string[]> ZRangeByScore(string key, double minScore, double maxScore, long? limit = null, long offset = 0) => PipeCommand(key, (c, k) => c.Value.ZRangeByScore(k, minScore, maxScore, false, false, false, offset, limit));
//		/// <summary>
//		/// 通过分数返回有序集合指定区间内的成员和分数
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="minScore">最小分数</param>
//		/// <param name="maxScore">最大分数</param>
//		/// <param name="limit">返回多少成员</param>
//		/// <param name="offset">返回条件偏移位置</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string[]> ZRangeByScoreWithScores(string key, double minScore, double maxScore, long? limit = null, long offset = 0) => PipeCommand(key, (c, k) => c.Value.ZRangeByScore(k, minScore, maxScore, true, false, false, offset, limit), obj => {
//			var res = obj as string[];
//			var ret = new List<(string member, double score)>();
//			if (res != null && res.Length % 2 == 0)
//				for (var a = 0; a < res.Length; a += 2)
//					ret.Add((res[a], double.TryParse(res[a + 1], out var tryd) ? tryd : 0));
//			return ret.ToArray();
//		});
//		/// <summary>
//		/// 返回有序集合中指定成员的索引
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="member">成员</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long?> ZRank(string key, object member) => PipeCommand(key, (c, k) => c.Value.ZRank(k, redis.SerializeInternal(member)));
//		/// <summary>
//		/// 移除有序集合中的一个或多个成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="member">一个或多个成员</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> ZRem(string key, params object[] member) {
//			if (member == null || member.Any() == false) throw new Exception("member 参数不可为空");
//			return PipeCommand(key, (c, k) => c.Value.ZRem(k, member?.Select(z => redis.SerializeInternal(z)).ToArray()));
//		}
//		/// <summary>
//		/// 移除有序集合中给定的排名区间的所有成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> ZRemRangeByRank(string key, long start, long stop) => PipeCommand(key, (c, k) => c.Value.ZRemRangeByRank(k, start, stop));
//		/// <summary>
//		/// 移除有序集合中给定的分数区间的所有成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="minScore">最小分数</param>
//		/// <param name="maxScore">最大分数</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> ZRemRangeByScore(string key, double minScore, double maxScore) => PipeCommand(key, (c, k) => c.Value.ZRemRangeByScore(k, minScore, maxScore));
//		/// <summary>
//		/// 返回有序集中指定区间内的成员，通过索引，分数从高到底
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string[]> ZRevRange(string key, long start, long stop) => PipeCommand(key, (c, k) => c.Value.ZRevRange(k, start, stop, false));
//		/// <summary>
//		/// 返回有序集中指定分数区间内的成员，分数从高到低排序
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="minScore">最小分数</param>
//		/// <param name="maxScore">最大分数</param>
//		/// <param name="limit">返回多少成员</param>
//		/// <param name="offset">返回条件偏移位置</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string[]> ZRevRangeByScore(string key, double maxScore, double minScore, long? limit = null, long? offset = 0) => PipeCommand(key, (c, k) => c.Value.ZRevRangeByScore(k, maxScore, minScore, false, false, false, offset, limit));
//		/// <summary>
//		/// 返回有序集中指定分数区间内的成员和分数，分数从高到低排序
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="minScore">最小分数</param>
//		/// <param name="maxScore">最大分数</param>
//		/// <param name="limit">返回多少成员</param>
//		/// <param name="offset">返回条件偏移位置</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<(string member, double score)[]> ZRevRangeByScoreWithScores(string key, double maxScore, double minScore, long? limit = null, long offset = 0) => PipeCommand(key, (c, k) => {
//			c.Value.ZRevRangeByScore(k, maxScore, minScore, true, false, false, offset, limit);
//			return new(string member, double score)[0];
//		}, obj => {
//			var res = obj as string[];
//			var ret = new List<(string member, double score)>();
//			if (res != null && res.Length % 2 == 0)
//				for (var a = 0; a < res.Length; a += 2)
//					ret.Add((res[a], double.TryParse(res[a + 1], out var tryd) ? tryd : 0));
//			return ret.ToArray();
//		});
//		/// <summary>
//		/// 返回有序集合中指定成员的排名，有序集成员按分数值递减(从大到小)排序
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="member">成员</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long?> ZRevRank(string key, object member) => PipeCommand(key, (c, k) => c.Value.ZRevRank(k, redis.SerializeInternal(member)));
//		/// <summary>
//		/// 返回有序集中，成员的分数值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="member">成员</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<double?> ZScore(string key, object member) => PipeCommand(key, (c, k) => c.Value.ZScore(k, redis.SerializeInternal(member)));
//		/// <summary>
//		/// 迭代有序集合中的元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="cursor">位置</param>
//		/// <param name="pattern">模式</param>
//		/// <param name="count">数量</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<RedisScan<(string, double)>> ZScan(string key, int cursor, string pattern = null, int? count = null) => PipeCommand(key, (c, k) => {
//			c.Value.ZScan(k, cursor, pattern, count);
//			return new RedisScan<(string, double)>(0, null);
//		}, obj => {
//			var scan = obj as RedisScan<Tuple<string, double>>;
//			return new RedisScan<(string, double)>(scan.Cursor, scan.Items.Select(z => (z.Item1, z.Item2)).ToArray());
//		});
//		#endregion

//		#region Set
//		/// <summary>
//		/// 向集合添加一个或多个成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="members">一个或多个成员</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> SAdd(string key, params object[] members) {
//			if (members == null || members.Any() == false) throw new Exception("members 参数不可为空");
//			return PipeCommand(key, (c, k) => c.Value.SAdd(k, members?.Select(z => redis.SerializeInternal(z)).ToArray()));
//		}
//		/// <summary>
//		/// 获取集合的成员数
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> SCard(string key) => PipeCommand(key, (c, k) => c.Value.SCard(k));
//		/// <summary>
//		/// 判断 member 元素是否是集合 key 的成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="member">成员</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> SIsMember(string key, object member) => PipeCommand(key, (c, k) => c.Value.SIsMember(k, redis.SerializeInternal(member)));
//		/// <summary>
//		/// 返回集合中的所有成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string[]> SMembers(string key) => PipeCommand(key, (c, k) => c.Value.SMembers(k));
//		/// <summary>
//		/// 移除并返回集合中的一个随机元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string> SPop(string key) => PipeCommand(key, (c, k) => c.Value.SPop(k));
//		/// <summary>
//		/// 返回集合中的一个随机元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string> SRandMember(string key) => PipeCommand(key, (c, k) => c.Value.SRandMember(k));
//		/// <summary>
//		/// 移除集合中一个或多个成员
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="members">一个或多个成员</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> SRem(string key, params object[] members) {
//			if (members == null || members.Any() == false) throw new Exception("members 参数不可为空");
//			return PipeCommand(key, (c, k) => c.Value.SRem(k, members?.Select(z => redis.SerializeInternal(z)).ToArray()));
//		}
//		/// <summary>
//		/// 迭代集合中的元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="cursor">位置</param>
//		/// <param name="pattern">模式</param>
//		/// <param name="count">数量</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<RedisScan<string>> SScan(string key, int cursor, string pattern = null, int? count = null) => PipeCommand(key, (c, k) => c.Value.SScan(k, cursor, pattern, count));
//		#endregion

//		#region List
//		/// <summary>
//		/// 通过索引获取列表中的元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="index">索引</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string> LIndex(string key, long index) => PipeCommand(key, (c, k) => c.Value.LIndex(k, index));
//		/// <summary>
//		/// 在列表的元素前面插入元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="pivot">列表的元素</param>
//		/// <param name="value">新元素</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> LInsertBefore(string key, string pivot, object value) => PipeCommand(key, (c, k) => c.Value.LInsert(k, RedisInsert.Before, pivot, redis.SerializeInternal(value)));
//		/// <summary>
//		/// 在列表的元素后面插入元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="pivot">列表的元素</param>
//		/// <param name="value">新元素</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> LInsertAfter(string key, string pivot, object value) => PipeCommand(key, (c, k) => c.Value.LInsert(k, RedisInsert.After, pivot, redis.SerializeInternal(value)));
//		/// <summary>
//		/// 获取列表长度
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> LLen(string key) => PipeCommand(key, (c, k) => c.Value.LLen(k));
//		/// <summary>
//		/// 移出并获取列表的第一个元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string> LPop(string key) => PipeCommand(key, (c, k) => c.Value.LPop(k));
//		/// <summary>
//		/// 移除并获取列表最后一个元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string> RPop(string key) => PipeCommand(key, (c, k) => c.Value.RPop(k));
//		/// <summary>
//		/// 将一个或多个值插入到列表头部
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">一个或多个值</param>
//		/// <returns>执行 LPUSH 命令后，列表的长度</returns>
//		public CSRedisClientPipe<long> LPush(string key, params object[] value) {
//			if (value == null || value.Any() == false) throw new Exception("value 参数不可为空"); ;
//			return PipeCommand(key, (c, k) => c.Value.LPush(k, value?.Select(z => redis.SerializeInternal(z)).ToArray()));
//		}
//		/// <summary>
//		/// 将一个值插入到已存在的列表头部
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">值</param>
//		/// <returns>执行 LPUSHX 命令后，列表的长度。</returns>
//		public CSRedisClientPipe<long> LPushX(string key, object value) => PipeCommand(key, (c, k) => c.Value.LPushX(k, redis.SerializeInternal(value)));
//		/// <summary>
//		/// 在列表中添加一个或多个值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">一个或多个值</param>
//		/// <returns>执行 RPUSH 命令后，列表的长度</returns>
//		public CSRedisClientPipe<long> RPush(string key, params object[] value) {
//			if (value == null || value.Any() == false) throw new Exception("value 参数不可为空"); ;
//			return PipeCommand(key, (c, k) => c.Value.RPush(k, value?.Select(z => redis.SerializeInternal(z)).ToArray()));
//		}
//		/// <summary>
//		/// 为已存在的列表添加值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">一个或多个值</param>
//		/// <returns>执行 RPUSHX 命令后，列表的长度</returns>
//		public CSRedisClientPipe<long> RPushX(string key, object value) => PipeCommand(key, (c, k) => c.Value.RPushX(k, redis.SerializeInternal(value)));
//		/// <summary>
//		/// 获取列表指定范围内的元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string[]> LRang(string key, long start, long stop) => PipeCommand(key, (c, k) => c.Value.LRange(k, start, stop));
//		/// <summary>
//		/// 根据参数 count 的值，移除列表中与参数 value 相等的元素
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="count">移除的数量，大于0时从表头删除数量count，小于0时从表尾删除数量-count，等于0移除所有</param>
//		/// <param name="value">元素</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> LRem(string key, long count, object value) => PipeCommand(key, (c, k) => c.Value.LRem(k, count, redis.SerializeInternal(value)));
//		/// <summary>
//		/// 通过索引设置列表元素的值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="index">索引</param>
//		/// <param name="value">值</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> LSet(string key, long index, object value) => PipeCommand(key, (c, k) => {
//			c.Value.LSet(k, index, redis.SerializeInternal(value));
//			return false;
//		}, ret => ret?.ToString() == "OK");
//		/// <summary>
//		/// 对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> LTrim(string key, long start, long stop) => PipeCommand(key, (c, k) => {
//			c.Value.LTrim(k, start, stop);
//			return false;
//		}, ret => ret?.ToString() == "OK");
//		#endregion

//		#region Hash 操作
//		/// <summary>
//		/// 将哈希表 key 中的字段 field 的值设为 value
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="field">字段</param>
//		/// <param name="value">值</param>
//		/// <returns>如果字段是哈希表中的一个新建字段，并且值设置成功，返回true。如果哈希表中域字段已经存在且旧值已被新值覆盖，返回false。</returns>
//		public CSRedisClientPipe<bool> HSet(string key, string field, object value) => PipeCommand(key, (c, k) => c.Value.HSet(k, field, redis.SerializeInternal(value)));
//		/// <summary>
//		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="keyValues">字段-值 元组数组</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> HMSet(string key, params (string field, object value)[] keyValues) {
//			if (keyValues == null || keyValues.Any() == false) throw new Exception("keyValues 参数不可为空");
//			return PipeCommand(key, (c, k) => {
//				c.Value.HMSet(k, redis.GetKeyValues(keyValues));
//				return false;
//			}, ret => ret?.ToString() == "OK");
//		}
//		/// <summary>
//		/// 只有在字段 field 不存在时，设置哈希表字段的值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="field">字段</param>
//		/// <param name="value">值(string 或 byte[])</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> HSetNx(string key, string field, object value) => PipeCommand(key, (c, k) => c.Value.HSetNx(k, field, redis.SerializeInternal(value)));
//		/// <summary>
//		/// 获取存储在哈希表中指定字段的值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="field">字段</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string> HGet(string key, string field) => PipeCommand(key, (c, k) => c.Value.HGet(k, field));
//		/// <summary>
//		/// 获取存储在哈希表中指定字段的值
//		/// </summary>
//		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="field">字段</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<T> HGet<T>(string key, string field) {
//			var type = typeof(T);
//			if (type.FullName == "System.String") return PipeCommand(key, (c, k) => { c.Value.HGet(k, field); return default(T); });
//			if (type.FullName == "System.Byte[]") return PipeCommand(key, (c, k) => { c.Value.HGetBytes(k, field); return default(T); });
//			return PipeCommand(key, (c, k) => { c.Value.HGet(k, field); return default(T); }, obj => redis._deserialize(obj?.ToString(), typeof(T)));
//		}
//		/// <summary>
//		/// 获取存储在哈希表中多个字段的值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="fields">字段</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string[]> HMGet(string key, params string[] fields) {
//			if (fields == null || fields.Any() == false) throw new Exception("fields 参数不可为空");
//			return PipeCommand(key, (c, k) => c.Value.HMGet(k, fields));
//		}
//		/// <summary>
//		/// 获取存储在哈希表中多个字段的值
//		/// </summary>
//		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="fields">一个或多个字段</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<T[]> HMGet<T>(string key, params string[] fields) {
//			if (fields == null || fields.Any() == false) throw new Exception("fields 参数不可为空");
//			var type = typeof(T);
//			if (type.FullName == "System.String") return PipeCommand(key, (c, k) => { c.Value.HMGet(k, fields); return new T[0]; });
//			if (type.FullName == "System.Byte[]") return PipeCommand(key, (c, k) => { c.Value.HMGetBytes(k, fields); return new T[0]; });
//			return PipeCommand(key, (c, k) => { c.Value.HMGet(k, fields); return new T[0]; }, obj => {
//				var ret = obj as string[];
//				var arr = new T[ret.Length];
//				for (var a = 0; a < ret.Length; a++) arr[a] = (T)redis._deserialize(ret[a], typeof(T));
//				return arr;
//			});
//		}
//		/// <summary>
//		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="field">字段</param>
//		/// <param name="value">增量值(默认=1)</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> HIncrBy(string key, string field, long value = 1) => PipeCommand(key, (c, k) => c.Value.HIncrBy(k, field, value));
//		/// <summary>
//		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="field">字段</param>
//		/// <param name="value">增量值(默认=1)</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<double> HIncrByFloat(string key, string field, double value = 1) => PipeCommand(key, (c, k) => c.Value.HIncrByFloat(k, field, value));
//		/// <summary>
//		/// 删除一个或多个哈希表字段
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="fields">字段</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> HDel(string key, params string[] fields) {
//			if (fields == null || fields.Any() == false) throw new Exception("fields 参数不可为空");
//			return PipeCommand(key, (c, k) => c.Value.HDel(k, fields));
//		}
//		/// <summary>
//		/// 查看哈希表 key 中，指定的字段是否存在
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="field">字段</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> HExists(string key, string field) => PipeCommand(key, (c, k) => c.Value.HExists(k, field));
//		/// <summary>
//		/// 获取哈希表中字段的数量
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> HLen(string key) => PipeCommand(key, (c, k) => c.Value.HLen(k));
//		/// <summary>
//		/// 获取在哈希表中指定 key 的所有字段和值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<Dictionary<string, string>> HGetAll(string key) => PipeCommand(key, (c, k) => c.Value.HGetAll(k));
//		/// <summary>
//		/// 获取所有哈希表中的字段
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string[]> HKeys(string key) => PipeCommand(key, (c, k) => c.Value.HKeys(k));
//		/// <summary>
//		/// 获取哈希表中所有值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string[]> HVals(string key) => PipeCommand(key, (c, k) => c.Value.HVals(k));
//		/// <summary>
//		/// 迭代哈希表中的键值对
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="cursor">位置</param>
//		/// <param name="pattern">模式</param>
//		/// <param name="count">数量</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<RedisScan<(string field, string value)>> HScan(string key, int cursor, string pattern = null, int? count = null) => PipeCommand(key, (c, k) => {
//			c.Value.HScan(k, cursor, pattern, count);
//			return new RedisScan<(string, string)>(1, null);
//		}, obj => {
//			var scan = obj as RedisScan<Tuple<string, string>>;
//			return new RedisScan<(string, string)>(scan.Cursor, scan.Items.Select(z => (z.Item1, z.Item2)).ToArray());
//		});
//		#endregion

//		#region String
//		/// <summary>
//		/// 设置指定 key 的值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">字符串、byte[]、对象</param>
//		/// <param name="expireSeconds">过期(秒单位)</param>
//		/// <param name="exists">Nx, Xx</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> Set(string key, object value, int expireSeconds = -1, RedisExistence? exists = null) {
//			object redisValule = redis.SerializeInternal(value);
//			if (expireSeconds <= 0 && exists == null) return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule); return false; }, obj => obj?.ToString() == "OK");
//			if (expireSeconds <= 0 && exists != null) return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule, null, exists); return false; }, obj => obj?.ToString() == "OK");
//			if (expireSeconds > 0 && exists == null) return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule, expireSeconds, null); return false; }, obj => obj?.ToString() == "OK");
//			if (expireSeconds > 0 && exists != null) return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule, expireSeconds, exists); return false; }, obj => obj?.ToString() == "OK");
//			return PipeCommand(key, (c, k) => { c.Value.Set(k, redisValule); return false; }, obj => obj?.ToString() == "OK");
//		}
//		/// <summary>
//		/// 只有在 key 不存在时设置 key 的值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">字符串、byte[]、对象</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> SetNx(string key, object value) => PipeCommand(key, (c, k) => c.Value.SetNx(k, redis.SerializeInternal(value)));
//		/// <summary>
//		/// 获取指定 key 的值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string> Get(string key) => PipeCommand(key, (c, k) => c.Value.Get(k));
//		/// <summary>
//		/// 获取指定 key 的值
//		/// </summary>
//		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<T> Get<T>(string key) {
//			var type = typeof(T);
//			if (type.FullName == "System.String") return PipeCommand(key, (c, k) => { c.Value.Get(k); return default(T); });
//			if (type.FullName == "System.Byte[]") return PipeCommand(key, (c, k) => { c.Value.GetBytes(k); return default(T); });
//			return PipeCommand(key, (c, k) => { c.Value.Get(k); return default(T); }, obj => redis._deserialize(obj?.ToString(), typeof(T)));
//		}
//		/// <summary>
//		/// 返回 key 中字符串值的子字符
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string> GetRange(string key, long start, long end) => PipeCommand(key, (c, k) => c.Value.GetRange(k, start, end));
//		/// <summary>
//		/// 用 value 参数覆写给定 key 所储存的字符串值，从偏移量 offset 开始
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="offset">偏移量</param>
//		/// <param name="value">值</param>
//		/// <returns>被修改后的字符串长度</returns>
//		public CSRedisClientPipe<long> SetRange(string key, uint offset, object value) => PipeCommand(key, (c, k) => c.Value.SetRange(k, offset, redis.SerializeInternal(value)));
//		/// <summary>
//		/// 将给定 key 的值设为 value
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">字符串</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string> GetSet(string key, object value) => PipeCommand(key, (c, k) => c.Value.GetSet(k, redis.SerializeInternal(value)));
//		/// <summary>
//		/// 将给定 key 的值设为 value ，并返回 key 的旧值(old value)
//		/// </summary>
//		/// <typeparam name="T">返回类型，支持 byte[] 及 其他类型</typeparam>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">字符串、byte[]、对象</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<T> GetSet<T>(string key, T value) {
//			var redisValue = redis.SerializeInternal(value);
//			var type = typeof(T);
//			if (type.FullName == "System.String") return PipeCommand(key, (c, k) => { c.Value.GetSet(k, redisValue); return default(T); });
//			if (type.FullName == "System.Byte[]") return PipeCommand(key, (c, k) => { c.Value.GetSetBytes(k, (byte[])redisValue); return default(T); });
//			return PipeCommand(key, (c, k) => { c.Value.GetSet(k, redisValue); return default(T); }, obj => (T)redis._deserialize(obj?.ToString(), typeof(T)));
//		}
//		/// <summary>
//		/// 对 key 所储存的字符串值，获取指定偏移量上的位(bit)
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="offset">偏移量</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> GetBit(string key, uint offset) => PipeCommand(key, (c, k) => c.Value.GetBit(k, offset));
//		/// <summary>
//		/// 对 key 所储存的字符串值，设置或清除指定偏移量上的位(bit)
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="offset">偏移量</param>
//		/// <param name="value">值</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> SetBit(string key, uint offset, bool value) => PipeCommand(key, (c, k) => c.Value.SetBit(k, offset, value));
//		/// <summary>
//		/// 计算给定字符串中，被设置为 1 的比特位的数量
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="start">开始位置</param>
//		/// <param name="end">结束位置</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> BitCount(string key, long start, long end) => PipeCommand(key, (c, k) => c.Value.BitCount(k, start, end));
//		/// <summary>
//		/// 返回 key 所储存的字符串值的长度
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> StrLen(string key) => PipeCommand(key, (c, k) => c.Value.StrLen(k));
//		/// <summary>
//		/// 将 key 所储存的值加上给定的增量值（increment）
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">增量值(默认=1)</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> IncrBy(string key, long value = 1) => PipeCommand(key, (c, k) => c.Value.IncrBy(k, value));
//		/// <summary>
//		/// 将 key 所储存的值加上给定的浮点增量值（increment）
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">增量值(默认=1)</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<double> IncrBy(string key, double value = 1) => PipeCommand(key, (c, k) => c.Value.IncrByFloat(k, value));
//		/// <summary>
//		/// 如果 key 已经存在并且是一个字符串， APPEND 命令将指定的 value 追加到该 key 原来值（value）的末尾
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="value">字符串</param>
//		/// <returns>追加指定值之后， key 中字符串的长度</returns>
//		public CSRedisClientPipe<long> Append(string key, object value) => PipeCommand(key, (c, k) => c.Value.Append(k, redis.SerializeInternal(value)));
//		#endregion

//		#region Key
//		/// <summary>
//		/// 用于在 key 存在时删除 key
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> Del(string key) => PipeCommand(key, (c, k) => c.Value.Del(k));
//		/// <summary>
//		/// 序列化给定 key ，并返回被序列化的值
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe< byte[]> Dump(string key) => PipeCommand(key, (c, k) => c.Value.Dump(k));
//		/// <summary>
//		/// 检查给定 key 是否存在
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> Exists(string key) => PipeCommand(key, (c, k) => c.Value.Exists(k));
//		/// <summary>
//		/// 为给定 key 设置过期时间
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="seconds">过期秒数</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> Expire(string key, int seconds) => PipeCommand(key, (c, k) => c.Value.Expire(k, seconds));
//		/// <summary>
//		/// 为给定 key 设置过期时间
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="expire">过期时间</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> Expire(string key, TimeSpan expire) => PipeCommand(key, (c, k) => c.Value.Expire(k, expire));
//		/// <summary>
//		/// 为给定 key 设置过期时间
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <param name="expire">过期时间</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> ExpireAt(string key, DateTime expire) => PipeCommand(key, (c, k) => c.Value.ExpireAt(k, expire));
//		/// <summary>
//		/// 查找所有分区节点中符合给定模式(pattern)的 key
//		/// </summary>
//		/// <param name="pattern">如：runoob*</param>
//		/// <returns></returns>
//		[Obsolete("管道模式下，Keys 功能在多节点分区模式下不可用。")]
//		public CSRedisClientPipe<string[]> Keys(string pattern) {
//			if (Nodes.Count > 1) throw new Exception("管道模式下，Keys 功能在多节点分区模式下不可用。");
//			return PipeCommand("", (c, k) => c.Value.Keys(pattern));
//		}
//		/// <summary>
//		/// 移除 key 的过期时间，key 将持久保持
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<bool> Persist(string key) => PipeCommand(key, (c, k) => c.Value.Persist(k));
//		/// <summary>
//		/// 以秒为单位，返回给定 key 的剩余生存时间
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> Ttl(string key) => PipeCommand(key, (c, k) => c.Value.Ttl(k));
//		/// <summary>
//		/// 以毫秒为单位返回 key 的剩余的过期时间
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long> PTtl(string key) => PipeCommand(key, (c, k) => c.Value.PTtl(k));
//		/// <summary>
//		/// 返回 key 所储存的值的类型
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<CSRedisClient.KeyType> Type(string key) => PipeCommand(key, (c, k) => { c.Value.Type(k); return CSRedisClient.KeyType.Unkown; }, obj => Enum.TryParse(obj?.ToString(), out CSRedisClient.KeyType tryenum) ? tryenum : CSRedisClient.KeyType.Unkown);
//		/// <summary>
//		/// 该返回给定 key 锁储存的值所使用的内部表示(representation)
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<string> ObjectEncoding(string key) => PipeCommand(key, (c, k) => c.Value.ObjectEncoding(k));
//		/// <summary>
//		/// 该返回给定 key 引用所储存的值的次数。此命令主要用于除错
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long?> ObjectRefCount(string key) => PipeCommand(key, (c, k) => c.Value.Object(RedisObjectSubCommand.RefCount, k));
//		/// <summary>
//		/// 返回给定 key 自储存以来的空转时间(idle， 没有被读取也没有被写入)，以秒为单位
//		/// </summary>
//		/// <param name="key">不含prefix前辍</param>
//		/// <returns></returns>
//		public CSRedisClientPipe<long?> ObjectIdleTime(string key) => PipeCommand(key, (c, k) => c.Value.Object(RedisObjectSubCommand.IdleTime, k));
//		/// <summary>
//		/// 返回给定列表、集合、有序集合 key 中经过排序的元素，参数资料：http://doc.redisfans.com/key/sort.html
//		/// </summary>
//		/// <param name="key">列表、集合、有序集合，不含prefix前辍</param>
//		/// <param name="offset">偏移量</param>
//		/// <param name="count">数量</param>
//		/// <param name="by">排序字段</param>
//		/// <param name="dir">排序方式</param>
//		/// <param name="isAlpha">对字符串或数字进行排序</param>
//		/// <param name="get">根据排序的结果来取出相应的键值</param>
//		/// <returns></returns>
//		[Obsolete("管道模式下，Sort 功能在多节点分区模式下不可用。")]
//		public CSRedisClientPipe<string[]> Sort(string key, long? offset = null, long? count = null, string by = null, RedisSortDir? dir = null, bool? isAlpha = null, params string[] get) {
//			if (Nodes.Count > 1) throw new Exception("Sort 功能在多节点分区模式下不可用。");
//			return PipeCommand(key, (c, k) => c.Value.Sort(k, offset, count, by, dir, isAlpha, get));
//		}
//		/// <summary>
//		/// 保存给定列表、集合、有序集合 key 中经过排序的元素，参数资料：http://doc.redisfans.com/key/sort.html
//		/// </summary>
//		/// <param name="key">列表、集合、有序集合，不含prefix前辍</param>
//		/// <param name="destinationKey">目标key，不含prefix前辍</param>
//		/// <param name="offset">偏移量</param>
//		/// <param name="count">数量</param>
//		/// <param name="by">排序字段</param>
//		/// <param name="dir">排序方式</param>
//		/// <param name="isAlpha">对字符串或数字进行排序</param>
//		/// <param name="get">根据排序的结果来取出相应的键值</param>
//		/// <returns></returns>
//		[Obsolete("管道模式下，SortAndStore 功能在多节点分区模式下不可用。")]
//		public CSRedisClientPipe<long> SortAndStore(string key, string destinationKey, long? offset = null, long? count = null, string by = null, RedisSortDir? dir = null, bool? isAlpha = null, params string[] get) {
//			if (Nodes.Count > 1) throw new Exception("SortAndStore 功能在多节点分区模式下不可用。");
//			return PipeCommand(key, (c, k) => c.Value.SortAndStore(k, (c.Pool as RedisClientPool)?.Prefix + destinationKey, offset, count, by, dir, isAlpha, get));
//		}
//		#endregion
//	}
//}
