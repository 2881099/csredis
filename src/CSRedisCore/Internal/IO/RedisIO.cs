using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CSRedis.Internal.IO
{
    class RedisIO : IDisposable
    {
        readonly RedisWriter _writer;
        RedisReader _reader;
        RedisPipeline _pipeline;
        BufferedStream _stream;

        public RedisWriter Writer { get { return _writer; } }
        public RedisReader Reader { get { return GetOrThrow(_reader); } }
        public Encoding Encoding { get; set; }
        public RedisPipeline Pipeline { get { return GetOrThrow(_pipeline); } }
        public Stream Stream { get { return GetOrThrow(_stream); } }
        public bool IsPipelined { get { return Pipeline == null ? false : Pipeline.Active; } }

        public RedisIO()
        {
            _writer = new RedisWriter(this);
            Encoding = new UTF8Encoding(false);
        }

        public void SetStream(Stream stream)
        {
            if (_stream != null)
                _stream.Dispose();

            _stream = new BufferedStream(stream);
            _reader = new RedisReader(this);
            _pipeline = new RedisPipeline(this);
        }

        public void Dispose()
        {
            if (_pipeline != null)
                _pipeline.Dispose();
            if (_stream != null)
                _stream.Dispose();
        }

        static T GetOrThrow<T>(T obj)
        {
            if (obj == null)
                throw new RedisClientException("Connection was not opened");
            return obj;
        }
    }
}
