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
			if (_asyncReadQueue.TryDequeue(out token))
			{
				try
				{
					if (ex != null)
						token.SetException(ex);
					else
						lock (_readLock)
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

		//void OnSocketReceive(SocketAsyncEventArgs e, MemoryStream ms = null) {
		//	if (e.SocketError == SocketError.Success) {
		//		// 检查远程主机是否关闭连接
		//		if (e.BytesTransferred > 0) {
		//			var s = (Socket)e.UserToken;

		//			if (s.Available == 0) {
		//				byte[] data = new byte[e.BytesTransferred];
		//				Array.Copy(e.Buffer, e.Offset, data, 0, data.Length);//从e.Buffer块中复制数据出来，保证它可重用

		//				Exception ex = null;
		//				object result = null;
		//				bool isended = false;
		//				try {
		//					if (ms == null) {
		//						if (data[0] == '+') {
		//							for (var a = 1; a < data.Length; a++)
		//								if (data[a] == '\r' && a < data.Length - 1 && data[a + 1] == '\n') {
		//									result = a == 1 ? "" : _io.Encoding.GetString(data, 1, a - 1);
		//									isended = true;
		//									break;
		//								}
		//						}
		//						if (data[0] == '-') {
		//							for (var a = 1; a < data.Length; a++)
		//								if (data[a] == '\r' && a < data.Length - 1 && data[a + 1] == '\n')
		//									throw new CSRedis.RedisException(a == 1 ? "" : _io.Encoding.GetString(data, 1, a - 1));
		//						}
		//						if (data[0] == ':') {
		//							for (var a = 2; a < data.Length; a++)
		//								if (data[a] == '\r' && a < data.Length - 1 && data[a + 1] == '\n') {
		//									result = long.Parse(_io.Encoding.GetString(data, 1, a - 1));
		//									isended = true;
		//									break;
		//								}
		//						}
		//						if (data[0] == '$') {
		//							long startIndex = 0;
		//							long len = -999;
		//							for (var a = 2; a < data.Length; a++)
		//								if (data[a] == '\r' && a < data.Length - 1 && data[a + 1] == '\n') {
		//									if (len > 0) {
		//										byte[] dest = new byte[len];
		//										Array.Copy(data, startIndex, dest, 0, len);
		//										result = dest;
		//										break;
		//									}
		//									len = long.Parse(_io.Encoding.GetString(data, 1, a - 1));
		//									startIndex = a + 1;
		//									if (len < 0) {
		//										result = null;
		//										isended = true;
		//										break;
		//									}
		//									if (len == 0) {
		//										result = "";
		//										isended = true;
		//										break;
		//									}
		//								}
		//						}
		//						//if (data[0] == '*') {
		//						//	long startIndex = 0;
		//						//	long len = -999;
		//						//	for (var a = 2; a < data.Length; a++)
		//						//		if (data[a] == '\r' && a < data.Length - 1 && data[a + 1] == '\n') {
		//						//			if (len > 0) {
		//						//				byte[] dest = new byte[len];
		//						//				Array.Copy(data, startIndex, dest, 0, len);
		//						//				result = dest;
		//						//				break;
		//						//			}
		//						//			len = long.Parse(_io.Encoding.GetString(data, 1, a - 1));
		//						//			startIndex = a + 1;
		//						//			if (len < 0) {
		//						//				result = null;
		//						//				isended = true;
		//						//				break;
		//						//			}
		//						//			if (len == 0) {
		//						//				result = "";
		//						//				isended = true;
		//						//				break;
		//						//			}
		//						//		}
		//						//}
		//					}
		//				} catch (Exception ex2) {
		//					ex = ex2;
		//				} finally {

		//				}
		//			}

		//			//ms.Write(data, 0, data.Length);


		//			//为接收下一段数据，投递接收请求，这个函数有可能同步完成，这时返回false，并且不会引发SocketAsyncEventArgs.Completed事件
		//			if (!s.ReceiveAsync(e)) {
		//				//同步接收时处理接收完成事件
		//				OnSocketReceive(e, ms);
		//			}
		//		}
		//	}
		//}

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
