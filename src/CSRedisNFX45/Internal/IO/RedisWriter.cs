using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CSRedis.Internal.IO
{
    class RedisWriter
    {
        const char Bulk = (char)RedisMessage.Bulk;
        const char MultiBulk = (char)RedisMessage.MultiBulk;
		const string EOL = "\r\n";

		readonly RedisIO _io;

        public RedisWriter(RedisIO io)
        {
            _io = io;
		}

        public int Write(RedisCommand command, Stream stream)
        {
            byte[] data = Prepare(command);
            stream.Write(data, 0, data.Length);
			//Console.WriteLine($"WriteSync: {Encoding.UTF8.GetString(data)}");
			return data.Length;
        }

        public int Write(RedisCommand command, byte[] buffer, int offset)
		{
			int b = 0;
			byte[] data = Prepare(command);
			var dataLen = data.Length;
			var bufferLen = buffer.Length;
			if (dataLen > bufferLen - offset) throw new Exception($"发送数据长度 {dataLen} 大于 异步写入缓冲块大小 {bufferLen - offset}，请设置连接串参数：writeBuffer");
			for (int a = offset; a < bufferLen && b < data.Length; a++, b++) buffer[a] = data[b];
			//Console.WriteLine($"WriteAsync: {Encoding.UTF8.GetString(data)}");
			return b;
        }

        public byte[] Prepare(RedisCommand command)
        {
            var parts = command.Command.Split(' ');
            int length = parts.Length + command.Arguments.Length;
			StringBuilder sb = new StringBuilder();
			sb.Append(MultiBulk).Append(length).Append(EOL);

			foreach (var part in parts)
                sb.Append(Bulk).Append(_io.Encoding.GetByteCount(part)).Append(EOL).Append(part).Append(EOL);

			MemoryStream ms = new MemoryStream();
			var data = _io.Encoding.GetBytes(sb.ToString());
			ms.Write(data, 0, data.Length);

			foreach (var arg in command.Arguments)
            {
				if (arg != null && arg.GetType() == typeof(byte[])) {
					data = arg as byte[];
					var data2 = _io.Encoding.GetBytes($"{Bulk}{data.Length}{EOL}");
					ms.Write(data2, 0, data2.Length);
					ms.Write(data, 0, data.Length);
					ms.Write(new byte[] { 13, 10 }, 0, 2);
				} else {
					string str = String.Format(CultureInfo.InvariantCulture, "{0}", arg);
					data = _io.Encoding.GetBytes($"{Bulk}{_io.Encoding.GetByteCount(str)}{EOL}{str}{EOL}");
					ms.Write(data, 0, data.Length);
				}
				//string str = String.Format(CultureInfo.InvariantCulture, "{0}", arg);
                //sb.Append(Bulk).Append(_io.Encoding.GetByteCount(str)).Append(EOL).Append(str).Append(EOL);
            }

            return ms.ToArray();
        }
    }
}
