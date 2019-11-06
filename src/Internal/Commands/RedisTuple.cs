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
            long count = reader.ReadInt(false);
            if (count != 2) return null; //使用 BLPop 命令在 RedisArray.cs 中报错的解决办法。 #22
                                         //reader.ExpectSize(2);
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

        public abstract class Generic<T1, T2, T3> : RedisCommand<Tuple<T1, T2, T3>>
        {
            readonly RedisCommand<T1> _command1;
            readonly RedisCommand<T2> _command2;
            readonly RedisCommand<T3> _command3;

            protected Generic(RedisCommand<T1> command1, RedisCommand<T2> command2, RedisCommand<T3> command3, string command, params object[] args)
                : base(command, args)
            {
                _command1 = command1;
                _command2 = command2;
                _command3 = command3;
            }

            protected Tuple<T1, T2, T3> Create(RedisReader reader)
            {
                return Tuple.Create(_command1.Parse(reader), _command2.Parse(reader), _command3.Parse(reader));
            }

            public class Repeating : Generic<T1, T2, T3>
            {
                public Repeating(RedisCommand<T1> command1, RedisCommand<T2> command2, RedisCommand<T3> command3, string command, params object[] args)
                    : base(command1, command2, command3, command, args) { }

                public override Tuple<T1, T2, T3> Parse(RedisReader reader)
                {
                    return Create(reader);
                }
            }

            public class Single : Generic<T1, T2, T3>
            {
                public Single(RedisCommand<T1> command1, RedisCommand<T2> command2, RedisCommand<T3> command3, string command, params object[] args)
                    : base(command1, command2, command3, command, args) { }

                public override Tuple<T1, T2, T3> Parse(RedisReader reader)
                {
                    reader.ExpectMultiBulk(3);
                    return Create(reader);
                }
            }
        }
        public abstract class Generic<T1, T2, T3, T4> : RedisCommand<Tuple<T1, T2, T3, T4>>
        {
            readonly RedisCommand<T1> _command1;
            readonly RedisCommand<T2> _command2;
            readonly RedisCommand<T3> _command3;
            readonly RedisCommand<T4> _command4;

            protected Generic(RedisCommand<T1> command1, RedisCommand<T2> command2, RedisCommand<T3> command3, RedisCommand<T4> command4, string command, params object[] args)
                : base(command, args)
            {
                _command1 = command1;
                _command2 = command2;
                _command3 = command3;
                _command4 = command4;
            }

            protected Tuple<T1, T2, T3, T4> Create(RedisReader reader)
            {
                return Tuple.Create(
                    _command1.Parse(reader),
                    _command2 == null ? default(T2) : _command2.Parse(reader),
                    _command3 == null ? default(T3) : _command3.Parse(reader),
                    _command4 == null ? default(T4) : _command4.Parse(reader));
            }

            public class Repeating : Generic<T1, T2, T3, T4>
            {
                public Repeating(RedisCommand<T1> command1, RedisCommand<T2> command2, RedisCommand<T3> command3, RedisCommand<T4> command4, string command, params object[] args)
                    : base(command1, command2, command3, command4, command, args) { }

                public override Tuple<T1, T2, T3, T4> Parse(RedisReader reader)
                {
                    return Create(reader);
                }
            }

            public class Single : Generic<T1, T2, T3, T4>
            {
                public Single(RedisCommand<T1> command1, RedisCommand<T2> command2, RedisCommand<T3> command3, RedisCommand<T4> command4, string command, params object[] args)
                    : base(command1, command2, command3, command4, command, args) { }

                public override Tuple<T1, T2, T3, T4> Parse(RedisReader reader)
                {
                    var size = 1 + (_command2 == null ? 0 : 1) + (_command3 == null ? 0 : 1) + (_command4 == null ? 0 : 1);
                    if (size > 1) reader.ExpectMultiBulk(size);
                    return Create(reader);
                }
            }
        }
    }
}
