using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CSRedis.Internal
{
    internal class MonitorListener : RedisListener<object>
    {
        public event EventHandler<RedisMonitorEventArgs> MonitorReceived;

        public MonitorListener(RedisConnector connection)
            : base(connection)
        { }

        public string Start()
        {
            string status = Call(RedisCommands.Monitor());
            Listen(x => x.Read());
            return status;
        }

        protected override void OnParsed(object value)
        {
            OnMonitorReceived(value);
        }

        protected override bool Continue()
        {
            return Connection.IsConnected;
        }

        private void OnMonitorReceived(object message)
        {
            if (MonitorReceived != null)
                MonitorReceived(this, new RedisMonitorEventArgs(message));
        }
    }
}