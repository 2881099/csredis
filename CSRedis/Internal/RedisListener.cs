using CSRedis.Internal.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace CSRedis.Internal
{
    abstract class RedisListner<TResponse>
    {
        readonly RedisConnector _connection;

        public bool Listening { get; private set; }
        protected RedisConnector Connection { get { return _connection; } }

        public RedisListner(RedisConnector connection)
        {
            _connection = connection;
        }

        protected void Listen(Func<RedisReader, TResponse> func)
        {
            Listening = true;
            do
            {
                try
                {
                    TResponse value = _connection.Read(func);
                    OnParsed(value);
                }
                catch (IOException)
                {
                    if (_connection.IsConnected)
                        throw;
                    break;
                }
            } while (Continue());
            Listening = false;
        }

        protected void Write<T>(RedisCommand<T> command)
        {
            _connection.Write(command);
        }

        protected T Call<T>(RedisCommand<T> command)
        {
            return _connection.Call(command);
        }

        protected abstract void OnParsed(TResponse value);
        protected abstract bool Continue();
    }
}
