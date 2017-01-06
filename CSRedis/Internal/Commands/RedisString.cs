
using CSRedis.Internal.IO;
using System;
using System.ComponentModel;
namespace CSRedis.Internal.Commands
{
    class RedisString : RedisCommand<string>
    {
        public RedisString(string command, params object[] args)
            : base(command, args)
        { }

        public override string Parse(RedisReader reader)
        {
            return reader.ReadBulkString();
        }

        public class Nullable : RedisString
        {
            public Nullable(string command, params object[] args)
                : base(command, args)
            { }

            public override string Parse(RedisReader reader)
            {
                RedisMessage type = reader.ReadType();
                if (type == RedisMessage.Bulk)
                    return reader.ReadBulkString(false);
                reader.ReadMultiBulk(false);
                return null;
            }
        }

        public class Integer : RedisCommand<int>
        {
            public Integer(string command, params object[] args)
                : base(command, args)
            { }

            public override int Parse(RedisReader reader)
            {
                return Int32.Parse(reader.ReadBulkString());
            }
        }

        public class Converter<T> : RedisCommand<T>
        {
            static Lazy<TypeConverter> converter
                = new Lazy<TypeConverter>(() => TypeDescriptor.GetConverter(typeof(T)));

            public Converter(string command, params object[] args)
                : base(command, args)
            { }

            public override T Parse(RedisReader reader)
            {
                return (T)converter.Value.ConvertFromInvariantString(reader.ReadBulkString());
            }
        }
    }
}
