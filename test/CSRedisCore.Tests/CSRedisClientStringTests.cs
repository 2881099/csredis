using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace CSRedisCore.Tests {
	public class CSRedisClientStringTests : TestBase {

		[Fact]
		public void Append() {
			var key = "TestAppend_null";
			rds.Set(key, base.String);
			rds.Append(key, base.Null);
			Assert.Equal(rds.Get(key), base.String);

			key = "TestAppend_string";
			rds.Set(key, base.String);
			rds.Append(key, base.String);
			Assert.Equal(rds.Get(key), base.String + base.String);
			var ms = new MemoryStream();
			rds.Get(key, ms);
			Assert.Equal(Encoding.UTF8.GetString(ms.ToArray()), base.String + base.String);
			ms.Close();

			key = "TestAppend_bytes";
			rds.Set(key, base.Bytes);
			rds.Append(key, base.Bytes);
			Assert.Equal(Convert.ToBase64String(rds.Get<byte[]>(key)), Convert.ToBase64String(base.Bytes.Concat(base.Bytes).ToArray()));
		}

		[Fact]
		async public Task AppendAsync()
		{
			var key = "TestAppendAsync_null";
			await rds.SetAsync(key, base.String);
			await rds.AppendAsync(key, base.Null);
			Assert.Equal(await rds.GetAsync(key), base.String);

			key = "TestAppendAsync_string";
			await rds.SetAsync(key, base.String);
			await rds.AppendAsync(key, base.String);
			Assert.Equal(await rds.GetAsync(key), base.String + base.String);

			key = "TestAppendAsync_bytes";
			await rds.SetAsync(key, base.Bytes);
			await rds.AppendAsync(key, base.Bytes);
			Assert.Equal(Convert.ToBase64String(await rds.GetAsync<byte[]>(key)), Convert.ToBase64String(base.Bytes.Concat(base.Bytes).ToArray()));
		}

		[Fact]
		public void BitCount() {
			var key = "TestBitCount";
			rds.SetBit(key, 100, true);
			rds.SetBit(key, 90, true);
			rds.SetBit(key, 80, true);
			Assert.Equal(3, rds.BitCount(key, 0, 101));
			Assert.Equal(3, rds.BitCount(key, 0, 100));
			Assert.Equal(3, rds.BitCount(key, 0, 99));
			Assert.Equal(3, rds.BitCount(key, 0, 60));
		}
		[Fact]
		public void BitOp() { }
		[Fact]
		public void BitPos() { }

		[Fact]
		public void Get() {
            var testss = rds.StartPipe(a =>
            {
                a.Get<int?>("1");
                a.Get<int?>("2");
                a.Get("3");
                a.Get<long>("4");
            });

            Task.Run(async() => {
                var key = "TestGet_null";
                await rds.SetAsync(key, base.Null);
                Assert.Equal((await rds.GetAsync(key))?.ToString() ?? "", base.Null?.ToString() ?? "");

                key = "TestGet_string";
                await rds.SetAsync(key, base.String);
                Assert.Equal(await rds.GetAsync(key), base.String);

                key = "TestGet_bytes";
                await rds.SetAsync(key, base.Bytes);
                Assert.Equal(await rds.GetAsync<byte[]>(key), base.Bytes);

                key = "TestGet_class";
                await rds.SetAsync(key, base.Class);
                Assert.Equal((await rds.GetAsync<TestClass>(key))?.ToString(), base.Class.ToString());

                key = "TestGet_classArray";
                await rds.SetAsync(key, new[] { base.Class, base.Class });
                Assert.Equal(2, rds.Get<TestClass[]>(key)?.Length);
                Assert.Equal((await rds.GetAsync<TestClass[]>(key))?.First().ToString(), base.Class.ToString());
                Assert.Equal((await rds.GetAsync<TestClass[]>(key))?.Last().ToString(), base.Class.ToString());
            }).Wait();

			
		}

		[Fact]
		public void GetBit() {
			var key = "TestGetBit";
			rds.SetBit(key, 100, true);
			rds.SetBit(key, 90, true);
			rds.SetBit(key, 80, true);
			Assert.True(rds.GetBit(key, 100));
			Assert.True(rds.GetBit(key, 90));
			Assert.True(rds.GetBit(key, 80));
			Assert.False(rds.GetBit(key, 79));
		}

		[Fact]
		public void GetRange() {
			var key = "TestGetRange_null";
			rds.Set(key, base.Null);
			Assert.Equal("", rds.GetRange(key, 10, 20));

			key = "TestGetRange_string";
			rds.Set(key, "abcdefg");
			Assert.Equal("cde", rds.GetRange(key, 2, 4));
			Assert.Equal("abcdefg", rds.GetRange(key, 0, -1));

			key = "TestGetRange_bytes";
			rds.Set(key, base.Bytes);
			Assert.Equal(base.Bytes.AsSpan(2, 3).ToArray(), rds.GetRange<byte[]>(key, 2, 4));
			Assert.Equal(base.Bytes, rds.GetRange<byte[]>(key, 0, -1));
		}

		[Fact]
		public void GetSet() {
			var key = "TestGetSet_null";
			rds.Set(key, base.Null);
			Assert.Equal("", rds.GetSet(key, base.Null));

			key = "TestGetSet_string";
			rds.Set(key, base.String);
			Assert.Equal(base.String, rds.GetSet(key, "newvalue"));
			Assert.Equal("newvalue", rds.Get(key));

			key = "TestGetSet_bytes";
			rds.Set(key, base.Bytes);
			Assert.Equal(base.Bytes, rds.GetSet<byte[]>(key, "newvalue"));
			Assert.Equal("newvalue", rds.Get(key));
		}

		[Fact]
		public void IncrBy() {
			var key = "TestIncrBy_null";
			//rds.Set(key, base.Null);
			Assert.Equal(1, rds.IncrBy(key, 1));

			//key = "TestIncrBy_string";
			//rds.Set(key, base.String);
			//Assert.Throws<CSRedis.RedisException>(() => rds.IncrBy(key, 1));

			//key = "TestIncrBy_bytes";
			//rds.Set(key, base.Bytes);
			//Assert.Throws<CSRedis.RedisException>(() => rds.IncrBy(key, 1));

			key = "TestIncrBy";
			Assert.Equal(1, rds.IncrBy(key, 1));
			Assert.Equal(11, rds.IncrBy(key, 10));
			Assert.Equal(21.5m, rds.IncrByFloat(key, 10.5m));
		}

		[Fact]
		public void MGet() {
			rds.Set("TestMGet_null1", base.Null);
			rds.Set("TestMGet_string1", base.String);
			rds.Set("TestMGet_bytes1", base.Bytes);
			rds.Set("TestMGet_class1", base.Class);
			rds.Set("TestMGet_null2", base.Null);
			rds.Set("TestMGet_string2", base.String);
			rds.Set("TestMGet_bytes2", base.Bytes);
			rds.Set("TestMGet_class2", base.Class);
			rds.Set("TestMGet_null3", base.Null);
			rds.Set("TestMGet_string3", base.String);
			rds.Set("TestMGet_bytes3", base.Bytes);
			rds.Set("TestMGet_class3", base.Class);

			Assert.Equal(4, rds.MGet("TestMGet_null1", "TestMGet_string1", "TestMGet_bytes1", "TestMGet_class1").Length);
			Assert.Equal("", rds.MGet("TestMGet_null1", "TestMGet_string1", "TestMGet_bytes1", "TestMGet_class1")[0]);
			Assert.Equal(base.String, rds.MGet("TestMGet_null1", "TestMGet_string1", "TestMGet_bytes1", "TestMGet_class1")[1]);
			Assert.Equal(Encoding.UTF8.GetString(base.Bytes), rds.MGet("TestMGet_null1", "TestMGet_string1", "TestMGet_bytes1", "TestMGet_class1")[2]);
			Assert.Equal(base.Class.ToString(), rds.MGet("TestMGet_null1", "TestMGet_string1", "TestMGet_bytes1", "TestMGet_class1")[3]);

			Assert.Equal(4, rds.MGet<byte[]>("TestMGet_null1", "TestMGet_string1", "TestMGet_bytes1", "TestMGet_class1").Length);
			Assert.Equal(new byte[0], rds.MGet<byte[]>("TestMGet_null1", "TestMGet_string1", "TestMGet_bytes1", "TestMGet_class1")[0]);
			Assert.Equal(Encoding.UTF8.GetBytes(base.String), rds.MGet<byte[]>("TestMGet_null1", "TestMGet_string1", "TestMGet_bytes1", "TestMGet_class1")[1]);
			Assert.Equal(base.Bytes, rds.MGet<byte[]>("TestMGet_null1", "TestMGet_string1", "TestMGet_bytes1", "TestMGet_class1")[2]);
			Assert.Equal(Encoding.UTF8.GetBytes(base.Class.ToString()), rds.MGet<byte[]>("TestMGet_null1", "TestMGet_string1", "TestMGet_bytes1", "TestMGet_class1")[3]);

			Assert.Equal(3, rds.MGet<TestClass>("TestMGet_class1", "TestMGet_class2", "TestMGet_class3").Length);
			Assert.Equal(base.Class.ToString(), rds.MGet<TestClass>("TestMGet_class1", "TestMGet_class2", "TestMGet_class3")[0]?.ToString());
			Assert.Equal(base.Class.ToString(), rds.MGet<TestClass>("TestMGet_class1", "TestMGet_class2", "TestMGet_class3")[1]?.ToString());
			Assert.Equal(base.Class.ToString(), rds.MGet<TestClass>("TestMGet_class1", "TestMGet_class2", "TestMGet_class3")[2]?.ToString());
		}

		[Fact]
		public void MSet() {
			Assert.True(rds.MSet("TestMSet_null1", base.Null, "TestMSet_string1", base.String, "TestMSet_bytes1", base.Bytes, "TestMSet_class1", base.Class));
			Assert.Equal("", rds.Get("TestMSet_null1"));
			Assert.Equal(base.String, rds.Get("TestMSet_string1"));
			Assert.Equal(base.Bytes, rds.Get<byte[]>("TestMSet_bytes1"));
			Assert.Equal(base.Class.ToString(), rds.Get<TestClass>("TestMSet_class1").ToString());
		}

		[Fact]
		public void MSetNx() {
			Assert.True(rds.MSetNx("TestMSetNx_null", base.Null));
			Assert.False(rds.MSetNx("TestMSetNx_null", base.Null));
			Assert.Equal("", rds.Get("TestMSetNx_null"));

			Assert.True(rds.MSetNx("TestMSetNx_string", base.String));
			Assert.False(rds.MSetNx("TestMSetNx_string", base.String));
			Assert.Equal(base.String, rds.Get("TestMSetNx_string"));

			Assert.True(rds.MSetNx("TestMSetNx_bytes", base.Bytes));
			Assert.False(rds.MSetNx("TestMSetNx_bytes", base.Bytes));
			Assert.Equal(base.Bytes, rds.Get<byte[]>("TestMSetNx_bytes"));

			Assert.True(rds.MSetNx("TestMSetNx_class", base.Class));
			Assert.False(rds.MSetNx("TestMSetNx_class", base.Class));
			Assert.Equal(base.Class.ToString(), rds.Get<TestClass>("TestMSetNx_class").ToString());

			rds.Set("abctest", 1);
			Assert.False(rds.MSetNx("abctest", 2, "TestMSetNx_null1", base.Null, "TestMSetNx_string1", base.String, "TestMSetNx_bytes1", base.Bytes, "TestMSetNx_class1", base.Class));
			Assert.True(rds.MSetNx("TestMSetNx_null1", base.Null, "TestMSetNx_string1", base.String, "TestMSetNx_bytes1", base.Bytes, "TestMSetNx_class1", base.Class));
			Assert.Equal(1, rds.Get<int>("abctest"));
			Assert.Equal("", rds.Get("TestMSetNx_null1"));
			Assert.Equal(base.String, rds.Get("TestMSetNx_string1"));
			Assert.Equal(base.Bytes, rds.Get<byte[]>("TestMSetNx_bytes1"));
			Assert.Equal(base.Class.ToString(), rds.Get<TestClass>("TestMSetNx_class1").ToString());
		}

		[Fact]
		public void Set() {
			Assert.True(rds.Set("TestSet_null", base.Null));
			Assert.Equal("", rds.Get("TestSet_null"));

			Assert.True(rds.Set("TestSet_string", base.String));
			Assert.Equal(base.String, rds.Get("TestSet_string"));

			Assert.True(rds.Set("TestSet_bytes", base.Bytes));
			Assert.Equal(base.Bytes, rds.Get<byte[]>("TestSet_bytes"));

			Assert.True(rds.Set("TestSet_class", base.Class));
			Assert.Equal(base.Class.ToString(), rds.Get<TestClass>("TestSet_class").ToString());
		}

		[Fact]
		public void SetBit() {
			var key = "TestSetBit";
			rds.SetBit(key, 100, true);
			rds.SetBit(key, 90, true);
			rds.SetBit(key, 80, true);
			Assert.True(rds.GetBit(key, 100));
			Assert.True(rds.GetBit(key, 90));
			Assert.True(rds.GetBit(key, 80));
			Assert.False(rds.GetBit(key, 79));
		}

		[Fact]
		public void SetNx() {
			Assert.True(rds.SetNx("TestSetNx_null", base.Null));
			Assert.False(rds.SetNx("TestSetNx_null", base.Null));
			Assert.Equal("", rds.Get("TestSetNx_null"));

			Assert.True(rds.SetNx("TestSetNx_string", base.String));
			Assert.False(rds.SetNx("TestSetNx_string", base.String));
			Assert.Equal(base.String, rds.Get("TestSetNx_string"));

			Assert.True(rds.SetNx("TestSetNx_bytes", base.Bytes));
			Assert.False(rds.SetNx("TestSetNx_bytes", base.Bytes));
			Assert.Equal(base.Bytes, rds.Get<byte[]>("TestSetNx_bytes"));

			Assert.True(rds.SetNx("TestSetNx_class", base.Class));
			Assert.False(rds.SetNx("TestSetNx_class", base.Class));
			Assert.Equal(base.Class.ToString(), rds.Get<TestClass>("TestSetNx_class").ToString());
		}

		[Fact]
		public void SetRange() {
			var key = "TestSetRange_null";
			rds.Set(key, base.Null);
			rds.SetRange(key, 10, base.String);
			Assert.Equal(base.String, rds.GetRange(key, 10, -1));

			key = "TestSetRange_string";
			rds.Set(key, "abcdefg");
			rds.SetRange(key, 2, "yyy");
			Assert.Equal("yyy", rds.GetRange(key, 2, 4));

			key = "TestSetRange_bytes";
			rds.Set(key, base.Bytes);
			rds.SetRange(key, 2, base.Bytes);
			Assert.Equal(base.Bytes, rds.GetRange<byte[]>(key, 2, base.Bytes.Length + 2));
		}

		[Fact]
		public void StrLen() {
			var key = "TestStrLen_null";
			rds.Set(key, base.Null);
			Assert.Equal(0, rds.StrLen(key));

			key = "TestStrLen_string";
			rds.Set(key, "abcdefg");
			Assert.Equal(7, rds.StrLen(key));

			key = "TestStrLen_string";
			rds.Set(key, base.String);
			Assert.Equal(15, rds.StrLen(key));

			key = "TestStrLen_bytes";
			rds.Set(key, base.Bytes);
			Assert.Equal(base.Bytes.Length, rds.StrLen(key));
		}
	}
}
