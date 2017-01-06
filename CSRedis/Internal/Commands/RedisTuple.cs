using CSRedis.Internal.IO;
using System;
using System.ComponentModel;
using System.IO;

namespace CSRedis.Internal.Commands
{
    class RedisTuple : RedisCommand<Tuple<string, string>>
    {
        public RedisTuple(string command, params object[] args)
            : base(command, args)
        { }

        public override Tuple<string, string> Parse(RedisReader reader)
        {
            reader.ExpectType(RedisMessage.MultiBulk);
            reader.ExpectSize(2);
            return Tuple.Create(reader.ReadBulkString(), reader.ReadBulkString());
        }

        public abstract class Generic<T1, T2> : RedisCommand<Tuple<T1, T2>>
        {
            readonly RedisCommand<T1> _command1;
            readonly RedisCommand<T2> _command2;

            protected Generic(RedisCommand<T1> command1, RedisCommand<T2> command2, string command, params object[] args) 
                : base(command, args)
            {
                _command1 = command1;
                _command2 = command2;
            }

            protected Tuple<T1, T2> Create(RedisReader reader)
            {
                return Tuple.Create(_command1.Parse(reader), _command2.Parse(reader));
            }

            public class Repeating : Generic<T1, T2>
            {
                public Repeating(RedisCommand<T1> command1, RedisCommand<T2> command2, string command, params object[] args)
                    : base(command1, command2, command, args)
                { }

                public override Tuple<T1, T2> Parse(RedisReader reader)
                {
                    return Create(reader);
                }
            }

            public class Single : Generic<T1, T2>
            {
                public Single(RedisCommand<T1> command1, RedisCommand<T2> command2, string command, params object[] args)
                    : base(command1, command2, command, args)
                { }

                public override Tuple<T1, T2> Parse(RedisReader reader)
                {
                    reader.ExpectMultiBulk(2);
                    return Create(reader);
                }
            }
        }
    }
}
