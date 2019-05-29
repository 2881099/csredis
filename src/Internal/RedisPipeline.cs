using CSRedis.Internal.IO;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
        readonly ConcurrentQueue<Func<object>> _parsers;

        public bool Active { get; private set; }

        internal RedisPipeline(RedisIO io)
        {
            _reader = io.Reader;
            _destination = io.Stream;
            _buffer = new MemoryStream();
            _writer = new RedisWriter(io);
            _parsers = new ConcurrentQueue<Func<object>>();
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
			try {
				_buffer.Position = 0;
				_buffer.CopyTo(_destination);

				object[] results = new object[_parsers.Count];
				for (int i = 0; i < results.Length; i++)
					if (_parsers.TryDequeue(out var func)) results[i] = func();

				return results;
			} finally {
				_buffer.SetLength(0);
				Active = false;
			}
        }

        public void Dispose()
        {
            _buffer.Dispose();

		}
    }
}
