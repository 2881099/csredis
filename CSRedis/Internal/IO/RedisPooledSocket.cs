using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CSRedis.Internal.IO
{
    class RedisPooledSocket : IRedisSocket
    {
        Socket _socket;
        readonly SocketPool _pool;

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

        public RedisPooledSocket(SocketPool pool)
        {
            _pool = pool;
        }

        public void Connect(EndPoint endpoint)
        {
            _socket = _pool.Connect();
            System.Diagnostics.Debug.WriteLine("Got socket #{0}", _socket.LocalEndPoint);
        }

        public bool ConnectAsync(SocketAsyncEventArgs args)
        {
            return _pool.ConnectAsync(args, out _socket);
        }

        public bool SendAsync(SocketAsyncEventArgs args)
        {
            return _socket.SendAsync(args);
        }

        public Stream GetStream()
        {
            return new NetworkStream(_socket);
        }

        public void Dispose()
        {
            _pool.Release(_socket);
        }
    }
}
