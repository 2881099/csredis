using CSRedis.Internal.ObjectPool;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

#if net40
#else
namespace CSRedis
{
    public partial class CSRedisClient
    {

        ConcurrentDictionary<string, AutoPipe> _autoPipe = new ConcurrentDictionary<string, AutoPipe>();
        class AutoPipe
        {
            public Object<RedisClient> Client;
            public long GetTimes;
            public long TimesZore;
            public bool IsSingleEndPipe;
            public Exception ReturnException;
        }
        async Task<AutoPipe> GetClientAsync(RedisClientPool pool)
        {
            if (pool._policy._asyncPipeline == false)
                return new AutoPipe { Client = await pool.GetAsync(), GetTimes = 1, TimesZore = 0, IsSingleEndPipe = false };

            if (_autoPipe.TryGetValue(pool.Key, out var ap) && ap.IsSingleEndPipe == false)
            {
                if (pool.UnavailableException != null) 
                    throw new Exception($"【{pool._policy.Name}】状态不可用，等待后台检查程序恢复方可使用。{pool.UnavailableException?.Message}", pool.UnavailableException);
                Interlocked.Increment(ref ap.GetTimes);
                return ap;
            }
            ap = new AutoPipe { Client = await pool.GetAsync(), GetTimes = 1, TimesZore = 0, IsSingleEndPipe = false };
            if (_autoPipe.TryAdd(pool.Key, ap))
            {
                ap.Client.Value._asyncPipe = new ConcurrentQueue<TaskCompletionSource<object>>();
                new Thread(() =>
                {
                    var rc = ap.Client.Value;

                    void trySetException(Exception ex)
                    {
                        pool.SetUnavailable(ex);
                        while (rc._asyncPipe?.IsEmpty == false)
                        {
                            TaskCompletionSource<object> trytsc = null;
                            if (rc._asyncPipe?.TryDequeue(out trytsc) == true)
                                trytsc.TrySetException(ex);
                        }
                        rc._asyncPipe = null;
                        pool.Return(ap.Client);
                        _autoPipe.TryRemove(pool.Key, out var oldap);
                    }
                    while (true)
                    {
                        Thread.CurrentThread.Join(1);
                        if (rc._asyncPipe?.IsEmpty == false)
                        {
                            try
                            {
                                var ret = rc.EndPipe();
                                if (ret.Length == 1) ap.IsSingleEndPipe = true;
                                else if (ret.Length > 1) ap.IsSingleEndPipe = false;
                                foreach (var rv in ret)
                                {
                                    TaskCompletionSource<object> trytsc = null;
                                    if (rc._asyncPipe?.TryDequeue(out trytsc) == true)
                                        trytsc.TrySetResult(rv);
                                }
                            }
                            catch (Exception ex)
                            {
                                trySetException(ex);
                                return;
                            }
                            continue;
                        }

                        if (ap.ReturnException != null)
                        {
                            trySetException(ap.ReturnException);
                            return;
                        }
                        var tmpTimes = Interlocked.Increment(ref ap.TimesZore);
                        if (tmpTimes >= 10) ap.IsSingleEndPipe = false;
                        if (tmpTimes >= 1000)
                        {
                            rc._asyncPipe = null;
                            pool.Return(ap.Client, ap.ReturnException);
                            _autoPipe.TryRemove(pool.Key, out var oldap);
                            break;
                        }
                    }
                }).Start();
            }
            return ap;
        }
        void ReturnClient(AutoPipe ap, Object<RedisClient> obj, RedisClientPool pool, Exception ex)
        {
            if (ap == null) return;
            var times = Interlocked.Decrement(ref ap.GetTimes);
            if (times <= 0)
                Interlocked.Exchange(ref ap.TimesZore, 0);
            ap.ReturnException = ex;
            if (_autoPipe.TryGetValue(pool.Key, out var dicap) == false || dicap != ap)
                pool.Return(ap.Client, ap.ReturnException);
        }

        async Task<T> GetAndExecuteAsync<T>(RedisClientPool pool, Func<Object<RedisClient>, Task<T>> handerAsync, int jump = 100, int errtimes = 0)
        {
            AutoPipe ap = null;
            Object<RedisClient> obj = null;
            Exception ex = null;
            var redirect = ParseClusterRedirect(null);
            try
            {
                ap = await GetClientAsync(pool);
                obj = ap.Client;
                while (true)
                { //因网络出错重试，默认1次
                    try
                    {
                        var ret = await handerAsync(obj);
                        return ret;
                    }
                    catch (RedisException ex3)
                    {
                        redirect = ParseClusterRedirect(ex3); //官方集群跳转
                        if (redirect == null || jump <= 0)
                        {
                            ex = ex3;
                            if (SentinelManager != null && ex.Message.Contains("READONLY"))
                            { //哨兵轮询
                                if (pool.SetUnavailable(ex) == true)
                                    BackgroundGetSentinelMasterValue();
                            }
                            throw ex;
                        }
                        break;
                    }
                    catch (Exception ex2)
                    {
                        ex = ex2;
                        if (pool.UnavailableException != null) throw ex;
                        var isPong = false;
                        try
                        {
                            await obj.Value.PingAsync();
                            isPong = true;
                        }
                        catch
                        {
                            obj.ResetValue();
                        }

                        if (isPong == false || ++errtimes > pool._policy._tryit)
                        {
                            if (SentinelManager != null)
                            { //哨兵轮询
                                if (pool.SetUnavailable(ex) == true)
                                    BackgroundGetSentinelMasterValue();
                                throw new Exception($"Redis Sentinel Master is switching：{ex.Message}");
                            }
                            throw ex; //重试次数完成
                        }
                        else
                        {
                            ex = null;
                            Trace.WriteLine($"csredis tryit ({errtimes}) ...");
                        }
                    }
                }
            }
            finally
            {
                ReturnClient(ap, obj, pool, ex);
                //pool.Return(obj, ex);
            }
            if (redirect == null)
                return await GetAndExecuteAsync<T>(pool, handerAsync, jump - 1, errtimes);

            var redirectHanderAsync = redirect.Value.isMoved ? handerAsync : async redirectObj =>
            {
                await redirectObj.Value.CallAsync("ASKING");
                return await handerAsync(redirectObj);
            };
            return await GetAndExecuteAsync<T>(GetRedirectPool(redirect.Value, pool), redirectHanderAsync, jump - 1);
        }

        async Task<T> NodesNotSupportAsync<T>(string[] keys, T defaultValue, Func<Object<RedisClient>, string[], Task<T>> callbackAsync)
        {
            if (keys == null || keys.Any() == false) return defaultValue;
            var rules = Nodes.Count > 1 ? keys.Select(a => NodeRuleRaw(a)).Distinct() : new[] { Nodes.FirstOrDefault().Key };
            if (rules.Count() > 1) throw new Exception("由于开启了分区模式，keys 分散在多个节点，无法使用此功能");
            var pool = Nodes.TryGetValue(rules.First(), out var b) ? b : Nodes.First().Value;
            string[] rkeys = new string[keys.Length];
            for (int a = 0; a < keys.Length; a++) rkeys[a] = string.Concat(pool.Prefix, keys[a]);
            if (rkeys.Length == 0) return defaultValue;
            return await GetAndExecuteAsync(pool, conn => callbackAsync(conn, rkeys));
        }
        Task<T> NodesNotSupportAsync<T>(string key, Func<Object<RedisClient>, string, Task<T>> callback)
        {
            if (IsMultiNode) throw new Exception("由于开启了分区模式，无法使用此功能");
            return ExecuteScalarAsync<T>(key, callback);
        }


