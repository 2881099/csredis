using CSRedis.Internal.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSRedis.Internal.IO
{
    interface IRedisAsyncCommandToken
    {
        Task Task { get; }
        RedisCommand Command { get; }
        void SetResult(RedisReader reader);
        void SetException(Exception e);
    }

    class RedisAsyncCommandToken<T> : IRedisAsyncCommandToken
    {
        readonly TaskCompletionSource<T> _tcs;
        readonly RedisCommand<T> _command;

        public TaskCompletionSource<T> TaskSource { get { return _tcs; } }
        public RedisCommand Command { get { return _command; } }
        public Task Task { get { return _tcs.Task; } }

        public RedisAsyncCommandToken(RedisCommand<T> command)
        {
            _tcs = new TaskCompletionSource<T>();
            _command = command;
        }

        public void SetResult(RedisReader reader)
        {
            _tcs.SetResult(_command.Parse(reader));
        }

        public void SetException(Exception e)
        {
            _tcs.SetException(e);
        }
    }
}
