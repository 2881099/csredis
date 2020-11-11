using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Caching.Distributed {
	public static class IDistributedCacheExtensions {

		/// <summary>
		/// 获取缓存，反序列化成对象返回
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="key">key</param>
		/// <returns>对象</returns>
		public static object GetObject(this IDistributedCache cache, string key) {
			return Deserialize(cache.Get(key));
		}
		/// <summary>
		/// 获取缓存，反序列化成对象返回
		/// </summary>
		/// <typeparam name="T">反序列化类型</typeparam>
		/// <param name="cache"></param>
		/// <param name="key">key</param>
		/// <returns>对象</returns>
		public static T GetObject<T>(this IDistributedCache cache, string key) {
			var obj = Deserialize(cache.Get(key));
			if (obj == null) return default(T);
			return (T)obj;
		}
		/// <summary>
		/// 获取缓存，反序列化成对象
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="key">key</param>
		/// <returns>对象</returns>
		async public static Task<object> GetObjectAsync(this IDistributedCache cache, string key) {
			return Deserialize(await cache.GetAsync(key));
		}
		/// <summary>
		/// 获取缓存，反序列化成对象
		/// </summary>
		/// <typeparam name="T">反序列化类型</typeparam>
		/// <param name="cache"></param>
		/// <param name="key">key</param>
		/// <returns>对象</returns>
		async public static Task<T> GetObjectAsync<T>(this IDistributedCache cache, string key) {
			var obj = Deserialize(await cache.GetAsync(key));
			if (obj == null) return default(T);
			return (T)obj;
		}
		/// <summary>
		/// 序列化对象后，设置缓存
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="key">key</param>
		/// <param name="value">对象</param>
		public static void SetObject(this IDistributedCache cache, string key, object value) {
			var data = Serialize(value);
			if (data == null) cache.Remove(key);
			else cache.Set(key, Serialize(value));
		}
		/// <summary>
		/// 序列化对象后，设置缓存
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="key">key</param>
		/// <param name="value">对象</param>
		/// <param name="options">策略</param>
		public static void SetObject(this IDistributedCache cache, string key, object value, DistributedCacheEntryOptions options) {
			var data = Serialize(value);
			if (data == null) cache.Remove(key);
			else cache.Set(key, Serialize(value), options);
		}
		/// <summary>
		/// 序列化对象后，设置缓存
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="key">key</param>
		/// <param name="value">对象</param>
		public static Task SetObjectAsync(this IDistributedCache cache, string key, object value) {
			var data = Serialize(value);
			if (data == null) return cache.RemoveAsync(key);
			else return cache.SetAsync(key, Serialize(value));
		}
		/// <summary>
		/// 序列化对象后，设置缓存
		/// </summary>
		/// <param name="cache"></param>
		/// <param name="key">key</param>
		/// <param name="value">对象</param>
		/// <param name="options">策略</param>
		public static Task SetObjectAsync(this IDistributedCache cache, string key, object value, DistributedCacheEntryOptions options) {
			var data = Serialize(value);
			if (data == null) return cache.RemoveAsync(key);
			else return cache.SetAsync(key, Serialize(value), options);
		}

		public static byte[] Serialize(object value) {
			if (value == null) return null;
			using (MemoryStream ms = new MemoryStream()) {
				IFormatter formatter = new BinaryFormatter();
#pragma warning disable SYSLIB0011 // 类型或成员已过时
                formatter.Serialize(ms, value);
#pragma warning restore SYSLIB0011 // 类型或成员已过时
                return ms.GetBuffer();
			}
		}
		public static object Deserialize(byte[] stream) {
			if (stream == null) return null;
			using (MemoryStream ms = new MemoryStream(stream)) {
				IFormatter formatter = new BinaryFormatter();
#pragma warning disable SYSLIB0011 // 类型或成员已过时
                return formatter.Deserialize(ms);
#pragma warning restore SYSLIB0011 // 类型或成员已过时
            }
		}
	}
}