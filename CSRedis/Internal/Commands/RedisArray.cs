using CSRedis.Internal.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CSRedis.Internal.Commands
{
    class RedisArray : RedisCommand<object[]>
    {
        readonly Queue<Func<RedisReader, object>> _parsers = new Queue<Func<RedisReader, object>>();

        public RedisArray(string command, params object[] args)
            : base(command, args)
        { }

        public override object[] Parse(RedisReader reader)
        {
            if (_parsers.Count == 0)
                return reader.ReadMultiBulk(bulkAsString: true);

            reader.ExpectType(RedisMessage.MultiBulk);
            long count = reader.ReadInt(false);
            if (count != _parsers.Count)
                throw new RedisProtocolException(String.Format("Expecting {0} array items; got {1}", _parsers.Count, count));

            object[] results = new object[_parsers.Count];
            for (int i = 0; i < results.Length; i++)
                results[i] = _parsers.Dequeue()(reader);
            return results;
        }

        public void AddParser(Func<RedisReader, object> parser)
        {
            _parsers.Enqueue(parser);
        }

        public class Generic<T> : RedisCommand<T[]>
        {
            readonly RedisCommand<T> _memberCommand;

            protected RedisCommand<T> MemberCommand { get { return _memberCommand; } }

            public Generic(RedisCommand<T> memberCommand)
                : base(memberCommand.Command, memberCommand.Arguments)
            {
                _memberCommand = memberCommand;
            }

            public override T[] Parse(RedisReader reader)
            {
                reader.ExpectType(RedisMessage.MultiBulk);
                long count = reader.ReadInt(false);
                return Read(count, reader);
            }

            protected virtual T[] Read(long count, RedisReader reader)
            {
                T[] array = new T[count];
                for (int i = 0; i < array.Length; i++)
                    array[i] = _memberCommand.Parse(reader);
                return array;
            }
        }

        public class IndexOf<T> : RedisCommand<T>
        {
            readonly RedisCommand<T> _command;
            readonly int _index;

            public IndexOf(RedisCommand<T> command, int index)
                : base(command.Command, command.Arguments)
            {
                _command = command;
                _index = index;
            }

            public override T Parse(RedisReader reader)
            {
                reader.ExpectType(RedisMessage.MultiBulk);
                long count = reader.ReadInt(false);
                T[] array = new T[count];
                for (int i = 0; i < array.Length; i++)
                    array[i] = _command.Parse(reader);
                return array[_index];
            }
        }

        public class Strings : Generic<string>
        {
            public Strings(string command, params object[] args)
                : base(new RedisString(command, args))
            { }
        }

        public class StrongPairs<T1, T2> : Generic<Tuple<T1, T2>>
        {
            public StrongPairs(RedisCommand<T1> command1, RedisCommand<T2> command2, string command, params object[] args)
                : base(new RedisTuple.Generic<T1, T2>.Repeating(command1, command2, command, args))
            { }

            protected override Tuple<T1, T2>[] Read(long count, RedisReader reader)
            {
                var array = new Tuple<T1, T2>[count / 2];
                for (int i = 0; i < count; i += 2)
                    array[i / 2] = MemberCommand.Parse(reader);
                return array;
            }
        }

        public class WeakPairs<T1, T2> : StrongPairs<T1, T2>
        {
            public WeakPairs(string command, params object[] args)
                : base(new RedisString.Converter<T1>(null), new RedisString.Converter<T2>(null), command, args)
            { }
        }
    }
}
