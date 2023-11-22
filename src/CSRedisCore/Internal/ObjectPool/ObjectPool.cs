﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSRedis.Internal.ObjectPool
{
    internal class TestTrace
    {
        internal static void WriteLine(string text, ConsoleColor backgroundColor)
        {
            try //#643
            {
                var bgcolor = Console.BackgroundColor;
                var forecolor = Console.ForegroundColor;
                Console.BackgroundColor = backgroundColor;

                switch (backgroundColor)
                {
                    case ConsoleColor.Yellow:
                        Console.ForegroundColor = ConsoleColor.White;
                        break;
                    case ConsoleColor.DarkGreen:
                        Console.ForegroundColor = ConsoleColor.White;
                        break;
                }
                Console.Write(text);
                Console.BackgroundColor = bgcolor;
                Console.ForegroundColor = forecolor;
                Console.WriteLine();
            }
            catch
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine(text);
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// 对象池管理类
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    public partial class ObjectPool<T> : IObjectPool<T>
    {
        public IPolicy<T> Policy { get; protected set; }

        private object _allObjectsLock = new object();
        internal List<Object<T>> _allObjects = new List<Object<T>>();
        internal ConcurrentStack<Object<T>> _freeObjects = new ConcurrentStack<Object<T>>();

        private ConcurrentQueue<GetSyncQueueInfo> _getSyncQueue = new ConcurrentQueue<GetSyncQueueInfo>();
        private ConcurrentQueue<TaskCompletionSource<Object<T>>> _getAsyncQueue = new ConcurrentQueue<TaskCompletionSource<Object<T>>>();
        private ConcurrentQueue<bool> _getQueue = new ConcurrentQueue<bool>();

        public bool IsAvailable => this.UnavailableException == null;
        public Exception UnavailableException { get; private set; }
        public DateTime? UnavailableTime { get; private set; }
        public DateTime? AvailableTime { get; private set; }
        private object UnavailableLock = new object();
        private bool running = true;

        public bool SetUnavailable(Exception exception, DateTime lastGetTime)
        {
            bool isseted = false;
            if (exception != null && UnavailableException == null)
            {
                lock (UnavailableLock)
                {
                    if (UnavailableException == null)
                    {
                        if (lastGetTime < AvailableTime) return false; //已经恢复
                        UnavailableException = exception;
                        UnavailableTime = DateTime.Now;
                        AvailableTime = null;
                        isseted = true;
                    }
                }
            }

            if (isseted)
            {
                Policy.OnUnavailable();
                CheckAvailable(Policy.CheckAvailableInterval);
            }

            return isseted;
        }

        /// <summary>
        /// 后台定时检查可用性
        /// </summary>
        /// <param name="interval"></param>
        private void CheckAvailable(int interval)
        {
            new Thread(() =>
            {
                if (UnavailableException != null)
                    TestTrace.WriteLine($"【{Policy.Name}】Next recovery time：{DateTime.Now.AddSeconds(interval)}", ConsoleColor.DarkYellow);

                while (UnavailableException != null)
                {
                    if (running == false) return;
                    Thread.CurrentThread.Join(TimeSpan.FromSeconds(interval));
                    if (running == false) return;

                    try
                    {
                        var conn = GetFree(false);
                        if (conn == null) throw new Exception($"【{Policy.Name}】Failed to get resource {this.Statistics}");

                        try
                        {
                            try
                            {
                                Policy.OnCheckAvailable(conn);
                                break;
                            }
                            catch
                            {
                                conn.ResetValue();
                            }
                            if (Policy.OnCheckAvailable(conn) == false) throw new Exception($"【{Policy.Name}】An exception needs to be thrown");
                            break;
                        }
                        finally
                        {
                            Return(conn);
                        }
                    }
                    catch (Exception ex)
                    {
                        TestTrace.WriteLine($"【{Policy.Name}】Next recovery time: {DateTime.Now.AddSeconds(interval)} ({ex.Message})", ConsoleColor.DarkYellow);
                    }
                }

                RestoreToAvailable();

            }).Start();
        }

        private void RestoreToAvailable()
        {

            bool isRestored = false;
            if (UnavailableException != null)
            {
                lock (UnavailableLock)
                {
                    if (UnavailableException != null)
                    {
                        lock (_allObjectsLock)
                            _allObjects.ForEach(a => a.LastGetTime = a.LastReturnTime = new DateTime(2000, 1, 1));
                        UnavailableException = null;
                        UnavailableTime = null;
                        AvailableTime = DateTime.Now;
                        isRestored = true;
                    }
                }
            }

            if (isRestored)
            {
                Policy.OnAvailable();
                TestTrace.WriteLine($"【{Policy.Name}】Recovered", ConsoleColor.DarkGreen);
            }
        }

        protected bool LiveCheckAvailable()
        {
            try
            {
                var conn = GetFree(false);
                if (conn == null) throw new Exception($"【{Policy.Name}】Failed to get resource {this.Statistics}");

                try
                {
                    if (Policy.OnCheckAvailable(conn) == false) throw new Exception("【{Policy.Name}】An exception needs to be thrown");
                }
                finally
                {
                    Return(conn);
                }
            }
            catch
            {
                return false;
            }

            RestoreToAvailable();
            return true;
        }

        public string Statistics => $"Pool: {_freeObjects.Count}/{_allObjects.Count}, Get wait: {_getSyncQueue.Count}, GetAsync wait: {_getAsyncQueue.Count}";
        public string StatisticsFullily
        {
            get
            {
                var sb = new StringBuilder();

                sb.AppendLine(Statistics);
                sb.AppendLine("");

                foreach (var obj in _allObjects)
                {
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
        public ObjectPool(int poolsize, Func<T> createObject, Action<Object<T>> onGetObject = null) : this(new DefaultPolicy<T> { PoolSize = poolsize, CreateObject = createObject, OnGetObject = onGetObject })
        {
        }
        /// <summary>
        /// 创建对象池
        /// </summary>
        /// <param name="policy">策略</param>
        public ObjectPool(IPolicy<T> policy)
        {
            Policy = policy;

            AppDomain.CurrentDomain.ProcessExit += (s1, e1) =>
            {
                if (Policy.IsAutoDisposeWithSystem)
                    running = false;
            };
            try
            {
                Console.CancelKeyPress += (s1, e1) =>
                {
                    if (e1.Cancel) return;
                    if (Policy.IsAutoDisposeWithSystem)
                        running = false;
                };
            }
            catch { }
        }

        public void AutoFree()
        {
            if (running == false) return;
            if (UnavailableException != null) return;

            var list = new List<Object<T>>();
            while (_freeObjects.TryPop(out var obj))
                list.Add(obj);
            foreach (var obj in list)
            {
                if (obj != null && obj.Value == null ||
                    obj != null && Policy.IdleTimeout > TimeSpan.Zero && DateTime.Now.Subtract(obj.LastReturnTime) > Policy.IdleTimeout)
                {
                    if (obj.Value != null)
                    {
                        Return(obj, true);
                        continue;
                    }
                }
                Return(obj);
            }
        }

        /// <summary>
        /// 获取可用资源，或创建资源
        /// </summary>
        /// <returns></returns>
        private Object<T> GetFree(bool checkAvailable)
        {

            if (running == false)
                throw new ObjectDisposedException($"【{Policy.Name}】The ObjectPool has been disposed, see: https://github.com/dotnetcore/FreeSql/discussions/1079");

            if (checkAvailable && UnavailableException != null)
                throw new Exception($"【{Policy.Name}】Status unavailable, waiting for recovery. {UnavailableException?.Message}", UnavailableException);

            if ((_freeObjects.TryPop(out var obj) == false || obj == null) && _allObjects.Count < Policy.PoolSize)
            {
                lock (_allObjectsLock)
                    if (_allObjects.Count < Policy.PoolSize)
                        _allObjects.Add(obj = new Object<T> { Pool = this, Id = _allObjects.Count + 1 });
            }

            if (obj != null)
                obj._isReturned = false;

            if (obj != null && obj.Value == null ||
                obj != null && Policy.IdleTimeout > TimeSpan.Zero && DateTime.Now.Subtract(obj.LastReturnTime) > Policy.IdleTimeout)
            {
                try
                {
                    obj.ResetValue();
                }
                catch
                {
                    Return(obj);
                    throw;
                }
            }

            return obj;
        }

        public Object<T> Get(TimeSpan? timeout = null)
        {
            var obj = GetFree(true);
            if (obj == null)
            {
                var queueItem = new GetSyncQueueInfo();

                _getSyncQueue.Enqueue(queueItem);
                _getQueue.Enqueue(false);

                if (timeout == null) timeout = Policy.SyncGetTimeout;

                try
                {
                    if (queueItem.Wait.Wait(timeout.Value))
                        obj = queueItem.ReturnValue;
                }
                catch { }

                if (obj == null) obj = queueItem.ReturnValue;
                if (obj == null) lock (queueItem.Lock) queueItem.IsTimeout = (obj = queueItem.ReturnValue) == null;
                if (obj == null) obj = queueItem.ReturnValue;
                if (queueItem.Exception != null) throw queueItem.Exception;

                if (obj == null)
                {
                    Policy.OnGetTimeout();
                    if (Policy.IsThrowGetTimeoutException)
                        throw new TimeoutException($"【{Policy.Name}】ObjectPool.Get() timeout {timeout.Value.TotalSeconds} seconds, see: https://github.com/dotnetcore/FreeSql/discussions/1081");

                    return null;
                }
            }

            try
            {
                Policy.OnGet(obj);
            }
            catch
            {
                Return(obj, true);
                throw;
            }

            obj.LastGetThreadId = Thread.CurrentThread.ManagedThreadId;
            obj.LastGetTime = DateTime.Now;
            obj.LastGetTimeCopy = DateTime.Now;
            Interlocked.Increment(ref obj._getTimes);

            return obj;
        }

#if net40
#else
        async public Task<Object<T>> GetAsync()
        {
            var obj = GetFree(true);
            if (obj == null)
            {
                if (Policy.AsyncGetCapacity > 0 && _getAsyncQueue.Count >= Policy.AsyncGetCapacity - 1)
                    throw new OutOfMemoryException($"【{Policy.Name}】ObjectPool.GetAsync() The queue is too long. Policy.AsyncGetCapacity = {Policy.AsyncGetCapacity}");

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
                //		throw new TimeoutException($"【{Policy.Name}】ObjectPool.GetAsync() timeout {timeout.Value.TotalSeconds} seconds, see: https://github.com/dotnetcore/FreeSql/discussions/1081");

                //	return null;
                //}
            }

            try
            {
                await Policy.OnGetAsync(obj);
            }
            catch
            {
                Return(obj, true);
                throw;
            }

            obj.LastGetThreadId = Thread.CurrentThread.ManagedThreadId;
            obj.LastGetTime = DateTime.Now;
            obj.LastGetTimeCopy = DateTime.Now;
            Interlocked.Increment(ref obj._getTimes);

            return obj;
        }
#endif

        public void Return(Object<T> obj, bool isReset = false)
        {
            if (obj == null) return;
            if (obj._isReturned) return;

            if (running == false)
            {
                Policy.OnDestroy(obj.Value);
                try { (obj.Value as IDisposable)?.Dispose(); } catch { }
                return;
            }

            if (isReset) obj.ResetValue();
            bool isReturn = false;

            while (isReturn == false && _getQueue.TryDequeue(out var isAsync))
            {
                if (isAsync == false)
                {
                    if (_getSyncQueue.TryDequeue(out var queueItem) && queueItem != null)
                    {
                        lock (queueItem.Lock)
                            if (queueItem.IsTimeout == false)
                                queueItem.ReturnValue = obj;

                        if (queueItem.ReturnValue != null)
                        {
                            if (UnavailableException != null)
                            {
                                queueItem.Exception = new Exception($"【{Policy.Name}】Status unavailable, waiting for recovery. {UnavailableException?.Message}", UnavailableException);
                                try
                                {
                                    queueItem.Wait.Set();
                                }
                                catch { }
                            }
                            else
                            {
                                obj.LastReturnThreadId = Thread.CurrentThread.ManagedThreadId;
                                obj.LastReturnTime = DateTime.Now;

                                try
                                {
                                    queueItem.Wait.Set();
                                    isReturn = true;
                                }
                                catch { }
                            }
                        }

                        try { queueItem.Dispose(); } catch { }
                    }
                }
                else
                {
                    if (_getAsyncQueue.TryDequeue(out var tcs) && tcs != null && tcs.Task.IsCanceled == false)
                    {
                        if (UnavailableException != null)
                        {
                            try
                            {
                                tcs.TrySetException(new Exception($"【{Policy.Name}】Status unavailable, waiting for recovery. {UnavailableException?.Message}", UnavailableException));
                            }
                            catch { }
                        }
                        else
                        {
                            obj.LastReturnThreadId = Thread.CurrentThread.ManagedThreadId;
                            obj.LastReturnTime = DateTime.Now;

                            try
                            {
                                isReturn = tcs.TrySetResult(obj);
                            }
                            catch { }
                        }
                    }
                }
            }

            //无排队，直接归还
            if (isReturn == false)
            {
                try
                {
                    Policy.OnReturn(obj);
                }
                catch
                {
                    throw;
                }
                finally
                {
                    obj.LastReturnThreadId = Thread.CurrentThread.ManagedThreadId;
                    obj.LastReturnTime = DateTime.Now;
                    obj._isReturned = true;

                    _freeObjects.Push(obj);
                }
            }
        }

        public void Dispose()
        {
            running = false;

            while (_freeObjects.TryPop(out var fo)) ;
            while (_getSyncQueue.TryDequeue(out var sync))
            {
                try { sync.Wait.Set(); } catch { }
            }

            while (_getAsyncQueue.TryDequeue(out var async))
                async.TrySetCanceled();

            while (_getQueue.TryDequeue(out var qs)) ;

            for (var a = 0; a < _allObjects.Count; a++)
            {
                Policy.OnDestroy(_allObjects[a].Value);
                try { (_allObjects[a].Value as IDisposable)?.Dispose(); } catch { }
            }

            _allObjects.Clear();
        }

        class GetSyncQueueInfo : IDisposable
        {
            internal ManualResetEventSlim Wait { get; set; } = new ManualResetEventSlim();
            internal Object<T> ReturnValue { get; set; }
            internal object Lock = new object();
            internal bool IsTimeout { get; set; } = false;
            internal Exception Exception { get; set; }

            public void Dispose()
            {
                try
                {
                    if (Wait != null)
                        Wait.Dispose();
                }
                catch
                {
                }
            }
        }
    }
}