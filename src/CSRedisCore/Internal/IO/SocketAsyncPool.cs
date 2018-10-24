using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CSRedis.Internal.IO
{
    class SocketAsyncPool : IDisposable
    {
        readonly byte[] _buffer;
        readonly ConcurrentStack<SocketAsyncEventArgs> _pool;
        readonly int _bufferSize;
        readonly Semaphore _acquisitionGate;

        public event EventHandler<SocketAsyncEventArgs> Completed;

        public SocketAsyncPool(int concurrency, int bufferSize)
        {
			_pool = new ConcurrentStack<SocketAsyncEventArgs>();
			_bufferSize = bufferSize;
			_buffer = new byte[concurrency * bufferSize];
            _acquisitionGate = new Semaphore(concurrency, concurrency);
            for (int i = 0; i < concurrency; i++)
            {
                SocketAsyncEventArgs args = new SocketAsyncEventArgs();
				args.Completed += OnSocketCompleted;
				args.SetBuffer(_buffer, i * _bufferSize, _bufferSize);

				_pool.Push(args);
            }
        }

        public SocketAsyncEventArgs Acquire()
        {
            if (!_acquisitionGate.WaitOne())
                throw new Exception();

			return _pool.TryPop(out var result) ? result : null;
        }

        public void Release(SocketAsyncEventArgs args)
        {
			if (args.Buffer.Equals(_buffer))
				_pool.Push(args);
			else
				args.Dispose();		
			_acquisitionGate.Release();
        }

		public void Dispose() {
			Array.Clear(_buffer, 0, _buffer.Length);
			GC.SuppressFinalize(_buffer);
			while (_pool.Any())
				if (_pool.TryPop(out var p))
					p.Dispose();

			try { _acquisitionGate.Release(); } catch { }
		}

        void OnSocketCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (Completed != null)
                Completed(sender, e);
        }
    }
}
