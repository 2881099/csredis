using CSRedis.Internal.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSRedis.Internal
{
    class RedisConnector
    {
        readonly int _concurrency;
        readonly int _bufferSize;
        internal readonly IRedisSocket _redisSocket;
        readonly EndPoint _endPoint;
        internal readonly RedisIO _io;

        public event EventHandler Connected;

        public bool IsConnected { get { return _redisSocket.Connected; } }
        public EndPoint EndPoint { get { return _endPoint; } }
        public bool IsPipelined { get { return _io.IsPipelined; } }
        public RedisPipeline Pipeline { get { return _io.Pipeline; } }
        public int ReconnectAttempts { get; set; }
        public int ReconnectWait { get; set; }
        public int ReceiveTimeout
        {
            get { return _redisSocket.ReceiveTimeout; }
            set { _redisSocket.ReceiveTimeout = value; }
        }
        public int SendTimeout
        {
            get { return _redisSocket.SendTimeout; }
            set { _redisSocket.SendTimeout = value; }
        }
        public Encoding Encoding
        {
            get { return _io.Encoding; }
            set { _io.Encoding = value; }
        }


        public RedisConnector(EndPoint endPoint, IRedisSocket socket, int concurrency, int bufferSize)
        {
            _concurrency = concurrency;
            _bufferSize = bufferSize;
            _endPoint = endPoint;
            _redisSocket = socket;
            _io = new RedisIO();
            //_autoPipeline = new AutoPipelineOption(_io);
        }

        public bool Connect(int timeout)
        {
            _redisSocket.Connect(_endPoint, timeout);

            if (_redisSocket.Connected)
                OnConnected();

            return _redisSocket.Connected;
        }

#if net40
#else
        public Task<bool> ConnectAsync()
        {
            return _redisSocket.ConnectAsync(_endPoint);
        }
#endif

        //public IAutoPipelineOption AutoPipeline => _autoPipeline;
        //AutoPipelineOption _autoPipeline;

        public T Call<T>(RedisCommand<T> command)
        {
            ConnectIfNotConnected();

            try
            {
                if (IsPipelined)
                    return _io.Pipeline.Write(command);

                //if (_autoPipeline.IsEnabled)
                //	return _autoPipeline.EnqueueSync(command);

                //Console.WriteLine("--------------Call " + command.ToString());
                _io.Write(_io.Writer.Prepare(command));
                return command.Parse(_io.Reader);
            }
            catch (IOException)
            {
                if (ReconnectAttempts == 0)
                    throw;
                Reconnect();
                return Call(command);
            }
            catch (RedisException ex)
            {
                throw new RedisException($"{ex.Message}\r\nCommand: {command}", ex);
            }
        }

        public void CallNoneRead(RedisCommand command)
        {
            ConnectIfNotConnected();

            try
            {
                //Console.WriteLine("--------------Call " + command.ToString());
                _io.Write(_io.Writer.Prepare(command));
            }
            catch (IOException)
            {
                if (ReconnectAttempts == 0)
                    throw;
                Reconnect();
                CallNoneRead(command);
            }
            catch (RedisException ex)
            {
                throw new RedisException($"{ex.Message}\r\nCommand: {command}", ex);
            }
        }

#if net40
#else
        async public Task<T> CallAsync<T>(RedisCommand<T> command)
        {
            //if (_autoPipeline.IsEnabled)
            //	return _autoPipeline.EnqueueAsync(command);

            //Console.WriteLine("--------------CallAsync");
            await _io.WriteAsync(command);
            //_io.Stream.BeginRead()
            return command.Parse(_io.Reader);
        }
#endif

        public void Write(RedisCommand command)
        {
            ConnectIfNotConnected();

            try
            {
                //Console.WriteLine("--------------Write");
                _io.Write(_io.Writer.Prepare(command));
            }
            catch (IOException)
            {
                if (ReconnectAttempts == 0)
                    throw;
                Reconnect();
                Write(command);
            }
        }

        public T Read<T>(Func<RedisReader, T> func)
        {
            ExpectConnected();

            try
            {
                return func(_io.Reader);
            }
            catch (IOException)
            {
                if (ReconnectAttempts == 0)
                    throw;
                Reconnect();
                return Read(func);
            }
        }

        public void Read(Stream destination, int bufferSize)
        {
            ExpectConnected();

            try
            {
                _io.Reader.ExpectType(RedisMessage.Bulk);
                _io.Reader.ReadBulkBytes(destination, bufferSize, false);
            }
            catch (IOException)
            {
                if (ReconnectAttempts == 0)
                    throw;
                Reconnect();
                Read(destination, bufferSize);
            }
        }

        public void BeginPipe()
        {
            ConnectIfNotConnected();
            _io.Pipeline.Begin();
        }

        public object[] EndPipe()
        {
            ExpectConnected();

            try
            {
                return _io.Pipeline.Flush();
            }
            catch (IOException)
            {
                if (ReconnectAttempts == 0)
                    throw;
                Reconnect();
                return EndPipe();
            }
        }

        public void Dispose()
        {
            _io.Dispose();

            if (_redisSocket != null)
                _redisSocket.Dispose();

        }

        void Reconnect()
        {
            int attempts = 0;
            while (attempts++ < ReconnectAttempts || ReconnectAttempts == -1)
            {
                if (Connect(-1))
                    return;

                Thread.Sleep(TimeSpan.FromMilliseconds(ReconnectWait));
            }

            throw new IOException("Could not reconnect after " + attempts + " attempts");
        }

        void OnConnected()
        {
            _io.SetStream(_redisSocket.GetStream());
            if (Connected != null)
                Connected(this, new EventArgs());
        }

        void OnAsyncConnected(object sender, EventArgs args)
        {
            OnConnected();
        }

        void ConnectIfNotConnected()
        {
            if (!IsConnected)
                Connect(-1);
        }

        void ExpectConnected()
        {
            if (!IsConnected)
                throw new RedisClientException("Client is not connected");
        }
    }
}
