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
                if (lvl2Count != 2) throw new RedisProtocolException("XRange/XRevRange/XClaim 返回数据格式 2级 MultiBulk 长度应该为 2");

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
                if (lvl2Count != 2) throw new RedisProtocolException("XRead 返回数据格式 2级 MultiBulk 长度应该为 2");

                var key = reader.ReadBulkString();

                reader.ExpectType(RedisMessage.MultiBulk);
                var lvl3Count = reader.ReadInt(false);

                var data = new (string id, string[] items)[lvl3Count];
                for (var c = 0; c < lvl3Count; c++)
                {
                    reader.ExpectType(RedisMessage.MultiBulk);
                    var lvl4Count = reader.ReadInt(false);
                    if (lvl4Count != 2) throw new RedisProtocolException("XRead 返回数据格式 4级 MultiBulk 长度应该为 2");

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


    class RedisXPendingCommand : RedisCommand<(long count, string minId, string maxId, (string consumer, long count)[] pendings)>
    {
        public RedisXPendingCommand(string command, params object[] args)
            : base(command, args)
        {
        }

        public override (long count, string minId, string maxId, (string consumer, long count)[] pendings) Parse(RedisReader reader)
        {
            reader.ExpectType(RedisMessage.MultiBulk);
            long count = reader.ReadInt(false);
            if (count != 4) throw new RedisProtocolException("XPedding 返回数据格式 1级 MultiBulk 长度应该为 4");

            var retCount = reader.ReadInt();
            var minId = reader.ReadBulkString();
            var maxId = reader.ReadBulkString();

            reader.ExpectType(RedisMessage.MultiBulk);
            var lvl2Count = reader.ReadInt(false);
            var pendings = new (string consumer, long count)[lvl2Count];

            for (var a = 0; a < lvl2Count; a++)
            {
                reader.ExpectType(RedisMessage.MultiBulk);
                var lvl3Count = reader.ReadInt(false);
                if (lvl3Count != 2) throw new RedisProtocolException("XPedding 返回数据格式 3级 MultiBulk 长度应该为 2");

                pendings[a] = (reader.ReadBulkString(), long.Parse(reader.ReadBulkString()));
            }

            return (retCount, minId, maxId, pendings);
        }
    }
    class RedisXPendingStartEndCountCommand : RedisCommand<(string id, string consumer, long idle, long transferTimes)[]>
    {
        public RedisXPendingStartEndCountCommand(string command, params object[] args)
            : base(command, args)
        {
        }

        public override (string id, string consumer, long idle, long transferTimes)[] Parse(RedisReader reader)
        {
            reader.ExpectType(RedisMessage.MultiBulk);
            long count = reader.ReadInt(false);
            var ret = new (string id, string consumer, long idle, long transferTimes)[count];

            for (var a = 0; a < count; a++)
            {
                var id = reader.ReadBulkString();
                var consumer = reader.ReadBulkString();
                var idle = reader.ReadInt();
                var transferTimes = reader.ReadInt();
                ret[a] = (id, consumer, idle, transferTimes);
            }

            return ret;
        }
    }


    class RedisXInfoStreamCommand : RedisCommand<(long length, long radixTreeKeys, long radixTreeNodes, long groups, string lastGeneratedId, (string id, string[] items) firstEntry, (string id, string[] items) lastEntry)>
    {
        public RedisXInfoStreamCommand(string command, params object[] args)
            : base(command, args)
        {
        }

        public override (long length, long radixTreeKeys, long radixTreeNodes, long groups, string lastGeneratedId, (string id, string[] items) firstEntry, (string id, string[] items) lastEntry) Parse(RedisReader reader)
        {
            reader.ExpectType(RedisMessage.MultiBulk);
            long count = reader.ReadInt(false);
            if (count % 2 != 0) throw new RedisProtocolException("XInfo Stream 返回数据格式 1级 MultiBulk 长度应该为偶数");

            (long length, long radixTreeKeys, long radixTreeNodes, long groups, string lastGeneratedId, (string id, string[] items) firstEntry, (string id, string[] items) lastEntry) ret = default((long length, long radixTreeKeys, long radixTreeNodes, long groups, string lastGeneratedId, (string id, string[] items) firstEntry, (string id, string[] items) lastEntry));

            for (var a = 0; a < count; a += 2)
            {
                var key = reader.ReadBulkString().ToLower();
                switch (key)
                {
                    case "length":
                        ret.length = reader.ReadInt();
                        break;
                    case "radix-tree-keys":
                        ret.radixTreeKeys = reader.ReadInt();
                        break;
                    case "radix-tree-nodes":
                        ret.radixTreeNodes = reader.ReadInt();
                        break;
                    case "groups":
                        ret.groups = reader.ReadInt();
                        break;
                    case "last-generated-id":
                        ret.lastGeneratedId = reader.ReadBulkString();
                        break;
                    case "first-entry":
                    case "last-entry":
                        reader.ExpectType(RedisMessage.MultiBulk);
                        var lvl2Count = reader.ReadInt(false);
                        if (lvl2Count != 2) throw new RedisProtocolException("XInfo Stream 返回数据格式 2级 MultiBulk 长度应该为 2");

                        var id = reader.ReadBulkString();

                        reader.ExpectType(RedisMessage.MultiBulk);
                        var lvl3Count = reader.ReadInt(false);

                        var items = new string[lvl3Count];
                        for (var e = 0; e < lvl3Count; e++)
                            items[e] = reader.ReadBulkString();

                        if (key == "first-entry") ret.firstEntry = (id, items);
                        else ret.lastEntry = (id, items);
                        break;
                    default:
                        reader.ReadBulkString();
                        break;
                }
            }
            return ret;
        }
    }
    class RedisXInfoGroupsCommand : RedisCommand<(string name, long consumers, long pending, string lastDeliveredId)[]>
    {
        public RedisXInfoGroupsCommand(string command, params object[] args)
            : base(command, args)
        {
        }

        public override (string name, long consumers, long pending, string lastDeliveredId)[] Parse(RedisReader reader)
        {
            reader.ExpectType(RedisMessage.MultiBulk);
            long count = reader.ReadInt(false);
            var ret = new (string name, long consumers, long pending, string lastDeliveredId)[count];

            for (var a = 0; a < count; a++)
            {
                reader.ExpectType(RedisMessage.MultiBulk);
                long lvl2Count = reader.ReadInt(false);
                if (lvl2Count % 2 != 0) throw new RedisProtocolException("XInfo Groups 返回数据格式 2级 MultiBulk 长度应该为偶数");

                for (var b = 0; b < lvl2Count; b+= 2)
                {
                    var key = reader.ReadBulkString().ToLower();
                    switch (key)
                    {
                        case "name":
                            ret[a].name = reader.ReadBulkString();
                            break;
                        case "consumers":
                            ret[a].consumers = reader.ReadInt();
                            break;
                        case "pending":
                            ret[a].pending = reader.ReadInt();
                            break;
                        case "last-delivered-id":
                            ret[a].lastDeliveredId = reader.ReadBulkString();
                            break;
                        default:
                            reader.ReadBulkString();
                            break;
                    }
                }
            }
            return ret;
        }
    }
    class RedisXInfoConsumersCommand : RedisCommand<(string name, long pending, long idle)[]>
    {
        public RedisXInfoConsumersCommand(string command, params object[] args)
            : base(command, args)
        {
        }

        public override (string name, long pending, long idle)[] Parse(RedisReader reader)
        {
            reader.ExpectType(RedisMessage.MultiBulk);
            long count = reader.ReadInt(false);
            var ret = new (string name, long pending, long idle)[count];

            for (var a = 0; a < count; a++)
            {
                reader.ExpectType(RedisMessage.MultiBulk);
                long lvl2Count = reader.ReadInt(false);
                if (lvl2Count % 2 != 0) throw new RedisProtocolException("XInfo Consumers 返回数据格式 2级 MultiBulk 长度应该为偶数");

                for (var b = 0; b < lvl2Count; b += 2)
                {
                    var key = reader.ReadBulkString().ToLower();
                    switch (key)
                    {
                        case "name":
                            ret[a].name = reader.ReadBulkString();
                            break;
                        case "pending":
                            ret[a].pending = reader.ReadInt();
                            break;
                        case "idle":
                            ret[a].idle = reader.ReadInt();
                            break;
                        default:
                            reader.ReadBulkString();
                            break;
                    }
                }
            }
            return ret;
        }
    }
}

