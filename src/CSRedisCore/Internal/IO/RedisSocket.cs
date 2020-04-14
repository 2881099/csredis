using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CSRedis.Internal.IO
{
    class RedisSocketException : Exception
    {
        public RedisSocketException(string message)
            : base(message)
        {
        }
    }

    class RedisSocket : IRedisSocket
    {
        readonly bool _ssl;
        internal Socket _socket;
        EndPoint _remote;

        public bool Connected { get { return _socket == null ? false : _socket.Connected; } }

        public int ReceiveTimeout
        {
            get { return _socket.ReceiveTimeout; }
            set { _socket.ReceiveTimeout = value; }
        }

        public int SendTimeout
        {
            get { return _socket.SendTimeout; }
            set { _socket.SendTimeout = value; }
        }

        public RedisSocket(bool ssl)
        {
            _ssl = ssl;
        }

        public void Connect(EndPoint endpoint, int timeout)
        {
            InitSocket(endpoint);

            IAsyncResult result = _socket.BeginConnect(endpoint, null, null);
            if (!result.AsyncWaitHandle.WaitOne(timeout, true))
                throw new RedisSocketException("Connect to server timeout");
        }

#if net40
#else
        TaskCompletionSource<bool> connectTcs;
        public Task<bool> ConnectAsync(EndPoint endpoint)
        {
            InitSocket(endpoint);

            if (connectTcs != null) connectTcs.TrySetCanceled();
            connectTcs = new TaskCompletionSource<bool>();

            _socket.BeginConnect(endpoint, asyncResult =>
            {
                try
                {
                    _socket.EndConnect(asyncResult);
                    connectTcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    connectTcs.TrySetException(ex);
                }
            }, null);
            return connectTcs.Task;
        }
#endif

        public Stream GetStream()
        {
            Stream netStream = new NetworkStream(_socket, true);

            if (!_ssl) return netStream;

            var sslStream = new SslStream(netStream, true);
#if net40
            sslStream.AuthenticateAsClient(GetHostForAuthentication());
#else
            sslStream.AuthenticateAsClientAsync(GetHostForAuthentication()).Wait();
#endif
            return sslStream;
        }

        bool isDisposed = false;
        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket.Close(); } catch { }
            try { _socket.Dispose(); } catch { }
        }

        void InitSocket(EndPoint endpoint)
        {
            if (_socket != null)
            {
                try { _socket.Shutdown(SocketShutdown.Both); } catch { }
                try { _socket.Close(); } catch { }
                try { _socket.Dispose(); } catch { }
            }

            isDisposed = false;
            _socket = endpoint.AddressFamily == AddressFamily.InterNetworkV6 ?
                new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp) :
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _remote = endpoint;
        }

        string GetHostForAuthentication()
        {
            if (_remote == null)
                throw new ArgumentNullException("Remote endpoint is not set");
            else if (_remote is DnsEndPoint)
                return (_remote as DnsEndPoint).Host;
            else if (_remote is IPEndPoint)
                return (_remote as IPEndPoint).Address.ToString();

            throw new InvalidOperationException("Cannot get remote host");
        }
    }
}
