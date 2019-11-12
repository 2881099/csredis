using CSRedis.Internal.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CSRedis.Internal.Commands
{
    class RedisXRangeCommand : RedisCommand<(string id, string field, string value)[]>
    {

        public RedisXRangeCommand(string command, params object[] args)
            : base(command, args)
        {
        }

        public override (string id, string field, string value)[] Parse(RedisReader reader)
        {
            (string id, string field, string value)[] ret = null;

            reader.ExpectType(RedisMessage.MultiBulk);
            long count = reader.ReadInt(false);
            if (count == -1) return ret;
            ret = new (string id, string field, string value)[count];

            for (var a = 0; a < count; a++)
            {
                reader.ExpectType(RedisMessage.MultiBulk);
                var lvl2Count = reader.ReadInt(false);
                if (lvl2Count != 2) throw new RedisProtocolException("XRange 数据格式 2级 MultiBulk 长度应该为 2");

                var id = reader.ReadBulkString();

                reader.ExpectType(RedisMessage.MultiBulk);
                var lvl3Count = reader.ReadInt(false);
                if (lvl3Count != 4) throw new RedisProtocolException("XRange 数据格式 4级 MultiBulk 长度应该为 4");

                var maxlen = reader.ReadBulkString();
                var maxlenVal = reader.ReadBulkString();
                var field = reader.ReadBulkString();
                var value = reader.ReadBulkString();

                ret[a] = (id, field, value);
            }

            return ret;
        }
    }
}
