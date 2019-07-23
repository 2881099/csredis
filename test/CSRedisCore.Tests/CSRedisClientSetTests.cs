using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace CSRedisCore.Tests {
	public class CSRedisClientSetTests : TestBase {

		public void SAdd() { }
		public void SCard() { }
		public void SDiff() { }
		public void SDiffStore() { }
		public void SInter() { }
		public void SInterStore() { }
		public void SIsMember() { }
		public void SMembers() { }
		public void SMove() { }
		public void SPop() { }
		[Fact]
		public void SPopWithCount() {
			Assert.Equal(3, rds.SAdd("TestSPopWithCount", "string1", "string2", "string3"));
			Assert.Equal(2, rds.SPop("TestSPopWithCount", 2).Length);
			Assert.Equal(1, rds.SCard("TestSPopWithCount"));
		}
		public void SRandMember() { }
		public void SRandMembers() { }
		public void SRem() { }
		public void SUnion() { }
		public void SUnionStore() { }
		public void SScan() { }
	}
}
