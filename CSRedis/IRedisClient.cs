using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CSRedis
{
    /// <summary>
    /// Common properties of the RedisClient
    /// </summary>
    public interface IRedisClient :IDisposable
    {
        /// <summary>
        /// Occurs when a subscription message is received
        /// </summary>
        event EventHandler<RedisSubscriptionReceivedEventArgs> SubscriptionReceived;

        /// <summary>
        /// Occurs when a subscription channel is added or removed
        /// </summary>
        event EventHandler<RedisSubscriptionChangedEventArgs> SubscriptionChanged;

        /// <summary>
        /// Occurs when a transaction command is acknowledged by the server
        /// </summary>
        event EventHandler<RedisTransactionQueuedEventArgs> TransactionQueued;

        /// <summary>
        /// Occurs when a monitor message is received
        /// </summary>
        event EventHandler<RedisMonitorEventArgs> MonitorReceived;

        /// <summary>
        /// Occurs when the connection has sucessfully reconnected
        /// </summary>
        event EventHandler Connected;


        /// <summary>
        /// Get the Redis server hostname
        /// </summary>
        string Host { get; }

        /// <summary>
        /// Get the Redis server port
        /// </summary>
        int Port { get; }

        /// <summary>
        /// Get a value indicating whether the Redis client is connected to the server
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Get or set the string encoding used to communicate with the server
        /// </summary>
        Encoding Encoding { get; set; }

        /// <summary>
        /// Get or set the connection read timeout (milliseconds)
        /// </summary>
        int ReceiveTimeout { get; set; }

        /// <summary>
        /// Get or set the connection send timeout (milliseconds)
        /// </summary>
        int SendTimeout { get; set; }

        /// <summary>
        /// Get or set the number of times to attempt a reconnect after a connection fails
        /// </summary>
        int ReconnectAttempts { get; set; }

        /// <summary>
        /// Get or set the amount of time (milliseconds) to wait between reconnect attempts
        /// </summary>
        int ReconnectWait { get; set; }
    }
}
