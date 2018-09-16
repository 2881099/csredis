# csredis

ServiceStack.Redis 是商业版，免费版有限制；

StackExchange.Redis 是免费版，但是内核在 .NETCore 运行有问题经常 Timeout，暂无法解决；

CSRedis 是国外大神写的，经过少量修改，现已支持 .NETCore；鄙人作了以下扩展：

1、增加了 CSRedisClient 现实集群与连接池管理，和 RedisHelper 静态类快速上手

> nuget Install-Package CSRedisCore

## 普通模式

```csharp
var csredis = new CSRedis.CSRedisClient("127.0.0.1:6379,password=123,defaultDatabase=13,poolsize=50,ssl=false,writeBuffer=10240,prefix=key前辍");
```

# 集群模式

本功能现实多节点分担存储，与官方的集群、高可用方案不同。

> 例如：缓存数据达到500G，如果使用一台redis-server服务器光靠内存存储将非常吃力，使用硬盘又影响性能。
> 可使用此功能自动管理N台redis-server服务器分担存储，每台服务器只需约 (500/N)G 内存，且每台服务器匀可以配置官方高可用架构。

```csharp
var csredis = new CSRedis.CSRedisClient(null,
  "127.0.0.1:6371,password=123,defaultDatabase=11,poolsize=10,ssl=false,writeBuffer=10240,prefix=key前辍", 
  "127.0.0.1:6372,password=123,defaultDatabase=12,poolsize=11,ssl=false,writeBuffer=10240,prefix=key前辍",
  "127.0.0.1:6373,password=123,defaultDatabase=13,poolsize=12,ssl=false,writeBuffer=10240,prefix=key前辍",
  "127.0.0.1:6374,password=123,defaultDatabase=14,poolsize=13,ssl=false,writeBuffer=10240,prefix=key前辍");
//实现思路：根据key.GetHashCode() % 节点总数量，确定连向的节点
//也可以自定义规则(第一个参数设置)
```

> mvc分布式缓存注入 nuget Install-Package Caching.CSRedis

```csharp
//初始化 RedisHelper
RedisHelper.Initialization(csredis,
  value => Newtonsoft.Json.JsonConvert.SerializeObject(value),
  deserialize: (data, type) => Newtonsoft.Json.JsonConvert.DeserializeObject(data, type));
//注册mvc分布式缓存
services.AddSingleton<IDistributedCache>(new Microsoft.Extensions.Caching.Redis.CSRedisCache(RedisHelper.Instance));
```

> 提示：CSRedis.CSRedisClient 单例模式够用了，强烈建议使用 RedisHelper 静态类

```csharp
RedisHelper.Set("test1", "123123", 60);
RedisHelper.Get("test1");
//...函数名基本与 redis-cli 的命令相同
```

> 如果确定一定以及肯定非要有切换数据库的需求，请看以下代码：

```csharp
var redis = new CSRedisClient[14]; //定义成单例
for (var a = 0; a< redis.Length; a++) redis[a] = new CSRedisClient(connectionString + “; defualtDatabase=“ + a);

//访问数据库1的数据
redis[1].Get("test1");
```

# 2、订阅与发布

```csharp
//普通订阅
RedisHelper.Subscribe(
  ("chan1", msg => Console.WriteLine(msg.Body)),
  ("chan2", msg => Console.WriteLine(msg.Body)));

//模式订阅（通配符）
RedisHelper.PSubscribe(new[] { "test*", "*test001", "test*002" }, msg => {
  Console.WriteLine($"PSUB   {msg.MessageId}:{msg.Body}    {msg.Pattern}: chan:{msg.Channel}");
});
//模式订阅已经解决的难题：
//1、集群的节点匹配规则，导致通配符最大可能匹配全部节点，所以全部节点都要订阅
//2、本组 "test*", "*test001", "test*002" 订阅全部节点时，需要解决同一条消息不可执行多次

//发布，
RedisHelper.Publish("chan1", "123123123");
//无论是集群或普通模式，RedisHelper.Publish 都能正常通信
```

## 3、缓存壳

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
var t1 = RedisHelper.CacheShell("test1", 10, () => Test.Select.WhereId(1).ToOne());
var t2 = RedisHelper.CacheShell("test", "1", 10, () => Test.Select.WhereId(1).ToOne());
var t3 = RedisHelper.CacheShell("test", new [] { "1", "2" }, 10, notCacheFields => new [] {
  ("1", Test.Select.WhereId(1).ToOne()),
  ("2", Test.Select.WhereId(2).ToOne())
});
```

# Pipeline

使用管道模式，打包多条命令一起执行，从而提高性能。

```csharp
var ret1 = RedisHelper.StartPipe().Set("a", "1").Get("a").EndPipe();
var ret2 = RedisHelper.StartPipe(p => p.Set("a", "1").Get("a"));

var ret3 = RedisHelper.StartPipe().Get("b").Get("a").Get("a").EndPipe();
//与 RedisHelper.MGet("b", "a", "a") 性能相比，经测试差之毫厘
```

# Benchmark

![](https://img2018.cnblogs.com/blog/31407/201809/31407-20180915145859354-1024651127.png)