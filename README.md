# csredis

ServiceStack.redis 是商业版，免费版有限制；

StackExchange.Redis 是免费版，但是内核在 .NETCore 运行有问题，一并发就死锁，暂时无法解决；

 > CSRedis 是国外大神写的，经过少量修改，现已支持 .NETCore；

扩展：

1、重新开发了一个连接池管理 ConnectionPool

```csharp
//第一步：引入该项目 nuget Install-Package CSRedisCore 2.1.1

//第二步：将此类编写到您的项目中
public partial class RedisHelper : CSRedis.QuickHelperBase {
	public static IConfigurationRoot Configuration { get; internal set; }
	public static void InitializeConfiguration(IConfigurationRoot cfg) {
		Configuration = cfg;
		int port, poolsize, database;
		string ip, pass;
		if (!int.TryParse(cfg["ConnectionStrings:redis:port"], out port)) port = 6379;
		if (!int.TryParse(cfg["ConnectionStrings:redis:poolsize"], out poolsize)) poolsize = 50;
		if (!int.TryParse(cfg["ConnectionStrings:redis:database"], out database)) database = 0;
		ip = cfg["ConnectionStrings:redis:ip"];
		pass = cfg["ConnectionStrings:redis:pass"];
		Name = cfg["ConnectionStrings:redis:name"];
		Instance = new CSRedis.ConnectionPool(ip, port, poolsize);
		Instance.Connected += (s, o) => {
			CSRedis.RedisClient rc = s as CSRedis.RedisClient;
			if (!string.IsNullOrEmpty(pass)) rc.Auth(pass);
			if (database > 0) rc.Select(database);
		};
	}
}

//第二步：在 starup.cs 中配置 RedisHelper.InitializeConfiguration(Configuration);

//第三步：使用
RedisHelper.Set("test1", "123123", 60);
RedisHelper.Get("test1");

//...函数名基本与 redis-cli 的命令相同
```

2、原本作者没支持byte[]读与写，现已支持

```csharp
RedisHelper.SetBytes("test1", Encoding.UTF8.GetBytes("123123"), 60);
RedisHelper.GetBytes("test1");
```

# 3、缓存壳

```csharp
//不加缓存的时候，要从数据库查询
var t1 = Test.Select.WhereId(1).ToOne();

//一般的缓存代码，如不封装还挺繁琐的
var cacheValue = RedisHelper.Get("test1");
if (!string.IsNullOrEmpty(cacheValue)) {
	try {
		return JsonConvert.DeserializeObject(cacheValue);
	} catch {
		//出错时删除key
		RedisHelper.Remove("test1");
		throw;
	}
}
var t1 = Test.Select.WhereId(1).ToOne();
RedisHelper.Set("test1", JsonConvert.SerializeObject(t1), 10); //缓存10秒

//使用缓存壳效果同上，以下示例使用 string 和 hash 缓存数据
var t1 = RedisHelper.Cache("test1", 10, () => Test.Select.WhereId(1).ToOne());
var t2 = RedisHelper.Cache("test", "1", 10, () => Test.Select.WhereId(1).ToOne());
```

> 为减少csredis的依赖，缓存壳默认序列化，需要在 RedisHelper 自行重截，代码如下：

```csharp
#region 缓存壳
/// <summary>
/// 缓存壳
/// </summary>
/// <typeparam name="T">缓存类型</typeparam>
/// <param name="key">不含prefix前辍RedisHelper.Name</param>
/// <param name="timeoutSeconds">缓存秒数</param>
/// <param name="getData">获取源数据的函数</param>
/// <returns></returns>
public static T Cache<T>(string key, int timeoutSeconds, Func<T> getData) => Cache(key, timeoutSeconds, getData, data => Newtonsoft.Json.JsonConvert.SerializeObject(data), cacheValue => Newtonsoft.Json.JsonConvert.DeserializeObject<T>(cacheValue));
/// <summary>
/// 缓存壳(哈希表)
/// </summary>
/// <typeparam name="T">缓存类型</typeparam>
/// <param name="key">不含prefix前辍RedisHelper.Name</param>
/// <param name="field">字段</param>
/// <param name="timeoutSeconds">缓存秒数</param>
/// <param name="getData">获取源数据的函数</param>
/// <returns></returns>
public static T Cache<T>(string key, string field, int timeoutSeconds, Func<T> getData) => Cache(key, field, timeoutSeconds, getData, data => Newtonsoft.Json.JsonConvert.SerializeObject(data), cacheValue => Newtonsoft.Json.JsonConvert.DeserializeObject<(T, DateTime)>(cacheValue));
/// <summary>
/// 缓存壳
/// </summary>
/// <typeparam name="T">缓存类型</typeparam>
/// <param name="key">不含prefix前辍RedisHelper.Name</param>
/// <param name="timeoutSeconds">缓存秒数</param>
/// <param name="getDataAsync">获取源数据的函数</param>
/// <returns></returns>
async public static Task<T> CacheAsync<T>(string key, int timeoutSeconds, Func<Task<T>> getDataAsync) => await CacheAsync(key, timeoutSeconds, getDataAsync, data => Newtonsoft.Json.JsonConvert.SerializeObject(data), cacheValue => Newtonsoft.Json.JsonConvert.DeserializeObject<T>(cacheValue));
/// <summary>
/// 缓存壳(哈希表)
/// </summary>
/// <typeparam name="T">缓存类型</typeparam>
/// <param name="key">不含prefix前辍RedisHelper.Name</param>
/// <param name="field">字段</param>
/// <param name="timeoutSeconds">缓存秒数</param>
/// <param name="getDataAsync">获取源数据的函数</param>
/// <returns></returns>
async public static Task<T> CacheAsync<T>(string key, string field, int timeoutSeconds, Func<Task<T>> getDataAsync) => await CacheAsync(key, field, timeoutSeconds, getDataAsync, data => Newtonsoft.Json.JsonConvert.SerializeObject(data), cacheValue => Newtonsoft.Json.JsonConvert.DeserializeObject<(T, DateTime)>(cacheValue));
#endregion
```