using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;

namespace CSRedisCore.Tests {
	public class CSRedisClientListTests : TestBase {
		[Fact]
		public void BLPopWithKey() {
			Assert.Null(rds.BRPop(1, "TestBLPopWithKey1", "TestBLPopWithKey2"));

			new Thread(() => {
				Thread.CurrentThread.Join(500);
				rds.RPush("TestBLPopWithKey1", "testv1");
			}).Start();
			Assert.Equal(("TestBLPopWithKey1", "testv1"), rds.BLPopWithKey(5, "TestBLPopWithKey1", "TestBLPopWithKey2"));

			new Thread(() => {
				Thread.CurrentThread.Join(500);
				rds.RPush("TestBLPopWithKey2", "testv2");
			}).Start();
			Assert.Equal(("TestBLPopWithKey2", "testv2"), rds.BLPopWithKey(5, "TestBLPopWithKey1", "TestBLPopWithKey2"));
		}

		[Fact]
		public void BLPop() {
			Assert.Null(rds.BRPop(1, "TestBLPop1", "TestBLPop2"));

			new Thread(() => {
				Thread.CurrentThread.Join(500);
				rds.RPush("TestBLPop1", "testv1");
			}).Start();
			Assert.Equal("testv1", rds.BRPop(5, "TestBLPop1", "TestBLPop2"));

			new Thread(() => {
				Thread.CurrentThread.Join(500);
				rds.RPush("TestBLPop2", "testv2");
			}).Start();
			Assert.Equal("testv2", rds.BRPop(5, "TestBLPop1", "TestBLPop2"));
		}

		[Fact]
		public void BRPopWithKey() {
			Assert.Null(rds.BRPop(1, "TestBRPopWithKey1", "TestBRPopWithKey2"));

			new Thread(() => {
				Thread.CurrentThread.Join(500);
				rds.LPush("TestBRPopWithKey1", "testv1");
			}).Start();
			Assert.Equal(("TestBRPopWithKey1", "testv1"), rds.BRPopWithKey(5, "TestBRPopWithKey1", "TestBRPopWithKey2"));

			new Thread(() => {
				Thread.CurrentThread.Join(500);
				rds.LPush("TestBRPopWithKey2", "testv2");
			}).Start();
			Assert.Equal(("TestBRPopWithKey2", "testv2"), rds.BRPopWithKey(5, "TestBRPopWithKey1", "TestBRPopWithKey2"));
		}

		[Fact]
		public void BRPop() {

			Assert.Null(rds.BRPop(1, "TestBRPop1", "TestBRPop2"));

			new Thread(() => {
				Thread.CurrentThread.Join(500);
				rds.LPush("TestBRPop1", "testv1");
			}).Start();
			Assert.Equal("testv1", rds.BRPop(5, "TestBRPop1", "TestBRPop2"));

			new Thread(() => {
				Thread.CurrentThread.Join(500);
				rds.LPush("TestBRPop2", "testv2");
			}).Start();
			Assert.Equal("testv2", rds.BRPop(5, "TestBRPop1", "TestBRPop2"));
		}
		public void BRPopLPush() {
			
		}

		[Fact]
		public void LIndex() {
			Assert.Equal(8, rds.RPush("TestLIndex", base.Class, base.Class, base.Bytes, base.Bytes, base.String, base.String, base.Null, base.Null));

			Assert.Equal(base.Class.ToString(), rds.LIndex<TestClass>("TestLIndex", 0).ToString());
			Assert.Equal(base.Bytes, rds.LIndex<byte[]>("TestLIndex", 2));
			Assert.Equal(base.String, rds.LIndex("TestLIndex", 4));
			Assert.Equal("", rds.LIndex("TestLIndex", 6));
		}

