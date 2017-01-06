using System;
using System.Collections.Generic;
using System.Globalization;

namespace CSRedis.Internal.Utilities
{
    static class RedisArgs
    {
        /// <summary>
        /// Join arrays
        /// </summary>
        /// <param name="arrays">Arrays to join</param>
        /// <returns>Array of ToString() elements in each array</returns>
        public static string[] Concat(params object[][] arrays)
        {
            int count = 0;
            foreach (var ar in arrays)
                count += ar.Length;

            int pos = 0;
            string[] output = new string[count];
            for (int i = 0; i < arrays.Length; i++)
            {
                for (int j = 0; j < arrays[i].Length; j++)
                {
                    object obj = arrays[i][j];
                    output[pos++] = obj == null ? String.Empty : String.Format(CultureInfo.InvariantCulture, "{0}", obj);
                }
            }
            return output;
        }

        /// <summary>
        /// Joine string with arrays
        /// </summary>
        /// <param name="str">Leading string element</param>
        /// <param name="arrays">Array to join</param>
        /// <returns>Array of str and ToString() elements of arrays</returns>
        public static string[] Concat(string str, params object[] arrays)
        {
            return Concat(new[] { str }, arrays);
        }

        /// <summary>
        /// Convert array of two-element tuple into flat array arguments
        /// </summary>
        /// <typeparam name="TItem1">Type of first item</typeparam>
        /// <typeparam name="TItem2">Type of second item</typeparam>
        /// <param name="tuples">Array of tuple arguments</param>
        /// <returns>Flattened array of arguments</returns>
        public static object[] GetTupleArgs<TItem1, TItem2>(Tuple<TItem1, TItem2>[] tuples)
        {
            List<object> args = new List<object>();
            foreach (var kvp in tuples)
                args.AddRange(new object[] { kvp.Item1, kvp.Item2 });

            return args.ToArray();
        }

        /// <summary>
        /// Parse score for +/- infinity and inclusive/exclusive
        /// </summary>
        /// <param name="score">Numeric base score</param>
        /// <param name="isExclusive">Score is exclusive, rather than inclusive</param>
        /// <returns>String representing Redis score/range notation</returns>
        public static string GetScore(double score, bool isExclusive)
        {
            if (Double.IsNegativeInfinity(score) || score == Double.MinValue)
                return "-inf";
            else if (Double.IsPositiveInfinity(score) || score == Double.MaxValue)
                return "+inf";
            else if (isExclusive)
                return '(' + score.ToString();
            else
                return score.ToString();
        }

        public static object[] FromDict(Dictionary<string, string> dict)
        {
            var array = new List<object>();
            foreach (var keyValue in dict)
            {
                if (keyValue.Key != null && keyValue.Value != null)
                    array.AddRange(new[] { keyValue.Key, keyValue.Value });
            }
            return array.ToArray();
        }

        public static object[] FromObject<T>(T obj)
            where T : class
        {
            var dict = Serializer<T>.Serialize(obj);
            object[] array = new object[dict.Count * 2];
            int i = 0;
            foreach (var item in dict)
            {
                array[i++] = item.Key;
                array[i++] = item.Value;
            }
            return array;
        }
    }
}
