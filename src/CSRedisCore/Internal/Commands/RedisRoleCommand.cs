
using CSRedis.Internal.IO;
using CSRedis.Internal.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
namespace CSRedis.Internal.Commands
{
    class RedisRoleCommand : RedisCommand<RedisRole>
    {
        public RedisRoleCommand(string command, params object[] args)
            : base(command, args)
        { }

        public override RedisRole Parse(RedisReader reader)
        {
            reader.ExpectType(RedisMessage.MultiBulk);
            int count = (int)reader.ReadInt(false);

            string role = reader.ReadBulkString();
            switch (role)
            {
                case "master":
                    return ParseMaster(count, role, reader);
                case "slave":
                    return ParseSlave(count, role, reader);
                case "sentinel":
                    return ParseSentinel(count, role, reader);
                default:
                    throw new RedisProtocolException("Unexpected role: " + role);
            }
        }

        static RedisMasterRole ParseMaster(int num, string role, RedisReader reader)
        {
            reader.ExpectSize(3, num);
            long offset = reader.ReadInt();
            reader.ExpectType(RedisMessage.MultiBulk);
            var slaves = new Tuple<string, int, int>[reader.ReadInt(false)];
            for (int i = 0; i < slaves.Length; i++)
            {
                reader.ExpectType(RedisMessage.MultiBulk);
                reader.ExpectSize(3);
                string ip = reader.ReadBulkString();
                int port = Int32.Parse(reader.ReadBulkString());
                int slave_offset = Int32.Parse(reader.ReadBulkString());
                slaves[i] = new Tuple<string, int, int>(ip, port, slave_offset);
            }
            return new RedisMasterRole(role, offset, slaves);
        }
        static RedisSlaveRole ParseSlave(int num, string role, RedisReader reader)
        {
            reader.ExpectSize(5, num);
            string master_ip = reader.ReadBulkString();
            int port = (int)reader.ReadInt();
            string state = reader.ReadBulkString();
            long data = reader.ReadInt();
            return new RedisSlaveRole(role, master_ip, port, state, data);
        }
        static RedisSentinelRole ParseSentinel(int num, string role, RedisReader reader)
        {
            reader.ExpectSize(2, num);
            reader.ExpectType(RedisMessage.MultiBulk);
            string[] masters = new string[reader.ReadInt(false)];
            for (int i = 0; i < masters.Length; i++)
                masters[i] = reader.ReadBulkString();
            return new RedisSentinelRole(role, masters);
        }
    }
}
