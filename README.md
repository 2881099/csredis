# csredis

ServiceStack.redis 是商业版，免费版有限制；

StackExchange.Redis 是免费版，但是内核在 .NETCore 运行有问题，一并发就死锁，暂时无法解决；

 > CSRedis 是国外大神写的，经过少量修改，现已支持 .NETCore；

扩展：

1、重新开发了一个连接池管理 ConnectionPool

```c#
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

```c#
RedisHelper.SetBytes("test1", Encoding.UTF8.GetBytes("123123"), 60);
RedisHelper.GetBytes("test1");
```