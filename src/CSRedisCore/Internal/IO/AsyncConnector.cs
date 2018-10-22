using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CSRedis.Internal.IO
{
    class AsyncConnector : IDisposable
    {
        readonly SocketAsyncEventArgs _asyncConnectArgs;
        readonly SocketAsyncPool _asyncTransferPool;
        readonly ConcurrentQueue<IRedisAsyncCommandToken> _asyncReadQueue;
        readonly ConcurrentQueue<IRedisAsyncCommandToken> _asyncWriteQueue;
        readonly object _readLock;
        readonly object _writeLock;
        readonly int _concurrency;
        readonly int _bufferSize;
        readonly IRedisSocket _redisSocket;
        readonly RedisIO _io;

        bool _asyncConnectionStarted;
		readonly ConcurrentQueue<TaskCompletionSource<bool>> _connectionTaskSource;

        public event EventHandler Connected;


        public AsyncConnector(IRedisSocket socket, EndPoint endpoint, RedisIO io, int concurrency, int bufferSize)
        {
            _redisSocket = socket;
            _io = io;
            _concurrency = concurrency;
            _bufferSize = bufferSize;
            _asyncTransferPool = new SocketAsyncPool(concurrency, bufferSize);
            _asyncTransferPool.Completed += OnSocketCompleted;
            _asyncReadQueue = new ConcurrentQueue<IRedisAsyncCommandToken>();
            _asyncWriteQueue = new ConcurrentQueue<IRedisAsyncCommandToken>();
            _readLock = new object();
            _writeLock = new object();
            _asyncConnectArgs = new SocketAsyncEventArgs { RemoteEndPoint = endpoint };
            _asyncConnectArgs.Completed += OnSocketCompleted;
			_connectionTaskSource = new ConcurrentQueue<TaskCompletionSource<bool>>();
        }

		void SetConnectionTaskSourceResult(bool value, Exception exception, bool isCancel) {
			while(_connectionTaskSource.TryDequeue(out var tcs)) {
				if (isCancel) tcs.TrySetCanceled();
				else if (exception != null) tcs.TrySetException(exception);
				else tcs.TrySetResult(value);
			}
		}


		public Task<bool> ConnectAsync() {
			if (_redisSocket.Connected) {
				this.SetConnectionTaskSourceResult(true, null, false);
				return Task.FromResult(true);
			}

			var tcs = new TaskCompletionSource<bool>();
			_connectionTaskSource.Enqueue(tcs);

			if (!_asyncConnectionStarted && !_redisSocket.Connected) {
				lock (_asyncConnectArgs) {
					if (!_asyncConnectionStarted && !_redisSocket.Connected) {
						_asyncConnectionStarted = true;
						if (!_redisSocket.ConnectAsync(_asyncConnectArgs)) {
							OnSocketConnected(_asyncConnectArgs);
							this.SetConnectionTaskSourceResult(false, null, false);
						}
					}
				}
			}

			return tcs.Task;
		}

        public Task<T> CallAsync<T>(RedisCommand<T> command)
        {
			var token = new RedisAsyncCommandToken<T>(command);
            _asyncWriteQueue.Enqueue(token);
			ConnectAsync().ContinueWith(CallAsyncDeferred);
            return token.TaskSource.Task;
        }

        void CallAsyncDeferred(Task t)
        {
            lock (_writeLock)
            {
                IRedisAsyncCommandToken token;
				if (!_asyncWriteQueue.TryDequeue(out token))
					throw new Exception();

				_asyncReadQueue.Enqueue(token);

				var args = _asyncTransferPool.Acquire();
				int bytes;
				try
				{
					bytes = _io.Writer.Write(token.Command, args.Buffer, args.Offset);
				}
				catch (ArgumentException e)
				{
					OnSocketSent(args, e);
					throw new RedisClientException("Could not write command '" + token.Command.Command + "'. Argument size exceeds buffer allocation of " + args.Count + ".", e);
				}
				catch (Exception e)
				{
					OnSocketSent(args, e);
					throw e;
				}
                args.SetBuffer(args.Offset, bytes);

				if (!_redisSocket.SendAsync(args))
					OnSocketSent(args);
            }
        }

        void OnSocketCompleted(object sender, SocketAsyncEventArgs e)
        {
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Connect:
					try {
						OnSocketConnected(e);
					} catch (Exception socketConnectedException) {
						this.SetConnectionTaskSourceResult(false, socketConnectedException, false);
					}
                    break;
                case SocketAsyncOperation.Send:
                    OnSocketSent(e);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        void OnSocketConnected(SocketAsyncEventArgs args)
        {
            if (Connected != null)
                Connected(this, new EventArgs());

			this.SetConnectionTaskSourceResult(_redisSocket.Connected, null, false);
			_asyncConnectionStarted = false;
		}

        void OnSocketSent(SocketAsyncEventArgs args, Exception ex = null)
        {
            _asyncTransferPool.Release(args);

            IRedisAsyncCommandToken token;
            lock (_readLock)
            {
                if (_asyncReadQueue.TryDequeue(out token))
                {
                    try
                    {
						if (ex != null)
							token.SetException(ex);
                        else
							token.SetResult(_io.Reader);
                    }
                    /*catch (IOException) // TODO implement async retry
                    {
                        if (ReconnectAttempts == 0)
                            throw;
                        Reconnect();
                        _asyncWriteQueue.Enqueue(token);
                        ConnectAsync().ContinueWith(CallAsyncDeferred);
                    }*/
                    catch (Exception e)
                    {
                        token.SetException(e);
                    }
                }
            }
        }

        public void Dispose()
        {
			while (_asyncReadQueue.TryDequeue(out var token))
				try { token.SetException(new Exception("Error: Disposing...")); } catch { }

			while (_asyncWriteQueue.TryDequeue(out var token))
				try { token.SetException(new Exception("Error: Disposing...")); } catch { }

			this.SetConnectionTaskSourceResult(false, null, true);

            _asyncTransferPool.Dispose();
            _asyncConnectArgs.Dispose();
        }
    }
}
