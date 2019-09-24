using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SafeObjectPool;

namespace CSRedis
{
    public class CSRedisClientPipeTransaction<TObject> :CSRedisClientPipe<TObject>
    {
        
        internal CSRedisClientPipeTransaction(CSRedisClient csredis) : base(csredis)
        {
        }

        private CSRedisClientPipeTransaction(CSRedisClient csredis,
            ConcurrentDictionary<string, (List<int> indexes, Object<RedisClient> conn)> conns,
            Queue<Func<object, object>> parsers) : base(csredis, conns, parsers)
        {
        }

        public override object[] EndPipe()
        {
            var ret =  base.EndPipe();
            _disposed = true;
            return ret;
        }

        protected override CSRedisClientPipe<TReturn> PipeCommand<TReturn>(string key, Func<Object<RedisClient>, string, TReturn> handle, Func<object, object> parser)
        {
            if (string.IsNullOrEmpty(key)) throw new Exception("key 不可为空或null");
            var nodeKey = NodeRuleRaw == null || Nodes.Count == 1 ? Nodes.Keys.First() : NodeRuleRaw(key);
            if (Nodes.TryGetValue(nodeKey, out var pool) == false)
                Nodes.TryGetValue(nodeKey = Nodes.Keys.First(), out pool);

            try
            {
                if (Conns.TryGetValue(pool.Key, out var conn) == false)
                {
                    conn = (new List<int>(), pool.GetAsync().Result);
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
                        conn.conn.Value.StartPipeTransaction();
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

            if (typeof(TReturn) == typeof(TObject))
                return
                    this as CSRedisClientPipe<TReturn>; // return (CSRedisClientPipe<TReturn>)Convert.ChangeType(this, typeof(CSRedisClientPipe<TReturn>));
            //_disposed = true;
            return new CSRedisClientPipeTransaction<TReturn>(rds, this.Conns, this.Parsers);
        }

        public void Abort()
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var conn in Conns.Values)
            {
                try
                {
                    conn.conn.Value.Discard();
                }
                catch (Exception)
                {
                }
                (conn.conn.Pool as RedisClientPool).Return(conn.conn);
            }
            Conns.Clear();
        }

        private bool _disposed = false;
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;
            Abort();
            //base.Dispose(); //原来的有问题,先不调用了
        }
        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~CSRedisClientPipeTransaction()
        {
            Dispose(false);
        }
    }
}
