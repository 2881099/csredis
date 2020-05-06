using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace CSRedis.Internal
{
#if net40
#else
    internal static class CSRedisDiagnosticListenerExtensions
    {
        public const string DiagnosticListenerName = "CSRedisDiagnosticListener";

        public const string CSRedisPrefix = "CSRedis.";

        public const string CSRedisBeforeCall = CSRedisPrefix + nameof(WriteCallBefore);
        public const string CSRedisAfterCall = CSRedisPrefix + nameof(WriteCallAfter);
        public const string CSRedisErrorCall = CSRedisPrefix + nameof(WriteCallError);


        public static Guid WriteCallBefore(this DiagnosticListener @this, CallEventData eventData)
        {
            if (@this.IsEnabled(CSRedisBeforeCall))
            {
                Guid operationId = Guid.NewGuid();

                @this.Write(CSRedisBeforeCall, new
                {
                    OperationId = operationId,
                    EventData = eventData
                });

                return operationId;
            }

            return Guid.Empty;
        }

        public static Guid WriteCallAfter(this DiagnosticListener @this, Guid operationId, CallEventData eventData)
        {
            if (@this.IsEnabled(CSRedisAfterCall))
            {
                @this.Write(CSRedisAfterCall, new
                {
                    OperationId = operationId,
                    EventData = eventData
                });

                return operationId;
            }

            return Guid.Empty;
        }

        public static void WriteCallError(this DiagnosticListener @this, Guid operationId, CallEventData eventData, Exception ex)
        {
            if (@this.IsEnabled(CSRedisErrorCall))
            {
                @this.Write(CSRedisErrorCall, new
                {
                    OperationId = operationId,
                    EventData = eventData,
                    Exception = ex
                });
            }
        }
    }

    public class EventData
    {
        public EventData(string operation)
        {           
            Operation = operation;
        }

        public string Operation { get; }
    }

    public class CallEventData : EventData
    {
        public CallEventData(string operation) : base(operation)
        {
        }

        public string Key { get; set; }
    }
#endif
}
