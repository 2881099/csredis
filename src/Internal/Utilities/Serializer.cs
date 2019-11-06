using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;

namespace CSRedis.Internal.Utilities
{
    internal static class Serializer<T>
        where T : class
    {
        static readonly Lazy<Func<T, Dictionary<string, string>>>
            _propertySerializer;
        static readonly Lazy<Func<Dictionary<string, string>, T>>
            _propertyDeserializer;
        static readonly Lazy<Func<T, Dictionary<string, string>>>
            _serializer;
        static readonly Lazy<Func<Dictionary<string, string>, T>>
            _deserializer;

        static Serializer()
        {
            _propertySerializer
                = new Lazy<Func<T, Dictionary<string, string>>>(CompilePropertySerializer);
            _propertyDeserializer
                = new Lazy<Func<Dictionary<string, string>, T>>(CompilePropertyDeserializer);
            _serializer
                = new Lazy<Func<T, Dictionary<string, string>>>(CompileSerializer);
            _deserializer
                = new Lazy<Func<Dictionary<string, string>, T>>(CompileDeserializer);
        }

        public static Dictionary<string, string> Serialize(T obj)
        {
            if (typeof(ISerializable).IsAssignableFrom(typeof(T)))
                return _serializer.Value(obj);
            else
                return _propertySerializer.Value(obj);
        }

        public static T Deserialize(Dictionary<string, string> fields)
        {
            if (typeof(ISerializable).IsAssignableFrom(typeof(T)))
                return _deserializer.Value(fields);
            else
                return _propertyDeserializer.Value(fields);
        }


        static Func<T, Dictionary<string, string>> CompilePropertySerializer()
        {
            var o_t = typeof(T);
            var o = Expression.Parameter(o_t, "o");

            var d_t = typeof(Dictionary<string, string>);
            var d = Expression.Variable(d_t, "d");
            var d_init = Expression.MemberInit(Expression.New(d_t));
            var d_add = d_t.GetMethod("Add");
            var d_setters = o_t.GetProperties(BindingFlags.Public | BindingFlags.Instance) // build setters via Add(k,v)
                .Where(x => x.CanRead)
                .Select(x =>
                {
                    var prop = Expression.Property(o, x.Name);
                    var prop_mi_to_string = x.PropertyType.GetMethod("ToString", new Type[0]);
                    var add_to_dict = Expression.Call(d, d_add, Expression.Constant(x.Name), Expression.Call(prop, prop_mi_to_string));

                    if (!x.PropertyType.IsByRef)
                        return (Expression)add_to_dict;
                    else
                        return (Expression)Expression.IfThen(
                            Expression.Not(Expression.Equal(prop, Expression.Constant(null))),
                            add_to_dict);
                });

            // run this
            var body = Expression.Block(new[] { d }, // scope variables
                Expression.Assign(d, d_init), // initialize
                Expression.Block(d_setters), // set
                d); // return

            return Expression.Lambda<Func<T, Dictionary<string, string>>>(body, o)
                .Compile();
        }

        static Func<Dictionary<string, string>, T> CompilePropertyDeserializer()
        {
            var o_t = typeof(T);
            var o = Expression.Variable(o_t, "o");
            var o_new = Expression.New(typeof(T));

            var d_t = typeof(Dictionary<string, string>);
            var d = Expression.Parameter(d_t, "d");
            var d_mi_try_get_value = d_t.GetMethod("TryGetValue");

            var item_t = typeof(String);
            var item = Expression.Variable(item_t, "item");

            var tc_t = typeof(TypeConverter);
            var tc = Expression.Variable(tc_t, "tc");
            var tc_mi_can_convert_from = tc_t.GetMethod("CanConvertFrom", new[] { typeof(Type) });
            var tc_mi_convert_from = tc_t.GetMethod("ConvertFrom", new[] { typeof(Object) });

            var td_t = typeof(TypeDescriptor);
            var td_mi_get_converter = td_t.GetMethod("GetConverter", new[] { typeof(Type) });

            var binds = o_t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.CanRead)
                .Select(x =>
                {
                    var value_t = x.PropertyType;
                    var value = Expression.Variable(value_t, "value");
                    var target = Expression.Label(x.PropertyType);

                    return Expression.Bind(x, Expression.Block(new[] { item, value },
                        Expression.Assign(tc, Expression.Call(null, td_mi_get_converter, Expression.Constant(x.PropertyType))),
                        Expression.IfThen(
                            Expression.Call(d, d_mi_try_get_value, Expression.Constant(x.Name), item),
                            Expression.IfThen(
                                Expression.NotEqual(item, Expression.Constant(null)),
                                Expression.IfThen(
                                    Expression.Call(tc, tc_mi_can_convert_from, Expression.Constant(typeof(String))),
                                    Expression.Block(
                                        Expression.Assign(value, Expression.Convert(Expression.Call(tc, tc_mi_convert_from, item), x.PropertyType)),
                                        Expression.Return(target, value, x.PropertyType))))),
                        Expression.Label(target, value)
                    ));
                }).ToArray();

            var body = Expression.Block(new[] { o, tc },
                Expression.MemberInit(o_new, binds)
            );

            return Expression.Lambda<Func<Dictionary<string, string>, T>>(body, d)
                .Compile();
        }

