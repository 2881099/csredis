using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Text;

namespace CSRedis.Internal
{
    public static class Resp3Helper
    {
        public static object Read(Stream stream) => Read(stream, null);
        public static object Read(Stream stream, Encoding encoding) => new Resp3Reader(stream, encoding).ReadObject().ReturnValue;

        public static void Write(Stream stream, object data) => Write(stream, null, data);
        public static void Write(Stream stream, Encoding encoding, object data) => new Resp3Writer(stream, encoding).WriteObject(data);

        public static object Deserialize(string resp3Text)
        {
            using (var ms = new MemoryStream())
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(resp3Text);
                    ms.Write(bytes, 0, bytes.Length);
                    ms.Position = 0;
                    return Read(ms, Encoding.UTF8);
                }
                finally
                {
                    ms.Close();
                }
            }
        }
        public static string Serialize(object data)
        {
            using (var ms = new MemoryStream())
            {
                try
                {
                    Write(ms, Encoding.UTF8, data);
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
                finally
                {
                    ms.Close();
                }
            }
        }

        class Resp3Reader
        {
            Stream _stream;
            Encoding _encoding;

            public Resp3Reader(Stream stream, Encoding encoding)
            {
                _stream = stream;
                _encoding = encoding ?? Encoding.UTF8;
            }

            string ReadBlobString(char msgtype)
            {
                MemoryStream ms = null;
                try
                {
                    ms = new MemoryStream();
                    var lenstr = ReadLine(null);
                    if (int.TryParse(lenstr, out var len))
                    {
                        Read(ms, len);
                        ReadLine(null);
                        return _encoding.GetString(ms.ToArray());
                    }
                    if (lenstr == "?")
                    {
                        while (true)
                        {
                            char c = (char)_stream.ReadByte();
                            if (c != ';') throw new ProtocolViolationException($"Expecting fail Streamed strings ';', got '{c}'");
                            var clenstr = ReadLine(null);
                            if (int.TryParse(clenstr, out var clen))
                            {
                                if (clen == 0) break;
                                if (clen > 0)
                                {
                                    Read(ms, clen);
                                    ReadLine(null);
                                    continue;
                                }
                            }
                            throw new ProtocolViolationException($"Expecting fail Streamed strings ';0', got ';{clenstr}'");
                        }
                        return _encoding.GetString(ms.ToArray());
                    }
                    throw new ProtocolViolationException($"Expecting fail Blob string '{msgtype}0', got '{msgtype}{lenstr}'");
                }
                finally
                {
                    ms?.Close();
                    ms?.Dispose();
                }
            }
            string ReadSimpleString()
            {
                MemoryStream ms = null;
                try
                {
                    ms = new MemoryStream();
                    ReadLine(ms);
                    return _encoding.GetString(ms.ToArray());
                }
                finally
                {
                    ms?.Close();
                    ms?.Dispose();
                }
            }
            long ReadNumber(char msgtype)
            {
                var numstr = ReadLine(null);
                if (long.TryParse(numstr, out var num)) return num;
                throw new ProtocolViolationException($"Expecting fail Number '{msgtype}0', got '{msgtype}{numstr}'");
            }
            BigInteger ReadBigNumber(char msgtype)
            {
                var numstr = ReadLine(null);
                if (BigInteger.TryParse(numstr, NumberStyles.Any, null, out var num)) return num;
                throw new ProtocolViolationException($"Expecting fail Number '{msgtype}0', got '{msgtype}{numstr}'");
            }
            double ReadDouble(char msgtype)
            {
                var numstr = ReadLine(null);
                switch (numstr)
                {
                    case "inf": return double.PositiveInfinity;
                    case "-inf": return double.NegativeInfinity;
                }
                if (double.TryParse(numstr, NumberStyles.Any, null, out var num)) return num;
                throw new ProtocolViolationException($"Expecting fail Double '{msgtype}1.23', got '{msgtype}{numstr}'");
            }
            bool ReadBoolean(char msgtype)
            {
                var boolstr = ReadLine(null);
                switch (boolstr)
                {
                    case "t": return true;
                    case "f": return false;
                }
                throw new ProtocolViolationException($"Expecting fail Double '{msgtype}t', got '{msgtype}{boolstr}'");
            }

            List<object> ReadArray(char msgtype)
            {
                var arr = new List<object>();
                var lenstr = ReadLine(null);
                if (int.TryParse(lenstr, out var len))
                {
                    for (var a = 0; a < len; a++)
                        arr.Add(ReadObject().ReturnValue);
                    return arr;
                }
                if (lenstr == "?")
                {
                    while (true)
                    {
                        var ro = ReadObject();
                        if (ro.IsEnd) break;
                        arr.Add(ro.ReturnValue);
                    }
                    return arr;
                }
                throw new ProtocolViolationException($"Expecting fail Array '{msgtype}3', got '{msgtype}{lenstr}'");
            }
            Dictionary<object, object> ReadMap(char msgtype)
            {
                var dic = new Dictionary<object, object>();
                var lenstr = ReadLine(null);
                if (int.TryParse(lenstr, out var len))
                {
                    for (var a = 0; a < len; a++)
                        dic.Add(ReadObject().ReturnValue, ReadObject().ReturnValue);
                    return dic;
                }
                if (lenstr == "?")
                {
                    while (true)
                    {
                        var rokey = ReadObject();
                        var roval = ReadObject();
                        if (roval.IsEnd) break;
                        dic.Add(rokey.ReturnValue, roval.ReturnValue);
                    }
                    return dic;
                }
                throw new ProtocolViolationException($"Expecting fail Map '{msgtype}3', got '{msgtype}{lenstr}'");
            }

            internal ReadObjectReturn ReadObject()
            {
                while (true)
                {
                    char c = (char)_stream.ReadByte();
                    switch (c)
                    {
                        case '$': return new ReadObjectReturn(ReadBlobString(c), false);
                        case '+': return new ReadObjectReturn(ReadSimpleString(), false);
                        case '=': return new ReadObjectReturn(ReadBlobString(c), false);
                        case '-': return new ReadObjectReturn(ReadSimpleString(), false);
                        case '!': return new ReadObjectReturn(ReadBlobString(c), false);
                        case ':': return new ReadObjectReturn(ReadNumber(c), false);
                        case '(': return new ReadObjectReturn(ReadBigNumber(c), false);
                        case '_': ReadLine(null); return new ReadObjectReturn(null, false);
                        case ',': return new ReadObjectReturn(ReadDouble(c), false);
                        case '#': return new ReadObjectReturn(ReadBoolean(c), false);
                        case '*': return new ReadObjectReturn(ReadArray(c), false);
                        case '~': return new ReadObjectReturn(ReadArray(c), false);
                        case '>': return new ReadObjectReturn(ReadArray(c), false);
                        case '%': return new ReadObjectReturn(ReadMap(c), false);
                        case '|': return new ReadObjectReturn(ReadMap(c), false);
                        case '.': return new ReadObjectReturn(ReadLine(null), true);
                        case ' ': continue;
                    }
                    throw new ProtocolViolationException($"Expecting fail MessageType '{c}'");
                }
            }
            internal class ReadObjectReturn
            {
                public object ReturnValue { get; set; }
                public bool IsEnd { get; set; }
                public ReadObjectReturn(object value, bool isend)
                {
                    this.ReturnValue = value;
                    this.IsEnd = isend;
                }
            }

            void Read(Stream outStream, int len)
            {
                if (len <= 0) return;
                var buffer = new byte[Math.Min(1024, len)];
                var bufferLength = buffer.Length;
                while (true)
                {
                    var readed = _stream.Read(buffer, 0, bufferLength);
                    if (readed <= 0) throw new ProtocolViolationException($"Expecting fail Read surplus length: {len}");
                    if (readed > 0) outStream.Write(buffer, 0, readed);
                    len = len - readed;
                    if (len <= 0) break;
                    if (len < buffer.Length) bufferLength = len;
                }
            }
            string ReadLine(Stream outStream)
            {
                var sb = outStream == null ? new StringBuilder() : null;
                var buffer = new byte[1];
                var should_break = false;
                while (true)
                {
                    var readed = _stream.Read(buffer, 0, 1);
                    if (readed <= 0) throw new ProtocolViolationException($"Expecting fail ReadLine end of stream");
                    if (buffer[0] == 13)
                        should_break = true;
                    else if (buffer[0] == 10 && should_break)
                        break;
                    else
                    {
                        if (outStream == null) sb.Append((char)buffer[0]);
                        else outStream.WriteByte(buffer[0]);
                        should_break = false;
                    }
                }
                return sb?.ToString();
            }
        }

        class Resp3Writer
        {
            Stream _stream;
            Encoding _encoding;

            public Resp3Writer(Stream stream, Encoding encoding)
            {
                _stream = stream;
                _encoding = encoding ?? Encoding.UTF8;
            }

            readonly byte[] Crlf = new byte[] { 13, 10 };
            readonly byte[] Null = new byte[] { 93, 13, 10 }; //_\r\n
            Resp3Writer WriteBlobString(string text, char msgtype = '$')
            {
                if (text == null) return WriteNull();
                var data = _encoding.GetBytes(text);
                var size = _encoding.GetBytes($"{msgtype}{data.Length}\r\n");
                _stream.Write(size, 0, size.Length);
                _stream.Write(data, 0, data.Length);
                _stream.Write(Crlf, 0, Crlf.Length);
                return this;
            }
            Resp3Writer WriteSimpleString(string text)
            {
                if (text == null) return WriteNull();
                if (text.Contains("\r\n")) return WriteBlobString(text);
                return WriteRaw($"+{text}\r\n");
            }
            Resp3Writer WriteVerbatimString(string text) => WriteBlobString(text, '=');
            Resp3Writer WriteBlobError(string error) => WriteBlobString(error, '!');
            Resp3Writer WriteSimpleError(string error)
            {
                if (error == null) return WriteNull();
                if (error.Contains("\r\n")) return WriteBlobError(error);
                return WriteRaw($"-{error}\r\n");
            }
            Resp3Writer WriteNumber(char mstype, object number)
            {
                if (number == null) return WriteNull();
                return WriteRaw($"{mstype}{InvariantCultureToString(number)}\r\n");
            }
            Resp3Writer WriteDouble(double? number)
            {
                if (number == null) return WriteNull();
                switch (number)
                {
                    case double.PositiveInfinity: return WriteRaw($",inf\r\n");
                    case double.NegativeInfinity: return WriteRaw($",-inf\r\n");
                    default: return WriteRaw($",{InvariantCultureToString(number)}\r\n");
                }
            }
            Resp3Writer WriteBoolean(bool? val)
            {
                if (val == null) return WriteNull();
                return WriteRaw(val.Value ? $"#t\r\n" : "#f\r\n");
            }
            Resp3Writer WriteNull()
            {
                _stream.Write(Null, 0, Null.Length);
                return this;
            }
            string InvariantCultureToString(object obj) => string.Format(CultureInfo.InvariantCulture, @"{0}", obj);

            Resp3Writer WriteRaw(string raw)
            {
                if (string.IsNullOrEmpty(raw)) return this;
                var size = _encoding.GetBytes($"{InvariantCultureToString(raw)}");
                _stream.Write(size, 0, size.Length);
                return this;
            }

            public Resp3Writer WriteObject(object obj)
            {
                if (obj == null) WriteNull();
                if (obj is string str) return WriteBlobString(str);
                var objtype = obj.GetType();
                if (_dicIsNumberType.Value.TryGetValue(objtype, out var tryval))
                {
                    switch (tryval)
                    {
                        case 1: return WriteNumber(':', obj);
                        case 2: return WriteDouble((double?)obj);
                        case 3: return WriteNumber(',', obj);
                    }
                }
                objtype = objtype.NullableTypeOrThis();
                if (objtype == typeof(bool)) return WriteBoolean((bool?)obj);
                if (objtype.IsEnum) return WriteNumber(':', obj);
                if (objtype == typeof(char)) return WriteBlobString(((DateTimeOffset)obj).ToString("yyyy-MM-dd HH:mm:ss.fff zzzz"));
                if (objtype == typeof(DateTime)) return WriteBlobString(obj.ToString());
                if (objtype == typeof(DateTimeOffset)) return WriteBlobString(((DateTimeOffset)obj).ToString("yyyy-MM-dd HH:mm:ss.fff zzzz"));
                if (objtype == typeof(TimeSpan)) return WriteBlobString(InvariantCultureToString(obj));
                if (objtype == typeof(BigInteger)) return WriteNumber('(', obj);
                if (obj is Exception ex) return WriteSimpleError(ex.Message);

                if (obj is IDictionary dic)
                {
                    WriteNumber('%', dic.Count);
                    foreach (var key in dic.Keys)
                        WriteObject(key).WriteObject(dic[key]);
                    return this;
                }
                if (obj is IEnumerable ie)
                {
                    using (var ms = new MemoryStream())
                    {
                        var msWriter = new Resp3Writer(ms, _encoding);
                        var idx = 0;
                        foreach (var z in ie)
                        {
                            msWriter.WriteObject(z);
                            idx++;
                        }
                        if (idx > 0 && ms.Length > 0)
                        {
                            WriteNumber('*', idx);
                            ms.Position = 0;
                            ms.CopyTo(_stream);
                        }
                        ms.Close();
                    }
                    return this;
                }

                var ps = objtype.GetPropertiesDictIgnoreCase().Values;
                WriteNumber('%', ps.Count);
                foreach (var p in ps)
                {
                    var pvalue = p.GetValue(obj, null);
                    WriteObject(pvalue);
                }
                return this;
            }
        }
        public enum MessageType
        {
            /// <summary>
            /// $11\r\nhelloworld\r\n
            /// </summary>
            BlobString,

            /// <summary>
            /// +hello world\r\n
            /// </summary>
            SimpleString,

            /// <summary>
            /// =15\r\ntxt:Some string\r\n
            /// </summary>
            VerbatimString,

            /// <summary>
            /// -ERR this is the error description\r\n<para></para>
            /// The first word in the error is in upper case and describes the error code.
            /// </summary>
            SimpleError,

            /// <summary>
            /// !21\r\nSYNTAX invalid syntax\r\n<para></para>
            /// The first word in the error is in upper case and describes the error code.
            /// </summary>
            BlobError,

            /// <summary>
            /// :1234\r\n
            /// </summary>
            Number,

            /// <summary>
            /// (3492890328409238509324850943850943825024385\r\n
            /// </summary>
            BigNumber,

            /// <summary>
            /// _\r\n
            /// </summary>
            Null,

            /// <summary>
            /// ,1.23\r\n<para></para>
            /// ,inf\r\n<para></para>
            /// ,-inf\r\n
            /// </summary>
            Double,

            /// <summary>
            /// #t\r\n<para></para>
            /// #f\r\n
            /// </summary>
            Boolean,

            /// <summary>
            /// *3\r\n:1\r\n:2\r\n:3\r\n<para></para>
            /// [1, 2, 3]
            /// </summary>
            Array,

            /// <summary>
            /// ~5\r\n+orange\r\n+apple\r\n#t\r\n:100\r\n:999\r\n
            /// </summary>
            Set,

            /// <summary>
            /// >4\r\n+pubsub\r\n+message\r\n+somechannel\r\n+this is the message\r\n
            /// </summary>
            Push,

            /// <summary>
            /// %2\r\n+first\r\n:1\r\n+second\r\n:2\r\n<para></para>
            /// { "first": 1, "second": 2 }
            /// </summary>
            Map,

            /// <summary>
            /// |2\r\n+first\r\n:1\r\n+second\r\n:2\r\n<para></para>
            /// { "first": 1, "second": 2 }
            /// </summary>
            Attribute,
        }

        static Lazy<Dictionary<Type, byte>> _dicIsNumberType = new Lazy<Dictionary<Type, byte>>(() => new Dictionary<Type, byte>
        {
            [typeof(sbyte)] = 1,
            [typeof(sbyte?)] = 1,
            [typeof(short)] = 1,
            [typeof(short?)] = 1,
            [typeof(int)] = 1,
            [typeof(int?)] = 1,
            [typeof(long)] = 1,
            [typeof(long?)] = 1,
            [typeof(byte)] = 1,
            [typeof(byte?)] = 1,
            [typeof(ushort)] = 1,
            [typeof(ushort?)] = 1,
            [typeof(uint)] = 1,
            [typeof(uint?)] = 1,
            [typeof(ulong)] = 1,
            [typeof(ulong?)] = 1,
            [typeof(double)] = 2,
            [typeof(double?)] = 2,
            [typeof(float)] = 2,
            [typeof(float?)] = 2,
            [typeof(decimal)] = 3,
            [typeof(decimal?)] = 3
        });
        static bool IsIntegerType(this Type that) => that == null ? false : (_dicIsNumberType.Value.TryGetValue(that, out var tryval) ? tryval == 1 : false);
        static bool IsNumberType(this Type that) => that == null ? false : _dicIsNumberType.Value.ContainsKey(that);

        static bool IsNullableType(this Type that) => that.IsArray == false && that?.FullName.StartsWith("System.Nullable`1[") == true;
        static bool IsAnonymousType(this Type that) => that?.FullName.StartsWith("<>f__AnonymousType") == true;
        static bool IsArrayOrList(this Type that) => that == null ? false : (that.IsArray || typeof(IList).IsAssignableFrom(that));
        static Type NullableTypeOrThis(this Type that) => that?.IsNullableType() == true ? that.GetGenericArguments().First() : that;

        static ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> _dicGetPropertiesDictIgnoreCase = new ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>>();
        static Dictionary<string, PropertyInfo> GetPropertiesDictIgnoreCase(this Type that) => that == null ? null : _dicGetPropertiesDictIgnoreCase.GetOrAdd(that, tp =>
        {
            var props = that.GetProperties().GroupBy(p => p.DeclaringType).Reverse().SelectMany(p => p); //将基类的属性位置放在前面 #164
            var dict = new Dictionary<string, PropertyInfo>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var prop in props)
            {
                if (dict.ContainsKey(prop.Name)) continue;
                dict.Add(prop.Name, prop);
            }
            return dict;
        });
    }
}