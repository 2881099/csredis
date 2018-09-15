using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CSRedis {
	public partial class CSRedisClientPipe : IDisposable {
		private CSRedisClient redis;
		private Dictionary<string, ConnectionPool> ClusterNodes => redis.ClusterNodes;
		private List<string> ClusterKeys => redis.ClusterKeys;
		private Func<string, string> ClusterRule => redis.ClusterRule;
		private Dictionary<string, (List<int> indexes, RedisConnection2 conn)> Conns = new Dictionary<string, (List<int> indexes, RedisConnection2 conn)>();
		private Queue<Func<object, object>> Parsers = new Queue<Func<object, object>>();
		/// <summary>
		/// 执行命令数量
		/// </summary>
		public int Counter => Parsers.Count;

		public CSRedisClientPipe(CSRedisClient csredis) {
			redis = csredis;
		}

		/// <summary>
		/// 提交批命令
		/// </summary>
		/// <returns></returns>
		public object[] EndPipe() {
			var ret = new object[Parsers.Count];
			foreach (var conn in Conns) {
				var tmp = conn.Value.conn.Client.EndPipe();
				conn.Value.conn.Pool.ReleaseConnection(conn.Value.conn);
				for (var a = 0; a < tmp.Length; a++) {
					var retIdx = conn.Value.indexes[a];
					ret[retIdx] = tmp[a];
				}
			}
			for (var b = 0; b < ret.Length; b++) {
				var parse = Parsers.Dequeue();
				if (parse != null) ret[b] = parse(ret[b]);
			}
			Conns.Clear();
			return ret;
		}

		/// <summary>
		/// 提交批命令
		/// </summary>
		public void Dispose() {
			this.EndPipe();
		}

		private CSRedisClientPipe PipeCommand(string key, Action<RedisClient, string> hander, Func<object, object> parser = null) {
			if (key == null) return this;
			var clusterKey = ClusterRule == null || ClusterNodes.Count == 1 ? ClusterKeys[0] : ClusterRule(key);
			if (ClusterNodes.TryGetValue(clusterKey, out var pool)) ClusterNodes.TryGetValue(clusterKey = ClusterKeys[0], out pool);
			if (Conns.TryGetValue(clusterKey, out var conn) == false) {
				Conns.Add(clusterKey, conn = (new List<int>(), pool.GetConnection()));
				conn.conn.Client.StartPipe();
			}
			key = string.Concat(pool.Prefix, key);
			hander(conn.conn.Client, key);
			conn.indexes.Add(Parsers.Count);
			Parsers.Enqueue(parser);
			return this;
		}

		/// <summary>
		/// 设置指定 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字符串值</param>
		/// <param name="expireSeconds">过期(秒单位)</param>
		/// <param name="exists">Nx, Xx</param>
		/// <returns></returns>
		public CSRedisClientPipe Set(string key, string value, int expireSeconds = -1, CSRedisExistence? exists = null) =>
			PipeCommand(key, (c, k) => {
				if (expireSeconds > 0 || exists != null) c.Set(k, value, expireSeconds > 0 ? new int?(expireSeconds) : null, exists == CSRedisExistence.Nx ? new RedisExistence?(RedisExistence.Nx) : (exists == CSRedisExistence.Xx ? new RedisExistence?(RedisExistence.Xx) : null));
				else c.Set(k, value);
			}, v => "OK".Equals(v));

		/// <summary>
		/// 设置指定 key 的值(字节流)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">字节流</param>
		/// <param name="expireSeconds">过期(秒单位)</param>
		/// <param name="exists">Nx, Xx</param>
		/// <returns></returns>
		public CSRedisClientPipe SetBytes(string key, byte[] value, int expireSeconds = -1, CSRedisExistence? exists = null) =>
			PipeCommand(key, (c, k) => {
				if (expireSeconds > 0 || exists != null) c.Set(k, value, expireSeconds > 0 ? new int?(expireSeconds) : null, exists == CSRedisExistence.Nx ? new RedisExistence?(RedisExistence.Nx) : (exists == CSRedisExistence.Xx ? new RedisExistence?(RedisExistence.Xx) : null));
				else c.Set(k, value);
			}, v => "OK".Equals(v));
		/// <summary>
		/// 获取指定 key 的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe Get(string key) => PipeCommand(key, (c, k) => c.Get(k));
		/// <summary>
		/// 获取指定 key 的值(字节流)
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe GetBytes(string key) => PipeCommand(key, (c, k) => c.GetBytes(k));
		/// <summary>
		/// 用于在 key 存在时删除 key
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe Remove(string key) => PipeCommand(key, (c, k) => c.Del(k));
		/// <summary>
		/// 检查给定 key 是否存在
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe Exists(string key) => PipeCommand(key, (c, k) => c.Exists(k));
		/// <summary>
		/// 将 key 所储存的值加上给定的增量值（increment）
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public CSRedisClientPipe Increment(string key, long value = 1) => PipeCommand(key, (c, k) => c.IncrBy(k, value));
		/// <summary>
		/// 为给定 key 设置过期时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="expire">过期时间</param>
		/// <returns></returns>
		public CSRedisClientPipe Expire(string key, TimeSpan expire) => PipeCommand(key, (c, k) => c.Expire(k, expire));
		/// <summary>
		/// 以秒为单位，返回给定 key 的剩余生存时间
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe Ttl(string key) => PipeCommand(key, (c, k) => c.Ttl(k));
		/// <summary>
		/// 执行脚本
		/// </summary>
		/// <param name="script">脚本</param>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="args">参数</param>
		/// <returns></returns>
		public CSRedisClientPipe Eval(string script, string key, params object[] args) => PipeCommand(key, (c, k) => c.Eval(script, new[] { k }, args));
		/// <summary>
		/// Redis Publish 命令用于将信息发送到指定群集节点的频道
		/// </summary>
		/// <param name="channel">频道名</param>
		/// <param name="data">消息文本</param>
		/// <returns></returns>
		public CSRedisClientPipe Publish(string channel, string data) {
			var msgid = redis.HashIncrement("CSRedisPublishMsgId", channel, 1);
			return PipeCommand(channel, (c, k) => c.Publish(channel, $"{msgid}|{data}"));
		}

		#region Hash 操作
		/// <summary>
		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="keyValues">field1 value1 [field2 value2]</param>
		/// <returns></returns>
		public CSRedisClientPipe HashSet(string key, params object[] keyValues) => HashSetExpire(key, TimeSpan.Zero, keyValues);
		/// <summary>
		/// 同时将多个 field-value (域-值)对设置到哈希表 key 中
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="expire">过期时间</param>
		/// <param name="keyValues">field1 value1 [field2 value2]</param>
		/// <returns></returns>
		public CSRedisClientPipe HashSetExpire(string key, TimeSpan expire, params object[] keyValues) {
			if (keyValues == null || keyValues.Any() == false) return null;
			if (expire > TimeSpan.Zero) {
				var lua = "ARGV[1] = redis.call('HMSET', KEYS[1]";
				var argv = new List<object>();
				for (int a = 0, argvIdx = 3; a < keyValues.Length; a += 2, argvIdx++) {
					lua += ", '" + (keyValues[a]?.ToString().Replace("'", "\\'")) + "', ARGV[" + argvIdx + "]";
					argv.Add(keyValues[a + 1]);
				}
				lua += @") redis.call('EXPIRE', KEYS[1], ARGV[2]) return ARGV[1]";
				argv.InsertRange(0, new object[] { "", (long) expire.TotalSeconds });
				return Eval(lua, key, argv.ToArray());
			}
			return PipeCommand(key, (c, k) => c.HMSet(k, keyValues));
		}
		/// <summary>
		/// 获取存储在哈希表中指定字段的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		public CSRedisClientPipe HashGet(string key, string field) => PipeCommand(key, (c, k) => c.HGet(k, field));
		/// <summary>
		/// 获取存储在哈希表中多个字段的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="fields">字段</param>
		/// <returns></returns>
		public CSRedisClientPipe HashMGet(string key, params string[] fields) => PipeCommand(key, (c, k) => c.HMGet(k, fields));
		/// <summary>
		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public CSRedisClientPipe HashIncrement(string key, string field, long value = 1) => PipeCommand(key, (c, k) => c.HIncrBy(k, field, value));
		/// <summary>
		/// 为哈希表 key 中的指定字段的整数值加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <param name="value">增量值(默认=1)</param>
		/// <returns></returns>
		public CSRedisClientPipe HashIncrementFloat(string key, string field, double value = 1) => PipeCommand(key, (c, k) => c.HIncrByFloat(k, field, value));
		/// <summary>
		/// 删除一个或多个哈希表字段
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="fields">字段</param>
		/// <returns></returns>
		public CSRedisClientPipe HashDelete(string key, params string[] fields) => fields == null || fields.Any() == false ? this : PipeCommand(key, (c, k) => c.HDel(k, fields));
		/// <summary>
		/// 查看哈希表 key 中，指定的字段是否存在
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="field">字段</param>
		/// <returns></returns>
		public CSRedisClientPipe HashExists(string key, string field) => PipeCommand(key, (c, k) => c.HExists(k, field));
		/// <summary>
		/// 获取哈希表中字段的数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe HashLength(string key) => PipeCommand(key, (c, k) => c.HLen(k));
		/// <summary>
		/// 获取在哈希表中指定 key 的所有字段和值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe HashGetAll(string key) => PipeCommand(key, (c, k) => c.HGetAll(k));
		/// <summary>
		/// 获取所有哈希表中的字段
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe HashKeys(string key) => PipeCommand(key, (c, k) => c.HKeys(k));
		/// <summary>
		/// 获取哈希表中所有值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe HashVals(string key) => PipeCommand(key, (c, k) => c.HVals(k));
		#endregion

		#region List 操作
		/// <summary>
		/// 通过索引获取列表中的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="index">索引</param>
		/// <returns></returns>
		public CSRedisClientPipe LIndex(string key, long index) => PipeCommand(key, (c, k) => c.LIndex(k, index));
		/// <summary>
		/// 在列表的元素前面插入元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="pivot">列表的元素</param>
		/// <param name="value">新元素</param>
		/// <returns></returns>
		public CSRedisClientPipe LInsertBefore(string key, string pivot, string value) => PipeCommand(key, (c, k) => c.LInsert(k, RedisInsert.Before, pivot, value));
		/// <summary>
		/// 在列表的元素后面插入元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="pivot">列表的元素</param>
		/// <param name="value">新元素</param>
		/// <returns></returns>
		public CSRedisClientPipe LInsertAfter(string key, string pivot, string value) => PipeCommand(key, (c, k) => c.LInsert(k, RedisInsert.After, pivot, value));
		/// <summary>
		/// 获取列表长度
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe LLen(string key) => PipeCommand(key, (c, k) => c.LLen(k));
		/// <summary>
		/// 移出并获取列表的第一个元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe LPop(string key) => PipeCommand(key, (c, k) => c.LPop(k));
		/// <summary>
		/// 移除并获取列表最后一个元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe RPop(string key) => PipeCommand(key, (c, k) => c.RPop(k));
		/// <summary>
		/// 将一个或多个值插入到列表头部
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">一个或多个值</param>
		/// <returns></returns>
		public CSRedisClientPipe LPush(string key, params string[] value) => value == null || value.Any() == false ? this : PipeCommand(key, (c, k) => c.LPush(k, value));
		/// <summary>
		/// 在列表中添加一个或多个值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="value">一个或多个值</param>
		/// <returns></returns>
		public CSRedisClientPipe RPush(string key, params string[] value) => value == null || value.Any() == false ? this : PipeCommand(key, (c, k) => c.RPush(k, value));
		/// <summary>
		/// 获取列表指定范围内的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public CSRedisClientPipe LRang(string key, long start, long stop) => PipeCommand(key, (c, k) => c.LRange(k, start, stop));
		/// <summary>
		/// 根据参数 count 的值，移除列表中与参数 value 相等的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="count">移除的数量，大于0时从表头删除数量count，小于0时从表尾删除数量-count，等于0移除所有</param>
		/// <param name="value">元素</param>
		/// <returns></returns>
		public CSRedisClientPipe LRem(string key, long count, string value) => PipeCommand(key, (c, k) => c.LRem(k, count, value));
		/// <summary>
		/// 通过索引设置列表元素的值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="index">索引</param>
		/// <param name="value">值</param>
		/// <returns></returns>
		public CSRedisClientPipe LSet(string key, long index, string value) => PipeCommand(key, (c, k) => c.LSet(k, index, value));
		/// <summary>
		/// 对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public CSRedisClientPipe LTrim(string key, long start, long stop) => PipeCommand(key, (c, k) => c.LTrim(k, start, stop));
		#endregion

		#region Set 操作
		/// <summary>
		/// 向集合添加一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="members">一个或多个成员</param>
		/// <returns></returns>
		public CSRedisClientPipe SAdd(string key, params string[] members) => members == null || members.Any() == false ? this : PipeCommand(key, (c, k) => c.SAdd(k, members));
		/// <summary>
		/// 获取集合的成员数
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe SCard(string key) => PipeCommand(key, (c, k) => c.SCard(k));
		/// <summary>
		/// 返回集合中的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe SMembers(string key) => PipeCommand(key, (c, k) => c.SMembers(k));
		/// <summary>
		/// 移除并返回集合中的一个随机元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe SPop(string key) => PipeCommand(key, (c, k) => c.SPop(k));
		/// <summary>
		/// 返回集合中一个或多个随机数的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="count">返回个数</param>
		/// <returns></returns>
		public CSRedisClientPipe SRandMember(string key, int count = 1) => PipeCommand(key, (c, k) => c.SRandMember(k, count));
		/// <summary>
		/// 移除集合中一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="members">一个或多个成员</param>
		/// <returns></returns>
		public CSRedisClientPipe SRem(string key, params string[] members) => members == null || members.Any() == false ? this : PipeCommand(key, (c, k) => c.SRem(k, members));
		/// <summary>
		/// 迭代集合中的元素
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="cursor">位置</param>
		/// <param name="pattern">模式</param>
		/// <param name="count">数量</param>
		/// <returns></returns>
		public CSRedisClientPipe SScan(string key, int cursor, string pattern = null, int? count = null) => PipeCommand(key, (c, k) => c.SScan(k, cursor, pattern, count));
		#endregion

		#region Sorted Set 操作
		/// <summary>
		/// 向有序集合添加一个或多个成员，或者更新已存在成员的分数
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="memberScores">一个或多个成员分数</param>
		/// <returns></returns>
		public CSRedisClientPipe ZAdd(string key, params (double, string)[] memberScores) {
			if (memberScores == null || memberScores.Any() == false) return this;
			var ms = memberScores.Select(a => new Tuple<double, string>(a.Item1, a.Item2)).ToArray();
			return PipeCommand(key, (c, k) => c.ZAdd<double, string>(k, ms));
		}
		/// <summary>
		/// 获取有序集合的成员数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <returns></returns>
		public CSRedisClientPipe ZCard(string key) => PipeCommand(key, (c, k) => c.ZCard(k));
		/// <summary>
		/// 计算在有序集合中指定区间分数的成员数量
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="min">分数最小值</param>
		/// <param name="max">分数最大值</param>
		/// <returns></returns>
		public CSRedisClientPipe ZCount(string key, double min, double max) => PipeCommand(key, (c, k) => c.ZCount(k, min, max));
		/// <summary>
		/// 有序集合中对指定成员的分数加上增量 increment
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="memeber">成员</param>
		/// <param name="increment">增量值(默认=1)</param>
		/// <returns></returns>
		public CSRedisClientPipe ZIncrBy(string key, string memeber, double increment = 1) => PipeCommand(key, (c, k) => c.ZIncrBy(k, increment, memeber));

		/// <summary>
		/// 通过索引区间返回有序集合成指定区间内的成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public CSRedisClientPipe ZRange(string key, long start, long stop) => PipeCommand(key, (c, k) => c.ZRange(k, start, stop, false));
		/// <summary>
		/// 通过分数返回有序集合指定区间内的成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public CSRedisClientPipe ZRangeByScore(string key, double minScore, double maxScore, long? limit = null, long offset = 0) => PipeCommand(key, (c, k) => c.ZRangeByScore(k, minScore, maxScore, false, false, false, offset, limit));
		/// <summary>
		/// 返回有序集合中指定成员的索引
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public CSRedisClientPipe ZRank(string key, string member) => PipeCommand(key, (c, k) => c.ZRank(k, member));
		/// <summary>
		/// 移除有序集合中的一个或多个成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">一个或多个成员</param>
		/// <returns></returns>
		public CSRedisClientPipe ZRem(string key, params string[] member) => PipeCommand(key, (c, k) => c.ZRem(k, member));
		/// <summary>
		/// 移除有序集合中给定的排名区间的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public CSRedisClientPipe ZRemRangeByRank(string key, long start, long stop) => PipeCommand(key, (c, k) => c.ZRemRangeByRank(k, start, stop));
		/// <summary>
		/// 移除有序集合中给定的分数区间的所有成员
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <returns></returns>
		public CSRedisClientPipe ZRemRangeByScore(string key, double minScore, double maxScore) => PipeCommand(key, (c, k) => c.ZRemRangeByScore(k, minScore, maxScore));
		/// <summary>
		/// 返回有序集中指定区间内的成员，通过索引，分数从高到底
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
		/// <returns></returns>
		public CSRedisClientPipe ZRevRange(string key, long start, long stop) => PipeCommand(key, (c, k) => c.ZRevRange(k, start, stop, false));
		/// <summary>
		/// 返回有序集中指定分数区间内的成员，分数从高到低排序
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="minScore">最小分数</param>
		/// <param name="maxScore">最大分数</param>
		/// <param name="limit">返回多少成员</param>
		/// <param name="offset">返回条件偏移位置</param>
		/// <returns></returns>
		public CSRedisClientPipe ZRevRangeByScore(string key, double maxScore, double minScore, long? limit = null, long? offset = 0) => PipeCommand(key, (c, k) => c.ZRevRangeByScore(k, maxScore, minScore, false, false, false, offset, limit));
		/// <summary>
		/// 返回有序集合中指定成员的排名，有序集成员按分数值递减(从大到小)排序
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public CSRedisClientPipe ZRevRank(string key, string member) => PipeCommand(key, (c, k) => c.ZRevRank(k, member));
		/// <summary>
		/// 返回有序集中，成员的分数值
		/// </summary>
		/// <param name="key">不含prefix前辍</param>
		/// <param name="member">成员</param>
		/// <returns></returns>
		public CSRedisClientPipe ZScore(string key, string member) => PipeCommand(key, (c, k) => c.ZScore(k, member));
		#endregion
	}
}
