using CSRedis;
using System;
using System.Collections.Generic;
using System.Text;

namespace CSRedisCore.Tests
{
    public class TestBase
    {
		//测试 redis-cluster 不能设置 defaultDatabase

		protected CSRedisClient rds = new CSRedisClient("127.0.0.1,defaultDatabase=0,poolsize=3,tryit=0");

		protected readonly object Null = null;
		protected readonly string String = "我是中国人";
		protected readonly byte[] Bytes = Encoding.UTF8.GetBytes("这是一个byte字节");
		protected readonly TestClass Class = new TestClass { Id = 1, Name = "Class名称", CreateTime = DateTime.Now, TagId = new[] { 1, 3, 3, 3, 3 } };

		public TestBase() {
			//rds.NodesServerManager.FlushAll();
		}
    }

	public class TestClass {
		public int Id { get; set; }
		public string Name { get; set; }
		public DateTime CreateTime { get; set; }

		public int[] TagId { get; set; }

		public override string ToString() {
			return Newtonsoft.Json.JsonConvert.SerializeObject(this);
		}
	}
}
