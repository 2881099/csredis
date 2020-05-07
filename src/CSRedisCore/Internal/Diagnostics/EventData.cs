using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace CSRedis.Internal.Diagnostics
{
#if net40
#else
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
        public CallEventData(string operation, string key) : base(operation)
        {
            Key = key;
        }

        public string Key { get; private set; }
    }
#endif
}