		[Fact]
		public void LInsertBefore() {
			Assert.Equal(8, rds.RPush("TestLInsertBefore", base.Class, base.Class, base.Bytes, base.Bytes, base.String, base.String, base.Null, base.Null));

			Assert.Equal(9, rds.LInsertBefore("TestLInsertBefore", base.Class, "TestLInsertBefore"));
			Assert.Equal("TestLInsertBefore", rds.LIndex("TestLInsertBefore", 0));
			Assert.Equal(base.Class.ToString(), rds.LIndex<TestClass>("TestLInsertBefore", 1).ToString());
		}

		[Fact]
		public void LInsertAfter() {
			Assert.Equal(8, rds.RPush("TestLInsertAfter", base.Class, base.Class, base.Bytes, base.Bytes, base.String, base.String, base.Null, base.Null));

			Assert.Equal(9, rds.LInsertAfter("TestLInsertAfter", base.Class, "TestLInsertAfter"));
			Assert.Equal("TestLInsertAfter", rds.LIndex("TestLInsertAfter", 1));
			Assert.Equal(base.Class.ToString(), rds.LIndex<TestClass>("TestLInsertAfter", 0).ToString());
			Assert.Equal(base.Class.ToString(), rds.LIndex<TestClass>("TestLInsertAfter", 2).ToString());
		}

		[Fact]
		public void LLen() {
			Assert.Equal(8, rds.RPush("TestLLen", base.Class, base.Class, base.Bytes, base.Bytes, base.String, base.String, base.Null, base.Null));

			Assert.Equal(8, rds.LLen("TestLLen"));
			Assert.True(rds.LTrim("TestLLen", -1, -1));
			Assert.Equal(1, rds.LLen("TestLLen"));
		}

		[Fact]
		public void LPop() {
			Assert.Equal(8, rds.LPush("TestLPop", base.Class, base.Class, base.Bytes, base.Bytes, base.String, base.String, base.Null, base.Null));
			Assert.Equal("", rds.LPop("TestLPop"));
			Assert.Equal("", rds.LPop("TestLPop"));
			Assert.Equal(base.String, rds.LPop("TestLPop"));
			Assert.Equal(base.String, rds.LPop("TestLPop"));
			Assert.Equal(base.Bytes, rds.LPop<byte[]>("TestLPop"));
			Assert.Equal(base.Bytes, rds.LPop<byte[]>("TestLPop"));
			Assert.Equal(base.Class.ToString(), rds.LPop<TestClass>("TestLPop").ToString());
			Assert.Equal(base.Class.ToString(), rds.LPop<TestClass>("TestLPop").ToString());
		}

		[Fact]
		public void LPush() {
			Assert.Equal(8, rds.LPush("TestLPush", base.Class, base.Class, base.Bytes, base.Bytes, base.String, base.String, base.Null, base.Null));

			Assert.Equal(2, rds.LRange("TestLPush", 0, 1).Length);
			Assert.Equal("", rds.LRange("TestLPush", 0, 1)[0]);
			Assert.Equal("", rds.LRange("TestLPush", 0, 1)[1]);

			Assert.Equal(2, rds.LRange("TestLPush", 2, 3).Length);
			Assert.Equal(base.String, rds.LRange("TestLPush", 2, 3)[0]);
			Assert.Equal(base.String, rds.LRange("TestLPush", 2, 3)[1]);

			Assert.Equal(2, rds.LRange("TestLPush", 4, 5).Length);
			Assert.Equal(base.Bytes, rds.LRange<byte[]>("TestLPush", 4, 5)[0]);
			Assert.Equal(base.Bytes, rds.LRange<byte[]>("TestLPush", 4, 5)[1]);

			Assert.Equal(2, rds.LRange("TestLPush", 6, -1).Length);
			Assert.Equal(base.Class.ToString(), rds.LRange<TestClass>("TestLPush", 6, -1)[0].ToString());
			Assert.Equal(base.Class.ToString(), rds.LRange<TestClass>("TestLPush", 6, -1)[1].ToString());
		}

