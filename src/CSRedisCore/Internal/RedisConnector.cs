using CSRedis.Internal.Diagnostics;
using CSRedis.Internal.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        readonly RedisIO _io;

#if net40
#else
        private static readonly DiagnosticListener _diagnosticListener = new DiagnosticListener(CSRedisDiagnosticListenerExtensions.DiagnosticListenerName);
#endif

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

#if !net40
            var operationId = Guid.Empty;
            var key = command.Arguments.Length > 0 ? command.Arguments[0].ToString() : "";
#endif

            try
            {
                if (IsPipelined)
                    return _io.Pipeline.Write(command);

                //if (_autoPipeline.IsEnabled)
                //	return _autoPipeline.EnqueueSync(command);

                //Console.WriteLine("--------------Call " + command.ToString());
#if !net40
                operationId = _diagnosticListener.WriteCallBefore(new CallEventData(command.Command, key));
#endif
                _io.Write(_io.Writer.Prepare(command));
                var res = command.Parse(_io.Reader);
#if !net40
                _diagnosticListener.WriteCallAfter(operationId, new CallEventData(command.Command, key));
#endif
                return res;
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
#if !net40
                _diagnosticListener.WriteCallError(operationId, new CallEventData(command.Command, key), ex);
#endif
                throw new RedisException($"{ex.Message}\r\nCommand: {command}", ex);
            }
        }

#if net40
#else
        async public Task<T> CallAsync<T>(RedisCommand<T> command)
        {
            var operationId = Guid.Empty;
            var key = command.Arguments.Length > 0 ? command.Arguments[0].ToString() : "";

            try
            {
                operationId = _diagnosticListener.WriteCallBefore(new CallEventData(command.Command, key));

                await _io.WriteAsync(command);
                var res = command.Parse(_io.Reader);

                _diagnosticListener.WriteCallAfter(operationId, new CallEventData(command.Command, key));

                return res;
            }
            catch (Exception ex)
            {
                _diagnosticListener.WriteCallError(operationId, new CallEventData(command.Command, key), ex);
                throw;
            }
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
