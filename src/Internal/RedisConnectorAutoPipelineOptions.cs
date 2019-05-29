using CSRedis.Internal.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSRedis {
	public interface IAutoPipelineOption {
		/// <summary>
		/// 合并的数量，当达到此设置后发送请求
		/// </summary>
		int MergeSize { get; set; }
		/// <summary>
		/// 合并等待时间（第一次发送到现在），当达到此设置后发送请求
		/// </summary>
		TimeSpan TimeWait { get; set; }
	}

	class AutoPipelineOption : IAutoPipelineOption {
		public int MergeSize { get; set; } = 100;
		public TimeSpan TimeWait { get; set; } = TimeSpan.FromMilliseconds(1);

		internal bool IsEnabled => this.MergeSize > 1 && TimeWait >= TimeSpan.FromMilliseconds(1);

		internal RedisIO _io;
		internal AutoPipelineOption(RedisIO io) {
			AppDomain.CurrentDomain.ProcessExit += (s1, e1) => {
				running = false;
			};
			Console.CancelKeyPress += (s1, e1) => {
				running = false;
			};
			_io = io;
		}

		int _asyncqs = 0;
		internal T EnqueueSync<T>(RedisCommand<T> command) {
			var qs = Interlocked.Increment(ref _asyncqs);
			var cmd = new PipelineCommandToken<T>(false, command);
			_cmdqs.Enqueue(cmd);

			if (MergeSize > 1 && qs >= MergeSize) {
				FlushAndSend();
				if (cmd.SyncException != null) throw cmd.SyncException;
				return cmd.SyncReturnValue;
			}
			if (qs == 1) {
				if (cmd.SyncWait.Wait(TimeWait)) {
					if (cmd.SyncException != null) throw cmd.SyncException;
					return cmd.SyncReturnValue;
				}
				FlushAndSend();
				if (cmd.SyncException != null) throw cmd.SyncException;
				return cmd.SyncReturnValue;
			}
			T obj;
			try {
				if (cmd.SyncWait.Wait(TimeSpan.FromSeconds(10)))
					obj = cmd.SyncReturnValue;
				else {
					cmd.SyncIsTimeout = true;
					cmd.SyncException = new TimeoutException($"AutoPipeline 同步执行超时（{command.Command + string.Join(" ", command.Arguments)}）。");
				}
			} catch { }
			
			if (cmd.SyncException != null) throw cmd.SyncException;
			return cmd.SyncReturnValue;
		}
		async internal Task<T> EnqueueAsync<T>(RedisCommand<T> command) {
			var qs = Interlocked.Increment(ref _asyncqs);
			var cmd = new PipelineCommandToken<T>(true, command);
			_cmdqs.Enqueue(cmd);

			if (MergeSize > 1 && qs >= MergeSize) {
				FlushAndSend();
				return await cmd.TaskSource.Task;
			}
			if (qs == 1) {
				await Task.Delay(TimeWait);
				FlushAndSend();
				return await cmd.TaskSource.Task;
			}
			return await cmd.TaskSource.Task;
		}

		bool _flushAndSending = false;
		object _flushAndSendingLock = new object();
		internal void FlushAndSend() {
			if (_flushAndSending) return;
			lock (_flushAndSendingLock) {
				if (_flushAndSending) return;
				_flushAndSendingLock = true;
			}
			var _buffer = new MemoryStream();
			var _parsers = new Queue<IPipelineCommandToken>();
			while (true) {
				if (!_cmdqs.TryDequeue(out var token)) {

					if (_cmdqs.Any() == false) {
						Interlocked.Exchange(ref _asyncqs, 0);
						break;
					}
				}

				_io.Writer.Write(token.Command, _buffer);
				_parsers.Enqueue(token);
			}
			if (_parsers.Any()) {
				Exception ex = null;
				try {
					_buffer.Position = 0;
					_buffer.CopyTo(_io.Stream);
				} catch (Exception ex2) {
					ex = ex2;
				} finally {
					_buffer.SetLength(0);
				}

				//System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
				//sw.Start();
				while (_parsers.Any()) {
					var token = _parsers.Dequeue();
					try {
						if (ex != null)
							token.SetException(ex);
						else
							token.SetResult(_io.Reader);
					} catch (Exception e) {
						token.SetException(e);
					}
				}
				//sw.Stop();
				//var par = sw.ElapsedMilliseconds;
				//Console.WriteLine("ms: " + par);
			}
			_buffer.Close();
			lock (_flushAndSendingLock) {
				_flushAndSendingLock = false;
			}
		}

		internal ConcurrentQueue<IPipelineCommandToken> _cmdqs = new ConcurrentQueue<IPipelineCommandToken>();
		internal bool running = true;

		internal interface IPipelineCommandToken {
			Task Task { get; }
			RedisCommand Command { get; }
			void SetResult(RedisReader reader);
			void SetException(Exception e);
		}

		class PipelineCommandToken<T> : IPipelineCommandToken {

			bool _isAsync = false;
			internal bool IsAsync {
				get => _isAsync;
				set {
					_isAsync = value;
					if (_isAsync) {
						if (this._tcs == null) {
							this._tcs = new TaskCompletionSource<T>();
						}
					} else {
						if (this.SyncWait == null) {
							this.SyncWait = new ManualResetEventSlim();
							this.SyncLock = new object();
						}
					}
				}
			}
			internal ManualResetEventSlim SyncWait { get; set; }
			internal T SyncReturnValue { get; set; }
			internal Exception SyncException { get; set; }
			internal object SyncLock { get; set; }
			internal bool SyncIsTimeout { get; set; }
			public void Dispose() {
				try {
					if (SyncWait != null)
						SyncWait.Dispose();
				} catch {
				}
			}

			TaskCompletionSource<T> _tcs;
			RedisCommand<T> _command;

			public TaskCompletionSource<T> TaskSource { get { return _tcs; } }
			public RedisCommand Command { get { return _command; } }
			public Task Task { get { return _tcs.Task; } }

			public PipelineCommandToken(bool isAsync, RedisCommand<T> command) {
				this.IsAsync = isAsync;
				this._command = command;
			}

			public void SetResult(RedisReader reader) {
				if (IsAsync) {
					if (reader == null) {
						_tcs.SetResult(default(T));
						return;
					}
					_tcs.SetResult(_command.Parse(reader));
				} else {
					if (reader == null) {
						SyncReturnValue = default(T);
						SyncWait.Set();
						return;
					}
					SyncReturnValue = _command.Parse(reader);
					SyncWait.Set();
				}
			}

			public void SetException(Exception e) {
				if (IsAsync) {
					_tcs.SetException(e);
				} else {
					SyncException = e;
					SyncWait.Set();
				}
			}
		}

		~AutoPipelineOption() {
			Dispose();
		}
		bool _isDisposed = false;
		public void Dispose() {
			if (_isDisposed) return;
			_isDisposed = true;
			running = false;

			while (_cmdqs.TryDequeue(out var sync)) {
				try { sync.SetException(new ObjectDisposedException("CSRedis")); } catch { }
			}
		}
	}
}