		[Fact]
		public void LPushX() {
			Assert.Equal(0, rds.LPushX("TestLPushX", base.Null));
			Assert.Equal(0, rds.LPushX("TestLPushX", base.String));
			Assert.Equal(0, rds.LPushX("TestLPushX", base.Bytes));
			Assert.Equal(0, rds.LPushX("TestLPushX", base.Class));

			Assert.Equal(1, rds.RPush("TestLPushX", base.Null));
			Assert.Equal(2, rds.LPushX("TestLPushX", base.Null));
			Assert.Equal(3, rds.LPushX("TestLPushX", base.String));
			Assert.Equal(4, rds.LPushX("TestLPushX", base.Bytes));
			Assert.Equal(5, rds.LPushX("TestLPushX", base.Class));
		}

		[Fact]
		public void LRange() {
			rds.LPush("TestLRange", base.Class, base.Class, base.Bytes, base.Bytes, base.String, base.String, base.Null, base.Null);

			Assert.Equal(2, rds.LRange("TestLRange", 0, 1).Length);
			Assert.Equal("", rds.LRange("TestLRange", 0, 1)[0]);
			Assert.Equal("", rds.LRange("TestLRange", 0, 1)[1]);

			Assert.Equal(2, rds.LRange("TestLRange", 2, 3).Length);
			Assert.Equal(base.String, rds.LRange("TestLRange", 2, 3)[0]);
			Assert.Equal(base.String, rds.LRange("TestLRange", 2, 3)[1]);

			Assert.Equal(2, rds.LRange("TestLRange", 4, 5).Length);
			Assert.Equal(base.Bytes, rds.LRange<byte[]>("TestLRange", 4, 5)[0]);
			Assert.Equal(base.Bytes, rds.LRange<byte[]>("TestLRange", 4, 5)[1]);

			Assert.Equal(2, rds.LRange("TestLRange", 6, -1).Length);
			Assert.Equal(base.Class.ToString(), rds.LRange<TestClass>("TestLRange", 6, -1)[0].ToString());
			Assert.Equal(base.Class.ToString(), rds.LRange<TestClass>("TestLRange", 6, -1)[1].ToString());
		}

		[Fact]
		public void LRem() {
			Assert.Equal(8, rds.LPush("TestLRem", base.Class, base.Class, base.Bytes, base.Bytes, base.String, base.String, base.Null, base.Null));

			Assert.Equal(2, rds.LRem("TestLRem", 0, base.Class));
			Assert.Equal(0, rds.LRem("TestLRem", 0, base.Class));
			Assert.Equal(2, rds.LRem("TestLRem", 0, base.Bytes));
			Assert.Equal(0, rds.LRem("TestLRem", 0, base.Bytes));
			Assert.Equal(2, rds.LRem("TestLRem", 0, base.String));
			Assert.Equal(0, rds.LRem("TestLRem", 0, base.String));
			Assert.Equal(2, rds.LRem("TestLRem", 0, base.Null));
			Assert.Equal(0, rds.LRem("TestLRem", 0, base.Null));
		}

		[Fact]
		public void LSet() {
			Assert.Equal(8, rds.RPush("TestLSet", base.Class, base.Class, base.Bytes, base.Bytes, base.String, base.String, base.Null, base.Null));

			var now = DateTime.Now;
			Assert.True(rds.LSet("TestLSet", -1, now));
			Assert.Equal(now.ToString(), rds.LIndex<DateTime>("TestLSet", -1).ToString());
		}

		[Fact]
		public void LTrim() {
			Assert.Equal(8, rds.RPush("TestLTrim", base.Class, base.Class, base.Bytes, base.Bytes, base.String, base.String, base.Null, base.Null));

			Assert.True(rds.LTrim("TestLTrim", -1, -1));
			Assert.Equal(1, rds.LLen("TestLTrim"));
			Assert.Equal("", rds.LRange("TestLTrim", 0, -1)[0]);
		}

