using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSRedis
{
    class TaskEx
    {
        public static Task<T> FromResult<T>(T value)
        {
#if net40
            return new Task<T>(() => value);
#else
            return Task.FromResult(value);
#endif
        }
        public static Task Run(Action action)
        {
#if net40
            var tcs = new TaskCompletionSource<object>();
            new Thread(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            })
            { IsBackground = true }.Start();
            return tcs.Task;
#else
            return Task.Run(action);
#endif
        }
        public static Task<TResult> Run<TResult>(Func<TResult> function)
        {
            var tcs = new TaskCompletionSource<TResult>();
            new Thread(() =>
            {
                try
                {
                    tcs.SetResult(function());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            })
            { IsBackground = true }.Start();
            return tcs.Task;
        }
        public static Task Delay(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<object>();
            var timer = new System.Timers.Timer(timeout.TotalMilliseconds) { AutoReset = false };
            timer.Elapsed += delegate { timer.Dispose(); tcs.SetResult(null); };
            timer.Start();
            return tcs.Task;
        }
    }
}