        static Func<T, Dictionary<string, string>> CompileSerializer()
        {
            var o_t = typeof(T);
            var o = Expression.Parameter(o_t, "original");
            var o_get_object_data = o_t.GetMethod("GetObjectData");

            var d_t = typeof(Dictionary<string, string>);
            var d = Expression.Variable(d_t, "d"); // define object variable
            var d_init = Expression.MemberInit(Expression.New(d_t)); // object ctor
            var d_add = d_t.GetMethod("Add"); // add method

            var fc_t = typeof(IFormatterConverter);// typeof(LocalVariableInfo);
            var fc = Expression.Variable(fc_t, "fc");
            var fc_init = Expression.MemberInit(Expression.New(typeof(System.Runtime.Serialization.FormatterConverter))); //Expression.MemberInit(Expression.New(fc_t));

            var info_t = typeof(SerializationInfo);
            var info = Expression.Variable(info_t, "info");
            var info_ctor = info_t.GetConstructor(new[] { typeof(Type), fc_t });
            var info_init = Expression.MemberInit(Expression.New(info_ctor, Expression.Constant(o_t), fc));
            var info_get_enumerator = info_t.GetMethod("GetEnumerator");

            var ctx_t = typeof(StreamingContext);
            var ctx = Expression.Variable(ctx_t, "ctx");
            var ctx_init = Expression.MemberInit(Expression.New(ctx_t));

            var enumerator_t = typeof(SerializationInfoEnumerator);
            var enumerator = Expression.Variable(enumerator_t, "enumerator");
            var enumerator_move_next = enumerator_t.GetMethod("MoveNext");
            var enumerator_name = Expression.Property(enumerator, "Name");
            var enumerator_value = Expression.Property(enumerator, "Value");
            var mi_to_string = typeof(Object).GetMethod("ToString", new Type[0]);
            var exit_loop = Expression.Label("exit_loop");
            var body = Expression.Block(new[] { d, fc, info, ctx },
                Expression.Assign(d, d_init),
                Expression.Assign(fc, fc_init),
                Expression.Assign(info, info_init),
                Expression.Assign(ctx, ctx_init),
                Expression.Call(o, o_get_object_data, info, ctx),

                Expression.Block(new[] { enumerator },
                    Expression.Assign(enumerator, Expression.Call(info, info_get_enumerator)),
                    Expression.Loop(
                        Expression.IfThenElse(
                            Expression.Call(enumerator, enumerator_move_next), // test
                            Expression.IfThen(
                                Expression.NotEqual(enumerator_value, Expression.Constant(null)),
                                Expression.Call(d, d_add, enumerator_name, Expression.Call(enumerator_value, mi_to_string))
                            ),
                            Expression.Break(exit_loop)), // if false
                        exit_loop)),
                d); // return

            // compile
            return Expression.Lambda<Func<T, Dictionary<string, string>>>(body, o)
                .Compile();
        }

        static Func<Dictionary<string, string>, T> CompileDeserializer()
        {
            var o_t = typeof(T);
            var o_ctor = o_t.GetConstructor(new[] { typeof(SerializationInfo), typeof(StreamingContext) });

            var d_t = typeof(Dictionary<string, string>);
            var d = Expression.Parameter(d_t, "d");
            var d_mi_get_enumerator = d_t.GetMethod("GetEnumerator");

            var fc_t = typeof(IFormatterConverter);// typeof(LocalVariableInfo);
            var fc = Expression.Variable(fc_t, "fc");
            var fc_init = Expression.MemberInit(Expression.New(typeof(System.Runtime.Serialization.FormatterConverter))); //Expression.MemberInit(Expression.New(fc_t));

            var info_t = typeof(SerializationInfo);
            var info = Expression.Variable(info_t, "info");
            var info_ctor = info_t.GetConstructor(new[] { typeof(Type), fc_t });
            var info_init = Expression.MemberInit(Expression.New(info_ctor, Expression.Constant(o_t), fc));
            var info_mi_add_value = info_t.GetMethod("AddValue", new[] { typeof(String), typeof(Object) });

            var ctx_t = typeof(StreamingContext);
            var ctx = Expression.Variable(ctx_t, "ctx");
            var ctx_init = Expression.MemberInit(Expression.New(ctx_t));

            var enumerator_t = typeof(Dictionary<string, string>.Enumerator);
            var enumerator = Expression.Variable(enumerator_t, "enumerator");
            var enumerator_mi_move_next = enumerator_t.GetMethod("MoveNext");
            var enumerator_current = Expression.Property(enumerator, "Current");

            var kvp_t = typeof(KeyValuePair<string, string>);
            var kvp_pi_key = kvp_t.GetProperty("Key");
            var kvp_pi_value = kvp_t.GetProperty("Value");

            var exit_loop = Expression.Label("exit_loop");

            var body = Expression.Block(new[] { fc, info, ctx, enumerator },
                Expression.Assign(fc, fc_init),
                Expression.Assign(info, info_init),
                Expression.Assign(ctx, ctx_init),
                Expression.Assign(enumerator, Expression.Call(d, d_mi_get_enumerator)),

                Expression.Loop(
                    Expression.IfThenElse(
                        Expression.Call(enumerator, enumerator_mi_move_next),
                        Expression.Call(info, info_mi_add_value, Expression.Property(enumerator_current, kvp_pi_key), Expression.Property(enumerator_current, kvp_pi_value)),
                        Expression.Break(exit_loop)),
                    exit_loop),

                Expression.MemberInit(Expression.New(o_ctor, info, ctx))
            );

            return Expression.Lambda<Func<Dictionary<string, string>, T>>(body, d)
                .Compile();
        }
    }
}
