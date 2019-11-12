using CSRedis.Internal.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CSRedis.Internal.Commands
{
    class RedisXRangeCommand : RedisCommand<(string id, string[] items)[]>
    {

        public RedisXRangeCommand(string command, params object[] args)
            : base(command, args)
        {
        }

        public override (string id, string[] items)[] Parse(RedisReader reader)
        {
            (string id, string[] items)[] ret = null;

            reader.ExpectType(RedisMessage.MultiBulk);
            long count = reader.ReadInt(false);
            if (count == -1) return ret;
            ret = new (string id, string[] items)[count];

            for (var a = 0; a < count; a++)
            {
                reader.ExpectType(RedisMessage.MultiBulk);
                var lvl2Count = reader.ReadInt(false);
                if (lvl2Count != 2) throw new RedisProtocolException("XRange/XRevRange 数据格式 2级 MultiBulk 长度应该为 2");

                var id = reader.ReadBulkString();

                reader.ExpectType(RedisMessage.MultiBulk);
                var lvl3Count = reader.ReadInt(false);

                var items = new string[lvl3Count];
                for (var e = 0; e < lvl3Count; e++)
                {
                    items[e] = reader.ReadBulkString();
                }

                ret[a] = (id, items);
            }

            return ret;
        }
    }
}
