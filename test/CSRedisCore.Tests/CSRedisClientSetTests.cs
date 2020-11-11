using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace CSRedisCore.Tests {
	public class CSRedisClientSetTests : TestBase {

		[Fact]
		public void SPopWithCount() {
			Assert.Equal(3, rds.SAdd("TestSPopWithCount", "string1", "string2", "string3"));
			Assert.Equal(2, rds.SPop("TestSPopWithCount", 2).Length);
			Assert.Equal(1, rds.SCard("TestSPopWithCount"));
		}
	}
}
