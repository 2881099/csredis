using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SafeObjectPool {

	/// <summary>
	/// 对象池管理类
	/// </summary>
	/// <typeparam name="T">对象类型</typeparam>
	public partial class ObjectPool<T> : IDisposable {

		public IPolicy<T> Policy { get; protected set; }

		private List<Object<T>> _allObjects = new List<Object<T>>();
		private object _allObjectsLock = new object();
		private ConcurrentQueue<Object<T>> _freeObjects = new ConcurrentQueue<Object<T>>();

		private ConcurrentQueue<GetSyncQueueInfo> _getSyncQueue = new ConcurrentQueue<GetSyncQueueInfo>();
		private ConcurrentQueue<TaskCompletionSource<Object<T>>> _getAsyncQueue = new ConcurrentQueue<TaskCompletionSource<Object<T>>>();
		private ConcurrentQueue<bool> _getQueue = new ConcurrentQueue<bool>();

		/// <summary>
		/// 是否可用
		/// </summary>
		public bool IsAvailable => this.UnavailableException == null;
		/// <summary>
		/// 不可用错误
		/// </summary>
		public Exception UnavailableException { get; private set; }
		/// <summary>
		/// 不可用时间
		/// </summary>
		public DateTime? UnavailableTime { get; private set; }
		private object UnavailableLock = new object();
		private bool running = true;

		/// <summary>
		/// 将连接池设置为不可用，后续 Get/GetAsync 均会报错，同时启动后台定时检查服务恢复可用
		/// </summary>
		/// <returns>由【可用】变成【不可用】时返回true，否则返回false</returns>
		public bool SetUnavailable(Exception exception) {

			bool isseted = false;

			if (exception != null && UnavailableException == null) {

				lock (UnavailableLock) {

					if (UnavailableException == null) {

						UnavailableException = exception;
						UnavailableTime = DateTime.Now;
						isseted = true;
					}
				}
			}

			if (isseted) {

				Policy.OnUnavailable();
				CheckAvailable(Policy.CheckAvailableInterval);
			}

			return isseted;
		}

		/// <summary>
		/// 后台定时检查可用性
		/// </summary>
		/// <param name="interval"></param>
		private void CheckAvailable(int interval) {

			new Thread(() => {

				if (UnavailableException != null) {
					var bgcolor = Console.BackgroundColor;
					var forecolor = Console.ForegroundColor;
					Console.BackgroundColor = ConsoleColor.DarkYellow;
					Console.ForegroundColor = ConsoleColor.White;
					Console.Write($"【{Policy.Name}】恢复检查时间：{DateTime.Now.AddSeconds(interval)}");
					Console.BackgroundColor = bgcolor;
					Console.ForegroundColor = forecolor;
					Console.WriteLine();
				}

				while (UnavailableException != null) {

					if (running == false) return;

					Thread.CurrentThread.Join(TimeSpan.FromSeconds(interval));

					if (running == false) return;

					try {

						var conn = getFree(false);
						if (conn == null) throw new Exception($"CheckAvailable 无法获得资源，{this.Statistics}");

						try {

							if (Policy.OnCheckAvailable(conn) == false) throw new Exception("CheckAvailable 应抛出异常，代表仍然不可用。");
							break;

						} finally {

							Return(conn);
						}

					} catch (Exception ex) {
						var bgcolor = Console.BackgroundColor;
						var forecolor = Console.ForegroundColor;
						Console.BackgroundColor = ConsoleColor.DarkYellow;
						Console.ForegroundColor = ConsoleColor.White;
						Console.Write($"【{Policy.Name}】仍然不可用，下一次恢复检查时间：{DateTime.Now.AddSeconds(interval)}，错误：({ex.Message})");
						Console.BackgroundColor = bgcolor;
						Console.ForegroundColor = forecolor;
						Console.WriteLine();
					}
				}

				bool isRestored = false;
				if (UnavailableException != null) {

					lock (UnavailableLock) {

						if (UnavailableException != null) {

							UnavailableException = null;
							UnavailableTime = null;
							isRestored = true;
						}
					}
				}

				if (isRestored) {

					lock (_allObjectsLock)
						_allObjects.ForEach(a => a.LastGetTime = a.LastReturnTime = new DateTime(2000, 1, 1));

					Policy.OnAvailable();

					var bgcolor = Console.BackgroundColor;
					var forecolor = Console.ForegroundColor;
					Console.BackgroundColor = ConsoleColor.DarkGreen;
					Console.ForegroundColor = ConsoleColor.White;
					Console.Write($"【{Policy.Name}】已恢复工作");
					Console.BackgroundColor = bgcolor;
					Console.ForegroundColor = forecolor;
					Console.WriteLine();
				}

			}).Start();
		}

		/// <summary>
		/// 统计
		/// </summary>
		public string Statistics => $"Pool: {_freeObjects.Count}/{_allObjects.Count}, Get wait: {_getSyncQueue.Count}, GetAsync wait: {_getAsyncQueue.Count}";
		/// <summary>
		/// 统计（完整)
		/// </summary>
		public string StatisticsFullily {
			get {
				var sb = new StringBuilder();

				sb.AppendLine(Statistics);
				sb.AppendLine("");

				foreach (var obj in _allObjects) {
					sb.AppendLine($"{obj.Value}, Times: {obj.GetTimes}, ThreadId(R/G): {obj.LastReturnThreadId}/{obj.LastGetThreadId}, Time(R/G): {obj.LastReturnTime.ToString("yyyy-MM-dd HH:mm:ss:ms")}/{obj.LastGetTime.ToString("yyyy-MM-dd HH:mm:ss:ms")}, ");
				}

				return sb.ToString();
			}
		}

		/// <summary>
		/// 创建对象池
		/// </summary>
		/// <param name="poolsize">池大小</param>
		/// <param name="createObject">池内对象的创建委托</param>
		/// <param name="onGetObject">获取池内对象成功后，进行使用前操作</param>
		public ObjectPool(int poolsize, Func<T> createObject, Action<Object<T>> onGetObject = null) : this(new DefaultPolicy<T> { PoolSize = poolsize, CreateObject = createObject, OnGetObject = onGetObject }) {
		}
		/// <summary>
		/// 创建对象池
		/// </summary>
		/// <param name="policy">策略</param>
		public ObjectPool(IPolicy<T> policy) {
			Policy = policy;

			AppDomain.CurrentDomain.ProcessExit += (s1, e1) => {
				running = false;
			};
			Console.CancelKeyPress += (s1, e1) => {
				running = false;
			};
		}

		/// <summary>
		/// 获取可用资源，或创建资源
		/// </summary>
		/// <returns></returns>
		private Object<T> getFree(bool checkAvailable) {

			if (running == false)
				throw new ObjectDisposedException($"【{Policy.Name}】对象池已释放，无法访问。");

			if (checkAvailable && UnavailableException != null)
				throw new Exception($"【{Policy.Name}】状态不可用，等待后台检查程序恢复方可使用。{UnavailableException.Message}");

			if ((_freeObjects.TryDequeue(out var obj) == false || obj == null) && _allObjects.Count < Policy.PoolSize) {

				lock (_allObjectsLock)
					if (_allObjects.Count < Policy.PoolSize)
						_allObjects.Add(obj = new Object<T> { Pool = this, Id = _allObjects.Count + 1 });
			}

			if (obj != null && obj.Value == null)
				obj.Value = Policy.OnCreate();

			return obj;
		}

		/// <summary>
		/// 获取资源
		/// </summary>
		/// <param name="timeout">超时</param>
		/// <returns>资源</returns>
		public Object<T> Get(TimeSpan? timeout = null) {

			var obj = getFree(true);

			if (obj == null) {

				var queueItem = new GetSyncQueueInfo();

				_getSyncQueue.Enqueue(queueItem);
				_getQueue.Enqueue(false);

				if (timeout == null) timeout = Policy.SyncGetTimeout;

				try {
					if (queueItem.Wait.Wait(timeout.Value))
						obj = queueItem.ReturnValue;
				} catch { }

				if (obj == null) obj = queueItem.ReturnValue;
				if (obj == null) lock (queueItem.Lock) queueItem.IsTimeout = (obj = queueItem.ReturnValue) == null;
				if (obj == null) obj = queueItem.ReturnValue;

				if (obj == null) {

					Policy.OnGetTimeout();

					if (Policy.IsThrowGetTimeoutException)
						throw new TimeoutException($"SafeObjectPool.Get 获取超时（{timeout.Value.TotalSeconds}秒），设置 Policy.IsThrowGetTimeoutException 可以避免该异常。");

					return null;
				}
			}

			try {
				Policy.OnGet(obj);
			} catch {
				Return(obj);
				throw;
			}

			obj.LastGetThreadId = Thread.CurrentThread.ManagedThreadId;
			obj.LastGetTime = DateTime.Now;
			Interlocked.Increment(ref obj._getTimes);

			return obj;
		}

		/// <summary>
		/// 获取资源
		/// </summary>
		/// <returns>资源</returns>
		async public Task<Object<T>> GetAsync() {

			var obj = getFree(true);

			if (obj == null) {

				if (Policy.AsyncGetCapacity > 0 && _getAsyncQueue.Count >= Policy.AsyncGetCapacity - 1)
					throw new OutOfMemoryException($"SafeObjectPool.GetAsync 无可用资源且队列过长，Policy.AsyncGetCapacity = {Policy.AsyncGetCapacity}。");

				var tcs = new TaskCompletionSource<Object<T>>();

				_getAsyncQueue.Enqueue(tcs);
				_getQueue.Enqueue(true);

				obj = await tcs.Task;

				//if (timeout == null) timeout = Policy.SyncGetTimeout;

				//if (tcs.Task.Wait(timeout.Value))
				//	obj = tcs.Task.Result;

				//if (obj == null) {

				//	tcs.TrySetCanceled();
				//	Policy.GetTimeout();

				//	if (Policy.IsThrowGetTimeoutException)
				//		throw new Exception($"SafeObjectPool.GetAsync 获取超时（{timeout.Value.TotalSeconds}秒），设置 Policy.IsThrowGetTimeoutException 可以避免该异常。");

				//	return null;
				//}
			}

			try {
				await Policy.OnGetAsync(obj);
			} catch {
				Return(obj);
				throw;
			}

			obj.LastGetThreadId = Thread.CurrentThread.ManagedThreadId;
			obj.LastGetTime = DateTime.Now;
			Interlocked.Increment(ref obj._getTimes);

			return obj;
		}

		/// <summary>
		/// 使用完毕后，归还资源
		/// </summary>
		/// <param name="obj">对象</param>
		/// <param name="isReset">是否重新创建</param>
		public void Return(Object<T> obj, bool isReset = false) {

			if (obj == null) return;

			if (running == false) {

				Policy.OnDestroy(obj.Value);
				try { (obj.Value as IDisposable)?.Dispose(); } catch { }

				return;
			}

			if (isReset) obj.ResetValue();

			bool isReturn = false;

			while (isReturn == false && _getQueue.TryDequeue(out var isAsync)) {

				if (isAsync == false) {

					if (_getSyncQueue.TryDequeue(out var queueItem) && queueItem != null) {

						lock (queueItem.Lock)
							if (queueItem.IsTimeout == false)
								queueItem.ReturnValue = obj;

						if (queueItem.ReturnValue != null) {

							obj.LastReturnThreadId = Thread.CurrentThread.ManagedThreadId;
							obj.LastReturnTime = DateTime.Now;

							queueItem.Wait.Set();
							isReturn = true;
						}

						queueItem.Dispose();
					}

				} else {

					if (_getAsyncQueue.TryDequeue(out var tcs) && tcs != null && tcs.Task.IsCanceled == false) {

						obj.LastReturnThreadId = Thread.CurrentThread.ManagedThreadId;
						obj.LastReturnTime = DateTime.Now;

						isReturn = tcs.TrySetResult(obj);
					}
				}
			}

			//无排队，直接归还
			if (isReturn == false) {

				try {

					Policy.OnReturn(obj);

				} catch {

					throw;

				} finally {

					obj.LastReturnThreadId = Thread.CurrentThread.ManagedThreadId;
					obj.LastReturnTime = DateTime.Now;

					_freeObjects.Enqueue(obj);
				}
			}
		}

		public void Dispose() {

			running = false;

			while (_freeObjects.TryDequeue(out var fo)) ;

			while (_getSyncQueue.TryDequeue(out var sync)) {
				try { sync.Wait.Set(); } catch { }
			}

			while(_getAsyncQueue.TryDequeue(out var async)) {
				async.TrySetCanceled();
			}

			while (_getQueue.TryDequeue(out var qs)) ;

			for (var a = 0; a < _allObjects.Count; a++) {
				Policy.OnDestroy(_allObjects[a].Value);
				try { (_allObjects[a].Value as IDisposable)?.Dispose(); } catch { }
			}

			_allObjects.Clear();
		}

		class GetSyncQueueInfo : IDisposable {

			internal ManualResetEventSlim Wait { get; set; } = new ManualResetEventSlim();

			internal Object<T> ReturnValue { get; set; }

			internal object Lock = new object();

			internal bool IsTimeout { get; set; } = false;

			public void Dispose() {
				try {
					if (Wait != null)
						Wait.Dispose();
				} catch {
				}
			}
		}
	}
}