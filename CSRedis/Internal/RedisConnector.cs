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
        readonly Lazy<AsyncConnector> _asyncConnector;
        readonly IRedisSocket _redisSocket;
        readonly EndPoint _endPoint;
        readonly RedisIO _io;

        public event EventHandler Connected;

        public AsyncConnector Async { get { return _asyncConnector.Value; } }
        public bool IsConnected { get { return _redisSocket.Connected; } }
        public EndPoint EndPoint { get { return _endPoint; } }
        public bool IsPipelined { get { return _io.IsPipelined; } }
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
            _asyncConnector = new Lazy<AsyncConnector>(AsyncConnectorFactory);
        }

        public bool Connect()
        {
            _redisSocket.Connect(_endPoint);

            if (_redisSocket.Connected)
                OnConnected();

            return _redisSocket.Connected;
        }

        public Task<bool> ConnectAsync()
        {
            return Async.ConnectAsync();
        }

        public T Call<T>(RedisCommand<T> command)
        {
            ConnectIfNotConnected();

            try
            {
                if (IsPipelined)
                    return _io.Pipeline.Write(command);

                _io.Writer.Write(command, _io.Stream);
                return command.Parse(_io.Reader);
            }
            catch (IOException)
            {
                if (ReconnectAttempts == 0)
                    throw;
                Reconnect();
                return Call(command);
            }
        }

        public Task<T> CallAsync<T>(RedisCommand<T> command)
        {
            return Async.CallAsync(command);
        }

        public void Write(RedisCommand command)
        {
            ConnectIfNotConnected();

            try
            {
                _io.Writer.Write(command, _io.Stream);
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
            if (_asyncConnector.IsValueCreated)
                _asyncConnector.Value.Dispose();

            _io.Dispose();

            if (_redisSocket != null)
                _redisSocket.Dispose();

        }

        void Reconnect()
        {
            int attempts = 0;
            while (attempts++ < ReconnectAttempts || ReconnectAttempts == -1)
            {
                if (Connect())
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

        AsyncConnector AsyncConnectorFactory()
        {
            var connector = new AsyncConnector(_redisSocket, _endPoint, _io, _concurrency, _bufferSize);
            connector.Connected += OnAsyncConnected;
            return connector;
        }

        void ConnectIfNotConnected()
        {
            if (!IsConnected)
                Connect();
        }

        void ExpectConnected()
        {
            if (!IsConnected)
                throw new RedisClientException("Client is not connected");
        }
    }
}
