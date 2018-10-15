using CSRedis.Internal.IO;
using System;
using System.IO;

namespace CSRedis.Internal.Commands
{
    class RedisDate : RedisCommand<DateTime>
    {
        static readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public RedisDate(string command, params object[] args)
            : base(command, args)
        { }

        public override DateTime Parse(RedisReader reader)
        {
            return FromTimestamp(reader.ReadInt());
        }

        public class Micro : RedisCommand<DateTime>
        {
            public Micro(string command, params object[] args)
                : base(command, args)
            { }

            public override DateTime Parse(RedisReader reader)
            {
                reader.ExpectType(RedisMessage.MultiBulk);
                reader.ExpectSize(2);

                int timestamp = Int32.Parse(reader.ReadBulkString());
                int microseconds = Int32.Parse(reader.ReadBulkString());

                return FromTimestamp(timestamp, microseconds);
            }

            public static DateTime FromTimestamp(long timestamp, long microseconds)
            {
                return RedisDate.FromTimestamp(timestamp) + FromMicroseconds(microseconds);
            }
            

            public static TimeSpan FromMicroseconds(long microseconds)
            {
                return TimeSpan.FromTicks(microseconds * (TimeSpan.TicksPerMillisecond / 1000));
            }

            public static long ToMicroseconds(TimeSpan span)
            {
                return span.Ticks / (TimeSpan.TicksPerMillisecond / 1000);
            }
        }

        public static DateTime FromTimestamp(long seconds)
        {
            return _epoch + TimeSpan.FromSeconds(seconds);
        }

        public static TimeSpan ToTimestamp(DateTime date)
        {
            return date - _epoch;
        }
    }
}
