using CSRedis.Internal.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSRedis.Internal
{
    class RedisTransaction
    {
        readonly RedisConnector _connector;
        readonly RedisArray _execCommand;
        readonly List<Tuple<string, object[]>> _pipeCommands = new List<Tuple<string, object[]>>();

        public event EventHandler<RedisTransactionQueuedEventArgs> TransactionQueued;

        bool _active;
        public bool Active { get { return _active; } }

        public RedisTransaction(RedisConnector connector)
        {
            _connector = connector;
            _execCommand = RedisCommands.Exec();
        }

        public string Start()
        {
            _active = true;
            return _connector.Call(RedisCommands.Multi());
        }

        public Task<string> StartAsync()
        {
            _active = true;
            return _connector.CallAsync(RedisCommands.Multi());
        }

        public T Write<T>(RedisCommand<T> command)
        {
            string response = _connector.Call(RedisCommands.AsTransaction(command));
            OnTransactionQueued(command, response);

            _execCommand.AddParser(x => command.Parse(x));
            return default(T);
        }

        public Task<T> WriteAsync<T>(RedisCommand<T> command)
        {
            lock (_execCommand)
            {
                _execCommand.AddParser(x => command.Parse(x));
                return _connector.CallAsync(RedisCommands.AsTransaction(command))
                    .ContinueWith(t => OnTransactionQueued(command, t.Result))
                    .ContinueWith(t => default(T));
            }
        }

        public object[] Execute()
        {
            _active = false;

            if (_connector.IsConnected && _connector.IsPipelined)
            {
                _connector.Call(_execCommand);
                object[] response = _connector.EndPipe();
                for (int i = 1; i < response.Length - 1; i++)
                    OnTransactionQueued(_pipeCommands[i - 1].Item1, _pipeCommands[i - 1].Item2, response[i - 1].ToString());
                
                object transaction_response = response[response.Length - 1];
                if (!(transaction_response is object[]))
                    throw new RedisProtocolException("Unexpected response");

                return transaction_response as object[];
            }

            return _connector.Call(_execCommand);
        }

        public Task<object[]> ExecuteAsync()
        {
            _active = false;
            return _connector.CallAsync(_execCommand);
        }

        public string Abort()
        {
            _active = false;
            return _connector.Call(RedisCommands.Discard());
        }

        public Task<string> AbortAsync()
        {
            _active = false;
            return _connector.CallAsync(RedisCommands.Discard());
        }

        void OnTransactionQueued<T>(RedisCommand<T> command, string response)
        {
            if (_connector.IsPipelined)
                _pipeCommands.Add(Tuple.Create(command.Command, command.Arguments));
            else
                OnTransactionQueued(command.Command, command.Arguments, response);
        }

        void OnTransactionQueued(string command, object[] args, string response)
        {
            if (TransactionQueued != null)
                TransactionQueued(this, new RedisTransactionQueuedEventArgs(response, command, args));
        }
    }
}
