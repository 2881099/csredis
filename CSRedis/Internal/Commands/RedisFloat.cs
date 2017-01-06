using CSRedis.Internal.IO;
using System;
using System.Globalization;

namespace CSRedis.Internal.Commands
{
    class RedisFloat : RedisCommand<double>
    {
        public RedisFloat(string command, params object[] args)
            : base(command, args)
        { }

        public override double Parse(RedisReader reader)
        {
            return FromString(reader.ReadBulkString());
        }

        static double FromString(string input)
        {
            return Double.Parse(input, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        public class Nullable : RedisCommand<double?>
        {
            public Nullable(string command, params object[] args)
                : base(command, args)
            { }

            public override double? Parse(RedisReader reader)
            {
                string result = reader.ReadBulkString();
                if (result == null)
                    return null;
                return RedisFloat.FromString(result);
            }
        }
    }
}
