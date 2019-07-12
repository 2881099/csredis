using CSRedis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace CSRedisCore.Tests {
	public class CSRedisClientGeoTests_ : TestBase {

		/*
		 * 
		 * Geo 只有 redis-server 3.2+ 才提供，测试代码请连接高版本
		 * 
		 * */

		[Fact]
		public void GeoAdd() {
			Assert.Equal(3, rds.GeoAdd("TestGeoAdd", (10, 20, "m1"), (11, 21, "m2"), (12, 22, "m3")));
		}
		[Fact]
		public void GeoDist() {
			Assert.Equal(3, rds.GeoAdd("TestGeoDist", (10, 20, "m1"), (11, 21, "m2"), (12, 22, "m3")));

			Assert.NotNull(rds.GeoDist("TestGeoDist", "m1", "m2"));
			Assert.NotNull(rds.GeoDist("TestGeoDist", "m1", "m3"));
			Assert.NotNull(rds.GeoDist("TestGeoDist", "m2", "m3"));
			Assert.Null(rds.GeoDist("TestGeoDist", "m1", "m31"));
			Assert.Null(rds.GeoDist("TestGeoDist", "m11", "m31"));
		}
		[Fact]
		public void GeoHash() {
			Assert.Equal(3, rds.GeoAdd("TestGeoHash", (10, 20, "m1"), (11, 21, "m2"), (12, 22, "m3")));

			Assert.Equal(2, rds.GeoHash("TestGeoHash", new[] { "m1", "m2" }).Select(a => string.IsNullOrEmpty(a) == false).Count());
			Assert.Equal(2, rds.GeoHash("TestGeoHash", new[] { "m1", "m2", "m22" }).Where(a => string.IsNullOrEmpty(a) == false).Count());
		}
		[Fact]
		public void GeoPos() {
			Assert.Equal(3, rds.GeoAdd("TestGeoPos", (10, 20, "m1"), (11, 21, "m2"), (12, 22, "m3")));

			Assert.Equal(4, rds.GeoPos("TestGeoPos", new[] { "m1", "m2", "m22", "m3" }).Length);
			//Assert.Equal((10, 20), rds.GeoPos("TestGeoPos", new[] { "m1", "m2", "m22", "m3" })[0]);
			//Assert.Equal((11, 21), rds.GeoPos("TestGeoPos", new[] { "m1", "m2", "m22", "m3" })[1]);
			Assert.Null(rds.GeoPos("TestGeoPos", new[] { "m1", "m2", "m22", "m3" })[2]);
			//Assert.Equal((12, 22), rds.GeoPos("TestGeoPos", new[] { "m1", "m2", "m22", "m3" })[3]);
		}

		[Fact]
		public void GeoRadius() {
			Assert.Equal(3, rds.GeoAdd("TestGeoRadius", (10, 20, "m1"), (11, 21, "m2"), (12, 22, "m3")));

			var geopos = rds.GeoPos("TestGeoRadius", new[] { "m1", "Catania", "m2", "Palermo", "Catania2" });

			var georadius1 = rds.GeoRadius("TestGeoRadius", 15, 37, 200, GeoUnit.km, null, null);
			var georadius2 = rds.GeoRadius<byte[]>("TestGeoRadius", 15, 37, 200, GeoUnit.km, null, null);
			
			var georadius5 = rds.GeoRadiusWithDist("TestGeoRadius", 15, 37, 200, GeoUnit.km, null, null);
			var georadius6 = rds.GeoRadiusWithDist<byte[]>("TestGeoRadius", 15, 37, 200, GeoUnit.km, null);
			var georadius7 = rds.GeoRadiusWithDistAndCoord("TestGeoRadius", 15, 37, 200, GeoUnit.km, null);
			var georadius8 = rds.GeoRadiusWithDistAndCoord<byte[]>("TestGeoRadius", 15, 37, 200, GeoUnit.km);
			
			var georadius11 = rds.GeoRadiusByMember("TestGeoRadius", "m1", 200, GeoUnit.km, null, null);
			var georadius12 = rds.GeoRadiusByMember<byte[]>("TestGeoRadius", "m1", 200, GeoUnit.km, null, null);
			var georadius15 = rds.GeoRadiusByMemberWithDist("TestGeoRadius", "m1", 200, GeoUnit.km, null, null);
			var georadius16 = rds.GeoRadiusByMemberWithDist<byte[]>("TestGeoRadius", "m1", 200, GeoUnit.km, null);
			var georadius17 = rds.GeoRadiusByMemberWithDistAndCoord("TestGeoRadius", "m1", 200, GeoUnit.km, null);
			var georadius18 = rds.GeoRadiusByMemberWithDistAndCoord<byte[]>("TestGeoRadius", "m1", 200, GeoUnit.km);
		}
	}
}
