using CSRedis.Internal.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CSRedis.Internal.Commands
{
    class RedisXReadCommand : RedisCommand<(string key, (string id, string[] items)[] data)[]>
    {

        public RedisXReadCommand(string command, params object[] args)
            : base(command, args)
        {
        }

        public override (string key, (string id, string[] items)[] data)[] Parse(RedisReader reader)
        {
            (string key, (string id, string[] items)[])[] ret = null;

            reader.ExpectType(RedisMessage.MultiBulk);
            var count = reader.ReadInt(false);
            if (count == -1) return ret;
            ret = new (string key, (string id, string[] items)[] data)[count];

            for (var a = 0; a < count; a++)
            {
                reader.ExpectType(RedisMessage.MultiBulk);
                var lvl2Count = reader.ReadInt(false);
                if (lvl2Count != 2) throw new RedisProtocolException("XRead 数据格式 2级 MultiBulk 长度应该为 2");

                var key = reader.ReadBulkString();

                reader.ExpectType(RedisMessage.MultiBulk);
                var lvl3Count = reader.ReadInt(false);

                var data = new (string id, string[] items)[lvl3Count];
                for (var c = 0; c < lvl3Count; c++)
                {
                    reader.ExpectType(RedisMessage.MultiBulk);
                    var lvl4Count = reader.ReadInt(false);
                    if (lvl4Count != 2) throw new RedisProtocolException("XRead 数据格式 4级 MultiBulk 长度应该为 2");

                    var id = reader.ReadBulkString();

                    reader.ExpectType(RedisMessage.MultiBulk);
                    var lvl5Count = reader.ReadInt(false);

                    var items = new string[lvl5Count];
                    for (var e = 0; e < lvl5Count; e++)
                    {
                        items[e] = reader.ReadBulkString();
                    }

                    data[c] = (id, items);
                }

                ret[a] = (key, data);
            }

            return ret;
        }
    }
}
