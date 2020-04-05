using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CSRedis.Internal.IO
{
    class RedisIO : IDisposable
    {
        readonly RedisWriter _writer;
        RedisReader _reader;
        RedisPipeline _pipeline;
        BufferedStream _stream;
        object _streamLock = new object();
        NetworkStream _networkStream;

        public BufferedStream Stream { get { return GetOrThrow(_stream); } }
        public RedisWriter Writer { get { return _writer; } }
        public RedisReader Reader { get { return GetOrThrow(_reader); } }
        public Encoding Encoding { get; set; }
        public RedisPipeline Pipeline { get { return GetOrThrow(_pipeline); } }
        public bool IsPipelined { get { return _pipeline == null ? false : _pipeline.Active; } }

        public RedisIO()
        {
            _writer = new RedisWriter(this);
            Encoding = new UTF8Encoding(false);
        }

        public void SetStream(Stream stream)
        {
            if (_stream != null)
                _stream.Dispose();

            _networkStream = stream as NetworkStream;
            _stream = new BufferedStream(stream);
            _reader = new RedisReader(this);
            _pipeline = new RedisPipeline(this);
        }


#if net40
#else
        public Task<int> WriteAsync(RedisCommand command)
        {
            var data = _writer.Prepare(command);

            var tcs = new TaskCompletionSource<int>();
            lock (_streamLock)
            {
                Stream.BeginWrite(data, 0, data.Length, asyncResult =>
                {
                    try
                    {
                        _stream.EndWrite(asyncResult);
                        tcs.TrySetResult(data.Length);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }, null);
            }
            return tcs.Task;
        }
#endif

        public void Write(byte[] data)
        {
            lock (_streamLock)
                Stream.Write(data, 0, data.Length);
        }
        public void Write(Stream stream)
        {
            lock (_streamLock)
                stream.CopyTo(Stream);
        }
        public int ReadByte()
        {
            lock (_streamLock)
                return Stream.ReadByte();
        }
        public int Read(byte[] data, int offset, int count)
        {
            lock (_streamLock)
                return Stream.Read(data, offset, count);
        }
        public Byte[] ReadAll()
        {
            if (_networkStream == null) return new byte[0];
            using (var ms = new MemoryStream())
            {
                try
                {
                    var data = new byte[1024];
                    while (_networkStream.DataAvailable && _networkStream.CanRead)
                    {
                        int numBytesRead = 0;
                        lock (_streamLock)
                            numBytesRead = _networkStream.Read(data, 0, data.Length);
                        if (numBytesRead <= 0) break;
                        ms.Write(data, 0, numBytesRead);
                    }
                }
                catch { }
                return ms.ToArray();
            }
        }

        public void Dispose()
        {
            if (_pipeline != null)
                _pipeline.Dispose();
            if (_stream != null)
            {
                try { _stream.Close(); } catch { }
                _stream.Dispose();
            }
        }

        static T GetOrThrow<T>(T obj)
        {
            if (obj == null)
                throw new RedisClientException("Connection was not opened");
            return obj;
        }
    }
}
