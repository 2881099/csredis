# csredis

ServiceStack.redis 是商业版，免费版有限制；

StackExchange.Redis 是免费版，但是内核在 .NETCore 运行有问题，一并发就死锁，暂时无法解决；

 > CSRedis 是国外大神写的，经过少量修改，现已支持 .NETCore；

扩展：

1、重新开发了 CSRedisClient，集成连接池和扩展方法

```csharp
//第一步：引入该项目 nuget Install-Package CSRedisCore 2.2.2

//第二步：使用单例模式定义

var csredis = new CSRedis.CSRedisClient(ip: "127.0.0.1", port: 6379, pass: "", poolsize: 50, database: 0, name: "prefix前辍");

//第三步：使用
csredis.Set("test1", "123123", 60);
csredis.Get("test1");

//...函数名基本与 redis-cli 的命令相同
```

2、原本作者没支持byte[]读与写，现已支持

```csharp
csredis.SetBytes("test1", Encoding.UTF8.GetBytes("123123"), 60);
csredis.GetBytes("test1");
```

# 3、缓存壳

```csharp
//不加缓存的时候，要从数据库查询
var t1 = Test.Select.WhereId(1).ToOne();

//一般的缓存代码，如不封装还挺繁琐的
var cacheValue = csredis.Get("test1");
if (!string.IsNullOrEmpty(cacheValue)) {
	try {
		return JsonConvert.DeserializeObject(cacheValue);
	} catch {
		//出错时删除key
		csredis.Remove("test1");
		throw;
	}
}
var t1 = Test.Select.WhereId(1).ToOne();
csredis.Set("test1", JsonConvert.SerializeObject(t1), 10); //缓存10秒

//使用缓存壳效果同上，以下示例使用 string 和 hash 缓存数据
var t1 = csredis.Cache("test1", 10, () => Test.Select.WhereId(1).ToOne());
var t2 = csredis.Cache("test", "1", 10, () => Test.Select.WhereId(1).ToOne());
```

> 为减少csredis的依赖，缓存壳默认序列化，请使用新类继承 CSRedisClient 重截以下方法：

```csharp
#region 缓存壳
/// <summary>
/// 缓存壳
/// </summary>
/// <typeparam name="T">缓存类型</typeparam>
/// <param name="key">不含prefix前辍</param>
/// <param name="timeoutSeconds">缓存秒数</param>
/// <param name="getData">获取源数据的函数</param>
/// <returns></returns>
public static T Cache<T>(string key, int timeoutSeconds, Func<T> getData) => Cache(key, timeoutSeconds, getData, data => Newtonsoft.Json.JsonConvert.SerializeObject(data), cacheValue => Newtonsoft.Json.JsonConvert.DeserializeObject<T>(cacheValue));
/// <summary>
/// 缓存壳(哈希表)
/// </summary>
/// <typeparam name="T">缓存类型</typeparam>
/// <param name="key">不含prefix前辍</param>
/// <param name="field">字段</param>
/// <param name="timeoutSeconds">缓存秒数</param>
/// <param name="getData">获取源数据的函数</param>
/// <returns></returns>
public static T Cache<T>(string key, string field, int timeoutSeconds, Func<T> getData) => Cache(key, field, timeoutSeconds, getData, data => Newtonsoft.Json.JsonConvert.SerializeObject(data), cacheValue => Newtonsoft.Json.JsonConvert.DeserializeObject<(T, DateTime)>(cacheValue));
/// <summary>
/// 缓存壳
/// </summary>
/// <typeparam name="T">缓存类型</typeparam>
/// <param name="key">不含prefix前辍</param>
/// <param name="timeoutSeconds">缓存秒数</param>
/// <param name="getDataAsync">获取源数据的函数</param>
/// <returns></returns>
async public static Task<T> CacheAsync<T>(string key, int timeoutSeconds, Func<Task<T>> getDataAsync) => await CacheAsync(key, timeoutSeconds, getDataAsync, data => Newtonsoft.Json.JsonConvert.SerializeObject(data), cacheValue => Newtonsoft.Json.JsonConvert.DeserializeObject<T>(cacheValue));
/// <summary>
/// 缓存壳(哈希表)
/// </summary>
/// <typeparam name="T">缓存类型</typeparam>
/// <param name="key">不含prefix前辍</param>
/// <param name="field">字段</param>
/// <param name="timeoutSeconds">缓存秒数</param>
/// <param name="getDataAsync">获取源数据的函数</param>
/// <returns></returns>
async public static Task<T> CacheAsync<T>(string key, string field, int timeoutSeconds, Func<Task<T>> getDataAsync) => await CacheAsync(key, field, timeoutSeconds, getDataAsync, data => Newtonsoft.Json.JsonConvert.SerializeObject(data), cacheValue => Newtonsoft.Json.JsonConvert.DeserializeObject<(T, DateTime)>(cacheValue));
#endregion
```