        #region 缓存壳
        /// <summary>
        /// 缓存壳
        /// </summary>
        /// <typeparam name="T">缓存类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="timeoutSeconds">缓存秒数</param>
        /// <param name="getDataAsync">获取源数据的函数</param>
        /// <returns></returns>
        async public Task<T> CacheShellAsync<T>(string key, int timeoutSeconds, Func<Task<T>> getDataAsync)
        {
            if (timeoutSeconds == 0) return await getDataAsync();
            var cacheValue = await GetAsync(key);
            if (cacheValue != null)
            {
                try
                {
                    return this.DeserializeObject<T>(cacheValue);
                }
                catch
                {
                    await DelAsync(key);
                    throw;
                }
            }
            var ret = await getDataAsync();
            await SetAsync(key, this.SerializeObject(ret), timeoutSeconds);
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
        /// <returns></returns>
        async public Task<T> CacheShellAsync<T>(string key, string field, int timeoutSeconds, Func<Task<T>> getDataAsync)
        {
            if (timeoutSeconds == 0) return await getDataAsync();
            var cacheValue = await HGetAsync(key, field);
            if (cacheValue != null)
            {
                try
                {
                    var value = this.DeserializeObject<(T, long)>(cacheValue);
                    if (DateTime.Now.Subtract(_dt1970.AddSeconds(value.Item2)).TotalSeconds <= timeoutSeconds) return value.Item1;
                }
                catch
                {
                    await HDelAsync(key, field);
                    throw;
                }
            }
            var ret = await getDataAsync();
            await HSetAsync(key, field, this.SerializeObject((ret, (long)DateTime.Now.Subtract(_dt1970).TotalSeconds)));
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
        /// <returns></returns>
        async public Task<(string key, T value)[]> CacheShellAsync<T>(string key, string[] fields, int timeoutSeconds, Func<string[], Task<(string, T)[]>> getDataAsync)
        {
            fields = fields?.Distinct().ToArray();
            if (fields == null || fields.Length == 0) return new (string, T)[0];
            if (timeoutSeconds == 0) return await getDataAsync(fields);

            var ret = new (string, T)[fields.Length];
            var cacheValue = await HMGetAsync(key, fields);
            var fieldsMGet = new Dictionary<string, int>();

            for (var a = 0; a < ret.Length; a++)
            {
                if (cacheValue[a] != null)
                {
                    try
                    {
                        var value = this.DeserializeObject<(T, long)>(cacheValue[a]);
                        if (DateTime.Now.Subtract(_dt1970.AddSeconds(value.Item2)).TotalSeconds <= timeoutSeconds)
                        {
                            ret[a] = (fields[a], value.Item1);
                            continue;
                        }
                    }
                    catch
                    {
                        await HDelAsync(key, fields[a]);
                        throw;
                    }
                }
                fieldsMGet.Add(fields[a], a);
            }

            if (fieldsMGet.Any())
            {
                var getDataIntput = fieldsMGet.Keys.ToArray();
                var data = await getDataAsync(getDataIntput);
                var mset = new object[fieldsMGet.Count * 2];
                var msetIndex = 0;
                foreach (var d in data)
                {
                    if (fieldsMGet.ContainsKey(d.Item1) == false) throw new Exception($"使用 CacheShell 请确认 getData 返回值 (string, T)[] 中的 Item1 值: {d.Item1} 存在于 输入参数: {string.Join(",", getDataIntput)}");
                    ret[fieldsMGet[d.Item1]] = d;
                    mset[msetIndex++] = d.Item1;
                    mset[msetIndex++] = this.SerializeObject((d.Item2, (long)DateTime.Now.Subtract(_dt1970).TotalSeconds));
                    fieldsMGet.Remove(d.Item1);
                }
                foreach (var fieldNull in fieldsMGet.Keys)
                {
                    ret[fieldsMGet[fieldNull]] = (fieldNull, default(T));
                    mset[msetIndex++] = fieldNull;
                    mset[msetIndex++] = this.SerializeObject((default(T), (long)DateTime.Now.Subtract(_dt1970).TotalSeconds));
                }
                if (mset.Any()) await HMSetAsync(key, mset);
            }
            return ret;
        }
        #endregion

        #region 分区方式 ExecuteAsync
        async private Task<T> ExecuteScalarAsync<T>(string key, Func<Object<RedisClient>, string, Task<T>> handerAsync)
        {
            if (key == null) return default(T);
            var pool = NodeRuleRaw == null || Nodes.Count == 1 ? Nodes.First().Value : (Nodes.TryGetValue(NodeRuleRaw(key), out var b) ? b : Nodes.First().Value);
            key = string.Concat(pool.Prefix, key);
            return await GetAndExecuteAsync(pool, conn => handerAsync(conn, key));
        }
        async private Task<T[]> ExecuteArrayAsync<T>(string[] key, Func<Object<RedisClient>, string[], Task<T[]>> handerAsync)
        {
            if (key == null || key.Any() == false) return new T[0];
            if (NodeRuleRaw == null || Nodes.Count == 1)
            {
                var pool = Nodes.First().Value;
                var keys = key.Select(a => string.Concat(pool.Prefix, a)).ToArray();
                return await GetAndExecuteAsync(pool, conn => handerAsync(conn, keys));
            }
            var rules = new Dictionary<string, List<(string, int)>>();
            for (var a = 0; a < key.Length; a++)
            {
                var rule = NodeRuleRaw(key[a]);
                if (rules.ContainsKey(rule)) rules[rule].Add((key[a], a));
                else rules.Add(rule, new List<(string, int)> { (key[a], a) });
            }
            T[] ret = new T[key.Length];
            foreach (var r in rules)
            {
                var pool = Nodes.TryGetValue(r.Key, out var b) ? b : Nodes.First().Value;
                var keys = r.Value.Select(a => string.Concat(pool.Prefix, a.Item1)).ToArray();
                await GetAndExecuteAsync(pool, async conn =>
                {
                    var vals = await handerAsync(conn, keys);
                    for (var z = 0; z < r.Value.Count; z++)
                    {
                        ret[r.Value[z].Item2] = vals == null || z >= vals.Length ? default(T) : vals[z];
                    }
                    return 0;
                });
            }
            return ret;
        }
        async private Task<long> ExecuteNonQueryAsync(string[] key, Func<Object<RedisClient>, string[], Task<long>> handerAsync)
        {
            if (key == null || key.Any() == false) return 0;
            if (NodeRuleRaw == null || Nodes.Count == 1)
            {
                var pool = Nodes.First().Value;
                var keys = key.Select(a => string.Concat(pool.Prefix, a)).ToArray();
                return await GetAndExecuteAsync(pool, conn => handerAsync(conn, keys));
            }
            var rules = new Dictionary<string, List<string>>();
            for (var a = 0; a < key.Length; a++)
            {
                var rule = NodeRuleRaw(key[a]);
                if (rules.ContainsKey(rule)) rules[rule].Add(key[a]);
                else rules.Add(rule, new List<string> { key[a] });
            }
            long affrows = 0;
            foreach (var r in rules)
            {
                var pool = Nodes.TryGetValue(r.Key, out var b) ? b : Nodes.First().Value;
                var keys = r.Value.Select(a => string.Concat(pool.Prefix, a)).ToArray();
                affrows += await GetAndExecuteAsync(pool, conn => handerAsync(conn, keys));
            }
            return affrows;
        }
        #endregion

        #region 服务器命令
        public partial class NodesServerManagerProvider
        {

            async Task<(string node, T value)[]> NodesInternalAsync<T>(Func<Object<RedisClient>, Task<T>> handleAsync)
            {
                var ret = new List<(string, T)>();
                foreach (var pool in _csredis.Nodes.Values)
                    ret.Add((pool.Key, await _csredis.GetAndExecuteAsync(pool, c => handleAsync(c))));
                return ret.ToArray();
            }

            /// <summary>
            /// 异步执行一个 AOF（AppendOnly File） 文件重写操作
            /// </summary>
            /// <returns></returns>
            public Task<(string node, string value)[]> BgRewriteAofAsync() => NodesInternalAsync(c => c.Value.BgRewriteAofAsync());
            /// <summary>
            /// 在后台异步保存当前数据库的数据到磁盘
            /// </summary>
            /// <returns></returns>
            public Task<(string node, string value)[]> BgSaveAsync() => NodesInternalAsync(c => c.Value.BgSaveAsync());
            /// <summary>
            /// 关闭客户端连接
            /// </summary>
            /// <param name="ip">ip</param>
            /// <param name="port">端口</param>
            /// <returns></returns>
            public Task<(string node, string value)[]> ClientKillAsync(string ip, int port) => NodesInternalAsync(c => c.Value.ClientKillAsync(ip, port));
            /// <summary>
            /// 关闭客户端连接
            /// </summary>
            /// <param name="addr">ip:port</param>
            /// <param name="id">客户唯一标识</param>
            /// <param name="type">类型：normal | slave | pubsub</param>
            /// <param name="skipMe">跳过自己</param>
            /// <returns></returns>
            public Task<(string node, long value)[]> ClientKillAsync(string addr = null, string id = null, ClientKillType? type = null, bool? skipMe = null) => NodesInternalAsync(c => c.Value.ClientKillAsync(addr, id, type?.ToString(), skipMe));
            /// <summary>
            /// 获取连接到服务器的客户端连接列表
            /// </summary>
            /// <returns></returns>
            public Task<(string node, string value)[]> ClientListAsync() => NodesInternalAsync(c => c.Value.ClientListAsync());
            /// <summary>
            /// 获取连接的名称
            /// </summary>
            /// <returns></returns>
            public Task<(string node, string value)[]> ClientGetNameAsync() => NodesInternalAsync(c => c.Value.ClientGetNameAsync());
            /// <summary>
            /// 在指定时间内终止运行来自客户端的命令
            /// </summary>
            /// <param name="timeout">阻塞时间</param>
            /// <returns></returns>
            public Task<(string node, string value)[]> ClientPauseAsync(TimeSpan timeout) => NodesInternalAsync(c => c.Value.ClientPauseAsync(timeout));
            /// <summary>
            /// 设置当前连接的名称
            /// </summary>
            /// <param name="connectionName">连接名称</param>
            /// <returns></returns>
            public Task<(string node, string value)[]> ClientSetNameAsync(string connectionName) => NodesInternalAsync(c => c.Value.ClientSetNameAsync(connectionName));
            /// <summary>
            /// 返回当前服务器时间
            /// </summary>
            /// <returns></returns>
            public Task<(string node, DateTime value)[]> TimeAsync() => NodesInternalAsync(c => c.Value.TimeAsync());
            /// <summary>
            /// 获取指定配置参数的值
            /// </summary>
            /// <param name="parameter">参数</param>
            /// <returns></returns>
            public Task<(string node, Dictionary<string, string> value)[]> ConfigGetAsync(string parameter) => NodesInternalAsync(async c => (await c.Value.ConfigGetAsync(parameter)).ToDictionary(z => z.Item1, y => y.Item2));
            /// <summary>
            /// 对启动 Redis 服务器时所指定的 redis.conf 配置文件进行改写
            /// </summary>
            /// <returns></returns>
            public Task<(string node, string value)[]> ConfigRewriteAsync() => NodesInternalAsync(c => c.Value.ConfigRewriteAsync());
            /// <summary>
            /// 修改 redis 配置参数，无需重启
            /// </summary>
            /// <param name="parameter">参数</param>
            /// <param name="value">值</param>
            /// <returns></returns>
            public Task<(string node, string value)[]> ConfigSetAsync(string parameter, string value) => NodesInternalAsync(c => c.Value.ConfigSetAsync(parameter, value));
            /// <summary>
            /// 重置 INFO 命令中的某些统计数据
            /// </summary>
            /// <returns></returns>
            public Task<(string node, string value)[]> ConfigResetStatAsync() => NodesInternalAsync(c => c.Value.ConfigResetStatAsync());
            /// <summary>
            /// 返回当前数据库的 key 的数量
            /// </summary>
            /// <returns></returns>
            public Task<(string node, long value)[]> DbSizeAsync() => NodesInternalAsync(c => c.Value.DbSizeAsync());
            /// <summary>
            /// 让 Redis 服务崩溃
            /// </summary>
            /// <returns></returns>
            public Task<(string node, string value)[]> DebugSegFaultAsync() => NodesInternalAsync(c => c.Value.DebugSegFaultAsync());
            /// <summary>
            /// 删除所有数据库的所有key
            /// </summary>
            /// <returns></returns>
            public Task<(string node, string value)[]> FlushAllAsync() => NodesInternalAsync(c => c.Value.FlushAllAsync());
            /// <summary>
            /// 删除当前数据库的所有key
            /// </summary>
            /// <returns></returns>
            public Task<(string node, string value)[]> FlushDbAsync() => NodesInternalAsync(c => c.Value.FlushDbAsync());
            /// <summary>
            /// 获取 Redis 服务器的各种信息和统计数值
            /// </summary>
            /// <param name="section">部分(all|default|server|clients|memory|persistence|stats|replication|cpu|commandstats|cluster|keyspace)</param>
            /// <returns></returns>
            public Task<(string node, string value)[]> InfoAsync(InfoSection? section = null) => NodesInternalAsync(c => c.Value.InfoAsync(section?.ToString()));
            /// <summary>
            /// 返回最近一次 Redis 成功将数据保存到磁盘上的时间
            /// </summary>
            /// <returns></returns>
            public Task<(string node, DateTime value)[]> LastSaveAsync() => NodesInternalAsync(c => c.Value.LastSaveAsync());
            /// <summary>
            /// 返回主从实例所属的角色
            /// </summary>
            /// <returns></returns>
            public Task<(string node, RedisRole value)[]> RoleAsync() => NodesInternalAsync(c => c.Value.RoleAsync());
            /// <summary>
            /// 同步保存数据到硬盘
            /// </summary>
            /// <returns></returns>
            public Task<(string node, string value)[]> SaveAsync() => NodesInternalAsync(c => c.Value.SaveAsync());
            /// <summary>
            /// 异步保存数据到硬盘，并关闭服务器
            /// </summary>
            /// <param name="isSave">是否保存</param>
            /// <returns></returns>
            public Task<(string node, string value)[]> ShutdownAsync(bool isSave = true) => NodesInternalAsync(c => c.Value.ShutdownAsync(isSave));
            /// <summary>
            /// 将服务器转变为指定服务器的从属服务器(slave server)，如果当前服务器已经是某个主服务器(master server)的从属服务器，那么执行 SLAVEOF host port 将使当前服务器停止对旧主服务器的同步，丢弃旧数据集，转而开始对新主服务器进行同步。
            /// </summary>
            /// <param name="host">主机</param>
            /// <param name="port">端口</param>
            /// <returns></returns>
            public Task<(string node, string value)[]> SlaveOfAsync(string host, int port) => NodesInternalAsync(c => c.Value.SlaveOfAsync(host, port));
            /// <summary>
            /// 从属服务器执行命令 SLAVEOF NO ONE 将使得这个从属服务器关闭复制功能，并从从属服务器转变回主服务器，原来同步所得的数据集不会被丢弃。
            /// </summary>
            /// <returns></returns>
            public Task<(string node, string value)[]> SlaveOfNoOneAsync() => NodesInternalAsync(c => c.Value.SlaveOfNoOneAsync());
            /// <summary>
            /// 管理 redis 的慢日志，按数量获取
            /// </summary>
            /// <param name="count">数量</param>
            /// <returns></returns>
            public Task<(string node, RedisSlowLogEntry[] value)[]> SlowLogGetAsync(long? count = null) => NodesInternalAsync(c => c.Value.SlowLogGetAsync(count));
            /// <summary>
            /// 管理 redis 的慢日志，总数量
            /// </summary>
            /// <returns></returns>
            public Task<(string node, long value)[]> SlowLogLenAsync() => NodesInternalAsync(c => c.Value.SlowLogLenAsync());
            /// <summary>
            /// 管理 redis 的慢日志，清空
            /// </summary>
            /// <returns></returns>
            public Task<(string node, string value)[]> SlowLogResetAsync() => NodesInternalAsync(c => c.Value.SlowLogResetAsync());
            /// <summary>
            /// 用于复制功能(replication)的内部命令
            /// </summary>
            /// <returns></returns>
            public Task<(string node, byte[] value)[]> SyncAsync() => NodesInternalAsync(c => c.Value.SyncAsync());
        }

        public partial class NodeServerManagerProvider
        {

            /// <summary>
            /// 异步执行一个 AOF（AppendOnly File） 文件重写操作
            /// </summary>
            /// <returns></returns>
            public Task<string> BgRewriteAofAsync() => _csredis.GetAndExecute(_pool, c => c.Value.BgRewriteAofAsync());
            /// <summary>
            /// 在后台异步保存当前数据库的数据到磁盘
            /// </summary>
            /// <returns></returns>
            public Task<string> BgSaveAsync() => _csredis.GetAndExecute(_pool, c => c.Value.BgSaveAsync());
            /// <summary>
            /// 关闭客户端连接
            /// </summary>
            /// <param name="ip">ip</param>
            /// <param name="port">端口</param>
            /// <returns></returns>
            public Task<string> ClientKillAsync(string ip, int port) => _csredis.GetAndExecute(_pool, c => c.Value.ClientKillAsync(ip, port));
            /// <summary>
            /// 关闭客户端连接
            /// </summary>
            /// <param name="addr">ip:port</param>
            /// <param name="id">客户唯一标识</param>
            /// <param name="type">类型：normal | slave | pubsub</param>
            /// <param name="skipMe">跳过自己</param>
            /// <returns></returns>
            public Task<long> ClientKillAsync(string addr = null, string id = null, ClientKillType? type = null, bool? skipMe = null) => _csredis.GetAndExecute(_pool, c => c.Value.ClientKillAsync(addr, id, type?.ToString(), skipMe));
            /// <summary>
            /// 获取连接到服务器的客户端连接列表
            /// </summary>
            /// <returns></returns>
            public Task<string> ClientListAsync() => _csredis.GetAndExecute(_pool, c => c.Value.ClientListAsync());
            /// <summary>
            /// 获取连接的名称
            /// </summary>
            /// <returns></returns>
            public Task<string> ClientGetNameAsync() => _csredis.GetAndExecute(_pool, c => c.Value.ClientGetNameAsync());
            /// <summary>
            /// 在指定时间内终止运行来自客户端的命令
            /// </summary>
            /// <param name="timeout">阻塞时间</param>
            /// <returns></returns>
            public Task<string> ClientPauseAsync(TimeSpan timeout) => _csredis.GetAndExecute(_pool, c => c.Value.ClientPauseAsync(timeout));
            /// <summary>
            /// 设置当前连接的名称
            /// </summary>
            /// <param name="connectionName">连接名称</param>
            /// <returns></returns>
            public Task<string> ClientSetNameAsync(string connectionName) => _csredis.GetAndExecute(_pool, c => c.Value.ClientSetNameAsync(connectionName));
            /// <summary>
            /// 返回当前服务器时间
            /// </summary>
            /// <returns></returns>
            public Task<DateTime> TimeAsync() => _csredis.GetAndExecute(_pool, c => c.Value.TimeAsync());
            /// <summary>
            /// 获取指定配置参数的值
            /// </summary>
            /// <param name="parameter">参数</param>
            /// <returns></returns>
            async public Task<Dictionary<string, string>> ConfigGetAsync(string parameter) => (await _csredis.GetAndExecute(_pool, c => c.Value.ConfigGetAsync(parameter))).ToDictionary(z => z.Item1, y => y.Item2);
            /// <summary>
            /// 对启动 Redis 服务器时所指定的 redis.conf 配置文件进行改写
            /// </summary>
            /// <returns></returns>
            public Task<string> ConfigRewriteAsync() => _csredis.GetAndExecute(_pool, c => c.Value.ConfigRewriteAsync());
            /// <summary>
            /// 修改 redis 配置参数，无需重启
            /// </summary>
            /// <param name="parameter">参数</param>
            /// <param name="value">值</param>
            /// <returns></returns>
            public Task<string> ConfigSetAsync(string parameter, string value) => _csredis.GetAndExecute(_pool, c => c.Value.ConfigSetAsync(parameter, value));
            /// <summary>
            /// 重置 INFO 命令中的某些统计数据
            /// </summary>
            /// <returns></returns>
            public Task<string> ConfigResetStatAsync() => _csredis.GetAndExecute(_pool, c => c.Value.ConfigResetStatAsync());
            /// <summary>
            /// 返回当前数据库的 key 的数量
            /// </summary>
            /// <returns></returns>
            public Task<long> DbSizeAsync() => _csredis.GetAndExecute(_pool, c => c.Value.DbSizeAsync());
            /// <summary>
            /// 让 Redis 服务崩溃
            /// </summary>
            /// <returns></returns>
            public Task<string> DebugSegFaultAsync() => _csredis.GetAndExecute(_pool, c => c.Value.DebugSegFaultAsync());
            /// <summary>
            /// 删除所有数据库的所有key
            /// </summary>
            /// <returns></returns>
            public Task<string> FlushAllAsync() => _csredis.GetAndExecute(_pool, c => c.Value.FlushAllAsync());
            /// <summary>
            /// 删除当前数据库的所有key
            /// </summary>
            /// <returns></returns>
            public Task<string> FlushDbAsync() => _csredis.GetAndExecute(_pool, c => c.Value.FlushDbAsync());
            /// <summary>
            /// 获取 Redis 服务器的各种信息和统计数值
            /// </summary>
            /// <param name="section">部分(Server | Clients | Memory | Persistence | Stats | Replication | CPU | Keyspace)</param>
            /// <returns></returns>
            public Task<string> InfoAsync(InfoSection? section = null) => _csredis.GetAndExecute(_pool, c => c.Value.InfoAsync(section?.ToString()));
            /// <summary>
            /// 返回最近一次 Redis 成功将数据保存到磁盘上的时间
            /// </summary>
            /// <returns></returns>
            public Task<DateTime> LastSaveAsync() => _csredis.GetAndExecute(_pool, c => c.Value.LastSaveAsync());
            /// <summary>
            /// 返回主从实例所属的角色
            /// </summary>
            /// <returns></returns>
            public Task<RedisRole> RoleAsync() => _csredis.GetAndExecute(_pool, c => c.Value.RoleAsync());
            /// <summary>
            /// 同步保存数据到硬盘
            /// </summary>
            /// <returns></returns>
            public Task<string> SaveAsync() => _csredis.GetAndExecute(_pool, c => c.Value.SaveAsync());
            /// <summary>
            /// 异步保存数据到硬盘，并关闭服务器
            /// </summary>
            /// <param name="isSave">是否保存</param>
            /// <returns></returns>
            public Task<string> ShutdownAsync(bool isSave = true) => _csredis.GetAndExecute(_pool, c => c.Value.ShutdownAsync(isSave));
            /// <summary>
            /// 将服务器转变为指定服务器的从属服务器(slave server)，如果当前服务器已经是某个主服务器(master server)的从属服务器，那么执行 SLAVEOF host port 将使当前服务器停止对旧主服务器的同步，丢弃旧数据集，转而开始对新主服务器进行同步。
            /// </summary>
            /// <param name="host">主机</param>
            /// <param name="port">端口</param>
            /// <returns></returns>
            public Task<string> SlaveOfAsync(string host, int port) => _csredis.GetAndExecute(_pool, c => c.Value.SlaveOfAsync(host, port));
            /// <summary>
            /// 从属服务器执行命令 SLAVEOF NO ONE 将使得这个从属服务器关闭复制功能，并从从属服务器转变回主服务器，原来同步所得的数据集不会被丢弃。
            /// </summary>
            /// <returns></returns>
            public Task<string> SlaveOfNoOneAsync() => _csredis.GetAndExecute(_pool, c => c.Value.SlaveOfNoOneAsync());
            /// <summary>
            /// 管理 redis 的慢日志，按数量获取
            /// </summary>
            /// <param name="count">数量</param>
            /// <returns></returns>
            public Task<RedisSlowLogEntry[]> SlowLogGetAsync(long? count = null) => _csredis.GetAndExecute(_pool, c => c.Value.SlowLogGetAsync(count));
            /// <summary>
            /// 管理 redis 的慢日志，总数量
            /// </summary>
            /// <returns></returns>
            public Task<long> SlowLogLenAsync() => _csredis.GetAndExecute(_pool, c => c.Value.SlowLogLenAsync());
            /// <summary>
            /// 管理 redis 的慢日志，清空
            /// </summary>
            /// <returns></returns>
            public Task<string> SlowLogResetAsync() => _csredis.GetAndExecute(_pool, c => c.Value.SlowLogResetAsync());
            /// <summary>
            /// 用于复制功能(replication)的内部命令
            /// </summary>
            /// <returns></returns>
            public Task<byte[]> SyncAsync() => _csredis.GetAndExecute(_pool, c => c.Value.SyncAsync());
        }
        #endregion

        #region 连接命令
        /// <summary>
        /// 验证密码是否正确
        /// </summary>
        /// <param name="nodeKey">分区key</param>
        /// <param name="password">密码</param>
        /// <returns></returns>
        [Obsolete("不建议手工执行，连接池自己管理最佳")]
        private Task<bool> AuthAsync(string nodeKey, string password) => GetAndExecuteAsync(GetNodeOrThrowNotFound(nodeKey), async c => await c.Value.AuthAsync(password) == "OK");
        /// <summary>
        /// 打印字符串
        /// </summary>
        /// <param name="nodeKey">分区key</param>
        /// <param name="message">消息</param>
        /// <returns></returns>
        public Task<string> EchoAsync(string nodeKey, string message) => GetAndExecuteAsync(GetNodeOrThrowNotFound(nodeKey), c => c.Value.EchoAsync(message));
        /// <summary>
        /// 打印字符串
        /// </summary>
        /// <param name="message">消息</param>
        /// <returns></returns>
        public Task<string> EchoAsync(string message) => GetAndExecuteAsync(Nodes.First().Value, c => c.Value.EchoAsync(message));
        /// <summary>
        /// 查看服务是否运行
        /// </summary>
        /// <param name="nodeKey">分区key</param>
        /// <returns></returns>
        public Task<bool> PingAsync(string nodeKey) => GetAndExecuteAsync(GetNodeOrThrowNotFound(nodeKey), async c => await c.Value.PingAsync() == "PONG");
        /// <summary>
        /// 查看服务是否运行
        /// </summary>
        /// <returns></returns>
        public Task<bool> PingAsync() => GetAndExecuteAsync(Nodes.First().Value, async c => await c.Value.PingAsync() == "PONG");
        /// <summary>
        /// 关闭当前连接
        /// </summary>
        /// <param name="nodeKey">分区key</param>
        /// <returns></returns>
        [Obsolete("不建议手工执行，连接池自己管理最佳")]
        private Task<bool> QuitAsync(string nodeKey) => GetAndExecuteAsync(GetNodeOrThrowNotFound(nodeKey), async c => await c.Value.QuitAsync() == "OK");
        /// <summary>
        /// 切换到指定的数据库
        /// </summary>
        /// <param name="nodeKey">分区key</param>
        /// <param name="index">数据库</param>
        /// <returns></returns>
        [Obsolete("不建议手工执行，连接池所有连接应该指向同一数据库，若手工修改将导致数据的不一致")]
        private Task<bool> SelectAsync(string nodeKey, int index) => GetAndExecuteAsync(GetNodeOrThrowNotFound(nodeKey), async c => await c.Value.SelectAsync(index) == "OK");
        #endregion

        #region Script
        /// <summary>
        /// 执行脚本
        /// </summary>
        /// <param name="script">Lua 脚本</param>
        /// <param name="key">用于定位分区节点，不含prefix前辍</param>
        /// <param name="args">参数</param>
        /// <returns></returns>
        public Task<object> EvalAsync(string script, string key, params object[] args)
        {
            var args2 = args?.Select(z => this.SerializeRedisValueInternal(z)).ToArray();
            return ExecuteScalarAsync(key, (c, k) => c.Value.EvalAsync(script, new[] { k }, args2));
        }
        /// <summary>
        /// 执行脚本
        /// </summary>
        /// <param name="sha1">脚本缓存的sha1</param>
        /// <param name="key">用于定位分区节点，不含prefix前辍</param>
        /// <param name="args">参数</param>
        /// <returns></returns>
        public Task<object> EvalSHAAsync(string sha1, string key, params object[] args)
        {
            var args2 = args?.Select(z => this.SerializeRedisValueInternal(z)).ToArray();
            return ExecuteScalarAsync(key, (c, k) => c.Value.EvalSHAAsync(sha1, new[] { k }, args2));
        }
        /// <summary>
        /// 校验所有分区节点中，脚本是否已经缓存。任何分区节点未缓存sha1，都返回false。
        /// </summary>
        /// <param name="sha1">脚本缓存的sha1</param>
        /// <returns></returns>
        async public Task<bool[]> ScriptExistsAsync(params string[] sha1)
        {
            var ret = new List<bool>();
            foreach (var pool in Nodes.Values)
                ret.Add((await GetAndExecuteAsync(pool, c => c.Value.ScriptExistsAsync(sha1)))?.Where(z => z == false).Any() == false);
            return ret.ToArray();
        }
        /// <summary>
        /// 清除所有分区节点中，所有 Lua 脚本缓存
        /// </summary>
        async public Task ScriptFlushAsync()
        {
            foreach (var pool in Nodes.Values)
                await GetAndExecuteAsync(pool, c => c.Value.ScriptFlushAsync());
        }
        /// <summary>
        /// 杀死所有分区节点中，当前正在运行的 Lua 脚本
        /// </summary>
        async public Task ScriptKillAsync()
        {
            foreach (var pool in Nodes.Values)
                await GetAndExecuteAsync(pool, c => c.Value.ScriptKillAsync());
        }
        /// <summary>
        /// 在所有分区节点中，缓存脚本后返回 sha1（同样的脚本在任何服务器，缓存后的 sha1 都是相同的）
        /// </summary>
        /// <param name="script">Lua 脚本</param>
        /// <returns></returns>
        async public Task<string> ScriptLoadAsync(string script)
        {
            string sha1 = null;
            foreach (var pool in Nodes.Values)
                sha1 = await GetAndExecuteAsync(pool, c => c.Value.ScriptLoadAsync(script));
            return sha1;
        }
        #endregion

        #region Pub/Sub
        /// <summary>
        /// 用于将信息发送到指定分区节点的频道，最终消息发布格式：1|message
        /// </summary>
        /// <param name="channel">频道名</param>
        /// <param name="message">消息文本</param>
        /// <returns></returns>
        async public Task<long> PublishAsync(string channel, string message)
        {
            var msgid = await HIncrByAsync("csredisclient:Publish:msgid", channel, 1);
            return await ExecuteScalarAsync(channel, (c, k) => c.Value.PublishAsync(channel, $"{msgid}|{message}"));
        }
        /// <summary>
        /// 用于将信息发送到指定分区节点的频道，与 Publish 方法不同，不返回消息id头，即 1|
        /// </summary>
        /// <param name="channel">频道名</param>
        /// <param name="message">消息文本</param>
        /// <returns></returns>
        public Task<long> PublishNoneMessageIdAsync(string channel, string message) => ExecuteScalarAsync(channel, (c, k) => c.Value.PublishAsync(channel, message));
        /// <summary>
        /// 查看所有订阅频道
        /// </summary>
        /// <param name="pattern"></param>
        /// <returns></returns>
        async public Task<string[]> PubSubChannelsAsync(string pattern)
        {
            var ret = new List<string>();
            foreach (var pool in Nodes.Values)
                ret.AddRange(await GetAndExecuteAsync(pool, c => c.Value.PubSubChannelsAsync(pattern)));
            return ret.ToArray();
        }
        /// <summary>
        /// 查看所有模糊订阅端的数量
        /// </summary>
        /// <returns></returns>
        [Obsolete("分区模式下，其他客户端的模糊订阅可能不会返回")]
        public Task<long> PubSubNumPatAsync() => GetAndExecuteAsync(Nodes.First().Value, c => c.Value.PubSubNumPatAsync());
        /// <summary>
        /// 查看所有订阅端的数量
        /// </summary>
        /// <param name="channels">频道</param>
        /// <returns></returns>
        [Obsolete("分区模式下，其他客户端的订阅可能不会返回")]
        async public Task<Dictionary<string, long>> PubSubNumSubAsync(params string[] channels) => (await ExecuteArrayAsync(channels, (c, k) =>
        {
            var prefix = (c.Pool as RedisClientPool).Prefix;
            return c.Value.PubSubNumSubAsync(k.Select(z => string.IsNullOrEmpty(prefix) == false && z.StartsWith(prefix) ? z.Substring(prefix.Length) : z).ToArray());
        })).ToDictionary(z => z.Item1, y => y.Item2);
        #endregion

        #region HyperLogLog
        /// <summary>
        /// 添加指定元素到 HyperLogLog
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="elements">元素</param>
        /// <returns></returns>
        async public Task<bool> PfAddAsync<T>(string key, params T[] elements)
        {
            if (elements == null || elements.Any() == false) return false;
            var args = elements.Select(z => this.SerializeRedisValueInternal(z)).ToArray();
            return await ExecuteScalarAsync(key, (c, k) => c.Value.PfAddAsync(k, args));
        }
        /// <summary>
        /// 返回给定 HyperLogLog 的基数估算值
        /// </summary>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        [Obsolete("分区模式下，若keys分散在多个分区节点时，将报错")]
        public Task<long> PfCountAsync(params string[] keys) => NodesNotSupportAsync(keys, 0, (c, k) => c.Value.PfCountAsync(k));
        /// <summary>
        /// 将多个 HyperLogLog 合并为一个 HyperLogLog
        /// </summary>
        /// <param name="destKey">新的 HyperLogLog，不含prefix前辍</param>
        /// <param name="sourceKeys">源 HyperLogLog，不含prefix前辍</param>
        /// <returns></returns>
        [Obsolete("分区模式下，若keys分散在多个分区节点时，将报错")]
        public Task<bool> PfMergeAsync(string destKey, params string[] sourceKeys) => NodesNotSupportAsync(new[] { destKey }.Concat(sourceKeys).ToArray(), false, async (c, k) => await c.Value.PfMergeAsync(k.First(), k.Skip(1).ToArray()) == "OK");
        #endregion

        #region Sorted Set
        /// <summary>
        /// [redis-server 5.0.0] 删除并返回有序集合key中的最多count个具有最高得分的成员。如未指定，count的默认值为1。指定一个大于有序集合的基数的count不会产生错误。 当返回多个元素时候，得分最高的元素将是第一个元素，然后是分数较低的元素。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        async public Task<(string member, decimal score)[]> ZPopMaxAsync(string key, long count) => (await ExecuteScalarAsync(key, (c, k) => c.Value.ZPopMaxAsync(k, count))).Select(a => (a.Item1, a.Item2)).ToArray();
        /// <summary>
        /// [redis-server 5.0.0] 删除并返回有序集合key中的最多count个具有最高得分的成员。如未指定，count的默认值为1。指定一个大于有序集合的基数的count不会产生错误。 当返回多个元素时候，得分最高的元素将是第一个元素，然后是分数较低的元素。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        async public Task<(T member, decimal score)[]> ZPopMaxAsync<T>(string key, long count) => this.DeserializeRedisValueTuple1Internal<T, decimal>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZPopMaxBytesAsync(k, count)));
        /// <summary>
        /// [redis-server 5.0.0] 删除并返回有序集合key中的最多count个具有最低得分的成员。如未指定，count的默认值为1。指定一个大于有序集合的基数的count不会产生错误。 当返回多个元素时候，得分最低的元素将是第一个元素，然后是分数较高的元素。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        async public Task<(string member, decimal score)[]> ZPopMinAsync(string key, long count) => (await ExecuteScalarAsync(key, (c, k) => c.Value.ZPopMinAsync(k, count))).Select(a => (a.Item1, a.Item2)).ToArray();
        /// <summary>
        /// [redis-server 5.0.0] 删除并返回有序集合key中的最多count个具有最低得分的成员。如未指定，count的默认值为1。指定一个大于有序集合的基数的count不会产生错误。 当返回多个元素时候，得分最低的元素将是第一个元素，然后是分数较高的元素。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        async public Task<(T member, decimal score)[]> ZPopMinAsync<T>(string key, long count) => this.DeserializeRedisValueTuple1Internal<T, decimal>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZPopMinBytesAsync(k, count)));

        /// <summary>
        /// 向有序集合添加一个或多个成员，或者更新已存在成员的分数
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="scoreMembers">一个或多个成员分数</param>
        /// <returns></returns>
        async public Task<long> ZAddAsync(string key, params (decimal, object)[] scoreMembers)
        {
            if (scoreMembers == null || scoreMembers.Any() == false) return 0;
            var args = scoreMembers.Select(a => new Tuple<decimal, object>(a.Item1, this.SerializeRedisValueInternal(a.Item2))).ToArray();
            return await ExecuteScalarAsync(key, (c, k) => c.Value.ZAddAsync(k, args));
        }
        /// <summary>
        /// 获取有序集合的成员数量
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> ZCardAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.ZCardAsync(k));
        /// <summary>
        /// 计算在有序集合中指定区间分数的成员数量
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <returns></returns>
        public Task<long> ZCountAsync(string key, decimal min, decimal max) => ExecuteScalarAsync(key, (c, k) => c.Value.ZCountAsync(k, min == decimal.MinValue ? "-inf" : min.ToString(), max == decimal.MaxValue ? "+inf" : max.ToString()));
        /// <summary>
        /// 计算在有序集合中指定区间分数的成员数量
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <returns></returns>
        public Task<long> ZCountAsync(string key, string min, string max) => ExecuteScalarAsync(key, (c, k) => c.Value.ZCountAsync(k, min, max));
        /// <summary>
        /// 有序集合中对指定成员的分数加上增量 increment
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member">成员</param>
        /// <param name="increment">增量值(默认=1)</param>
        /// <returns></returns>
        public Task<decimal> ZIncrByAsync(string key, string member, decimal increment = 1)
        {
            var args = this.SerializeRedisValueInternal(member);
            return ExecuteScalarAsync(key, (c, k) => c.Value.ZIncrByAsync(k, increment, args));
        }

        /// <summary>
        /// 计算给定的一个或多个有序集的交集，将结果集存储在新的有序集合 destination 中
        /// </summary>
        /// <param name="destination">新的有序集合，不含prefix前辍</param>
        /// <param name="weights">使用 WEIGHTS 选项，你可以为 每个 给定有序集 分别 指定一个乘法因子。如果没有指定 WEIGHTS 选项，乘法因子默认设置为 1 。</param>
        /// <param name="aggregate">Sum | Min | Max</param>
        /// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> ZInterStoreAsync(string destination, decimal[] weights, RedisAggregate aggregate, params string[] keys)
        {
            if (keys == null || keys.Length == 0) throw new Exception("keys 参数不可为空");
            if (weights != null && weights.Length != keys.Length) throw new Exception("weights 和 keys 参数长度必须相同");
            return NodesNotSupportAsync(new[] { destination }.Concat(keys).ToArray(), 0, (c, k) => c.Value.ZInterStoreAsync(k.First(), weights, aggregate, k.Skip(1).ToArray()));
        }

        /// <summary>
        /// 通过索引区间返回有序集合成指定区间内的成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public Task<string[]> ZRangeAsync(string key, long start, long stop) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeAsync(k, start, stop, false));
        /// <summary>
        /// 通过索引区间返回有序集合成指定区间内的成员
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        async public Task<T[]> ZRangeAsync<T>(string key, long start, long stop) => this.DeserializeRedisValueArrayInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeBytesAsync(k, start, stop, false)));
        /// <summary>
        /// 通过索引区间返回有序集合成指定区间内的成员和分数
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        async public Task<(string member, decimal score)[]> ZRangeWithScoresAsync(string key, long start, long stop) => (await ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeWithScoresAsync(k, start, stop))).Select(a => (a.Item1, a.Item2)).ToArray();
        /// <summary>
        /// 通过索引区间返回有序集合成指定区间内的成员和分数
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        async public Task<(T member, decimal score)[]> ZRangeWithScoresAsync<T>(string key, long start, long stop) => this.DeserializeRedisValueTuple1Internal<T, decimal>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeBytesWithScoresAsync(k, start, stop)));

        /// <summary>
        /// 通过分数返回有序集合指定区间内的成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public Task<string[]> ZRangeByScoreAsync(string key, decimal min, decimal max, long? count = null, long offset = 0) =>
            ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeByScoreAsync(k, min == decimal.MinValue ? "-inf" : min.ToString(), max == decimal.MaxValue ? "+inf" : max.ToString(), false, offset, count));
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
        async public Task<T[]> ZRangeByScoreAsync<T>(string key, decimal min, decimal max, long? count = null, long offset = 0) =>
            this.DeserializeRedisValueArrayInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeBytesByScoreAsync(k, min == decimal.MinValue ? "-inf" : min.ToString(), max == decimal.MaxValue ? "+inf" : max.ToString(), false, offset, count)));
        /// <summary>
        /// 通过分数返回有序集合指定区间内的成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public Task<string[]> ZRangeByScoreAsync(string key, string min, string max, long? count = null, long offset = 0) =>
            ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeByScoreAsync(k, min, max, false, offset, count));
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
        async public Task<T[]> ZRangeByScoreAsync<T>(string key, string min, string max, long? count = null, long offset = 0) =>
            this.DeserializeRedisValueArrayInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeBytesByScoreAsync(k, min, max, false, offset, count)));

        /// <summary>
        /// 通过分数返回有序集合指定区间内的成员和分数
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        async public Task<(string member, decimal score)[]> ZRangeByScoreWithScoresAsync(string key, decimal min, decimal max, long? count = null, long offset = 0) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeByScoreWithScoresAsync(k, min == decimal.MinValue ? "-inf" : min.ToString(), max == decimal.MaxValue ? "+inf" : max.ToString(), offset, count))).Select(z => (z.Item1, z.Item2)).ToArray();
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
        async public Task<(T member, decimal score)[]> ZRangeByScoreWithScoresAsync<T>(string key, decimal min, decimal max, long? count = null, long offset = 0) =>
            this.DeserializeRedisValueTuple1Internal<T, decimal>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeBytesByScoreWithScoresAsync(k, min == decimal.MinValue ? "-inf" : min.ToString(), max == decimal.MaxValue ? "+inf" : max.ToString(), offset, count)));
        /// <summary>
        /// 通过分数返回有序集合指定区间内的成员和分数
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        async public Task<(string member, decimal score)[]> ZRangeByScoreWithScoresAsync(string key, string min, string max, long? count = null, long offset = 0) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeByScoreWithScoresAsync(k, min, max, offset, count))).Select(z => (z.Item1, z.Item2)).ToArray();
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
        async public Task<(T member, decimal score)[]> ZRangeByScoreWithScoresAsync<T>(string key, string min, string max, long? count = null, long offset = 0) =>
            this.DeserializeRedisValueTuple1Internal<T, decimal>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeBytesByScoreWithScoresAsync(k, min, max, offset, count)));

        /// <summary>
        /// 返回有序集合中指定成员的索引
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member">成员</param>
        /// <returns></returns>
        public Task<long?> ZRankAsync(string key, object member)
        {
            var args = this.SerializeRedisValueInternal(member);
            return ExecuteScalarAsync(key, (c, k) => c.Value.ZRankAsync(k, args));
        }
        /// <summary>
        /// 移除有序集合中的一个或多个成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member">一个或多个成员</param>
        /// <returns></returns>
        async public Task<long> ZRemAsync<T>(string key, params T[] member)
        {
            if (member == null || member.Any() == false) return 0;
            var args = member.Select(z => this.SerializeRedisValueInternal(z)).ToArray();
            return await ExecuteScalarAsync(key, (c, k) => c.Value.ZRemAsync(k, args));
        }
        /// <summary>
        /// 移除有序集合中给定的排名区间的所有成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public Task<long> ZRemRangeByRankAsync(string key, long start, long stop) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRemRangeByRankAsync(k, start, stop));
        /// <summary>
        /// 移除有序集合中给定的分数区间的所有成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <returns></returns>
        public Task<long> ZRemRangeByScoreAsync(string key, decimal min, decimal max) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRemRangeByScoreAsync(k, min == decimal.MinValue ? "-inf" : min.ToString(), max == decimal.MaxValue ? "+inf" : max.ToString()));
        /// <summary>
        /// 移除有序集合中给定的分数区间的所有成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <returns></returns>
        public Task<long> ZRemRangeByScoreAsync(string key, string min, string max) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRemRangeByScoreAsync(k, min, max));

        /// <summary>
        /// 返回有序集中指定区间内的成员，通过索引，分数从高到底
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public Task<string[]> ZRevRangeAsync(string key, long start, long stop) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeAsync(k, start, stop, false));
        /// <summary>
        /// 返回有序集中指定区间内的成员，通过索引，分数从高到底
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        async public Task<T[]> ZRevRangeAsync<T>(string key, long start, long stop) => this.DeserializeRedisValueArrayInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeBytesAsync(k, start, stop, false)));
        /// <summary>
        /// 返回有序集中指定区间内的成员和分数，通过索引，分数从高到底
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        async public Task<(string member, decimal score)[]> ZRevRangeWithScoresAsync(string key, long start, long stop) => (await ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeWithScoresAsync(k, start, stop))).Select(a => (a.Item1, a.Item2)).ToArray();
        /// <summary>
        /// 返回有序集中指定区间内的成员和分数，通过索引，分数从高到底
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        async public Task<(T member, decimal score)[]> ZRevRangeWithScoresAsync<T>(string key, long start, long stop) => this.DeserializeRedisValueTuple1Internal<T, decimal>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeBytesWithScoresAsync(k, start, stop)));

        /// <summary>
        /// 返回有序集中指定分数区间内的成员，分数从高到低排序
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public Task<string[]> ZRevRangeByScoreAsync(string key, decimal max, decimal min, long? count = null, long? offset = 0) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeByScoreAsync(k, max == decimal.MaxValue ? "+inf" : max.ToString(), min == decimal.MinValue ? "-inf" : min.ToString(), false, offset, count));
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
        async public Task<T[]> ZRevRangeByScoreAsync<T>(string key, decimal max, decimal min, long? count = null, long offset = 0) =>
            this.DeserializeRedisValueArrayInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeBytesByScoreAsync(k, max == decimal.MaxValue ? "+inf" : max.ToString(), min == decimal.MinValue ? "-inf" : min.ToString(), false, offset, count)));
        /// <summary>
        /// 返回有序集中指定分数区间内的成员，分数从高到低排序
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public Task<string[]> ZRevRangeByScoreAsync(string key, string max, string min, long? count = null, long? offset = 0) => ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeByScoreAsync(k, max, min, false, offset, count));
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
        async public Task<T[]> ZRevRangeByScoreAsync<T>(string key, string max, string min, long? count = null, long offset = 0) =>
            this.DeserializeRedisValueArrayInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeBytesByScoreAsync(k, max, min, false, offset, count)));

        /// <summary>
        /// 返回有序集中指定分数区间内的成员和分数，分数从高到低排序
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="max">分数最大值 decimal.MaxValue 10</param>
        /// <param name="min">分数最小值 decimal.MinValue 1</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        async public Task<(string member, decimal score)[]> ZRevRangeByScoreWithScoresAsync(string key, decimal max, decimal min, long? count = null, long offset = 0) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeByScoreWithScoresAsync(k, max == decimal.MaxValue ? "+inf" : max.ToString(), min == decimal.MinValue ? "-inf" : min.ToString(), offset, count))).Select(z => (z.Item1, z.Item2)).ToArray();
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
        async public Task<(T member, decimal score)[]> ZRevRangeByScoreWithScoresAsync<T>(string key, decimal max, decimal min, long? count = null, long offset = 0) =>
            this.DeserializeRedisValueTuple1Internal<T, decimal>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeBytesByScoreWithScoresAsync(k, max == decimal.MaxValue ? "+inf" : max.ToString(), min == decimal.MinValue ? "-inf" : min.ToString(), offset, count)));
        /// <summary>
        /// 返回有序集中指定分数区间内的成员和分数，分数从高到低排序
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="max">分数最大值 +inf (10 10</param>
        /// <param name="min">分数最小值 -inf (1 1</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        async public Task<(string member, decimal score)[]> ZRevRangeByScoreWithScoresAsync(string key, string max, string min, long? count = null, long offset = 0) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeByScoreWithScoresAsync(k, max, min, offset, count))).Select(z => (z.Item1, z.Item2)).ToArray();
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
        async public Task<(T member, decimal score)[]> ZRevRangeByScoreWithScoresAsync<T>(string key, string max, string min, long? count = null, long offset = 0) =>
            this.DeserializeRedisValueTuple1Internal<T, decimal>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRangeBytesByScoreWithScoresAsync(k, max, min, offset, count)));

        /// <summary>
        /// 返回有序集合中指定成员的排名，有序集成员按分数值递减(从大到小)排序
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member">成员</param>
        /// <returns></returns>
        public Task<long?> ZRevRankAsync(string key, object member)
        {
            var args = this.SerializeRedisValueInternal(member);
            return ExecuteScalarAsync(key, (c, k) => c.Value.ZRevRankAsync(k, args));
        }
        /// <summary>
        /// 返回有序集中，成员的分数值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member">成员</param>
        /// <returns></returns>
        public Task<decimal?> ZScoreAsync(string key, object member)
        {
            var args = this.SerializeRedisValueInternal(member);
            return ExecuteScalarAsync(key, (c, k) => c.Value.ZScoreAsync(k, args));
        }

        /// <summary>
        /// 计算给定的一个或多个有序集的并集，将结果集存储在新的有序集合 destination 中
        /// </summary>
        /// <param name="destination">新的有序集合，不含prefix前辍</param>
        /// <param name="weights">使用 WEIGHTS 选项，你可以为 每个 给定有序集 分别 指定一个乘法因子。如果没有指定 WEIGHTS 选项，乘法因子默认设置为 1 。</param>
        /// <param name="aggregate">Sum | Min | Max</param>
        /// <param name="keys">一个或多个有序集合，不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> ZUnionStoreAsync(string destination, decimal[] weights, RedisAggregate aggregate, params string[] keys)
        {
            if (keys == null || keys.Length == 0) throw new Exception("keys 参数不可为空");
            if (weights != null && weights.Length != keys.Length) throw new Exception("weights 和 keys 参数长度必须相同");
            return NodesNotSupportAsync(new[] { destination }.Concat(keys).ToArray(), 0, (c, k) => c.Value.ZUnionStoreAsync(k.First(), weights, aggregate, k.Skip(1).ToArray()));
        }

        /// <summary>
        /// 迭代有序集合中的元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        async public Task<RedisScan<(string member, decimal score)>> ZScanAsync(string key, long cursor, string pattern = null, long? count = null)
        {
            var scan = await ExecuteScalarAsync(key, (c, k) => c.Value.ZScanAsync(k, cursor, pattern, count));
            return new RedisScan<(string, decimal)>(scan.Cursor, scan.Items.Select(z => (z.Item1, z.Item2)).ToArray());
        }
        /// <summary>
        /// 迭代有序集合中的元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        async public Task<RedisScan<(T member, decimal score)>> ZScanAsync<T>(string key, long cursor, string pattern = null, long? count = null)
        {
            var scan = await ExecuteScalarAsync(key, (c, k) => c.Value.ZScanBytesAsync(k, cursor, pattern, count));
            return new RedisScan<(T, decimal)>(scan.Cursor, this.DeserializeRedisValueTuple1Internal<T, decimal>(scan.Items));
        }

        /// <summary>
        /// 当有序集合的所有成员都具有相同的分值时，有序集合的元素会根据成员的字典序来进行排序，这个命令可以返回给定的有序集合键 key 中，值介于 min 和 max 之间的成员。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
        /// <param name="max">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
        /// <param name="count">返回多少成员</param>
        /// <param name="offset">返回条件偏移位置</param>
        /// <returns></returns>
        public Task<string[]> ZRangeByLexAsync(string key, string min, string max, long? count = null, long offset = 0) =>
            ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeByLexAsync(k, min, max, offset, count));
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
        async public Task<T[]> ZRangeByLexAsync<T>(string key, string min, string max, long? count = null, long offset = 0) =>
            this.DeserializeRedisValueArrayInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.ZRangeBytesByLexAsync(k, min, max, offset, count)));

        /// <summary>
        /// 当有序集合的所有成员都具有相同的分值时，有序集合的元素会根据成员的字典序来进行排序，这个命令可以返回给定的有序集合键 key 中，值介于 min 和 max 之间的成员。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
        /// <param name="max">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
        /// <returns></returns>
        public Task<long> ZRemRangeByLexAsync(string key, string min, string max) =>
            ExecuteScalarAsync(key, (c, k) => c.Value.ZRemRangeByLexAsync(k, min, max));
        /// <summary>
        /// 当有序集合的所有成员都具有相同的分值时，有序集合的元素会根据成员的字典序来进行排序，这个命令可以返回给定的有序集合键 key 中，值介于 min 和 max 之间的成员。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="min">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
        /// <param name="max">'(' 表示包含在范围，'[' 表示不包含在范围，'+' 正无穷大，'-' 负无限。 ZRANGEBYLEX zset - + ，命令将返回有序集合中的所有元素</param>
        /// <returns></returns>
        public Task<long> ZLexCountAsync(string key, string min, string max) =>
            ExecuteScalarAsync(key, (c, k) => c.Value.ZLexCountAsync(k, min, max));
        #endregion

        #region Set
        /// <summary>
        /// 向集合添加一个或多个成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="members">一个或多个成员</param>
        /// <returns></returns>
        async public Task<long> SAddAsync<T>(string key, params T[] members)
        {
            if (members == null || members.Any() == false) return 0;
            var args = members.Select(z => this.SerializeRedisValueInternal(z)).ToArray();
            return await ExecuteScalarAsync(key, (c, k) => c.Value.SAddAsync(k, args));
        }
        /// <summary>
        /// 获取集合的成员数
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> SCardAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.SCardAsync(k));
        /// <summary>
        /// 返回给定所有集合的差集
        /// </summary>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        public Task<string[]> SDiffAsync(params string[] keys) => NodesNotSupportAsync(keys, new string[0], (c, k) => c.Value.SDiffAsync(k));
        /// <summary>
        /// 返回给定所有集合的差集
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        async public Task<T[]> SDiffAsync<T>(params string[] keys) => this.DeserializeRedisValueArrayInternal<T>(await NodesNotSupportAsync(keys, new byte[0][], (c, k) => c.Value.SDiffBytesAsync(k)));
        /// <summary>
        /// 返回给定所有集合的差集并存储在 destination 中
        /// </summary>
        /// <param name="destination">新的无序集合，不含prefix前辍</param>
        /// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> SDiffStoreAsync(string destination, params string[] keys) => NodesNotSupportAsync(new[] { destination }.Concat(keys).ToArray(), 0, (c, k) => c.Value.SDiffStoreAsync(k.First(), k.Skip(1).ToArray()));
        /// <summary>
        /// 返回给定所有集合的交集
        /// </summary>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        public Task<string[]> SInterAsync(params string[] keys) => NodesNotSupportAsync(keys, new string[0], (c, k) => c.Value.SInterAsync(k));
        /// <summary>
        /// 返回给定所有集合的交集
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        async public Task<T[]> SInterAsync<T>(params string[] keys) => this.DeserializeRedisValueArrayInternal<T>(await NodesNotSupportAsync(keys, new byte[0][], (c, k) => c.Value.SInterBytesAsync(k)));
        /// <summary>
        /// 返回给定所有集合的交集并存储在 destination 中
        /// </summary>
        /// <param name="destination">新的无序集合，不含prefix前辍</param>
        /// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> SInterStoreAsync(string destination, params string[] keys) => NodesNotSupportAsync(new[] { destination }.Concat(keys).ToArray(), 0, (c, k) => c.Value.SInterStoreAsync(k.First(), k.Skip(1).ToArray()));
        /// <summary>
        /// 判断 member 元素是否是集合 key 的成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member">成员</param>
        /// <returns></returns>
        public Task<bool> SIsMemberAsync(string key, object member)
        {
            var args = this.SerializeRedisValueInternal(member);
            return ExecuteScalarAsync(key, (c, k) => c.Value.SIsMemberAsync(k, args));
        }
        /// <summary>
        /// 返回集合中的所有成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<string[]> SMembersAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.SMembersAsync(k));
        /// <summary>
        /// 返回集合中的所有成员
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        async public Task<T[]> SMembersAsync<T>(string key) => this.DeserializeRedisValueArrayInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.SMembersBytesAsync(k)));
        /// <summary>
        /// 将 member 元素从 source 集合移动到 destination 集合
        /// </summary>
        /// <param name="source">无序集合key，不含prefix前辍</param>
        /// <param name="destination">目标无序集合key，不含prefix前辍</param>
        /// <param name="member">成员</param>
        /// <returns></returns>
        async public Task<bool> SMoveAsync(string source, string destination, object member)
        {
            string rule = string.Empty;
            if (Nodes.Count > 1)
            {
                var rule1 = NodeRuleRaw(source);
                var rule2 = NodeRuleRaw(destination);
                if (rule1 != rule2)
                {
                    if (await SRemAsync(source, member) <= 0) return false;
                    return await SAddAsync(destination, member) > 0;
                }
                rule = rule1;
            }
            var pool = Nodes.TryGetValue(rule, out var b) ? b : Nodes.First().Value;
            var key1 = string.Concat(pool.Prefix, source);
            var key2 = string.Concat(pool.Prefix, destination);
            var args = this.SerializeRedisValueInternal(member);
            return await GetAndExecuteAsync(pool, conn => conn.Value.SMoveAsync(key1, key2, args));
        }
        /// <summary>
        /// 移除并返回集合中的一个随机元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<string> SPopAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.SPopAsync(k));
        /// <summary>
        /// 移除并返回集合中的一个随机元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        async public Task<T> SPopAsync<T>(string key) => this.DeserializeRedisValueInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.SPopBytesAsync(k)));
        /// <summary>
        /// [redis-server 3.2] 移除并返回集合中的一个或多个随机元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">移除并返回的个数</param>
        /// <returns></returns>
        public Task<string[]> SPopAsync(string key, long count) => ExecuteScalarAsync(key, (c, k) => c.Value.SPopAsync(k, count));
        /// <summary>
        /// [redis-server 3.2] 移除并返回集合中的一个或多个随机元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">移除并返回的个数</param>
        /// <returns></returns>
        async public Task<T[]> SPopAsync<T>(string key, long count) => this.DeserializeRedisValueArrayInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.SPopBytesAsync(k, count)));
        /// <summary>
        /// 返回集合中的一个随机元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<string> SRandMemberAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.SRandMemberAsync(k));
        /// <summary>
        /// 返回集合中的一个随机元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        async public Task<T> SRandMemberAsync<T>(string key) => this.DeserializeRedisValueInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.SRandMemberBytesAsync(k)));
        /// <summary>
        /// 返回集合中一个或多个随机数的元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">返回个数</param>
        /// <returns></returns>
        public Task<string[]> SRandMembersAsync(string key, int count = 1) => ExecuteScalarAsync(key, (c, k) => c.Value.SRandMembersAsync(k, count));
        /// <summary>
        /// 返回集合中一个或多个随机数的元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">返回个数</param>
        /// <returns></returns>
        async public Task<T[]> SRandMembersAsync<T>(string key, int count = 1) => this.DeserializeRedisValueArrayInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.SRandMembersBytesAsync(k, count)));
        /// <summary>
        /// 移除集合中一个或多个成员
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="members">一个或多个成员</param>
        /// <returns></returns>
        async public Task<long> SRemAsync<T>(string key, params T[] members)
        {
            if (members == null || members.Any() == false) return 0;
            var args = members.Select(z => this.SerializeRedisValueInternal(z)).ToArray();
            return await ExecuteScalarAsync(key, (c, k) => c.Value.SRemAsync(k, args));
        }
        /// <summary>
        /// 返回所有给定集合的并集
        /// </summary>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        public Task<string[]> SUnionAsync(params string[] keys) => NodesNotSupportAsync(keys, new string[0], (c, k) => c.Value.SUnionAsync(k));
        /// <summary>
        /// 返回所有给定集合的并集
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        async public Task<T[]> SUnionAsync<T>(params string[] keys) => this.DeserializeRedisValueArrayInternal<T>(await NodesNotSupportAsync(keys, new byte[0][], (c, k) => c.Value.SUnionBytesAsync(k)));
        /// <summary>
        /// 所有给定集合的并集存储在 destination 集合中
        /// </summary>
        /// <param name="destination">新的无序集合，不含prefix前辍</param>
        /// <param name="keys">一个或多个无序集合，不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> SUnionStoreAsync(string destination, params string[] keys) => NodesNotSupportAsync(new[] { destination }.Concat(keys).ToArray(), 0, (c, k) => c.Value.SUnionStoreAsync(k.First(), k.Skip(1).ToArray()));
        /// <summary>
        /// 迭代集合中的元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public Task<RedisScan<string>> SScanAsync(string key, long cursor, string pattern = null, long? count = null) => ExecuteScalarAsync(key, (c, k) => c.Value.SScanAsync(k, cursor, pattern, count));
        /// <summary>
        /// 迭代集合中的元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        async public Task<RedisScan<T>> SScanAsync<T>(string key, long cursor, string pattern = null, long? count = null)
        {
            var scan = await ExecuteScalarAsync(key, (c, k) => c.Value.SScanBytesAsync(k, cursor, pattern, count));
            return new RedisScan<T>(scan.Cursor, this.DeserializeRedisValueArrayInternal<T>(scan.Items));
        }
        #endregion

        #region List
        /// <summary>
        /// 通过索引获取列表中的元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="index">索引</param>
        /// <returns></returns>
        public Task<string> LIndexAsync(string key, long index) => ExecuteScalarAsync(key, (c, k) => c.Value.LIndexAsync(k, index));
        /// <summary>
        /// 通过索引获取列表中的元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="index">索引</param>
        /// <returns></returns>
        async public Task<T> LIndexAsync<T>(string key, long index) => this.DeserializeRedisValueInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.LIndexBytesAsync(k, index)));
        /// <summary>
        /// 在列表中的元素前面插入元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="pivot">列表的元素</param>
        /// <param name="value">新元素</param>
        /// <returns></returns>
        public Task<long> LInsertBeforeAsync(string key, object pivot, object value)
        {
            var args = this.SerializeRedisValueInternal(value);
            return ExecuteScalarAsync(key, (c, k) => c.Value.LInsertAsync(k, RedisInsert.Before, pivot, args));
        }
        /// <summary>
        /// 在列表中的元素后面插入元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="pivot">列表的元素</param>
        /// <param name="value">新元素</param>
        /// <returns></returns>
        public Task<long> LInsertAfterAsync(string key, object pivot, object value)
        {
            var args = this.SerializeRedisValueInternal(value);
            return ExecuteScalarAsync(key, (c, k) => c.Value.LInsertAsync(k, RedisInsert.After, pivot, args));
        }
        /// <summary>
        /// 获取列表长度
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> LLenAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.LLenAsync(k));
        /// <summary>
        /// 移出并获取列表的第一个元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<string> LPopAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.LPopAsync(k));
        /// <summary>
        /// 移出并获取列表的第一个元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        async public Task<T> LPopAsync<T>(string key) => this.DeserializeRedisValueInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.LPopBytesAsync(k)));
        /// <summary>
        /// 将一个或多个值插入到列表头部
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">一个或多个值</param>
        /// <returns>执行 LPUSH 命令后，列表的长度</returns>
        async public Task<long> LPushAsync<T>(string key, params T[] value)
        {
            if (value == null || value.Any() == false) return 0;
            var args = value.Select(z => this.SerializeRedisValueInternal(z)).ToArray();
            return await ExecuteScalarAsync(key, (c, k) => c.Value.LPushAsync(k, args));
        }
        /// <summary>
        /// 将一个值插入到已存在的列表头部
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">值</param>
        /// <returns>执行 LPUSHX 命令后，列表的长度。</returns>
        public Task<long> LPushXAsync(string key, object value)
        {
            var args = this.SerializeRedisValueInternal(value);
            return ExecuteScalarAsync(key, (c, k) => c.Value.LPushXAsync(k, args));
        }
        /// <summary>
        /// 获取列表指定范围内的元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public Task<string[]> LRangeAsync(string key, long start, long stop) => ExecuteScalarAsync(key, (c, k) => c.Value.LRangeAsync(k, start, stop));
        /// <summary>
        /// 获取列表指定范围内的元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        async public Task<T[]> LRangeAsync<T>(string key, long start, long stop) => this.DeserializeRedisValueArrayInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.LRangeBytesAsync(k, start, stop)));
        /// <summary>
        /// 根据参数 count 的值，移除列表中与参数 value 相等的元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="count">移除的数量，大于0时从表头删除数量count，小于0时从表尾删除数量-count，等于0移除所有</param>
        /// <param name="value">元素</param>
        /// <returns></returns>
        public Task<long> LRemAsync(string key, long count, object value)
        {
            var args = this.SerializeRedisValueInternal(value);
            return ExecuteScalarAsync(key, (c, k) => c.Value.LRemAsync(k, count, args));
        }
        /// <summary>
        /// 通过索引设置列表元素的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="index">索引</param>
        /// <param name="value">值</param>
        /// <returns></returns>
        async public Task<bool> LSetAsync(string key, long index, object value)
        {
            var args = this.SerializeRedisValueInternal(value);
            return await ExecuteScalar(key, (c, k) => c.Value.LSetAsync(k, index, args)) == "OK";
        }
        /// <summary>
        /// 对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public Task<bool> LTrimAsync(string key, long start, long stop) => ExecuteScalarAsync(key, async (c, k) => await c.Value.LTrimAsync(k, start, stop) == "OK");
        /// <summary>
        /// 移除并获取列表最后一个元素
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<string> RPopAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.RPopAsync(k));
        /// <summary>
        /// 移除并获取列表最后一个元素
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        async public Task<T> RPopAsync<T>(string key) => this.DeserializeRedisValueInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.RPopBytesAsync(k)));
        /// <summary>
        /// 将列表 source 中的最后一个元素(尾元素)弹出，并返回给客户端。
        /// 将 source 弹出的元素插入到列表 destination ，作为 destination 列表的的头元素。
        /// </summary>
        /// <param name="source">源key，不含prefix前辍</param>
        /// <param name="destination">目标key，不含prefix前辍</param>
        /// <returns></returns>
        public Task<string> RPopLPushAsync(string source, string destination) => NodesNotSupportAsync(new[] { source, destination }, null, (c, k) => c.Value.RPopLPushAsync(k.First(), k.Last()));
        /// <summary>
        /// 将列表 source 中的最后一个元素(尾元素)弹出，并返回给客户端。
        /// 将 source 弹出的元素插入到列表 destination ，作为 destination 列表的的头元素。
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="source">源key，不含prefix前辍</param>
        /// <param name="destination">目标key，不含prefix前辍</param>
        /// <returns></returns>
        async public Task<T> RPopLPushAsync<T>(string source, string destination) => this.DeserializeRedisValueInternal<T>(await NodesNotSupportAsync(new[] { source, destination }, null, (c, k) => c.Value.RPopBytesLPushAsync(k.First(), k.Last())));
        /// <summary>
        /// 在列表中添加一个或多个值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">一个或多个值</param>
        /// <returns>执行 RPUSH 命令后，列表的长度</returns>
        async public Task<long> RPushAsync<T>(string key, params T[] value)
        {
            if (value == null || value.Any() == false) return 0;
            var args = value.Select(z => this.SerializeRedisValueInternal(z)).ToArray();
            return await ExecuteScalarAsync(key, (c, k) => c.Value.RPushAsync(k, args));
        }
        /// <summary>
        /// 为已存在的列表添加值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">一个或多个值</param>
        /// <returns>执行 RPUSHX 命令后，列表的长度</returns>
        public Task<long> RPushXAsync(string key, object value)
        {
            var args = this.SerializeRedisValueInternal(value);
            return ExecuteScalar(key, (c, k) => c.Value.RPushXAsync(k, args));
        }
        #endregion

        #region Hash
        /// <summary>
        /// [redis-server 3.2.0] 返回hash指定field的value的字符串长度，如果hash或者field不存在，返回0.
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <returns></returns>
        public Task<long> HStrLenAsync(string key, string field) => ExecuteScalarAsync(key, (c, k) => c.Value.HStrLenAsync(k, field));
        /// <summary>
        /// 删除一个或多个哈希表字段
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="fields">字段</param>
        /// <returns></returns>
        async public Task<long> HDelAsync(string key, params string[] fields) => fields == null || fields.Any() == false ? 0 :
            await ExecuteScalarAsync(key, (c, k) => c.Value.HDelAsync(k, fields));
        /// <summary>
        /// 查看哈希表 key 中，指定的字段是否存在
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <returns></returns>
        public Task<bool> HExistsAsync(string key, string field) => ExecuteScalarAsync(key, (c, k) => c.Value.HExistsAsync(k, field));
        /// <summary>
        /// 获取存储在哈希表中指定字段的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <returns></returns>
        public Task<string> HGetAsync(string key, string field) => ExecuteScalarAsync(key, (c, k) => c.Value.HGetAsync(k, field));
        /// <summary>
        /// 获取存储在哈希表中指定字段的值
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <returns></returns>
        async public Task<T> HGetAsync<T>(string key, string field) => this.DeserializeRedisValueInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.HGetBytesAsync(k, field)));
        /// <summary>
        /// 获取在哈希表中指定 key 的所有字段和值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<Dictionary<string, string>> HGetAllAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.HGetAllAsync(k));
        /// <summary>
        /// 获取在哈希表中指定 key 的所有字段和值
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        async public Task<Dictionary<string, T>> HGetAllAsync<T>(string key) => this.DeserializeRedisValueDictionaryInternal<string, T>(await ExecuteScalarAsync(key, (c, k) => c.Value.HGetAllBytesAsync(k)));
        /// <summary>
        /// 为哈希表 key 中的指定字段的整数值加上增量 increment
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <param name="value">增量值(默认=1)</param>
        /// <returns></returns>
        public Task<long> HIncrByAsync(string key, string field, long value = 1) => ExecuteScalarAsync(key, (c, k) => c.Value.HIncrByAsync(k, field, value));
        /// <summary>
        /// 为哈希表 key 中的指定字段的整数值加上增量 increment
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <param name="value">增量值(默认=1)</param>
        /// <returns></returns>
        public Task<decimal> HIncrByFloatAsync(string key, string field, decimal value) => ExecuteScalarAsync(key, (c, k) => c.Value.HIncrByFloatAsync(k, field, value));
        /// <summary>
        /// 获取所有哈希表中的字段
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<string[]> HKeysAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.HKeysAsync(k));
        /// <summary>
        /// 获取哈希表中字段的数量
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> HLenAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.HLenAsync(k));
        /// <summary>
        /// 获取存储在哈希表中多个字段的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="fields">字段</param>
        /// <returns></returns>
        async public Task<string[]> HMGetAsync(string key, params string[] fields) => fields == null || fields.Any() == false ? new string[0] :
            await ExecuteScalarAsync(key, (c, k) => c.Value.HMGetAsync(k, fields));
        /// <summary>
        /// 获取存储在哈希表中多个字段的值
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="fields">一个或多个字段</param>
        /// <returns></returns>
        async public Task<T[]> HMGetAsync<T>(string key, params string[] fields) => fields == null || fields.Any() == false ? new T[0] :
            this.DeserializeRedisValueArrayInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.HMGetBytesAsync(k, fields)));
        /// <summary>
        /// 同时将多个 field-value (域-值)对设置到哈希表 key 中
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="keyValues">key1 value1 [key2 value2]</param>
        /// <returns></returns>
        async public Task<bool> HMSetAsync(string key, params object[] keyValues)
        {
            if (keyValues == null || keyValues.Any() == false) return false;
            if (keyValues.Length % 2 != 0) throw new Exception("keyValues 参数是键值对，不应该出现奇数(数量)，请检查使用姿势。");
            var parms = new List<object>();
            for (var a = 0; a < keyValues.Length; a += 2)
            {
                var k = string.Concat(keyValues[a]);
                var v = keyValues[a + 1];
                if (string.IsNullOrEmpty(k)) throw new Exception("keyValues 参数是键值对，并且 key 不可为空");
                parms.Add(k);
                parms.Add(this.SerializeRedisValueInternal(v));
            }
            return await ExecuteScalarAsync(key, (c, k) => c.Value.HMSetAsync(k, parms.ToArray())) == "OK";
        }
        /// <summary>
        /// 将哈希表 key 中的字段 field 的值设为 value
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <param name="value">值</param>
        /// <returns>如果字段是哈希表中的一个新建字段，并且值设置成功，返回true。如果哈希表中域字段已经存在且旧值已被新值覆盖，返回false。</returns>
        public Task<bool> HSetAsync(string key, string field, object value)
        {
            var args = this.SerializeRedisValueInternal(value);
            return ExecuteScalarAsync(key, (c, k) => c.Value.HSetAsync(k, field, args));
        }
        /// <summary>
        /// 只有在字段 field 不存在时，设置哈希表字段的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <param name="value">值(string 或 byte[])</param>
        /// <returns></returns>
        public Task<bool> HSetNxAsync(string key, string field, object value)
        {
            var args = this.SerializeRedisValueInternal(value);
            return ExecuteScalarAsync(key, (c, k) => c.Value.HSetNxAsync(k, field, args));
        }
        /// <summary>
        /// 获取哈希表中所有值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<string[]> HValsAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.HValsAsync(k));
        /// <summary>
        /// 获取哈希表中所有值
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        async public Task<T[]> HValsAsync<T>(string key) => this.DeserializeRedisValueArrayInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.HValsBytesAsync(k)));
        /// <summary>
        /// 迭代哈希表中的键值对
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        async public Task<RedisScan<(string field, string value)>> HScanAsync(string key, long cursor, string pattern = null, long? count = null)
        {
            var scan = await ExecuteScalarAsync(key, (c, k) => c.Value.HScanAsync(k, cursor, pattern, count));
            return new RedisScan<(string, string)>(scan.Cursor, scan.Items.Select(z => (z.Item1, z.Item2)).ToArray());
        }
        /// <summary>
        /// 迭代哈希表中的键值对
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        async public Task<RedisScan<(string field, T value)>> HScanAsync<T>(string key, long cursor, string pattern = null, long? count = null)
        {
            var scan = await ExecuteScalarAsync(key, (c, k) => c.Value.HScanBytesAsync(k, cursor, pattern, count));
            return new RedisScan<(string, T)>(scan.Cursor, scan.Items.Select(z => (z.Item1, this.DeserializeRedisValueInternal<T>(z.Item2))).ToArray());
        }
        #endregion

        #region String
        /// <summary>
        /// 如果 key 已经存在并且是一个字符串， APPEND 命令将指定的 value 追加到该 key 原来值（value）的末尾
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">字符串</param>
        /// <returns>追加指定值之后， key 中字符串的长度</returns>
        public Task<long> AppendAsync(string key, object value)
        {
            var args = this.SerializeRedisValueInternal(value);
            return ExecuteScalarAsync(key, (c, k) => c.Value.AppendAsync(k, args));
        }
        /// <summary>
        /// 计算给定位置被设置为 1 的比特位的数量
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置</param>
        /// <param name="end">结束位置</param>
        /// <returns></returns>
        public Task<long> BitCountAsync(string key, long start, long end) => ExecuteScalarAsync(key, (c, k) => c.Value.BitCountAsync(k, start, end));
        /// <summary>
        /// 对一个或多个保存二进制位的字符串 key 进行位元操作，并将结果保存到 destkey 上
        /// </summary>
        /// <param name="op">And | Or | XOr | Not</param>
        /// <param name="destKey">不含prefix前辍</param>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns>保存到 destkey 的长度，和输入 key 中最长的长度相等</returns>
        async public Task<long> BitOpAsync(RedisBitOp op, string destKey, params string[] keys)
        {
            if (string.IsNullOrEmpty(destKey)) throw new Exception("destKey 不能为空");
            if (keys == null || keys.Length == 0) throw new Exception("keys 不能为空");
            return await NodesNotSupportAsync(new[] { destKey }.Concat(keys).ToArray(), 0, (c, k) => c.Value.BitOpAsync(op, k.First(), k.Skip(1).ToArray()));
        }
        /// <summary>
        /// 对 key 所储存的值，查找范围内第一个被设置为1或者0的bit位
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="bit">查找值</param>
        /// <param name="start">开始位置，-1是最后一个，-2是倒数第二个</param>
        /// <param name="end">结果位置，-1是最后一个，-2是倒数第二个</param>
        /// <returns>返回范围内第一个被设置为1或者0的bit位</returns>
        public Task<long> BitPosAsync(string key, bool bit, long? start = null, long? end = null) => ExecuteScalarAsync(key, (c, k) => c.Value.BitPosAsync(k, bit, start, end));
        /// <summary>
        /// 获取指定 key 的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<string> GetAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.GetAsync(k));
        /// <summary>
        /// 获取指定 key 的值
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        async public Task<T> GetAsync<T>(string key) => this.DeserializeRedisValueInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.GetBytesAsync(k)));
        /// <summary>
        /// 对 key 所储存的值，获取指定偏移量上的位(bit)
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="offset">偏移量</param>
        /// <returns></returns>
        public Task<bool> GetBitAsync(string key, uint offset) => ExecuteScalarAsync(key, (c, k) => c.Value.GetBitAsync(k, offset));
        /// <summary>
        /// 返回 key 中字符串值的子字符
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="end">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        public Task<string> GetRangeAsync(string key, long start, long end) => ExecuteScalarAsync(key, (c, k) => c.Value.GetRangeAsync(k, start, end));
        /// <summary>
        /// 返回 key 中字符串值的子字符
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="end">结束位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <returns></returns>
        async public Task<T> GetRangeAsync<T>(string key, long start, long end) => this.DeserializeRedisValueInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.GetRangeBytesAsync(k, start, end)));
        /// <summary>
        /// 将给定 key 的值设为 value ，并返回 key 的旧值(old value)
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">值</param>
        /// <returns></returns>
        public Task<string> GetSetAsync(string key, object value)
        {
            var args = this.SerializeRedisValueInternal(value);
            return ExecuteScalarAsync(key, (c, k) => c.Value.GetSetAsync(k, args));
        }
        /// <summary>
        /// 将给定 key 的值设为 value ，并返回 key 的旧值(old value)
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">值</param>
        /// <returns></returns>
        async public Task<T> GetSetAsync<T>(string key, object value)
        {
            var args = this.SerializeRedisValueInternal(value);
            return this.DeserializeRedisValueInternal<T>(await ExecuteScalarAsync(key, (c, k) => c.Value.GetSetBytesAsync(k, args)));
        }
        /// <summary>
        /// 将 key 所储存的值加上给定的增量值（increment）
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">增量值(默认=1)</param>
        /// <returns></returns>
        public Task<long> IncrByAsync(string key, long value = 1) => ExecuteScalarAsync(key, (c, k) => c.Value.IncrByAsync(k, value));
        /// <summary>
        /// 将 key 所储存的值加上给定的浮点增量值（increment）
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">增量值(默认=1)</param>
        /// <returns></returns>
        public Task<decimal> IncrByFloatAsync(string key, decimal value) => ExecuteScalarAsync(key, (c, k) => c.Value.IncrByFloatAsync(k, value));
        /// <summary>
        /// 获取多个指定 key 的值(数组)
        /// </summary>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        public Task<string[]> MGetAsync(params string[] keys) => ExecuteArrayAsync(keys, (c, k) => c.Value.MGetAsync(k));
        /// <summary>
        /// 获取多个指定 key 的值(数组)
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="keys">不含prefix前辍</param>
        /// <returns></returns>
        async public Task<T[]> MGetAsync<T>(params string[] keys) => this.DeserializeRedisValueArrayInternal<T>(await ExecuteArrayAsync(keys, (c, k) => c.Value.MGetBytesAsync(k)));
        /// <summary>
        /// 同时设置一个或多个 key-value 对
        /// </summary>
        /// <param name="keyValues">key1 value1 [key2 value2]</param>
        /// <returns></returns>
        public Task<bool> MSetAsync(params object[] keyValues) => MSetInternalAsync(RedisExistence.Xx, keyValues);
        /// <summary>
        /// 同时设置一个或多个 key-value 对，当且仅当所有给定 key 都不存在
        /// </summary>
        /// <param name="keyValues">key1 value1 [key2 value2]</param>
        /// <returns></returns>
        public Task<bool> MSetNxAsync(params object[] keyValues) => MSetInternalAsync(RedisExistence.Nx, keyValues);
        async internal Task<bool> MSetInternalAsync(RedisExistence exists, params object[] keyValues)
        {
            if (keyValues == null || keyValues.Any() == false) return false;
            if (keyValues.Length % 2 != 0) throw new Exception("keyValues 参数是键值对，不应该出现奇数(数量)，请检查使用姿势。");
            var dic = new Dictionary<string, object>();
            for (var a = 0; a < keyValues.Length; a += 2)
            {
                var k = string.Concat(keyValues[a]);
                var v = this.SerializeRedisValueInternal(keyValues[a + 1]);
                if (string.IsNullOrEmpty(k)) throw new Exception("keyValues 参数是键值对，并且 key 不可为空");
                if (dic.ContainsKey(k)) dic[k] = v;
                else dic.Add(k, v);
            }
            Func<Object<RedisClient>, string[], Task<long>> handle = async (c, k) =>
            {
                var prefix = (c.Pool as RedisClientPool)?.Prefix;
                var parms = new object[k.Length * 2];
                for (var a = 0; a < k.Length; a++)
                {
                    parms[a * 2] = k[a];
                    parms[a * 2 + 1] = dic[string.IsNullOrEmpty(prefix) ? k[a] : k[a].Substring(prefix.Length)];
                }
                if (exists == RedisExistence.Nx) return await c.Value.MSetNxAsync(parms) ? 1 : 0;
                return await c.Value.MSetAsync(parms) == "OK" ? 1 : 0;
            };
            if (exists == RedisExistence.Nx) return await NodesNotSupportAsync(dic.Keys.ToArray(), 0, handle) > 0;
            return await ExecuteNonQueryAsync(dic.Keys.ToArray(), handle) > 0;
        }
        /// <summary>
        /// 设置指定 key 的值，所有写入参数object都支持string | byte[] | 数值 | 对象
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">值</param>
        /// <param name="expireSeconds">过期(秒单位)</param>
        /// <param name="exists">Nx, Xx</param>
        /// <returns></returns>
        async public Task<bool> SetAsync(string key, object value, int expireSeconds = -1, RedisExistence? exists = null)
        {
            object redisValule = this.SerializeRedisValueInternal(value);
            if (expireSeconds <= 0 && exists == null) return await ExecuteScalarAsync(key, (c, k) => c.Value.SetAsync(k, redisValule)) == "OK";
            if (expireSeconds <= 0 && exists != null) return await ExecuteScalarAsync(key, (c, k) => c.Value.SetAsync(k, redisValule, null, exists)) == "OK";
            if (expireSeconds > 0 && exists == null) return await ExecuteScalarAsync(key, (c, k) => c.Value.SetAsync(k, redisValule, expireSeconds, null)) == "OK";
            if (expireSeconds > 0 && exists != null) return await ExecuteScalarAsync(key, (c, k) => c.Value.SetAsync(k, redisValule, expireSeconds, exists)) == "OK";
            return false;
        }
        async public Task<bool> SetAsync(string key, object value, TimeSpan expire, RedisExistence? exists = null)
        {
            object redisValule = this.SerializeRedisValueInternal(value);
            if (expire <= TimeSpan.Zero && exists == null) return await ExecuteScalar(key, (c, k) => c.Value.SetAsync(k, redisValule)) == "OK";
            if (expire <= TimeSpan.Zero && exists != null) return await ExecuteScalar(key, (c, k) => c.Value.SetAsync(k, redisValule, null, exists)) == "OK";
            if (expire > TimeSpan.Zero && exists == null) return await ExecuteScalar(key, (c, k) => c.Value.SetAsync(k, redisValule, expire, null)) == "OK";
            if (expire > TimeSpan.Zero && exists != null) return await ExecuteScalar(key, (c, k) => c.Value.SetAsync(k, redisValule, expire, exists)) == "OK";
            return false;
        }
        /// <summary>
        /// 对 key 所储存的字符串值，设置或清除指定偏移量上的位(bit)
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="offset">偏移量</param>
        /// <param name="value">值</param>
        /// <returns></returns>
        public Task<bool> SetBitAsync(string key, uint offset, bool value) => ExecuteScalarAsync(key, (c, k) => c.Value.SetBitAsync(k, offset, value));
        /// <summary>
        /// 只有在 key 不存在时设置 key 的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="value">值</param>
        /// <returns></returns>
        public Task<bool> SetNxAsync(string key, object value)
        {
            var args = this.SerializeRedisValueInternal(value);
            return ExecuteScalarAsync(key, (c, k) => c.Value.SetNxAsync(k, args));
        }
        /// <summary>
        /// 用 value 参数覆写给定 key 所储存的字符串值，从偏移量 offset 开始
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="offset">偏移量</param>
        /// <param name="value">值</param>
        /// <returns>被修改后的字符串长度</returns>
        public Task<long> SetRangeAsync(string key, uint offset, object value)
        {
            var args = this.SerializeRedisValueInternal(value);
            return ExecuteScalarAsync(key, (c, k) => c.Value.SetRangeAsync(k, offset, args));
        }
        /// <summary>
        /// 返回 key 所储存的字符串值的长度
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> StrLenAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.StrLenAsync(k));
        #endregion

        #region Key
        /// <summary>
        /// [redis-server 3.2.1] 修改指定key(s) 最后访问时间 若key不存在，不做操作
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> TouchAsync(params string[] key) => ExecuteNonQueryAsync(key, (c, k) => c.Value.TouchAsync(k));
        /// <summary>
        /// [redis-server 4.0.0] Delete a key, 该命令和DEL十分相似：删除指定的key(s),若key不存在则该key被跳过。但是，相比DEL会产生阻塞，该命令会在另一个线程中回收内存，因此它是非阻塞的。 这也是该命令名字的由来：仅将keys从keyspace元数据中删除，真正的删除会在后续异步操作。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> UnLinkAsync(params string[] key) => ExecuteNonQueryAsync(key, (c, k) => c.Value.UnLinkAsync(k));
        /// <summary>
        /// 用于在 key 存在时删除 key
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> DelAsync(params string[] key) => ExecuteNonQueryAsync(key, (c, k) => c.Value.DelAsync(k));
        /// <summary>
        /// 序列化给定 key ，并返回被序列化的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<byte[]> DumpAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.DumpAsync(k));
        /// <summary>
        /// 检查给定 key 是否存在
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<bool> ExistsAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.ExistsAsync(k));
        /// <summary>
		/// [redis-server 3.0] 检查给定多个 key 是否存在
		/// </summary>
		/// <param name="keys">不含prefix前辍</param>
		/// <returns></returns>
		public Task<long> ExistsAsync(string[] keys) => NodesNotSupportAsync(keys, 0, (c, k) => c.Value.ExistsAsync(k));
        /// <summary>
        /// 为给定 key 设置过期时间
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="seconds">过期秒数</param>
        /// <returns></returns>
        public Task<bool> ExpireAsync(string key, int seconds) => ExecuteScalarAsync(key, (c, k) => c.Value.ExpireAsync(k, seconds));
        /// <summary>
        /// 为给定 key 设置过期时间
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="expire">过期时间</param>
        /// <returns></returns>
        public Task<bool> ExpireAsync(string key, TimeSpan expire) => ExecuteScalarAsync(key, (c, k) => c.Value.ExpireAsync(k, expire));
        /// <summary>
        /// 为给定 key 设置过期时间
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="expire">过期时间</param>
        /// <returns></returns>
        public Task<bool> ExpireAtAsync(string key, DateTime expire) => ExecuteScalarAsync(key, (c, k) => c.Value.ExpireAtAsync(k, expire));
        /// <summary>
        /// 查找所有分区节点中符合给定模式(pattern)的 key
        /// </summary>
        /// <param name="pattern">如：runoob*</param>
        /// <returns></returns>
        async public Task<string[]> KeysAsync(string pattern)
        {
            List<string> ret = new List<string>();
            foreach (var pool in Nodes)
                ret.AddRange(await GetAndExecuteAsync(pool.Value, conn => conn.Value.KeysAsync(pattern)));
            return ret.ToArray();
        }
        /// <summary>
        /// 将当前数据库的 key 移动到给定的数据库 db 当中
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="database">数据库</param>
        /// <returns></returns>
        public Task<bool> MoveAsync(string key, int database) => ExecuteScalarAsync(key, (c, k) => c.Value.MoveAsync(k, database));
        /// <summary>
        /// 该返回给定 key 锁储存的值所使用的内部表示(representation)
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<string> ObjectEncodingAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.ObjectEncodingAsync(k));
        /// <summary>
        /// 该返回给定 key 引用所储存的值的次数。此命令主要用于除错
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<long?> ObjectRefCountAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.ObjectAsync(RedisObjectSubCommand.RefCount, k));
        /// <summary>
        /// 返回给定 key 自储存以来的空转时间(idle， 没有被读取也没有被写入)，以秒为单位
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<long?> ObjectIdleTimeAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.ObjectAsync(RedisObjectSubCommand.IdleTime, k));
        /// <summary>
        /// 移除 key 的过期时间，key 将持久保持
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<bool> PersistAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.PersistAsync(k));
        /// <summary>
        /// 为给定 key 设置过期时间（毫秒）
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="milliseconds">过期毫秒数</param>
        /// <returns></returns>
        public Task<bool> PExpireAsync(string key, int milliseconds) => ExecuteScalarAsync(key, (c, k) => c.Value.PExpireAsync(k, milliseconds));
        /// <summary>
        /// 为给定 key 设置过期时间（毫秒）
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="expire">过期时间</param>
        /// <returns></returns>
        public Task<bool> PExpireAsync(string key, TimeSpan expire) => ExecuteScalarAsync(key, (c, k) => c.Value.PExpireAsync(k, expire));
        /// <summary>
        /// 为给定 key 设置过期时间（毫秒）
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="expire">过期时间</param>
        /// <returns></returns>
        public Task<bool> PExpireAtAsync(string key, DateTime expire) => ExecuteScalarAsync(key, (c, k) => c.Value.PExpireAtAsync(k, expire));
        /// <summary>
        /// 以毫秒为单位返回 key 的剩余的过期时间
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> PTtlAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.PTtlAsync(k));
        /// <summary>
        /// 从所有节点中随机返回一个 key
        /// </summary>
        /// <returns>返回的 key 如果包含 prefix前辍，则会去除后返回</returns>
        public Task<string> RandomKeyAsync() => GetAndExecuteAsync(Nodes[NodesIndex[_rnd.Next(0, NodesIndex.Count)]], async c =>
        {
            var rk = await c.Value.RandomKeyAsync();
            var prefix = (c.Pool as RedisClientPool).Prefix;
            if (string.IsNullOrEmpty(prefix) == false && rk.StartsWith(prefix)) return rk.Substring(prefix.Length);
            return rk;
        });
        /// <summary>
        /// 修改 key 的名称
        /// </summary>
        /// <param name="key">旧名称，不含prefix前辍</param>
        /// <param name="newKey">新名称，不含prefix前辍</param>
        /// <returns></returns>
        async public Task<bool> RenameAsync(string key, string newKey)
        {
            string rule = string.Empty;
            if (Nodes.Count > 1)
            {
                var rule1 = NodeRuleRaw(key);
                var rule2 = NodeRuleRaw(newKey);
                if (rule1 != rule2)
                {
                    var ret = StartPipe(a => a.Dump(key).Del(key));
                    int.TryParse(ret[1]?.ToString(), out var tryint);
                    if (ret[0] == null || tryint <= 0) return false;
                    return await RestoreAsync(newKey, (byte[])ret[0]);
                }
                rule = rule1;
            }
            var pool = Nodes.TryGetValue(rule, out var b) ? b : Nodes.First().Value;
            var key1 = string.Concat(pool.Prefix, key);
            var key2 = string.Concat(pool.Prefix, newKey);
            return await GetAndExecuteAsync(pool, conn => conn.Value.RenameAsync(key1, key2)) == "OK";
        }
        /// <summary>
        /// 修改 key 的名称
        /// </summary>
        /// <param name="key">旧名称，不含prefix前辍</param>
        /// <param name="newKey">新名称，不含prefix前辍</param>
        /// <returns></returns>
        public Task<bool> RenameNxAsync(string key, string newKey) => NodesNotSupportAsync(new[] { key, newKey }, false, (c, k) => c.Value.RenameNxAsync(k.First(), k.Last()));
        /// <summary>
        /// 反序列化给定的序列化值，并将它和给定的 key 关联
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="serializedValue">序列化值</param>
        /// <returns></returns>
        public Task<bool> RestoreAsync(string key, byte[] serializedValue) => ExecuteScalarAsync(key, async (c, k) => await c.Value.RestoreAsync(k, 0, serializedValue) == "OK");
        /// <summary>
        /// 反序列化给定的序列化值，并将它和给定的 key 关联
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="ttlMilliseconds">毫秒为单位为 key 设置生存时间</param>
        /// <param name="serializedValue">序列化值</param>
        /// <returns></returns>
        public Task<bool> RestoreAsync(string key, long ttlMilliseconds, byte[] serializedValue) => ExecuteScalarAsync(key, async (c, k) => await c.Value.RestoreAsync(k, ttlMilliseconds, serializedValue) == "OK");
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
        public Task<string[]> SortAsync(string key, long? count = null, long offset = 0, string by = null, RedisSortDir? dir = null, bool? isAlpha = null, params string[] get) =>
            NodesNotSupportAsync(key, (c, k) => c.Value.SortAsync(k, offset, count, by, dir, isAlpha, get));
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
        public Task<long> SortAndStoreAsync(string key, string destination, long? count = null, long offset = 0, string by = null, RedisSortDir? dir = null, bool? isAlpha = null, params string[] get) =>
            NodesNotSupportAsync(key, (c, k) => c.Value.SortAndStoreAsync(k, (c.Pool as RedisClientPool)?.Prefix + destination, offset, count, by, dir, isAlpha, get));
        /// <summary>
        /// 以秒为单位，返回给定 key 的剩余生存时间
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Task<long> TtlAsync(string key) => ExecuteScalarAsync(key, (c, k) => c.Value.TtlAsync(k));
        /// <summary>
        /// 返回 key 所储存的值的类型
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        async public Task<KeyType> TypeAsync(string key) => Enum.TryParse(await ExecuteScalarAsync(key, (c, k) => c.Value.TypeAsync(k)), true, out KeyType tryenum) ? tryenum : KeyType.None;
        /// <summary>
        /// 迭代当前数据库中的数据库键
        /// </summary>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public Task<RedisScan<string>> ScanAsync(long cursor, string pattern = null, long? count = null) => NodesNotSupportAsync("ScanAsync", (c, k) => c.Value.ScanAsync(cursor, pattern, count));
        /// <summary>
        /// 迭代当前数据库中的数据库键
        /// </summary>
        /// <typeparam name="T">byte[] 或其他类型</typeparam>
        /// <param name="cursor">位置</param>
        /// <param name="pattern">模式</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        async public Task<RedisScan<T>> ScanAsync<T>(long cursor, string pattern = null, long? count = null)
        {
            var scan = await NodesNotSupportAsync("ScanAsync<T>", (c, k) => c.Value.ScanBytesAsync(cursor, pattern, count));
            return new RedisScan<T>(scan.Cursor, this.DeserializeRedisValueArrayInternal<T>(scan.Items));
        }
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
        async public Task<bool> GeoAddAsync(string key, decimal longitude, decimal latitude, object member) => await GeoAddAsync(key, (longitude, latitude, member)) == 1;
        /// <summary>
        /// 将指定的地理空间位置（纬度、经度、成员）添加到指定的key中。这些数据将会存储到sorted set这样的目的是为了方便使用GEORADIUS或者GEORADIUSBYMEMBER命令对数据进行半径查询等操作。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="values">批量添加的值</param>
        /// <returns>添加到sorted set元素的数目，但不包括已更新score的元素。</returns>
        async public Task<long> GeoAddAsync(string key, params (decimal longitude, decimal latitude, object member)[] values)
        {
            if (values == null || values.Any() == false) return 0;
            var args = values.Select(z => (z.longitude, z.latitude, this.SerializeRedisValueInternal(z.member))).ToArray();
            return await ExecuteScalarAsync(key, (c, k) => c.Value.GeoAddAsync(k, args));
        }
        /// <summary>
        /// 返回两个给定位置之间的距离。如果两个位置之间的其中一个不存在， 那么命令返回空值。GEODIST 命令在计算距离时会假设地球为完美的球形， 在极限情况下， 这一假设最大会造成 0.5% 的误差。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member1">成员1</param>
        /// <param name="member2">成员2</param>
        /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
        /// <returns>计算出的距离会以双精度浮点数的形式被返回。 如果给定的位置元素不存在， 那么命令返回空值。</returns>
        public Task<decimal?> GeoDistAsync(string key, object member1, object member2, GeoUnit unit = GeoUnit.m)
        {
            var args1 = this.SerializeRedisValueInternal(member1);
            var args2 = this.SerializeRedisValueInternal(member2);
            return ExecuteScalarAsync(key, (c, k) => c.Value.GeoDistAsync(k, args1, args2, unit));
        }
        /// <summary>
        /// 返回一个或多个位置元素的 Geohash 表示。通常使用表示位置的元素使用不同的技术，使用Geohash位置52点整数编码。由于编码和解码过程中所使用的初始最小和最大坐标不同，编码的编码也不同于标准。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="members">多个查询的成员</param>
        /// <returns>一个数组， 数组的每个项都是一个 geohash 。 命令返回的 geohash 的位置与用户给定的位置元素的位置一一对应。</returns>
        async public Task<string[]> GeoHashAsync(string key, object[] members)
        {
            if (members == null || members.Any() == false) return new string[0];
            var args = members.Select(z => this.SerializeRedisValueInternal(z)).ToArray();
            return await ExecuteScalarAsync(key, (c, k) => c.Value.GeoHashAsync(k, args));
        }
        /// <summary>
        /// 从key里返回所有给定位置元素的位置（经度和纬度）。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="members">多个查询的成员</param>
        /// <returns>GEOPOS 命令返回一个数组， 数组中的每个项都由两个元素组成： 第一个元素为给定位置元素的经度， 而第二个元素则为给定位置元素的纬度。当给定的位置元素不存在时， 对应的数组项为空值。</returns>
        async public Task<(decimal longitude, decimal latitude)?[]> GeoPosAsync(string key, object[] members)
        {
            if (members == null || members.Any() == false) return new (decimal, decimal)?[0];
            var args = members.Select(z => this.SerializeRedisValueInternal(z)).ToArray();
            return await ExecuteScalarAsync(key, (c, k) => c.Value.GeoPosAsync(k, args));
        }

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
        async public Task<string[]> GeoRadiusAsync(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusAsync(k, longitude, latitude, radius, unit, count, sorting, false, false, false))).Select(a => a.member).ToArray();
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
        async public Task<T[]> GeoRadiusAsync<T>(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusBytesAsync(k, longitude, latitude, radius, unit, count, sorting, false, false, false))).Select(a => this.DeserializeRedisValueInternal<T>(a.member)).ToArray();

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
        async public Task<(string member, decimal dist)[]> GeoRadiusWithDistAsync(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusAsync(k, longitude, latitude, radius, unit, count, sorting, false, true, false))).Select(a => (a.member, a.dist)).ToArray();
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
        async public Task<(T member, decimal dist)[]> GeoRadiusWithDistAsync<T>(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusBytesAsync(k, longitude, latitude, radius, unit, count, sorting, false, true, false))).Select(a => (this.DeserializeRedisValueInternal<T>(a.member), a.dist)).ToArray();

        /// <summary>
        /// 以给定的经纬度为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含经度、纬度）。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="longitude">经度</param>
        /// <param name="latitude">纬度</param>
        /// <param name="radius">距离</param>
        /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
        /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
        /// <param name="sorting">排序</param>
        /// <returns></returns>
        async private Task<(string member, decimal longitude, decimal latitude)[]> GeoRadiusWithCoordAsync(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusAsync(k, longitude, latitude, radius, unit, count, sorting, true, false, false))).Select(a => (a.member, a.longitude, a.latitude)).ToArray();
        /// <summary>
        /// 以给定的经纬度为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含经度、纬度）。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="longitude">经度</param>
        /// <param name="latitude">纬度</param>
        /// <param name="radius">距离</param>
        /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
        /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
        /// <param name="sorting">排序</param>
        /// <returns></returns>
        async private Task<(T member, decimal longitude, decimal latitude)[]> GeoRadiusWithCoordAsync<T>(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusBytesAsync(k, longitude, latitude, radius, unit, count, sorting, true, false, false))).Select(a => (this.DeserializeRedisValueInternal<T>(a.member), a.longitude, a.latitude)).ToArray();

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
        async public Task<(string member, decimal dist, decimal longitude, decimal latitude)[]> GeoRadiusWithDistAndCoordAsync(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusAsync(k, longitude, latitude, radius, unit, count, sorting, true, true, false))).Select(a => (a.member, a.dist, a.longitude, a.latitude)).ToArray();
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
        async public Task<(T member, decimal dist, decimal longitude, decimal latitude)[]> GeoRadiusWithDistAndCoordAsync<T>(string key, decimal longitude, decimal latitude, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusBytesAsync(k, longitude, latitude, radius, unit, count, sorting, true, true, false))).Select(a => (this.DeserializeRedisValueInternal<T>(a.member), a.dist, a.longitude, a.latitude)).ToArray();

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
        async public Task<string[]> GeoRadiusByMemberAsync(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusByMemberAsync(k, member, radius, unit, count, sorting, false, false, false))).Select(a => a.member).ToArray();
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
        async public Task<T[]> GeoRadiusByMemberAsync<T>(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusBytesByMemberAsync(k, member, radius, unit, count, sorting, false, false, false))).Select(a => this.DeserializeRedisValueInternal<T>(a.member)).ToArray();

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
        async public Task<(string member, decimal dist)[]> GeoRadiusByMemberWithDistAsync(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusByMemberAsync(k, member, radius, unit, count, sorting, false, true, false))).Select(a => (a.member, a.dist)).ToArray();
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
        async public Task<(T member, decimal dist)[]> GeoRadiusByMemberWithDistAsync<T>(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusBytesByMemberAsync(k, member, radius, unit, count, sorting, false, true, false))).Select(a => (this.DeserializeRedisValueInternal<T>(a.member), a.dist)).ToArray();

        /// <summary>
        /// 以给定的成员为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含经度、纬度）。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member">成员</param>
        /// <param name="radius">距离</param>
        /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
        /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
        /// <param name="sorting">排序</param>
        /// <returns></returns>
        async private Task<(string member, decimal longitude, decimal latitude)[]> GeoRadiusByMemberWithCoordAsync(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusByMemberAsync(k, member, radius, unit, count, sorting, true, false, false))).Select(a => (a.member, a.longitude, a.latitude)).ToArray();
        /// <summary>
        /// 以给定的成员为中心， 返回键包含的位置元素当中， 与中心的距离不超过给定最大距离的所有位置元素（包含经度、纬度）。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="member">成员</param>
        /// <param name="radius">距离</param>
        /// <param name="unit">m 表示单位为米；km 表示单位为千米；mi 表示单位为英里；ft 表示单位为英尺；</param>
        /// <param name="count">虽然用户可以使用 COUNT 选项去获取前 N 个匹配元素， 但是因为命令在内部可能会需要对所有被匹配的元素进行处理， 所以在对一个非常大的区域进行搜索时， 即使只使用 COUNT 选项去获取少量元素， 命令的执行速度也可能会非常慢。 但是从另一方面来说， 使用 COUNT 选项去减少需要返回的元素数量， 对于减少带宽来说仍然是非常有用的。</param>
        /// <param name="sorting">排序</param>
        /// <returns></returns>
        async private Task<(T member, decimal longitude, decimal latitude)[]> GeoRadiusByMemberWithCoordAsync<T>(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusBytesByMemberAsync(k, member, radius, unit, count, sorting, true, false, false))).Select(a => (this.DeserializeRedisValueInternal<T>(a.member), a.longitude, a.latitude)).ToArray();

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
        async public Task<(string member, decimal dist, decimal longitude, decimal latitude)[]> GeoRadiusByMemberWithDistAndCoordAsync(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusByMemberAsync(k, member, radius, unit, count, sorting, true, true, false))).Select(a => (a.member, a.dist, a.longitude, a.latitude)).ToArray();
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
        async public Task<(T member, decimal dist, decimal longitude, decimal latitude)[]> GeoRadiusByMemberWithDistAndCoordAsync<T>(string key, object member, decimal radius, GeoUnit unit = GeoUnit.m, long? count = null, GeoOrderBy? sorting = null) =>
            (await ExecuteScalarAsync(key, (c, k) => c.Value.GeoRadiusBytesByMemberAsync(k, member, radius, unit, count, sorting, true, true, false))).Select(a => (this.DeserializeRedisValueInternal<T>(a.member), a.dist, a.longitude, a.latitude)).ToArray();
        #endregion
    }
}
#endif