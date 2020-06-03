using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Text;

namespace CSRedis.Internal
{
    public static class Resp3Helper
    {
        public static ReadResult<T> Read<T>(Stream stream) => Read<T>(stream, Encoding.UTF8);
        public static ReadResult<T> Read<T>(Stream stream, Encoding encoding) => ConvertReadResult<T>(new Resp3Reader(stream, encoding ?? Encoding.UTF8).ReadObject());
        public static ReadResult<T> ReadBytes<T>(Stream stream) => ConvertReadResult<T>(new Resp3Reader(stream, null).ReadObject());
        static ReadResult<T> ConvertReadResult<T>(ReadResult<object> rt)
        {
            var obj = rt.Value;
            if (obj is T val) return rt.NewValue(a => val);
            return rt.NewValue(a => (T)Clone(a, typeof(T)));

            object Clone(object source, Type targetType)
            {
                if (source == null) return targetType.CreateInstanceGetDefaultValue();
                if (targetType == typeof(string) || targetType.IsNumberType() || targetType == typeof(BigInteger)) return GetDataReaderValue(targetType, source);
                if (targetType.IsArray)
                {
                    if (source is IList sourceList)
                    {
                        var sourceArrLen = sourceList.Count;
                        var targetElementType = targetType.GetElementType();
                        var target = Array.CreateInstance(targetElementType, sourceArrLen);
                        for (var a = 0; a < sourceArrLen; a++) target.SetValue(Clone(sourceList[a], targetElementType), a);
                        return target;
                    }
                    if (source is Array sourceArr)
                    {
                        var sourceArrLen = sourceArr.Length;
                        var targetElementType = targetType.GetElementType();
                        var target = Array.CreateInstance(targetElementType, sourceArrLen);
                        for (var a = 0; a < sourceArrLen; a++) target.SetValue(Clone(sourceArr.GetValue(a), targetElementType), a);
                        return target;
                    }
                }
                var sourceType = source.GetType();
                if (sourceType == targetType) return source;
                throw new ArgumentException($"ReadResult convert failed to {targetType.DisplayCsharp()}");
            }
        }

        public static void Write(Stream stream, object[] command, bool isresp2) => Write(stream, null, command, isresp2);
        public static void Write(Stream stream, Encoding encoding, object[] command, bool isresp2) => new Resp3Writer(stream, encoding, isresp2).WriteCommand(command);

