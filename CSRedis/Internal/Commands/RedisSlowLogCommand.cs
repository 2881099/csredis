using CSRedis.Internal.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CSRedis.Internal.Commands
{
    class RedisSlowLogCommand : RedisCommand<RedisSlowLogEntry>
    {
        public RedisSlowLogCommand(string command, object[] args)
            : base(command, args)
        { }

        public override RedisSlowLogEntry Parse(RedisReader reader)
        {
            reader.ExpectType(RedisMessage.MultiBulk);
            reader.ExpectSize(4);
            long id = reader.ReadInt();
            long timestamp = reader.ReadInt();
            long microseconds = reader.ReadInt();
            reader.ExpectType(RedisMessage.MultiBulk);
            string[] arguments = new string[reader.ReadInt(false)];
            for (int i = 0; i < arguments.Length; i++)
                arguments[i] = reader.ReadBulkString();

            return new RedisSlowLogEntry(id, RedisDate.FromTimestamp(timestamp), RedisDate.Micro.FromMicroseconds(microseconds), arguments);
        }
    }
}
