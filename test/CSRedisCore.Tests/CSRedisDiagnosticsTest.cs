using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace CSRedisCore.Tests
{
    public class CSRedisDiagnosticsTest : TestBase
    {
        [Fact]
        public async Task WriteCallTest()
        {
            var statsLogged = false;

            FakeDiagnosticListenerObserver diagnosticListenerObserver = new FakeDiagnosticListenerObserver(kvp =>
            {
                if (kvp.Key.Equals("CSRedis.WriteCallBefore"))
                {
                    Assert.NotNull(kvp.Value);

                    statsLogged = true;
                }
                else if (kvp.Key.Equals("CSRedis.WriteCallAfter"))
                {
                    Assert.NotNull(kvp.Value);

                    statsLogged = true;
                }
            });

            diagnosticListenerObserver.Enable();
            using (DiagnosticListener.AllListeners.Subscribe(diagnosticListenerObserver))
            {
                var key = "CSRedisDiagnostics";

                //await rds.SetAsync(key, base.String);
                await rds.AppendAsync(key, base.Null);

                Assert.True(statsLogged);

                diagnosticListenerObserver.Disable();
            }
        }
    }

    public sealed class FakeDiagnosticListenerObserver : IObserver<DiagnosticListener>
    {
        private class FakeDiagnosticSourceWriteObserver : IObserver<KeyValuePair<string, object>>
        {
            private readonly Action<KeyValuePair<string, object>> _writeCallback;

            public FakeDiagnosticSourceWriteObserver(Action<KeyValuePair<string, object>> writeCallback)
            {
                _writeCallback = writeCallback;
            }

            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {
            }

            public void OnNext(KeyValuePair<string, object> value)
            {
                _writeCallback(value);
            }
        }

        private readonly Action<KeyValuePair<string, object>> _writeCallback;
        private bool _writeObserverEnabled;

        public FakeDiagnosticListenerObserver(Action<KeyValuePair<string, object>> writeCallback)
        {
            _writeCallback = writeCallback;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(DiagnosticListener value)
        {
            if (value.Name.Equals("CSRedisDiagnosticListener"))
            {
                value.Subscribe(new FakeDiagnosticSourceWriteObserver(_writeCallback), IsEnabled);
            }
        }

        public void Enable()
        {
            _writeObserverEnabled = true;
        }
        public void Disable()
        {
            _writeObserverEnabled = false;
        }
        private bool IsEnabled(string s)
        {
            return _writeObserverEnabled;
        }
    }
}
