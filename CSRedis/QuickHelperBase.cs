using System;
using System.Collections.Generic;
using System.Linq;

namespace CSRedis {
	public partial class QuickHelperBase {
		protected static string Name { get; set; }
		public static ConnectionPool Instance { get; protected set; }
		public static bool Set(string key, string value, int expireSeconds = -1) {
			key = string.Concat(Name, key);
			using(var conn = Instance.GetConnection()) {
				if (expireSeconds > 0)
					return conn.Client.Set(key, value, TimeSpan.FromSeconds(expireSeconds)) == "OK";
				else
					return conn.Client.Set(key, value) == "OK";
			}
		}
		public static bool SetBytes(string key, byte[] value, int expireSeconds = -1) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				if (expireSeconds > 0)
					return conn.Client.Set(key, value, TimeSpan.FromSeconds(expireSeconds)) == "OK";
				else
					return conn.Client.Set(key, value) == "OK";
			}
		}
		public static string Get(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.Get(key);
			}
		}
		public static byte[] GetBytes(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.GetBytes(key);
			}
		}
		public static long Remove(params string[] key) {
			if (key == null || key.Length == 0) return 0;
			string[] rkeys = new string[key.Length];
			for (int a = 0; a < key.Length; a++) rkeys[a] = string.Concat(Name, key[a]);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.Del(rkeys);
			}
		}
		public static bool Exists(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.Exists(key);
			}
		}
		public static long Increment(string key, long value = 1) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.IncrBy(key, value);
			}
		}
		public static bool Expire(string key, TimeSpan expire) {
			if (expire <= TimeSpan.Zero) return false;
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.Expire(key, expire);
			}
		}
		public static string[] Keys(string pattern) {
			using (var conn = Instance.GetConnection()) {
				return conn.Client.Keys(pattern);
			}
		}
		public static long Publish(string channel, string data) {
			using (var conn = Instance.GetConnection()) {
				return conn.Client.Publish(channel, data);
			}
		}
		#region Hash 操作
		public static string HashSet(string key, params object[] keyValues) {
			return HashSetExpire(key, TimeSpan.Zero, keyValues);
		}
		public static string HashSetExpire(string key, TimeSpan expire, params object[] keyValues) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				var ret = conn.Client.HMSet(key, keyValues.Select(a => string.Concat(a)).ToArray());
				if (expire > TimeSpan.Zero) conn.Client.Expire(key, expire);
				return ret;
			}
		}
		public static string HashGet(string key, string field) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HGet(key, field);
			}
		}
		public static long HashIncrement(string key, string field, long value = 1) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HIncrBy(key, field, value);
			}
		}
		public static long HashDelete(string key, params string[] fields) {
			if (fields == null || fields.Length == 0) return 0;
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HDel(key, fields);
			}
		}
		public static bool HashExists(string key, string field) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HExists(key, field);
			}
		}
		public static long HashLength(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HLen(key);
			}
		}
		public static Dictionary<string, string> HashGetAll(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HGetAll(key);
			}
		}
		public static string[] HashKeys(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HKeys(key);
			}
		}
		public static string[] HashVals(string key) {
			key = string.Concat(Name, key);
			using (var conn = Instance.GetConnection()) {
				return conn.Client.HVals(key);
			}
		}
		#endregion
	}
}