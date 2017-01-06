using System;

namespace CSRedis
{
    /// <summary>
    /// Represents a Redis server error reply
    /// </summary>
    public class RedisException : RedisClientException
    {
        /// <summary>
        /// Instantiate a new instance of the RedisException class
        /// </summary>
        /// <param name="message">Server response</param>
        public RedisException(string message)
            : base(message)
        { }
    }

    /// <summary>
    /// The exception that is thrown when an unexpected value is found in a Redis request or response 
    /// </summary>
    public class RedisProtocolException : RedisClientException
    {
        /// <summary>
        /// Instantiate a new instance of the RedisProtocolException class
        /// </summary>
        /// <param name="message">Protocol violoation message</param>
        public RedisProtocolException(string message)
            : base(message)
        { }
    }

    /// <summary>
    /// Exception thrown by RedisClient
    /// </summary>
    public class RedisClientException : Exception
    {
        /// <summary>
        /// Instantiate a new instance of the RedisClientException class
        /// </summary>
        /// <param name="message">Exception message</param>
        public RedisClientException(string message)
            : base(message)
        { }

        /// <summary>
        /// Instantiate a new instance of the RedisClientException class
        /// </summary>
        /// <param name="message">Exception message</param>
        /// <param name="inner">Inner exception</param>
        public RedisClientException(string message, Exception inner)
            : base(message, inner)
        { }
    }
}
