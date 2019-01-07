using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CSRedis
{
    /// <summary>
    /// Provides data for the event that is raised when a subscription message is received
    /// </summary>
    public class RedisSubscriptionReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// The subscription message
        /// </summary>
        public RedisSubscriptionMessage Message { get; private set; }

        internal RedisSubscriptionReceivedEventArgs(RedisSubscriptionMessage message)
        {
            Message = message;
        }
    }

    /// <summary>
    /// Provides data for the event that is raised when a subscription channel is opened or closed
    /// </summary>
    public class RedisSubscriptionChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The subscription response
        /// </summary>
        public RedisSubscriptionChannel Response { get; private set; }

        internal RedisSubscriptionChangedEventArgs(RedisSubscriptionChannel response)
        {
            Response = response;
        }
    }

    /// <summary>
    /// Provides data for the event that is raised when a transaction command has been processed by the server
    /// </summary>
    public class RedisTransactionQueuedEventArgs : EventArgs
    {
        /// <summary>
        /// The status code of the transaction command
        /// </summary>
        public string Status { get; private set; }

        /// <summary>
        /// The command that was queued
        /// </summary>
        public string Command { get; private set; }

        /// <summary>
        /// The arguments of the queued command
        /// </summary>
        public object[] Arguments { get; private set; }

        internal RedisTransactionQueuedEventArgs(string status, string command, object[] arguments)
        {
            Status = status;
            Command = command;
            Arguments = arguments;
        }
    }

    /// <summary>
    /// Provides data for the event that is raised when a Redis MONITOR message is received
    /// </summary>
    public class RedisMonitorEventArgs : EventArgs
    {
        /// <summary>
        /// Monitor output
        /// </summary>
        public object Message { get; private set; }

        internal RedisMonitorEventArgs(object message)
        {
            Message = message;
        }
    }
}
