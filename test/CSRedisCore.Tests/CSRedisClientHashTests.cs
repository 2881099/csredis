using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace CSRedisCore.Tests {
	public class CSRedisClientHashTests : TestBase {

		[Fact]
		public void HDel() {
			Assert.True(rds.HMSet("TestHDel", "string1", base.String, "bytes1", base.Bytes, "class1", base.Class));
			Assert.Equal(3, rds.HDel("TestHDel", "string1", "bytes1", "class1"));
		}
		[Fact]
		public void HExists() {
			Assert.False(rds.HExists("TestHExists", "null1"));
			Assert.True(rds.HSet("TestHExists", "null1", 1));
			Assert.True(rds.HExists("TestHExists", "null1"));
			Assert.Equal(1, rds.HDel("TestHExists", "null1"));
			Assert.False(rds.HExists("TestHExists", "null1"));
		}

		[Fact]
		public void HGet() {
			Assert.True(rds.HMSet("TestHGet", "null1", base.Null, "string1", base.String, "bytes1", base.Bytes, "class1", base.Class, "class1array", new[] { base.Class, base.Class }));

			Assert.Equal(rds.HGet("TestHGet", "null1")?.ToString() ?? "", base.Null?.ToString() ?? "");
			Assert.Equal(rds.HGet("TestHGet", "string1"), base.String);
			Assert.Equal(rds.HGet<byte[]>("TestHGet", "bytes1"), base.Bytes);
			Assert.Equal(rds.HGet<TestClass>("TestHGet", "class1")?.ToString(), base.Class.ToString());

			Assert.Equal(2, rds.HGet<TestClass[]>("TestHGet", "class1array")?.Length);
			Assert.Equal(rds.HGet<TestClass[]>("TestHGet", "class1array")?.First().ToString(), base.Class.ToString());
			Assert.Equal(rds.HGet<TestClass[]>("TestHGet", "class1array")?.Last().ToString(), base.Class.ToString());
		}

		[Fact]
		public void HGetAll()
		{
			Assert.True(rds.HMSet("TestHGetAll", "string1", base.String, "bytes1", base.Bytes, "class1", base.Class, "class1array", new[] { base.Class, base.Class }));
			Assert.Equal(4, rds.HGetAll("TestHGetAll").Count);
			Assert.Equal(base.String, rds.HGetAll("TestHGetAll")["string1"]);
			Assert.Equal(Encoding.UTF8.GetString(base.Bytes), rds.HGetAll("TestHGetAll")["bytes1"]);
			Assert.Equal(base.Class.ToString(), rds.HGetAll("TestHGetAll")["class1"]);

			Task.Run(async () =>
			{
				var test = await rds.HGetAllAsync("TestHGetAll");

				rds.Set("TestHGetAll2", "1");
				try
				{
					var test2 = await rds.HGetAllAsync("TestHGetAll2");
				}
				catch
				{

				}

				for (var a = 0; a < 1000; a++)
					test = await rds.HGetAllAsync("TestHGetAll");
			}).Wait();
		}

		[Fact]
		public void HIncrBy() {
			Assert.True(rds.HMSet("TestHIncrBy", "null1", base.Null, "string1", base.String, "bytes1", base.Bytes, "class1", base.Class, "class1array", new[] { base.Class, base.Class }));
			Assert.Equal(1, rds.HIncrBy("TestHIncrBy", "null1", 1));
			Assert.Throws<CSRedis.RedisException>(() => rds.HIncrBy("TestHIncrBy", "string1", 1));
			Assert.Throws<CSRedis.RedisException>(() => rds.HIncrBy("TestHIncrBy", "bytes1", 1));

			Assert.Equal(2, rds.HIncrBy("TestHIncrBy", "null1", 1));
			Assert.Equal(12, rds.HIncrBy("TestHIncrBy", "null1", 10));
		}

		[Fact]
		public void HIncrByFloat() {
			rds.Del("TestHIncrByFloat");
			Assert.True(rds.HMSet("TestHIncrByFloat", "null1", base.Null, "string1", base.String, "bytes1", base.Bytes, "class1", base.Class, "class1array", new[] { base.Class, base.Class }));
			Assert.Equal(0.5m, rds.HIncrByFloat("TestHIncrByFloat", "null1", 0.5m));
			Assert.Throws<CSRedis.RedisException>(() => rds.HIncrByFloat("TestHIncrByFloat", "string1", 1.5m));
			Assert.Throws<CSRedis.RedisException>(() => rds.HIncrByFloat("TestHIncrByFloat", "bytes1", 5));

			Assert.Equal(3.8m, rds.HIncrByFloat("TestHIncrByFloat", "null1", 3.3m));
			Assert.Equal(14.0m, rds.HIncrByFloat("TestHIncrByFloat", "null1", 10.2m));
		}

		[Fact]
		public void HKeys() {
			Assert.True(rds.HMSet("TestHKeys", "string1", base.String, "bytes1", base.Bytes, "class1", base.Class, "class1array", new[] { base.Class, base.Class }));
			Assert.Equal(4, rds.HKeys("TestHKeys").Length);
			Assert.Contains("string1", rds.HKeys("TestHKeys"));
			Assert.Contains("bytes1", rds.HKeys("TestHKeys"));
			Assert.Contains("class1", rds.HKeys("TestHKeys"));
			Assert.Contains("class1array", rds.HKeys("TestHKeys"));
		}

		[Fact]
		public void HLen() {
			Assert.True(rds.HMSet("TestHLen", "string1", base.String, "bytes1", base.Bytes, "class1", base.Class, "class1array", new[] { base.Class, base.Class }));
			Assert.Equal(4, rds.HLen("TestHLen"));
		}

		[Fact]
		public void HMGet() {
			Assert.True(rds.HMSet("TestHMGet", "string1", base.String, "bytes1", base.Bytes, "class1", base.Class, "class1array", new[] { base.Class, base.Class }));
			Assert.True(rds.HMSet("TestHMGet", "string2", base.String, "bytes2", base.Bytes, "class2", base.Class, "class2array", new[] { base.Class, base.Class }));

			Assert.Equal(2, rds.HMGet("TestHMGet", "string1", "string2").Length);
			Assert.Contains(base.String, rds.HMGet("TestHMGet", "string1", "string2"));
			Assert.Equal(2, rds.HMGet<TestClass>("TestHMGet", "class1", "class2").Length);
			Assert.Contains(base.Class.ToString(), rds.HMGet<TestClass>("TestHMGet", "class1", "class2")?.Select(a => a.ToString()));
		}

		[Fact]
		public void HMSet() {
			Assert.True(rds.HMSet("TestHMSet", "string1", base.String, "bytes1", base.Bytes, "class1", base.Class, "class1array", new[] { base.Class, base.Class }));
			Assert.Equal(4, rds.HMGet("TestHMSet", "string1", "bytes1", "class1", "class1array").Length);
			Assert.Contains(base.String, rds.HMGet("TestHMSet", "string1", "bytes1", "class1", "class1array"));
			Assert.Contains(Encoding.UTF8.GetString(base.Bytes), rds.HMGet("TestHMSet", "string1", "bytes1", "class1", "class1array"));
			Assert.Contains(base.Class.ToString(), rds.HMGet("TestHMSet", "string1", "bytes1", "class1", "class1array"));

		}

		[Fact]
		public void HSet() {
			Assert.True(rds.HSet("TestHSet", "string1", base.String));
			Assert.Equal(base.String, rds.HGet("TestHSet", "string1"));

			Assert.True(rds.HSet("TestHSet", "bytes1", base.Bytes));
			Assert.Equal(base.Bytes, rds.HGet<byte[]>("TestHSet", "bytes1"));

			Assert.True(rds.HSet("TestHSet", "class1", base.Class));
			Assert.Equal(base.Class.ToString(), rds.HGet<TestClass>("TestHSet", "class1").ToString());
		}

		[Fact]
		public void HSetNx() {
			Assert.True(rds.HSet("TestHSetNx", "string1", base.String));
			Assert.Equal(base.String, rds.HGet("TestHSetNx", "string1"));
			Assert.False(rds.HSet("TestHSetNx", "string1", base.String));

			Assert.True(rds.HSet("TestHSetNx", "bytes1", base.Bytes));
			Assert.Equal(base.Bytes, rds.HGet<byte[]>("TestHSetNx", "bytes1"));
			Assert.False(rds.HSet("TestHSetNx", "bytes1", base.Bytes));

			Assert.True(rds.HSet("TestHSetNx", "class1", base.Class));
			Assert.Equal(base.Class.ToString(), rds.HGet<TestClass>("TestHSetNx", "class1").ToString());
			Assert.False(rds.HSet("TestHSetNx", "class1", base.Class));

		}

		[Fact]
		public void HVals() {
			Assert.True(rds.HMSet("TestHVals1", "string1", base.String, "bytes1", base.Bytes, "class1", base.Class, "class1array1", new[] { base.Class, base.Class }));
			Assert.True(rds.HMSet("TestHVals1", "string2", base.String, "bytes2", base.Bytes, "class2", base.Class, "class2array2", new[] { base.Class, base.Class }));
			Assert.Equal(8, rds.HVals("TestHVals1").Length);

			Assert.True(rds.HMSet("TestHVals2", "string1", base.String, "string2", base.String));
			Assert.Equal(2, rds.HVals("TestHVals2").Length);
			Assert.Contains(base.String, rds.HVals("TestHVals2"));

			Assert.True(rds.HMSet("TestHVals3", "bytes1", base.Bytes, "bytes2", base.Bytes));
			Assert.Equal(2, rds.HVals<byte[]>("TestHVals3").Length);
			Assert.Contains(base.Bytes, rds.HVals<byte[]>("TestHVals3"));

			Assert.True(rds.HMSet("TestHVals4", "class1", base.Class, "class2", base.Class));
			Assert.Equal(2, rds.HVals<TestClass>("TestHVals4").Length);
			Assert.Contains(base.Class.ToString(), rds.HVals<TestClass>("TestHVals4").Select(a => a.ToString()));

			Assert.True(rds.HMSet("TestHVals5", "class2array1", new[] { base.Class, base.Class }, "class2array2", new[] { base.Class, base.Class }));
			Assert.Equal(2, rds.HVals<TestClass[]>("TestHVals5").Length);
		}
		[Fact]
		public void HScan() { }
	}
}
