# csredis
.NETCore redis 驱动

ServiceStack.redis 是商业版，免费版有限制；

StackExchange.Redis 是免费版，但是内核在 .NETCore 运行有问题，一并发就死锁，暂时无法解决；

CSRedis 是国外大神写的，经过少量修改，现已支持 .NETCore；

默认已经集成到
https://github.com/2881099/dotnetGen_mysql
https://github.com/2881099/dotnetGen_postgresql
