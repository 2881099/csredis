using CSRedis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CSRedisCore.Tests {
	public class CSRedisClientLockTests : TestBase {

		[Fact]
		public void Lock1() {
			var tasks = new Task[4];
			for (var a = 0; a < tasks.Length; a++)
				tasks[a] = Task.Run(() => {
					var lk = rds.Lock("testlock1", 10);
					Thread.CurrentThread.Join(1000);
					Assert.True(lk.Unlock());
				});
			Task.WaitAll(tasks);
		}
	}
}
