using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CSRedis.Internal.Fakes
{
    class FakeStream : MemoryStream
    {
        readonly Queue<byte[]> _responses;
        readonly Queue<byte[]> _messages;

        public FakeStream()
        {
            _responses = new Queue<byte[]>();
            _messages = new Queue<byte[]>();
        }

        public void AddResponse(byte[] response)
        {
            _responses.Enqueue(response);
        }

        public byte[] GetMessage()
        {
            return _messages.Dequeue();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_responses)
            {
                byte[] message = new byte[count];
                Buffer.BlockCopy(buffer, offset, message, 0, count);
                _messages.Enqueue(message);
                base.Write(buffer, offset, count);

                byte[] next = _responses.Dequeue();
                base.Write(next, 0, next.Length);
                Position -= next.Length;
            }
        }

        

        // todo: use Monitor.Wait() to simulate blocking thread on Read()
    }
}
