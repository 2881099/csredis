using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace CSRedisCore.Tests {
	public class CSRedisClientKeyTests : TestBase {

		[Fact]
		public void Del() {
			Assert.True(rds.MSet("TestDel_null1", base.Null, "TestDel_string1", base.String, "TestDel_bytes1", base.Bytes, "TestDel_class1", base.Class));
			Assert.Equal(4, rds.Del("TestDel_null1", "TestDel_string1", "TestDel_bytes1", "TestDel_class1"));
		}

		[Fact]
		public void Dump() {
			Assert.True(rds.MSet("TestDump_null1", base.Null, "TestDump_string1", base.String, "TestDump_bytes1", base.Bytes, "TestDump_class1", base.Class));

			Assert.True(rds.Restore("TestDump_null2", rds.Dump("TestDump_null1")));
			Assert.Equal(rds.Get("TestDump_null2"), rds.Get("TestDump_null1"));

			Assert.True(rds.Restore("TestDump_string2", rds.Dump("TestDump_string1")));
			Assert.Equal(rds.Get("TestDump_string2"), rds.Get("TestDump_string1"));

			Assert.True(rds.Restore("TestDump_bytes2", rds.Dump("TestDump_bytes1")));
			Assert.Equal(rds.Get<byte[]>("TestDump_bytes2"), rds.Get<byte[]>("TestDump_bytes1"));

			Assert.True(rds.Restore("TestDump_class2", rds.Dump("TestDump_class1")));
			Assert.Equal(rds.Get<TestClass>("TestDump_class2").ToString(), rds.Get<TestClass>("TestDump_class1").ToString());
		}

		[Fact]
		public void Exists() {
			Assert.False(rds.Exists("TestExists_null1"));
			Assert.True(rds.Set("TestExists_null1", 1));
			Assert.True(rds.Exists("TestExists_null1"));
			Assert.Equal(1, rds.Del("TestExists_null1"));
			Assert.False(rds.Exists("TestExists_null1"));
		}

		[Fact]
		public void Expire() {
			Assert.True(rds.MSet("TestExpire_null1", base.Null, "TestExpire_string1", base.String, "TestExpire_bytes1", base.Bytes, "TestExpire_class1", base.Class));

			Assert.True(rds.Expire("TestExpire_null1", 10));
			Assert.Equal(10, rds.Ttl("TestExpire_null1"));
			Assert.True(rds.Expire("TestExpire_string1", TimeSpan.FromHours(1)));
			Assert.Equal(60 * 60, rds.Ttl("TestExpire_string1"));
		}

		[Fact]
		public void ExpireAt() {
			Assert.True(rds.MSet("TestExpireAt_null1", base.Null, "TestExpireAt_string1", base.String, "TestExpireAt_bytes1", base.Bytes, "TestExpireAt_class1", base.Class));

			Assert.True(rds.ExpireAt("TestExpireAt_null1", DateTime.UtcNow.AddSeconds(10)));
			Assert.InRange(rds.Ttl("TestExpireAt_null1"), 9, 10);
			Assert.True(rds.ExpireAt("TestExpireAt_string1", DateTime.UtcNow.AddHours(1)));
			Assert.InRange(rds.Ttl("TestExpireAt_string1"), 60 * 60 - 1, 60 * 60);
		}

		[Fact]
		public void Keys() {
			Assert.True(rds.MSet("TestKeys_null1", base.Null, "TestKeys_string1", base.String, "TestKeys_bytes1", base.Bytes, "TestKeys_class1", base.Class));
			Assert.Equal(4, rds.Keys("TestKeys_*").Length);
		}

		[Fact]
		public void Move() {
			Assert.True(rds.MSet("TestMove_null1", base.Null, "TestMove_string1", base.String, "TestMove_bytes1", base.Bytes, "TestMove_class1", base.Class));

			Assert.True(rds.Move("TestMove_string1", 1));
			Assert.False(rds.Exists("TestMove_string1"));

			using (var conn = rds.Nodes.First().Value.Get()) {
				conn.Value.Select(1);
				Assert.Equal(base.String, conn.Value.Get("TestMove_string1"));
				conn.Value.Select(2);
			}

			Assert.True(rds.Set("TestMove_string1", base.String));
			Assert.False(rds.Move("TestMove_string1", 1));
			Assert.Equal(base.String, rds.Get("TestMove_string1"));

			using (var conn = rds.Nodes.First().Value.Get()) {
				conn.Value.Select(1);
				Assert.Equal(base.String, conn.Value.Get("TestMove_string1"));
				conn.Value.Select(2);
			}
		}

		[Fact]
		public void ObjectEncoding() {
			Assert.True(rds.MSet("TestObjectEncoding_null1", base.Null, "TestObjectEncoding_string1", base.String, "TestObjectEncoding_bytes1", base.Bytes, "TestObjectEncoding_class1", base.Class));

			Assert.Equal("raw", rds.ObjectEncoding("TestObjectEncoding_string1"));
		}

		[Fact]
		public void ObjectRefCount() {
			Assert.True(rds.MSet("TestObjectRefCount_null1", base.Null, "TestObjectRefCount_string1", base.String, "TestObjectRefCount_bytes1", base.Bytes, "TestObjectRefCount_class1", base.Class));
			rds.Exists("TestObjectRefCount_string1");
			rds.Get("TestObjectRefCount_string1");

			Assert.Null(rds.ObjectRefCount("TestObjectRefCount_bytes11"));
			Assert.Equal(1, rds.ObjectRefCount("TestObjectRefCount_string1"));
		}
		public void ObjectIdleTime() { }

		[Fact]
		public void Persist() {
			Assert.True(rds.MSet("TestPersist_null1", base.Null, "TestPersist_string1", base.String, "TestPersist_bytes1", base.Bytes, "TestPersist_class1", base.Class));

			Assert.True(rds.Expire("TestPersist_null1", 10));
			Assert.Equal(10, rds.Ttl("TestPersist_null1"));
			Assert.True(rds.Expire("TestPersist_string1", TimeSpan.FromHours(1)));
			Assert.Equal(60 * 60, rds.Ttl("TestPersist_string1"));

			Assert.True(rds.Persist("TestPersist_null1"));
			Assert.False(rds.Persist("TestPersist_null11"));
			Assert.True(rds.Persist("TestPersist_string1"));
			Assert.False(rds.Persist("TestPersist_string11"));

			Assert.Equal(-1, rds.Ttl("TestPersist_null1"));
			Assert.Equal(-1, rds.Ttl("TestPersist_string1"));
		}

		[Fact]
		public void PExpire() {
			Assert.True(rds.MSet("TestPExpire_null1", base.Null, "TestPExpire_string1", base.String, "TestPExpire_bytes1", base.Bytes, "TestPExpire_class1", base.Class));

			Assert.True(rds.PExpire("TestPExpire_null1", 10000));
			Assert.InRange(rds.PTtl ("TestPExpire_null1"), 9000, 10000);
			Assert.True(rds.PExpire("TestPExpire_string1", TimeSpan.FromHours(1)));
			Assert.InRange(rds.PTtl("TestPExpire_string1"), 1000 * 60 * 60 - 1000, 1000 * 60 * 60);
		}

		[Fact]
		public void PExpireAt() {
			Assert.True(rds.MSet("TestPExpireAt_null1", base.Null, "TestPExpireAt_string1", base.String, "TestPExpireAt_bytes1", base.Bytes, "TestPExpireAt_class1", base.Class));

			Assert.True(rds.ExpireAt("TestPExpireAt_null1", DateTime.UtcNow.AddSeconds(10)));
			Assert.InRange(rds.PTtl("TestPExpireAt_null1"), 9000, 10000);
			Assert.True(rds.ExpireAt("TestPExpireAt_string1", DateTime.UtcNow.AddHours(1)));
			Assert.InRange(rds.PTtl("TestPExpireAt_string1"), 1000 * 60 * 60 - 1000, 1000 * 60 * 60);
		}

		[Fact]
		public void PTtl() {
			Assert.True(rds.MSet("TestPTtl_null1", base.Null, "TestPTtl_string1", base.String, "TestPTtl_bytes1", base.Bytes, "TestPTtl_class1", base.Class));

			Assert.True(rds.PExpire("TestPTtl_null1", 1000));
			Assert.InRange(rds.PTtl("TestPTtl_null1"), 500, 1000);
			Assert.InRange(rds.PTtl("TestPTtl_null11"), long.MinValue, -1);
		}

		[Fact]
		public void RandomKey() {
			Assert.True(rds.MSet("TestRandomKey_null1", base.Null, "TestRandomKey_string1", base.String, "TestRandomKey_bytes1", base.Bytes, "TestRandomKey_class1", base.Class));

			Assert.NotNull(rds.RandomKey());
		}

		[Fact]
		public void Rename() {
			Assert.True(rds.MSet("TestRename_null1", base.Null, "TestRename_string1", base.String, "TestRename_bytes1", base.Bytes, "TestRename_class1", base.Class));

			Assert.Equal(base.String, rds.Get("TestRename_string1"));
			Assert.True(rds.Rename("TestRename_string1", "TestRename_string11"));
			Assert.False(rds.Exists("TestRename_string1"));
			Assert.Equal(base.String, rds.Get("TestRename_string11"));

			Assert.True(rds.Rename("TestRename_class1", "TestRename_string11"));
			Assert.False(rds.Exists("TestRename_class1"));
			Assert.Equal(base.Class.ToString(), rds.Get<TestClass>("TestRename_string11").ToString());
		}

		[Fact]
		public void RenameNx() {
			Assert.True(rds.MSet("TestRenameNx_null1", base.Null, "TestRenameNx_string1", base.String, "TestRenameNx_bytes1", base.Bytes, "TestRenameNx_class1", base.Class));

			Assert.Equal(base.String, rds.Get("TestRenameNx_string1"));
			Assert.True(rds.Rename("TestRenameNx_string1", "TestRenameNx_string11"));
			Assert.False(rds.Exists("TestRenameNx_string1"));
			Assert.Equal(base.String, rds.Get("TestRenameNx_string11"));

			Assert.True(rds.Rename("TestRenameNx_class1", "TestRename_string11"));
			Assert.False(rds.Exists("TestRenameNx_class1"));
			Assert.Equal(base.Class.ToString(), rds.Get<TestClass>("TestRename_string11").ToString());
		}

		[Fact]
		public void Restore() {
			Assert.True(rds.MSet("TestRestore_null1", base.Null, "TestRestore_string1", base.String, "TestRestore_bytes1", base.Bytes, "TestRestore_class1", base.Class));

			Assert.True(rds.Restore("TestRestore_null2", rds.Dump("TestRestore_null1")));
			Assert.Equal(rds.Get("TestRestore_null2"), rds.Get("TestRestore_null1"));

			Assert.True(rds.Restore("TestRestore_string2", rds.Dump("TestRestore_string1")));
			Assert.Equal(rds.Get("TestRestore_string2"), rds.Get("TestRestore_string1"));

			Assert.True(rds.Restore("TestRestore_bytes2", rds.Dump("TestRestore_bytes1")));
			Assert.Equal(rds.Get<byte[]>("TestRestore_bytes2"), rds.Get<byte[]>("TestRestore_bytes1"));

			Assert.True(rds.Restore("TestRestore_class2", rds.Dump("TestRestore_class1")));
			Assert.Equal(rds.Get<TestClass>("TestRestore_class2").ToString(), rds.Get<TestClass>("TestRestore_class1").ToString());
		}
		public void Sort() { }
		public void SortAndStore() { }

		[Fact]
		public void Ttl() {
			Assert.True(rds.MSet("TestTtl_null1", base.Null, "TestTtl_string1", base.String, "TestTtl_bytes1", base.Bytes, "TestTtl_class1", base.Class));

			Assert.True(rds.Expire("TestTtl_null1", 10));
			Assert.InRange(rds.Ttl("TestTtl_null1"), 5, 10);
			Assert.InRange(rds.Ttl("TestTtl_null11"), long.MinValue, -1);

		}

		[Fact]
		public void Type() {
			Assert.True(rds.MSet("TestType_null1", base.Null, "TestType_string1", base.String, "TestType_bytes1", base.Bytes, "TestType_class1", base.Class));

			Assert.Equal(CSRedis.KeyType.None, rds.Type("TestType_string111111111123"));
			Assert.Equal(CSRedis.KeyType.String, rds.Type("TestType_string1"));
		}
		public void Scan() { }
	}
}