        public static object Deserialize(string resptext)
        {
            using (var ms = new MemoryStream())
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(resptext);
                    ms.Write(bytes, 0, bytes.Length);
                    ms.Position = 0;
                    return Read<object>(ms, Encoding.UTF8).Value;
                }
                finally
                {
                    ms.Close();
                }
            }
        }
        public static string Serialize(object data, bool isresp2)
        {
            using (var ms = new MemoryStream())
            {
                try
                {
                    new Resp3Writer(ms, Encoding.UTF8, isresp2).WriteObject(data);
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
                _encoding = encoding;
            }

            object ReadBlobString(char msgtype)
            {
                if (_encoding == null) return ReadClob();
                return _encoding.GetString(ReadClob());
                
                byte[] ReadClob()
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
                            return ms.ToArray();
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
                            return ms.ToArray();
                        }
                        throw new ProtocolViolationException($"Expecting fail Blob string '{msgtype}0', got '{msgtype}{lenstr}'");
                    }
                    finally
                    {
                        ms?.Close();
                        ms?.Dispose();
                    }
                }
            }
            object ReadSimpleString()
            {
                if (_encoding == null) return ReadClob();
                return _encoding.GetString(ReadClob());

                byte[] ReadClob()
                {
                    MemoryStream ms = null;
                    try
                    {
                        ms = new MemoryStream();
                        ReadLine(ms);
                        return ms.ToArray();
                    }
                    finally
                    {
                        ms?.Close();
                        ms?.Dispose();
                    }
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
                        arr.Add(ReadObject().Value);
                    return arr;
                }
                if (lenstr == "?")
                {
                    while (true)
                    {
                        var ro = ReadObject();
                        if (ro.IsEnd) break;
                        arr.Add(ro.Value);
                    }
                    return arr;
                }
                throw new ProtocolViolationException($"Expecting fail Array '{msgtype}3', got '{msgtype}{lenstr}'");
            }
            List<object> ReadMap(char msgtype)
            {
                var arr = new List<object>();
                var lenstr = ReadLine(null);
                if (int.TryParse(lenstr, out var len))
                {
                    for (var a = 0; a < len; a++)
                    {
                        arr.Add(ReadObject().Value); 
                        arr.Add(ReadObject().Value);
                    }
                    return arr;
                }
                if (lenstr == "?")
                {
                    while (true)
                    {
                        var rokey = ReadObject();
                        var roval = ReadObject();
                        if (roval.IsEnd) break;
                        arr.Add(rokey.Value);
                        arr.Add(roval.Value);
                    }
                    return arr;
                }
                throw new ProtocolViolationException($"Expecting fail Map '{msgtype}3', got '{msgtype}{lenstr}'");
            }

            internal ReadResult<object> ReadObject()
            {
                while (true)
                {
                    char c = (char)_stream.ReadByte();
                    switch (c)
                    {
                        case '$': return new ReadResult<object>(ReadBlobString(c), false, MessageType.BlobString);
                        case '+': return new ReadResult<object>(ReadSimpleString(), false, MessageType.SimpleString);
                        case '=': return new ReadResult<object>(ReadBlobString(c), false, MessageType.VerbatimString);
                        case '-': return new ReadResult<object>(ReadSimpleString(), false, MessageType.SimpleError);
                        case '!': return new ReadResult<object>(ReadBlobString(c), false, MessageType.BlobError);
                        case ':': return new ReadResult<object>(ReadNumber(c), false, MessageType.Number);
                        case '(': return new ReadResult<object>(ReadBigNumber(c), false, MessageType.BigNumber);
                        case '_': ReadLine(null); return new ReadResult<object>(null, false, MessageType.Null);
                        case ',': return new ReadResult<object>(ReadDouble(c), false, MessageType.Double);
                        case '#': return new ReadResult<object>(ReadBoolean(c), false, MessageType.Boolean);

                        case '*': return new ReadResult<object>(ReadArray(c), false, MessageType.Array);
                        case '~': return new ReadResult<object>(ReadArray(c), false, MessageType.Set);
                        case '>': return new ReadResult<object>(ReadArray(c), false, MessageType.Push);
                        case '%': return new ReadResult<object>(ReadMap(c), false, MessageType.Map);
                        case '|': return new ReadResult<object>(ReadMap(c), false, MessageType.Attribute);
                        case '.': ReadLine(null); return new ReadResult<object>(null, true, MessageType.SimpleString); //无类型
                        case ' ': continue;
                        default: throw new ProtocolViolationException($"Expecting fail MessageType '{c}'");
                    }
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
            bool _isresp2;

            public Resp3Writer(Stream stream, Encoding encoding, bool isresp2)
            {
                _stream = stream;
                _encoding = encoding ?? Encoding.UTF8;
                _isresp2 = isresp2;
            }

            readonly byte[] Crlf = new byte[] { 13, 10 };
            readonly byte[] Null = new byte[] { 93, 13, 10 }; //_\r\n
            Resp3Writer WriteBlobString(string text, char msgtype = '$')
            {
                if (text == null) return WriteNull();
                return WriteClob(_encoding.GetBytes(text), msgtype);
            }
            Resp3Writer WriteClob(byte[] data, char msgtype = '$')
            {
                if (data == null) return WriteNull();
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
                if (error.Contains("\r\n"))
                {
                    if (_isresp2) return WriteSimpleString(error.Replace("\r\n", " "));
                    return WriteBlobError(error);
                }
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
                if (_isresp2) return WriteBlobString(InvariantCultureToString(number));
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
                if (_isresp2) return WriteNumber(':', val.Value ? 1 : 0);
                return WriteRaw(val.Value ? $"#t\r\n" : "#f\r\n");
            }
            Resp3Writer WriteNull()
            {
                if (_isresp2) return WriteBlobString("");
                _stream.Write(Null, 0, Null.Length);
                return this;
            }
            internal static string InvariantCultureToString(object obj) => string.Format(CultureInfo.InvariantCulture, @"{0}", obj);

            Resp3Writer WriteRaw(string raw)
            {
                if (string.IsNullOrEmpty(raw)) return this;
                var data = _encoding.GetBytes($"{InvariantCultureToString(raw)}");
                _stream.Write(data, 0, data.Length);
                return this;
            }

            public void WriteCommand(object[] cmd)
            {
                WriteNumber('*', cmd.Length);
                foreach (var c in cmd) WriteBlobString(InvariantCultureToString(c));
            }

            public Resp3Writer WriteObject(object obj)
            {
                if (obj == null) WriteNull();
                if (obj is string str) return WriteBlobString(str);
                if (obj is byte[] byt) return WriteClob(byt);
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
                if (objtype == typeof(char)) return WriteBlobString(obj.ToString());
                if (objtype == typeof(DateTime)) return WriteBlobString(obj.ToString());
                if (objtype == typeof(DateTimeOffset)) return WriteBlobString(((DateTimeOffset)obj).ToString("yyyy-MM-dd HH:mm:ss.fff zzzz"));
                if (objtype == typeof(TimeSpan)) return WriteBlobString(InvariantCultureToString(obj));
                if (objtype == typeof(BigInteger)) return WriteNumber('(', obj);
                if (obj is Exception ex) return WriteSimpleError(ex.Message);

                if (obj is IDictionary dic)
                {
                    if (_isresp2) WriteNumber('*', dic.Count * 2);
                    else WriteNumber('%', dic.Count);
                    foreach (var key in dic.Keys)
                        WriteObject(key).WriteObject(dic[key]);
                    return this;
                }
                if (obj is IEnumerable ie)
                {
                    using (var ms = new MemoryStream())
                    {
                        var msWriter = new Resp3Writer(ms, _encoding, _isresp2);
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
                if (_isresp2) WriteNumber('*', ps.Count * 2);
                else WriteNumber('%', ps.Count);
                foreach (var p in ps)
                {
                    var pvalue = p.GetValue(obj, null);
                    WriteObject(p.Name).WriteObject(pvalue);
                }
                return this;
            }
        }

        public class ReadResult<T>
        {
            public T Value { get; }
            internal bool IsEnd { get; }
            public MessageType MessageType { get; }
            public bool IsError => this.MessageType == MessageType.SimpleError || this.MessageType == MessageType.BlobError;
            internal ReadResult(T value, bool isend, MessageType msgtype)
            {
                this.Value = value;
                this.IsEnd = isend;
                this.MessageType = msgtype;
            }
            public ReadResult<T2> NewValue<T2>(Func<T, T2> value) => new ReadResult<T2>(value(this.Value), true, this.MessageType);
        }
        public static T MapToClass<T>(this object[] list)
        {
            if (list?.Length % 2 != 0) throw new ArgumentException(nameof(list));
            var ttype = typeof(T);
            var ret = (T)ttype.CreateInstanceGetDefaultValue();
            var props = ttype.GetPropertiesDictIgnoreCase();
            for (var a = 0; a < list.Length; a+= 2)
            {
                var name = list[a].ToString().Replace("-", "");
                if (props.TryGetValue(name, out var tryprop) == false) throw new ArgumentException($"{typeof(T).DisplayCsharp()} undefined Property {list[a]}");
                var val = list[a + 1];
                if (val == null) continue;
                ttype.SetPropertyValue(ret, tryprop.Name, tryprop.PropertyType == val.GetType() ? val : GetDataReaderValue(tryprop.PropertyType, val));
            }
            return ret;
        }
        public static Dictionary<string, T> MapToHash<T>(this object[] list)
        {
            if (list?.Length % 2 != 0) throw new ArgumentException(nameof(list));
            var dic = new Dictionary<string, T>();
            for (var a = 0; a < list.Length; a += 2)
            {
                var key = Resp3Writer.InvariantCultureToString(list[a]);
                if (dic.ContainsKey(key)) continue;
                var val = list[a + 1];
                if (val == null) dic.Add(key, default(T));
                dic.Add(key, val is T conval ? conval : (T)GetDataReaderValue(typeof(T), list[a + 1]));
            }
            return dic;
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
        /// <summary>
        /// 获取 Type 的原始 c# 文本表示
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        internal static string DisplayCsharp(this Type type, bool isNameSpace = true)
        {
            if (type == null) return null;
            if (type == typeof(void)) return "void";
            if (type.IsGenericParameter) return type.Name;
            if (type.IsArray) return $"{DisplayCsharp(type.GetElementType())}[]";
            var sb = new StringBuilder();
            var nestedType = type;
            while (nestedType.IsNested)
            {
                sb.Insert(0, ".").Insert(0, DisplayCsharp(nestedType.DeclaringType, false));
                nestedType = nestedType.DeclaringType;
            }
            if (isNameSpace && string.IsNullOrEmpty(nestedType.Namespace) == false)
                sb.Insert(0, ".").Insert(0, nestedType.Namespace);

            if (type.IsGenericType == false)
                return sb.Append(type.Name).ToString();

            var genericParameters = type.GetGenericArguments();
            if (type.IsNested && type.DeclaringType.IsGenericType)
            {
                var dic = genericParameters.ToDictionary(a => a.Name);
                foreach (var nestedGenericParameter in type.DeclaringType.GetGenericArguments())
                    if (dic.ContainsKey(nestedGenericParameter.Name))
                        dic.Remove(nestedGenericParameter.Name);
                genericParameters = dic.Values.ToArray();
            }
            if (genericParameters.Any() == false)
                return sb.Append(type.Name).ToString();

            sb.Append(type.Name.Remove(type.Name.IndexOf('`'))).Append("<");
            var genericTypeIndex = 0;
            foreach (var genericType in genericParameters)
            {
                if (genericTypeIndex++ > 0) sb.Append(", ");
                sb.Append(DisplayCsharp(genericType, true));
            }
            return sb.Append(">").ToString();
        }
        internal static string DisplayCsharp(this MethodInfo method, bool isOverride)
        {
            if (method == null) return null;
            var sb = new StringBuilder();
            if (method.IsPublic) sb.Append("public ");
            if (method.IsAssembly) sb.Append("internal ");
            if (method.IsFamily) sb.Append("protected ");
            if (method.IsPrivate) sb.Append("private ");
            if (method.IsPrivate) sb.Append("private ");
            if (method.IsStatic) sb.Append("static ");
            if (method.IsAbstract && method.DeclaringType.IsInterface == false) sb.Append("abstract ");
            if (method.IsVirtual && method.DeclaringType.IsInterface == false) sb.Append(isOverride ? "override " : "virtual ");
            sb.Append(method.ReturnType.DisplayCsharp()).Append(" ").Append(method.Name);

            var genericParameters = method.GetGenericArguments();
            if (method.DeclaringType.IsNested && method.DeclaringType.DeclaringType.IsGenericType)
            {
                var dic = genericParameters.ToDictionary(a => a.Name);
                foreach (var nestedGenericParameter in method.DeclaringType.DeclaringType.GetGenericArguments())
                    if (dic.ContainsKey(nestedGenericParameter.Name))
                        dic.Remove(nestedGenericParameter.Name);
                genericParameters = dic.Values.ToArray();
            }
            if (genericParameters.Any())
                sb.Append("<")
                    .Append(string.Join(", ", genericParameters.Select(a => a.DisplayCsharp())))
                    .Append(">");

            sb.Append("(").Append(string.Join(", ", method.GetParameters().Select(a => $"{a.ParameterType.DisplayCsharp()} {a.Name}"))).Append(")");
            return sb.ToString();
        }
        public static object CreateInstanceGetDefaultValue(this Type that)
        {
            if (that == null) return null;
            if (that == typeof(string)) return default(string);
            if (that == typeof(Guid)) return default(Guid);
            if (that.IsArray) return Array.CreateInstance(that, 0);
            var ctorParms = that.InternalGetTypeConstructor0OrFirst(false)?.GetParameters();
            if (ctorParms == null || ctorParms.Any() == false) return Activator.CreateInstance(that, true);
            return Activator.CreateInstance(that, ctorParms
                .Select(a => a.ParameterType.IsInterface || a.ParameterType.IsAbstract || a.ParameterType == typeof(string) || a.ParameterType.IsArray ?
                null :
                Activator.CreateInstance(a.ParameterType, null)).ToArray());
        }
        internal static NewExpression InternalNewExpression(this Type that)
        {
            var ctor = that.InternalGetTypeConstructor0OrFirst();
            return Expression.New(ctor, ctor.GetParameters().Select(a => Expression.Constant(a.ParameterType.CreateInstanceGetDefaultValue(), a.ParameterType)));
        }

        static ConcurrentDictionary<Type, ConstructorInfo> _dicInternalGetTypeConstructor0OrFirst = new ConcurrentDictionary<Type, ConstructorInfo>();
        internal static ConstructorInfo InternalGetTypeConstructor0OrFirst(this Type that, bool isThrow = true)
        {
            var ret = _dicInternalGetTypeConstructor0OrFirst.GetOrAdd(that, tp =>
                tp.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[0], null) ??
                tp.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault());
            if (ret == null && isThrow) throw new ArgumentException($"{that.FullName} 类型无方法访问构造函数");
            return ret;
        }

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

        static ConcurrentDictionary<Type, ConcurrentDictionary<string, Action<object, string, object>>> _dicSetEntityValueWithPropertyName = new ConcurrentDictionary<Type, ConcurrentDictionary<string, Action<object, string, object>>>();
        static void SetPropertyValue(this Type entityType, object entity, string propertyName, object value)
        {
            if (entity == null) return;
            if (entityType == null) entityType = entity.GetType();
            var func = _dicSetEntityValueWithPropertyName
                .GetOrAdd(entityType, et => new ConcurrentDictionary<string, Action<object, string, object>>())
                .GetOrAdd(propertyName, pn =>
                {
                    var t = entityType;
                    var props = GetPropertiesDictIgnoreCase(t);
                    var parm1 = Expression.Parameter(typeof(object));
                    var parm2 = Expression.Parameter(typeof(string));
                    var parm3 = Expression.Parameter(typeof(object));
                    var var1Parm = Expression.Variable(t);
                    var exps = new List<Expression>(new Expression[] {
                        Expression.Assign(var1Parm, Expression.TypeAs(parm1, t))
                    });
                    if (props.ContainsKey(pn))
                    {
                        var prop = props[pn];
                        exps.Add(
                            Expression.Assign(
                                Expression.MakeMemberAccess(var1Parm, prop),
                                Expression.Convert(
                                    parm3,
                                    prop.PropertyType
                                )
                            )
                        );
                    }
                    return Expression.Lambda<Action<object, string, object>>(Expression.Block(new[] { var1Parm }, exps), new[] { parm1, parm2, parm3 }).Compile();
                });
            func(entity, propertyName, value);
        }

        #region ExpressionTree
        static BigInteger ToBigInteger(string that)
        {
            if (string.IsNullOrEmpty(that)) return 0;
            if (BigInteger.TryParse(that, System.Globalization.NumberStyles.Any, null, out var trybigint)) return trybigint;
            return 0;
        }
        static string ToStringConcat(object obj)
        {
            if (obj == null) return null;
            return string.Concat(obj);
        }
        static byte[] GuidToBytes(Guid guid)
        {
            var bytes = new byte[16];
            var guidN = guid.ToString("N");
            for (var a = 0; a < guidN.Length; a += 2)
                bytes[a / 2] = byte.Parse($"{guidN[a]}{guidN[a + 1]}", System.Globalization.NumberStyles.HexNumber);
            return bytes;
        }
        static Guid BytesToGuid(byte[] bytes)
        {
            if (bytes == null) return Guid.Empty;
            return Guid.TryParse(BitConverter.ToString(bytes, 0, Math.Min(bytes.Length, 36)).Replace("-", ""), out var tryguid) ? tryguid : Guid.Empty;
        }
        static ConcurrentDictionary<Type, ConcurrentDictionary<Type, Func<object, object>>> _dicGetDataReaderValue = new ConcurrentDictionary<Type, ConcurrentDictionary<Type, Func<object, object>>>();
        static MethodInfo MethodArrayGetValue = typeof(Array).GetMethod("GetValue", new[] { typeof(int) });
        static MethodInfo MethodArrayGetLength = typeof(Array).GetMethod("GetLength", new[] { typeof(int) });
        static MethodInfo MethodGuidTryParse = typeof(Guid).GetMethod("TryParse", new[] { typeof(string), typeof(Guid).MakeByRefType() });
        static MethodInfo MethodEnumParse = typeof(Enum).GetMethod("Parse", new[] { typeof(Type), typeof(string), typeof(bool) });
        static MethodInfo MethodConvertChangeType = typeof(Convert).GetMethod("ChangeType", new[] { typeof(object), typeof(Type) });
        static MethodInfo MethodTimeSpanFromSeconds = typeof(TimeSpan).GetMethod("FromSeconds");
        static MethodInfo MethodSByteTryParse = typeof(sbyte).GetMethod("TryParse", new[] { typeof(string), typeof(sbyte).MakeByRefType() });
        static MethodInfo MethodShortTryParse = typeof(short).GetMethod("TryParse", new[] { typeof(string), typeof(System.Globalization.NumberStyles), typeof(IFormatProvider), typeof(short).MakeByRefType() });
        static MethodInfo MethodIntTryParse = typeof(int).GetMethod("TryParse", new[] { typeof(string), typeof(System.Globalization.NumberStyles), typeof(IFormatProvider), typeof(int).MakeByRefType() });
        static MethodInfo MethodLongTryParse = typeof(long).GetMethod("TryParse", new[] { typeof(string), typeof(System.Globalization.NumberStyles), typeof(IFormatProvider), typeof(long).MakeByRefType() });
        static MethodInfo MethodByteTryParse = typeof(byte).GetMethod("TryParse", new[] { typeof(string), typeof(byte).MakeByRefType() });
        static MethodInfo MethodUShortTryParse = typeof(ushort).GetMethod("TryParse", new[] { typeof(string), typeof(System.Globalization.NumberStyles), typeof(IFormatProvider), typeof(ushort).MakeByRefType() });
        static MethodInfo MethodUIntTryParse = typeof(uint).GetMethod("TryParse", new[] { typeof(string), typeof(System.Globalization.NumberStyles), typeof(IFormatProvider), typeof(uint).MakeByRefType() });
        static MethodInfo MethodULongTryParse = typeof(ulong).GetMethod("TryParse", new[] { typeof(string), typeof(System.Globalization.NumberStyles), typeof(IFormatProvider), typeof(ulong).MakeByRefType() });
        static MethodInfo MethodDoubleTryParse = typeof(double).GetMethod("TryParse", new[] { typeof(string), typeof(System.Globalization.NumberStyles), typeof(IFormatProvider), typeof(double).MakeByRefType() });
        static MethodInfo MethodFloatTryParse = typeof(float).GetMethod("TryParse", new[] { typeof(string), typeof(System.Globalization.NumberStyles), typeof(IFormatProvider), typeof(float).MakeByRefType() });
        static MethodInfo MethodDecimalTryParse = typeof(decimal).GetMethod("TryParse", new[] { typeof(string), typeof(System.Globalization.NumberStyles), typeof(IFormatProvider), typeof(decimal).MakeByRefType() });
        static MethodInfo MethodTimeSpanTryParse = typeof(TimeSpan).GetMethod("TryParse", new[] { typeof(string), typeof(TimeSpan).MakeByRefType() });
        static MethodInfo MethodDateTimeTryParse = typeof(DateTime).GetMethod("TryParse", new[] { typeof(string), typeof(DateTime).MakeByRefType() });
        static MethodInfo MethodDateTimeOffsetTryParse = typeof(DateTimeOffset).GetMethod("TryParse", new[] { typeof(string), typeof(DateTimeOffset).MakeByRefType() });
        static MethodInfo MethodToString = typeof(Resp3Helper).GetMethod("ToStringConcat", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(object) }, null);
        static MethodInfo MethodBigIntegerParse = typeof(Resp3Helper).GetMethod("ToBigInteger", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
        static PropertyInfo PropertyDateTimeOffsetDateTime = typeof(DateTimeOffset).GetProperty("DateTime", BindingFlags.Instance | BindingFlags.Public);
        static PropertyInfo PropertyDateTimeTicks = typeof(DateTime).GetProperty("Ticks", BindingFlags.Instance | BindingFlags.Public);
        static ConstructorInfo CtorDateTimeOffsetArgsTicks = typeof(DateTimeOffset).GetConstructor(new[] { typeof(long), typeof(TimeSpan) });
        static Encoding DefaultEncoding = Encoding.UTF8;
        static MethodInfo MethodEncodingGetBytes = typeof(Encoding).GetMethod("GetBytes", new[] { typeof(string) });
        static MethodInfo MethodEncodingGetString = typeof(Encoding).GetMethod("GetString", new[] { typeof(byte[]) });
        static MethodInfo MethodGuidToBytes = typeof(Resp3Helper).GetMethod("GuidToBytes", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Guid) }, null);
        static MethodInfo MethodBytesToGuid = typeof(Resp3Helper).GetMethod("BytesToGuid", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(byte[]) }, null);

        public static ConcurrentBag<Func<LabelTarget, Expression, Type, Expression>> GetDataReaderValueBlockExpressionSwitchTypeFullName = new ConcurrentBag<Func<LabelTarget, Expression, Type, Expression>>();
        public static ConcurrentBag<Func<LabelTarget, Expression, Expression, Type, Expression>> GetDataReaderValueBlockExpressionObjectToStringIfThenElse = new ConcurrentBag<Func<LabelTarget, Expression, Expression, Type, Expression>>();
        public static Expression GetDataReaderValueBlockExpression(Type type, Expression value)
        {
            var returnTarget = Expression.Label(typeof(object));
            var valueExp = Expression.Variable(typeof(object), "locvalue");
            Func<Expression> funcGetExpression = () =>
            {
                if (type.FullName == "System.Byte[]") return Expression.IfThenElse(
                    Expression.TypeEqual(valueExp, type),
                    Expression.Return(returnTarget, valueExp),
                    Expression.IfThenElse(
                        Expression.TypeEqual(valueExp, typeof(string)),
                        Expression.Return(returnTarget, Expression.Call(Expression.Constant(DefaultEncoding), MethodEncodingGetBytes, Expression.Convert(valueExp, typeof(string)))),
                        Expression.IfThenElse(
                            Expression.Or(Expression.TypeEqual(valueExp, typeof(Guid)), Expression.TypeEqual(valueExp, typeof(Guid?))),
                            Expression.Return(returnTarget, Expression.Call(MethodGuidToBytes, Expression.Convert(valueExp, typeof(Guid)))),
                            Expression.Return(returnTarget, Expression.Call(Expression.Constant(DefaultEncoding), MethodEncodingGetBytes, Expression.Call(MethodToString, valueExp)))
                        )
                    )
                );
                if (type.IsArray)
                {
                    var elementType = type.GetElementType();
                    var arrNewExp = Expression.Variable(type, "arrNew");
                    var arrExp = Expression.Variable(typeof(Array), "arr");
                    var arrLenExp = Expression.Variable(typeof(int), "arrLen");
                    var arrXExp = Expression.Variable(typeof(int), "arrX");
                    var arrReadValExp = Expression.Variable(typeof(object), "arrReadVal");
                    var label = Expression.Label(typeof(int));
                    return Expression.IfThenElse(
                        Expression.TypeEqual(valueExp, type),
                        Expression.Return(returnTarget, valueExp),
                        Expression.Block(
                            new[] { arrNewExp, arrExp, arrLenExp, arrXExp, arrReadValExp },
                            Expression.Assign(arrExp, Expression.TypeAs(valueExp, typeof(Array))),
                            Expression.IfThenElse(
                                Expression.Equal(arrExp, Expression.Constant(null)),
                                Expression.Assign(arrLenExp, Expression.Constant(0)),
                                Expression.Assign(arrLenExp, Expression.Call(arrExp, MethodArrayGetLength, Expression.Constant(0)))
                            ),
                            Expression.Assign(arrXExp, Expression.Constant(0)),
                            Expression.Assign(arrNewExp, Expression.NewArrayBounds(elementType, arrLenExp)),
                            Expression.Loop(
                                Expression.IfThenElse(
                                    Expression.LessThan(arrXExp, arrLenExp),
                                    Expression.Block(
                                        Expression.Assign(arrReadValExp, GetDataReaderValueBlockExpression(elementType, Expression.Call(arrExp, MethodArrayGetValue, arrXExp))),
                                        Expression.IfThenElse(
                                            Expression.Equal(arrReadValExp, Expression.Constant(null)),
                                            Expression.Assign(Expression.ArrayAccess(arrNewExp, arrXExp), Expression.Default(elementType)),
                                            Expression.Assign(Expression.ArrayAccess(arrNewExp, arrXExp), Expression.Convert(arrReadValExp, elementType))
                                        ),
                                        Expression.PostIncrementAssign(arrXExp)
                                    ),
                                    Expression.Break(label, arrXExp)
                                ),
                                label
                            ),
                            Expression.Return(returnTarget, arrNewExp)
                        )
                    );
                }
                var typeOrg = type;
                if (type.IsNullableType()) type = type.GetGenericArguments().First();
                if (type.IsEnum)
                    return Expression.Block(
                        Expression.IfThenElse(
                            Expression.Equal(Expression.TypeAs(valueExp, typeof(string)), Expression.Constant(string.Empty)),
                            Expression.Return(returnTarget, Expression.Convert(Expression.Default(type), typeof(object))),
                            Expression.Return(returnTarget, Expression.Call(MethodEnumParse, Expression.Constant(type, typeof(Type)), Expression.Call(MethodToString, valueExp), Expression.Constant(true, typeof(bool))))
                        )
                    );
                Expression tryparseExp = null;
                Expression tryparseBooleanExp = null;
                ParameterExpression tryparseVarExp = null;
                switch (type.FullName)
                {
                    case "System.Guid":
                        tryparseExp = Expression.Block(
                           new[] { tryparseVarExp = Expression.Variable(typeof(Guid)) },
                           new Expression[] {
                                Expression.IfThenElse(
                                    Expression.IsTrue(Expression.Call(MethodGuidTryParse, Expression.Convert(valueExp, typeof(string)), tryparseVarExp)),
                                    Expression.Return(returnTarget, Expression.Convert(tryparseVarExp, typeof(object))),
                                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                )
                               }
                           );
                        break;
                    case "System.Numerics.BigInteger": return Expression.Return(returnTarget, Expression.Convert(Expression.Call(MethodBigIntegerParse, Expression.Call(MethodToString, valueExp)), typeof(object)));
                    case "System.TimeSpan":
                        ParameterExpression tryparseVarTsExp, valueStrExp;
                        return Expression.Block(
                               new[] { tryparseVarExp = Expression.Variable(typeof(double)), tryparseVarTsExp = Expression.Variable(typeof(TimeSpan)), valueStrExp = Expression.Variable(typeof(string)) },
                               new Expression[] {
                                    Expression.Assign(valueStrExp, Expression.Call(MethodToString, valueExp)),
                                    Expression.IfThenElse(
                                        Expression.IsTrue(Expression.Call(MethodDoubleTryParse, valueStrExp, Expression.Constant(System.Globalization.NumberStyles.Any), Expression.Constant(null, typeof(IFormatProvider)), tryparseVarExp)),
                                        Expression.Return(returnTarget, Expression.Convert(Expression.Call(MethodTimeSpanFromSeconds, tryparseVarExp), typeof(object))),
                                        Expression.IfThenElse(
                                            Expression.IsTrue(Expression.Call(MethodTimeSpanTryParse, valueStrExp, tryparseVarTsExp)),
                                            Expression.Return(returnTarget, Expression.Convert(tryparseVarTsExp, typeof(object))),
                                            Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                        )
                                    )
                               }
                           );
                    case "System.SByte":
                        tryparseExp = Expression.Block(
                           new[] { tryparseVarExp = Expression.Variable(typeof(sbyte)) },
                           new Expression[] {
                                Expression.IfThenElse(
                                    Expression.IsTrue(Expression.Call(MethodSByteTryParse, Expression.Convert(valueExp, typeof(string)), tryparseVarExp)),
                                    Expression.Return(returnTarget, Expression.Convert(tryparseVarExp, typeof(object))),
                                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                )
                               }
                           );
                        break;
                    case "System.Int16":
                        tryparseExp = Expression.Block(
                              new[] { tryparseVarExp = Expression.Variable(typeof(short)) },
                               new Expression[] {
                                Expression.IfThenElse(
                                    Expression.IsTrue(Expression.Call(MethodShortTryParse, Expression.Convert(valueExp, typeof(string)), Expression.Constant(System.Globalization.NumberStyles.Any), Expression.Constant(null, typeof(IFormatProvider)), tryparseVarExp)),
                                    Expression.Return(returnTarget, Expression.Convert(tryparseVarExp, typeof(object))),
                                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                )
                               }
                           );
                        break;
                    case "System.Int32":
                        tryparseExp = Expression.Block(
                              new[] { tryparseVarExp = Expression.Variable(typeof(int)) },
                               new Expression[] {
                                Expression.IfThenElse(
                                    Expression.IsTrue(Expression.Call(MethodIntTryParse, Expression.Convert(valueExp, typeof(string)), Expression.Constant(System.Globalization.NumberStyles.Any), Expression.Constant(null, typeof(IFormatProvider)), tryparseVarExp)),
                                    Expression.Return(returnTarget, Expression.Convert(tryparseVarExp, typeof(object))),
                                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                )
                               }
                           );
                        break;
                    case "System.Int64":
                        tryparseExp = Expression.Block(
                              new[] { tryparseVarExp = Expression.Variable(typeof(long)) },
                               new Expression[] {
                                Expression.IfThenElse(
                                    Expression.IsTrue(Expression.Call(MethodLongTryParse, Expression.Convert(valueExp, typeof(string)), Expression.Constant(System.Globalization.NumberStyles.Any), Expression.Constant(null, typeof(IFormatProvider)), tryparseVarExp)),
                                    Expression.Return(returnTarget, Expression.Convert(tryparseVarExp, typeof(object))),
                                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                )
                               }
                           );
                        break;
                    case "System.Byte":
                        tryparseExp = Expression.Block(
                              new[] { tryparseVarExp = Expression.Variable(typeof(byte)) },
                               new Expression[] {
                                Expression.IfThenElse(
                                    Expression.IsTrue(Expression.Call(MethodByteTryParse, Expression.Convert(valueExp, typeof(string)), tryparseVarExp)),
                                    Expression.Return(returnTarget, Expression.Convert(tryparseVarExp, typeof(object))),
                                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                )
                               }
                           );
                        break;
                    case "System.UInt16":
                        tryparseExp = Expression.Block(
                               new[] { tryparseVarExp = Expression.Variable(typeof(ushort)) },
                               new Expression[] {
                                Expression.IfThenElse(
                                    Expression.IsTrue(Expression.Call(MethodUShortTryParse, Expression.Convert(valueExp, typeof(string)), Expression.Constant(System.Globalization.NumberStyles.Any), Expression.Constant(null, typeof(IFormatProvider)), tryparseVarExp)),
                                    Expression.Return(returnTarget, Expression.Convert(tryparseVarExp, typeof(object))),
                                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                )
                               }
                           );
                        break;
                    case "System.UInt32":
                        tryparseExp = Expression.Block(
                               new[] { tryparseVarExp = Expression.Variable(typeof(uint)) },
                               new Expression[] {
                                Expression.IfThenElse(
                                    Expression.IsTrue(Expression.Call(MethodUIntTryParse, Expression.Convert(valueExp, typeof(string)), Expression.Constant(System.Globalization.NumberStyles.Any), Expression.Constant(null, typeof(IFormatProvider)), tryparseVarExp)),
                                    Expression.Return(returnTarget, Expression.Convert(tryparseVarExp, typeof(object))),
                                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                )
                               }
                           );
                        break;
                    case "System.UInt64":
                        tryparseExp = Expression.Block(
                              new[] { tryparseVarExp = Expression.Variable(typeof(ulong)) },
                               new Expression[] {
                                Expression.IfThenElse(
                                    Expression.IsTrue(Expression.Call(MethodULongTryParse, Expression.Convert(valueExp, typeof(string)), Expression.Constant(System.Globalization.NumberStyles.Any), Expression.Constant(null, typeof(IFormatProvider)), tryparseVarExp)),
                                    Expression.Return(returnTarget, Expression.Convert(tryparseVarExp, typeof(object))),
                                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                )
                               }
                           );
                        break;
                    case "System.Single":
                        tryparseExp = Expression.Block(
                              new[] { tryparseVarExp = Expression.Variable(typeof(float)) },
                               new Expression[] {
                                Expression.IfThenElse(
                                    Expression.IsTrue(Expression.Call(MethodFloatTryParse, Expression.Convert(valueExp, typeof(string)), Expression.Constant(System.Globalization.NumberStyles.Any), Expression.Constant(null, typeof(IFormatProvider)), tryparseVarExp)),
                                    Expression.Return(returnTarget, Expression.Convert(tryparseVarExp, typeof(object))),
                                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                )
                               }
                           );
                        break;
                    case "System.Double":
                        tryparseExp = Expression.Block(
                               new[] { tryparseVarExp = Expression.Variable(typeof(double)) },
                               new Expression[] {
                                Expression.IfThenElse(
                                    Expression.IsTrue(Expression.Call(MethodDoubleTryParse, Expression.Convert(valueExp, typeof(string)), Expression.Constant(System.Globalization.NumberStyles.Any), Expression.Constant(null, typeof(IFormatProvider)), tryparseVarExp)),
                                    Expression.Return(returnTarget, Expression.Convert(tryparseVarExp, typeof(object))),
                                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                )
                               }
                           );
                        break;
                    case "System.Decimal":
                        tryparseExp = Expression.Block(
                              new[] { tryparseVarExp = Expression.Variable(typeof(decimal)) },
                               new Expression[] {
                                Expression.IfThenElse(
                                    Expression.IsTrue(Expression.Call(MethodDecimalTryParse, Expression.Convert(valueExp, typeof(string)), Expression.Constant(System.Globalization.NumberStyles.Any), Expression.Constant(null, typeof(IFormatProvider)), tryparseVarExp)),
                                    Expression.Return(returnTarget, Expression.Convert(tryparseVarExp, typeof(object))),
                                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                )
                               }
                           );
                        break;
                    case "System.DateTime":
                        tryparseExp = Expression.Block(
                              new[] { tryparseVarExp = Expression.Variable(typeof(DateTime)) },
                               new Expression[] {
                                Expression.IfThenElse(
                                    Expression.IsTrue(Expression.Call(MethodDateTimeTryParse, Expression.Convert(valueExp, typeof(string)), tryparseVarExp)),
                                    Expression.Return(returnTarget, Expression.Convert(tryparseVarExp, typeof(object))),
                                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                )
                               }
                           );
                        break;
                    case "System.DateTimeOffset":
                        tryparseExp = Expression.Block(
                              new[] { tryparseVarExp = Expression.Variable(typeof(DateTimeOffset)) },
                               new Expression[] {
                                Expression.IfThenElse(
                                    Expression.IsTrue(Expression.Call(MethodDateTimeOffsetTryParse, Expression.Convert(valueExp, typeof(string)), tryparseVarExp)),
                                    Expression.Return(returnTarget, Expression.Convert(tryparseVarExp, typeof(object))),
                                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(typeOrg), typeof(object)))
                                )
                               }
                           );
                        break;
                    case "System.Boolean":
                        tryparseBooleanExp = Expression.Return(returnTarget,
                                Expression.Convert(
                                    Expression.Not(
                                        Expression.Or(
                                            Expression.Equal(Expression.Convert(valueExp, typeof(string)), Expression.Constant("False")),
                                        Expression.Or(
                                            Expression.Equal(Expression.Convert(valueExp, typeof(string)), Expression.Constant("false")),
                                            Expression.Equal(Expression.Convert(valueExp, typeof(string)), Expression.Constant("0"))))),
                            typeof(object))
                        );
                        break;
                    default:
                        foreach (var switchFunc in GetDataReaderValueBlockExpressionSwitchTypeFullName)
                        {
                            var switchFuncRet = switchFunc(returnTarget, valueExp, type);
                            if (switchFuncRet != null) return switchFuncRet;
                        }
                        break;
                }
                Expression callToStringExp = Expression.Return(returnTarget, Expression.Convert(Expression.Call(MethodToString, valueExp), typeof(object)));
                foreach (var toStringFunc in GetDataReaderValueBlockExpressionObjectToStringIfThenElse)
                    callToStringExp = toStringFunc(returnTarget, valueExp, callToStringExp, type);
                Expression switchExp = Expression.Return(returnTarget, Expression.Call(MethodConvertChangeType, valueExp, Expression.Constant(type, typeof(Type))));
                Expression defaultRetExp = switchExp;
                if (tryparseExp != null)
                    switchExp = Expression.Switch(
                        Expression.Constant(type),
                        Expression.SwitchCase(tryparseExp,
                            Expression.Constant(typeof(Guid)),
                            Expression.Constant(typeof(sbyte)), Expression.Constant(typeof(short)), Expression.Constant(typeof(int)), Expression.Constant(typeof(long)),
                            Expression.Constant(typeof(byte)), Expression.Constant(typeof(ushort)), Expression.Constant(typeof(uint)), Expression.Constant(typeof(ulong)),
                            Expression.Constant(typeof(double)), Expression.Constant(typeof(float)), Expression.Constant(typeof(decimal)),
                            Expression.Constant(typeof(DateTime)), Expression.Constant(typeof(DateTimeOffset))
                        )
                    );
                else if (tryparseBooleanExp != null)
                    switchExp = Expression.Switch(
                        Expression.Constant(type),
                        Expression.SwitchCase(tryparseBooleanExp, Expression.Constant(typeof(bool)))
                    );
                else if (type == typeof(string))
                    defaultRetExp = switchExp = callToStringExp;

                return Expression.IfThenElse(
                    Expression.TypeEqual(valueExp, type),
                    Expression.Return(returnTarget, valueExp),
                    Expression.IfThenElse(
                        Expression.TypeEqual(valueExp, typeof(string)),
                        switchExp,
                        Expression.IfThenElse(
                            Expression.AndAlso(Expression.Equal(Expression.Constant(type), Expression.Constant(typeof(DateTime))), Expression.TypeEqual(valueExp, typeof(DateTimeOffset))),
                            Expression.Return(returnTarget, Expression.Convert(Expression.MakeMemberAccess(Expression.Convert(valueExp, typeof(DateTimeOffset)), PropertyDateTimeOffsetDateTime), typeof(object))),
                            Expression.IfThenElse(
                                Expression.AndAlso(Expression.Equal(Expression.Constant(type), Expression.Constant(typeof(DateTimeOffset))), Expression.TypeEqual(valueExp, typeof(DateTime))),
                                Expression.Return(returnTarget, Expression.Convert(
                                    Expression.New(CtorDateTimeOffsetArgsTicks, Expression.MakeMemberAccess(Expression.Convert(valueExp, typeof(DateTime)), PropertyDateTimeTicks), Expression.Constant(TimeSpan.Zero)), typeof(object))),
                                Expression.IfThenElse(
                                    Expression.TypeEqual(valueExp, typeof(byte[])),
                                    Expression.IfThenElse(
                                        Expression.Or(Expression.Equal(Expression.Constant(type), Expression.Constant(typeof(Guid))), Expression.Equal(Expression.Constant(type), Expression.Constant(typeof(Guid?)))),
                                        Expression.Return(returnTarget, Expression.Convert(Expression.Call(MethodBytesToGuid, Expression.Convert(valueExp, typeof(byte[]))), typeof(object))),
                                        Expression.IfThenElse(
                                            Expression.Equal(Expression.Constant(type), Expression.Constant(typeof(string))),
                                            Expression.Return(returnTarget, Expression.Convert(Expression.Call(Expression.Constant(DefaultEncoding), MethodEncodingGetString, Expression.Convert(valueExp, typeof(byte[]))), typeof(object))),
                                            defaultRetExp
                                        )
                                    ),
                                    defaultRetExp
                                )
                            )
                        )
                    )
                );
            };

            return Expression.Block(
                new[] { valueExp },
                Expression.Assign(valueExp, Expression.Convert(value, typeof(object))),
                Expression.IfThenElse(
                    Expression.Or(
                        Expression.Equal(valueExp, Expression.Constant(null)),
                        Expression.Equal(valueExp, Expression.Constant(DBNull.Value))
                    ),
                    Expression.Return(returnTarget, Expression.Convert(Expression.Default(type), typeof(object))),
                    funcGetExpression()
                ),
                Expression.Label(returnTarget, Expression.Default(typeof(object)))
            );
        }
        public static object GetDataReaderValue(Type type, object value)
        {
            //if (value == null || value == DBNull.Value) return Activator.CreateInstance(type);
            if (type == null) return value;
            var func = _dicGetDataReaderValue.GetOrAdd(type, k1 => new ConcurrentDictionary<Type, Func<object, object>>()).GetOrAdd(value?.GetType() ?? type, valueType =>
            {
                var parmExp = Expression.Parameter(typeof(object), "value");
                var exp = GetDataReaderValueBlockExpression(type, parmExp);
                return Expression.Lambda<Func<object, object>>(exp, parmExp).Compile();
            });
            try
            {
                return func(value);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"ExpressionTree 转换类型错误，值({string.Concat(value)})，类型({value.GetType().FullName})，目标类型({type.FullName})，{ex.Message}");
            }
        }


        #endregion
    }
}