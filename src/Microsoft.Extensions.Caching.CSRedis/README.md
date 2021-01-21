由于 StackExchange.Redis 不可靠，导致 Microsoft.Extensions.Caching.Redis 不能放心使用。故使用 CSRedisCore 作为分布式缓存。

| Package Name |  NuGet | Downloads | |
|--------------|  ------- |  ---- | -- |
| CSRedisCore | [![nuget](https://img.shields.io/nuget/v/CSRedisCore.svg?style=flat-square)](https://www.nuget.org/packages/CSRedisCore) | [![stats](https://img.shields.io/nuget/dt/CSRedisCore.svg?style=flat-square)](https://www.nuget.org/stats/packages/CSRedisCore?groupby=Version) |
| Caching.CSRedis | [![nuget](https://img.shields.io/nuget/v/Caching.CSRedis.svg?style=flat-square)](https://www.nuget.org/packages/Caching.CSRedis) | [![stats](https://img.shields.io/nuget/dt/Caching.CSRedis.svg?style=flat-square)](https://www.nuget.org/stats/packages/Caching.CSRedis?groupby=Version) | IDistributedCache |

# 使用方法

> Install-Package Caching.CSRedis

## 普通模式

```csharp
var csredis = new CSRedis.CSRedisClient("127.0.0.1:6379,password=123,defaultDatabase=13,ssl=false,writeBuffer=10240,poolsize=50,prefix=key前辍");
services.AddSingleton<IDistributedCache>(new Microsoft.Extensions.Caching.Redis.CSRedisCache(csredis));
```

# 集群模式

```csharp
var csredis = new CSRedis.CSRedisClient(null,
  "127.0.0.1:6371,password=123,defaultDatabase=11,poolsize=10,ssl=false,writeBuffer=10240,prefix=key前辍", 
  "127.0.0.1:6372,password=123,defaultDatabase=12,poolsize=11,ssl=false,writeBuffer=10240,prefix=key前辍",
  "127.0.0.1:6373,password=123,defaultDatabase=13,poolsize=12,ssl=false,writeBuffer=10240,prefix=key前辍",
  "127.0.0.1:6374,password=123,defaultDatabase=14,poolsize=13,ssl=false,writeBuffer=10240,prefix=key前辍");
services.AddSingleton<IDistributedCache>(new Microsoft.Extensions.Caching.Redis.CSRedisCache(csredis));
```

# 缓存对象扩展方法

```csharp
IDistributedCache cache = xxxx;

object obj1 = new xxxx();
cache.SetObject("key1", obj1);

object obj2 = cache.GetObject("key1");
T obj3 = cache.GetObject<T>("key1");
```

# 批量删除

```csharp
IDistributedCache cache = xxxx;
cache.Remove("key1|key2");
```