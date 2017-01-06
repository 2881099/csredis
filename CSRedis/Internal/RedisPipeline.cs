using CSRedis.Internal.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CSRedis.Internal
{
    class RedisPipeline : IDisposable
    {
        readonly Stream _buffer;
        readonly Stream _destination;
        readonly RedisWriter _writer;
        readonly RedisReader _reader;
        readonly Queue<Func<object>> _parsers;

        public bool Active { get; private set; }

        internal RedisPipeline(RedisIO io)
        {
            _reader = io.Reader;
            _destination = io.Stream;
            _buffer = new MemoryStream();
            _writer = new RedisWriter(io);
            _parsers = new Queue<Func<object>>();
        }

        public T Write<T>(RedisCommand<T> command)
        {
            _writer.Write(command, _buffer);
            _parsers.Enqueue(() => command.Parse(_reader));
            return default(T);
        }

        public void Begin()
        {
            Active = true;
        }

        public object[] Flush()
        {
            _buffer.Position = 0;
            _buffer.CopyTo(_destination);

            object[] results = new object[_parsers.Count];
            for (int i = 0; i < results.Length; i++)
                results[i] = _parsers.Dequeue()();
            _buffer.SetLength(0);
            Active = false;
            return results;
        }

        public void Dispose()
        {
            _buffer.Dispose();
        }
    }
}
