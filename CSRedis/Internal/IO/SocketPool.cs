using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CSRedis.Internal.IO
{
    class SocketPool : IDisposable
    {
        readonly EndPoint _endPoint;
        readonly ConcurrentStack<Socket> _pool;
        readonly int _max;

        public SocketPool(EndPoint endPoint, int max)
        {
            _max = max;
            _endPoint = endPoint;
            _pool = new ConcurrentStack<Socket>();
        }

        public Socket Connect()
        {
            Socket socket = Acquire();
            if (!socket.Connected)
                socket.Connect(_endPoint);
            return socket;
        }

        public bool ConnectAsync(SocketAsyncEventArgs connectArgs, out Socket socket)
        {
            socket = Acquire();
            if (socket.Connected)
                return false;
            return socket.ConnectAsync(connectArgs);
        }

        public void Release(Socket socket)
        {
            _pool.Push(socket);
        }

        public void Dispose()
        {
            foreach (var socket in _pool)
            {
                System.Diagnostics.Debug.WriteLine("Disposing socket #{0}", socket.LocalEndPoint);
                socket.Dispose();
            }
        }

        Socket Acquire()
        {
            Socket socket;
            if (!_pool.TryPop(out socket))
            {
                Add();
                return Acquire();
            }
            else if (socket.IsBound && !socket.Connected)
            {
                socket.Dispose();
                return Acquire();
            }
            else if (socket.Poll(1000, SelectMode.SelectRead))
            {
                socket.Dispose();
                return Acquire();
            }
            return socket;
        }

        void Add()
        {
            if (_pool.Count > _max)
                throw new InvalidOperationException("Maximum sockets");
            _pool.Push(SocketFactory());
        }

        Socket SocketFactory()
        {
            System.Diagnostics.Debug.WriteLine("NEW SOCKET");
            return new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }
    }
}
