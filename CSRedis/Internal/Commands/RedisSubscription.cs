using CSRedis.Internal.IO;
using System.IO;

namespace CSRedis.Internal.Commands
{
    class RedisSubscription : RedisCommand<RedisSubscriptionResponse>
    {
        public RedisSubscription(string command, params object[] args)
            : base(command, args)
        { }

        public override RedisSubscriptionResponse Parse(RedisReader reader)
        {
            reader.ExpectType(RedisMessage.MultiBulk);
            long count = reader.ReadInt(false);
            string type = reader.ReadBulkString();
            switch (type)
            {
                case "subscribe":
                case "unsubscribe":
                case "psubscribe":
                case "punsubscribe":
                    return ParseChannel(type, reader);

                case "message":
                case "pmessage":
                    return ParseMessage(type, reader);
            }

            throw new RedisProtocolException("Unexpected type " + type);
        }

        static RedisSubscriptionChannel ParseChannel(string type, RedisReader reader)
        {
            switch (type)
            {
                case "subscribe":
                case "unsubscribe":
                    return new RedisSubscriptionChannel(type, reader.ReadBulkString(), null, reader.ReadInt());

                case "psubscribe":
                case "punsubscribe":
                    return new RedisSubscriptionChannel(type, null, reader.ReadBulkString(), reader.ReadInt());
            }

            throw new RedisProtocolException("Unexpected type " + type);
        }

        static RedisSubscriptionMessage ParseMessage(string type, RedisReader reader)
        {
            if (type == "message")
                return new RedisSubscriptionMessage(type, reader.ReadBulkString(), reader.ReadBulkString());

            else if (type == "pmessage")
                return new RedisSubscriptionMessage(type, reader.ReadBulkString(), reader.ReadBulkString(), reader.ReadBulkString());

            throw new RedisProtocolException("Unexpected type " + type);
        }
    }
}
