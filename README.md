ServiceStack.Redis是商业版，免费版有限制；

StackExchange.Redis是免费版，早期有Timeout Bug，当前版本使用需要全部使用异步方法方可解决；

CSRedis于2016年开始支持.NETCore一直迭代至今（解决上述Bug），实现了低门槛、高性能，和分区高级玩法的redis-cli SDK；

## v3.0 更新功能

1、CSRedisClient、RedisHelper 方法名调整，所有方法名与redis-cli保持一持；

> java,python,go,nodejs,php SDK 方法名基本都与 redis-cli 一致，反对二次命名的库

2、CSRedisClient 增加反序列对象获取，如：Get<byte[]>、HGet<byte[]>，所以获取方法都重载了<T>，默认获取仍然是string；

3、增加 geo 命令支持（需要 redis-server 3.2 以上支持）；

4、增加官方集群 redis-trib.rb 支持；

5、增加哨兵模式支持；

6、增加 stream 命令支持（需要 redis-server 5.0 以上支持）；

| Package Name |  NuGet | Downloads | |
|--------------|  ------- |  ---- | -- |
| CSRedisCore | [![nuget](https://img.shields.io/nuget/v/CSRedisCore.svg?style=flat-square)](https://www.nuget.org/packages/CSRedisCore) | [![stats](https://img.shields.io/nuget/dt/CSRedisCore.svg?style=flat-square)](https://www.nuget.org/stats/packages/CSRedisCore?groupby=Version) |
| Caching.CSRedis | [![nuget](https://img.shields.io/nuget/v/Caching.CSRedis.svg?style=flat-square)](https://www.nuget.org/packages/Caching.CSRedis) | [![stats](https://img.shields.io/nuget/dt/Caching.CSRedis.svg?style=flat-square)](https://www.nuget.org/stats/packages/Caching.CSRedis?groupby=Version) | IDistributedCache |

# 功能介绍

1、实现分区与连接池管理类CSRedisClient，静态类RedisHelper快速上手，<font color=darkgreen>方法名与redis-cli保持一致</font>。

> dotnet add package CSRedisCore

## 普通模式

```csharp
var csredis = new CSRedis.CSRedisClient("127.0.0.1:6379,password=123,defaultDatabase=13,prefix=key前辍");
```

| 参数名 | 默认值 | 说明 |
| :---------------- | --------------: | :------------------- |
| password          | <空>  | 密码 |
| defaultDatabase   | 0     | 默认数据库 |
| poolsize          | 50    | 连接池大小 |
| connectTimeout    | 5000  | 连接超时设置(毫秒) |
| syncTimeout       | 10000 | 发送/接收超时设置(毫秒) |
| idleTimeout       | 0     | 连接池内元素空闲时间(毫秒)，适用连接远程redis-server |
| preheat           | true  | 预热连接，接收数值如 preheat=5 预热5个连接 |
| ssl               | false | 是否开启加密传输 |
| testcluster       | true  | 是否尝试集群模式，阿里云、腾讯云集群需要设置此选项为 false |
| writeBuffer       | 10240 | 异步方法写入缓冲区大小(字节) |
| tryit             | 0     | 执行命令出错，尝试重试的次数 |
| name              | <空>  | 连接名称，可以使用 Client List 命令查看 |
| prefix            | <空>  | key前辍，所有方法都会附带此前辍，csredis.Set(prefix + "key", 111); |

# 哨兵模式

```csharp
var csredis = new CSRedis.CSRedisClient(
    "mymaster,password=123,prefix=key前辍", 
    new [] { "192.169.1.10:26379", "192.169.1.11:26379", "192.169.1.12:26379" });
```

连接字符串中的 mymaster 是哨兵监听的名称，其他配置参数与普通模式一致

# 官方集群

假设你已经配置好 redis-trib 集群，定义一个【普通模式】的 CSRedisClient 对象，它会根据 redis-server 返回的 MOVED | ASK 错误记录slot，自动增加节点 Nodes 属性。

> 127.0.0.1:6379,password=123,defaultDatabase=0,poolsize=50,prefix=

> 其他节点在运行过程中自动增加，确保每个节点密码一致。

警告：本模式与【分区模式】同时使用时，切记不可设置“prefix=key前辍”（或者全部设置成一样），否则会导致 keySlot 计算结果与服务端不匹配，无法记录 slotCache。

> 注意：官方集群不支持多 keys 的命令、【管道】、Eval（脚本）等众多杀手级功能。

# 分区模式

本功能现实多个服务节点分担存储，与官方的分区、集群、高可用方案不同。

> 例如：缓存数据达到500G，如果使用一台redis-server服务器光靠内存存储将非常吃力，使用硬盘又影响性能。
> 可以使用此功能自动管理N台redis-server服务器分担存储，每台服务器只需约 (500/N)G 内存，且每台服务器匀可以配置官方高可用架构。

```csharp
var csredis = new CSRedis.CSRedisClient(null,
  "127.0.0.1:6371,password=123,defaultDatabase=11,poolsize=10", 
  "127.0.0.1:6372,password=123,defaultDatabase=12,poolsize=11",
  "127.0.0.1:6373,password=123,defaultDatabase=13,poolsize=12",
  "127.0.0.1:6374,password=123,defaultDatabase=14,poolsize=13");
//实现思路：根据CRC16(key) % 节点总数量，确定连向的节点
//也可以自定义规则(第一个参数设置)
```

> mvc分布式缓存注入 dotnet add package Caching.CSRedis

```csharp
//初始化 RedisHelper
RedisHelper.Initialization(csredis);
//注册mvc分布式缓存
services.AddSingleton<IDistributedCache>(new Microsoft.Extensions.Caching.Redis.CSRedisCache(RedisHelper.Instance));
```

> 提示：CSRedis.CSRedisClient 单例模式够用了，强烈建议使用 RedisHelper 静态类

```csharp
RedisHelper.Set("test1", "123123", 60);
RedisHelper.Get("test1");
//...函数名与 redis-cli 的命令相同
```

> 如果确定一定以及肯定非要有切换数据库的需求，请看以下代码：

```csharp
var connectionString = "127.0.0.1:6379,password=123,poolsize=10";
var redis = new CSRedisClient[14]; //定义成单例
for (var a = 0; a< redis.Length; a++) redis[a] = new CSRedisClient(connectionString + ",defaultDatabase=" + a);

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
//1、分区的节点匹配规则，导致通配符最大可能匹配全部节点，所以全部节点都要订阅
//2、本组 "test*", "*test001", "test*002" 订阅全部节点时，需要解决同一条消息不可执行多次

//发布
RedisHelper.Publish("chan1", "123123123");
//无论是分区或普通模式，RedisHelper.Publish 都可以正常通信
```

参考资料：[【由浅至深】redis 实现发布订阅的几种方式](https://www.cnblogs.com/kellynic/p/9952386.html)

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
