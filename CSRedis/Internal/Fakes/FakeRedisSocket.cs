using CSRedis.Internal.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CSRedis.Internal.Fakes
{
    class FakeRedisSocket : IRedisSocket
    {
        bool _connected;
        readonly FakeStream _stream;

        public bool Connected { get { return _connected; } }

        public int ReceiveTimeout { get; set; }

        public int SendTimeout { get; set; }

        public FakeRedisSocket(params string[] responses)
            : this(Encoding.UTF8, responses)
        { }

        public FakeRedisSocket(Encoding encoding, params string[] responses)
            : this(ToBytes(encoding, responses))
        { }

        public FakeRedisSocket(params byte[][] responses)
        {
            _stream = new FakeStream();
            foreach (var response in responses)
                _stream.AddResponse(response);
        }

        public void Connect(EndPoint endpoint)
        {
            _connected = true;
        }

        public bool ConnectAsync(SocketAsyncEventArgs args)
        {
            return false;
        }

        public bool SendAsync(SocketAsyncEventArgs args)
        {
            _stream.Write(args.Buffer, args.Offset, args.Count);
            return false;
        }

        public Stream GetStream()
        {
            return _stream;
        }

        public void Dispose()
        {
            _stream.Dispose();
            _connected = false;
        }

        public string GetMessage()
        {
            return GetMessage(Encoding.UTF8);
        }
        public string GetMessage(Encoding encoding)
        {
            return encoding.GetString(_stream.GetMessage());
        }

        static byte[][] ToBytes(Encoding encoding, string[] strings)
        {
            byte[][] set = new byte[strings.Length][];
            for (int i = 0; i < strings.Length; i++)
                set[i] = encoding.GetBytes(strings[i]);
            return set;
        }
    }
}