		[Fact]
		public void RPop() {
			Assert.Equal(8, rds.RPush("TestRPop", base.Class, base.Class, base.Bytes, base.Bytes, base.String, base.String, base.Null, base.Null));
			Assert.Equal("", rds.RPop("TestRPop"));
			Assert.Equal("", rds.RPop("TestRPop"));
			Assert.Equal(base.String, rds.RPop("TestRPop"));
			Assert.Equal(base.String, rds.RPop("TestRPop"));
			Assert.Equal(base.Bytes, rds.RPop<byte[]>("TestRPop"));
			Assert.Equal(base.Bytes, rds.RPop<byte[]>("TestRPop"));
			Assert.Equal(base.Class.ToString(), rds.RPop<TestClass>("TestRPop").ToString());
			Assert.Equal(base.Class.ToString(), rds.RPop<TestClass>("TestRPop").ToString());
		}

		[Fact]
		public void RPopLPush() {
			Assert.Equal(8, rds.RPush("TestRPopLPush", base.Class, base.Class, base.Bytes, base.Bytes, base.String, base.String, base.Null, base.Null));

			Assert.Equal("", rds.RPopLPush("TestRPopLPush", "TestRPopLPush"));
			Assert.Equal("", rds.RPopLPush("TestRPopLPush", "TestRPopLPush"));
			Assert.Equal(base.String, rds.RPopLPush("TestRPopLPush", "TestRPopLPush"));
			Assert.Equal(base.String, rds.RPopLPush("TestRPopLPush", "TestRPopLPush"));
			Assert.Equal(base.Bytes, rds.RPopLPush<byte[]>("TestRPopLPush", "TestRPopLPush"));
			Assert.Equal(base.Bytes, rds.RPopLPush<byte[]>("TestRPopLPush", "TestRPopLPush"));
			Assert.Equal(base.Class.ToString(), rds.RPopLPush<TestClass>("TestRPopLPush", "TestRPopLPush").ToString());
			Assert.Equal(base.Class.ToString(), rds.RPopLPush<TestClass>("TestRPopLPush", "TestRPopLPush").ToString());
		}

		[Fact]
		public void RPush() {
			Assert.Equal(8, rds.RPush("TestRPush", base.Null, base.Null, base.String, base.String, base.Bytes, base.Bytes, base.Class, base.Class));

			Assert.Equal(2, rds.LRange("TestRPush", 0, 1).Length);
			Assert.Equal("", rds.LRange("TestRPush", 0, 1)[0]);
			Assert.Equal("", rds.LRange("TestRPush", 0, 1)[1]);

			Assert.Equal(2, rds.LRange("TestRPush", 2, 3).Length);
			Assert.Equal(base.String, rds.LRange("TestRPush", 2, 3)[0]);
			Assert.Equal(base.String, rds.LRange("TestRPush", 2, 3)[1]);

			Assert.Equal(2, rds.LRange("TestRPush", 4, 5).Length);
			Assert.Equal(base.Bytes, rds.LRange<byte[]>("TestRPush", 4, 5)[0]);
			Assert.Equal(base.Bytes, rds.LRange<byte[]>("TestRPush", 4, 5)[1]);

			Assert.Equal(2, rds.LRange("TestRPush", 6, -1).Length);
			Assert.Equal(base.Class.ToString(), rds.LRange<TestClass>("TestRPush", 6, -1)[0].ToString());
			Assert.Equal(base.Class.ToString(), rds.LRange<TestClass>("TestRPush", 6, -1)[1].ToString());
		}
		[Fact]
		public void RPushX() {
			Assert.Equal(0, rds.RPushX("TestRPushX", base.Null));
			Assert.Equal(0, rds.RPushX("TestRPushX", base.String));
			Assert.Equal(0, rds.RPushX("TestRPushX", base.Bytes));
			Assert.Equal(0, rds.RPushX("TestRPushX", base.Class));

			Assert.Equal(1, rds.RPush("TestRPushX", base.Null));
			Assert.Equal(2, rds.RPushX("TestRPushX", base.Null));
			Assert.Equal(3, rds.RPushX("TestRPushX", base.String));
			Assert.Equal(4, rds.RPushX("TestRPushX", base.Bytes));
			Assert.Equal(5, rds.RPushX("TestRPushX", base.Class));
		}
	}
}
