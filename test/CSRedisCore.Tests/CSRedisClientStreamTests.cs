using CSRedis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace CSRedisCore.Tests {
	public class CSRedisClientStreamTests : TestBase {

		/*
		 * 
		 * Stream 只有 redis-server 5.0+ 才提供，测试代码请连接高版本
		 * 
		 * */

		[Fact]
        public void XAck()
        {

        }

        [Fact]
        public void XAdd()
        {
            rds.XAdd("testXAdd01", ("f1", "v1"), ("f2", "v2"));
            rds.XAdd("testXAdd02", "*", ("f1", "v1"), ("f2", "v2"));
            rds.XAdd("testXAdd03", 128, "*", ("f1", "v1"), ("f2", "v2"));
            rds.XAdd("testXAdd04", -128, "*", ("f1", "v1"), ("f2", "v2"));
            rds.Del("testXAdd01", "testXAdd02", "testXAdd03", "testXAdd04");

            rds.XAdd("testXAdd01", "42-0", ("f1", "v1"), ("f2", "v2"));
            rds.XAdd("testXAdd02", 128, "43-0", ("f1", "v1"), ("f2", "v2"));
            rds.XAdd("testXAdd03", -128, "44-0", ("f1", "v1"), ("f2", "v2"));
            rds.Del("testXAdd01", "testXAdd02", "testXAdd03", "testXAdd04");
        }

        [Fact]
        public void XClaim()
        {
            var id = rds.XAdd("testXClaim01", ("f1", "v1"), ("f2", "v2"));
            //rds.XGroupCreate("testXClaimKey01", "group01", id, true);
            rds.XClaim("testXClaimKey01", "group01", "consumer01", 5000, id);
            rds.XClaim("testXClaimKey01", "group01", "consumer01", 5000, new string[] { id }, 3000, 3, false);
            rds.XClaim("testXClaimKey01", "group01", "consumer01", 5000, new string[] { id }, 3000, 3, true);

            var d11 = rds.XClaim("mystream", "group55", "Alice", 1000, "1573547631296-0");
            var d22 = rds.XClaim("mystream", "group55", "Alice", 1000, new[] { "1573547631296-0" }, 1000, 3, true);
            var d33 = rds.XClaim("mystream", "group55", "Alice", 1000, new[] { "1573547631296-0" }, 1000, 3, false);
        }

        [Fact]
        public void XClaimJustId()
        {
            var id = rds.XAdd("testXClaimJustId01", ("f1", "v1"), ("f2", "v2"));
            //rds.XGroupCreate("testXClaimJustIdKey01", "group01", id, true);
            rds.XClaimJustId("testXClaimJustIdKey01", "group01", "consumer01", 5000, id);
            rds.XClaimJustId("testXClaimJustIdKey01", "group01", "consumer01", 5000, new string[] { id }, 3000, 3, false);
            rds.XClaimJustId("testXClaimJustIdKey01", "group01", "consumer01", 5000, new string[] { id }, 3000, 3, true);

            var d11 = rds.XClaimJustId("mystream", "group55", "Alice", 1000, "1573547631296-0");
            var d22 = rds.XClaimJustId("mystream", "group55", "Alice", 1000, new[] { "1573547631296-0" }, 1000, 3, true);
            var d33 = rds.XClaimJustId("mystream", "group55", "Alice", 1000, new[] { "1573547631296-0" }, 1000, 3, false);
        }

        [Fact]
        public void XDel()
        {
            var id = rds.XAdd("testXDel01", ("f1", "v1"), ("f2", "v2"));
            rds.XDel("testtestXDelKey01", id);
        }

        [Fact]
        public void XGroupCreate()
        {
            var id = rds.XAdd("testXGroupCreate01", ("f1", "v1"), ("f2", "v2"));
            //rds.XGroupCreate("testXGroupCreateKey01", "group01", id, true);
            //rds.XGroupCreate("testXGroupCreateKey01", "group02", "$", true);
        }

        [Fact]
        public void XGroupSetId()
        {
            //rds.XGroupCreate("testXGroupSetIdKey01", "group04", "$", true);
            var id = rds.XAdd("testXGroupSetId01", ("f1", "v1"), ("f2", "v2"));
            rds.XGroupSetId("testXGroupSetIdKey01", "group04", id);
        }

        [Fact]
        public void XGroupDestroy()
        {
            rds.XGroupCreate("testXGroupDestroyKey01", "group04", "$", true);
            rds.XGroupDestroy("testXGroupDestroyKey01", "group04");
        }

        [Fact]
        public void XGroupDelConsumer()
        {
            //rds.XGroupCreate("testXGroupDelConsumerKey01", "group04", "$", true);
            rds.XGroupDelConsumer("testXGroupDelConsumerKey01", "group04", "consumer01");
        }

        [Fact]
        public void XLen()
        {
            rds.XLen("textsss");
        }

        [Fact]
        public void XRange()
        {
            rds.XRange("textXRangeKey01", "-", "+", 1);

            for (var i = 0; i < 10; i++)
            { 
                //if (i >= 5) 
                // Thread.Sleep(TimeSpan.FromSeconds(1));
                rds.XAdd("mystream", 5, "*", ($"k{i}", $"v{i}"));
            }

            var ttt1 = rds.XRange("mystream", "-", "+", 1);
            var ttt2 = rds.XRange("mystream", "-", "+", 2);
        }

        [Fact]
        public void XRevRange()
        {
            rds.XRevRange("textXRangeKey01", "-", "+", 1);

            for (var i = 0; i < 10; i++)
            {
                //if (i >= 5) 
                // Thread.Sleep(TimeSpan.FromSeconds(1));
                rds.XAdd("mystream", 5, "*", ($"k{i}", $"v{i}"));
            }

            var ttt1 = rds.XRevRange("mystream", "-", "+", 1);
            var ttt2 = rds.XRevRange("mystream", "-", "+", 2);
        }

        [Fact]
        public void XRead()
        {
            var id1 = rds.XAdd("testXRead01", ("f1", "v1"), ("f2", "v2"));
            var id2 = rds.XAdd("testXRead02", ("f1", "v1"), ("f2", "v2"));
            rds.XRead(10, 1000, ("testKey01", id1), ("testKey02", id2));

            rds.XAdd("mt2", ("aaa", "111"), ("bbb", "222"));
            var ttt1 = rds.XRead(2, 1000, ("mt2", "0-0"), ("mystream", "0-0"));
        }

        [Fact]
        public void XReadGroup()
        {
            var id1 = rds.XAdd("testXReadGroupKey01", ("f1", "v1"), ("f2", "v2"));
            var id2 = rds.XAdd("testXReadGroupKey02", ("f1", "v1"), ("f2", "v2"));
            //rds.XGroupCreate("testXReadGroupKey01", "testXReadGroup01", id1, true);
            //rds.XGroupCreate("testXReadGroupKey02", "testXReadGroup01", id2, true);
            rds.XReadGroup("testXReadGroup01", "consumer01", 10, 1000, ("testXReadGroupKey01", ">"), ("testXReadGroupKey02", ">"));
        }

        [Fact]
        public void XTrim()
        {
            rds.XTrim("testXTrimKey01", 5);
        }

        [Fact]
        public void XInfo()
        {
            var d11 = rds.XInfoStream("mystream");
            var d22 = rds.XInfoGroups("mystream");
            var d33 = rds.XInfoConsumers("mystream", "group55");
        }
    }
}
