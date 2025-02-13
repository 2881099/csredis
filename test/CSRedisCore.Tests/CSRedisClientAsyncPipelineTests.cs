using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using CSRedis;
using Xunit;

namespace CSRedisCore.Tests;

public class CSRedisClientAsyncPipelineTests
{
    private const string ConnectionString = "localhost:6379,asyncPipeline=true";

    [Fact]
    public async Task Test_Parallel_Call_GetAsync_When_AsyncPipeline_Is_True()
    {
        var range = Enumerable.Range(0, 100).ToArray();

        var rds = new CSRedisClient(ConnectionString);

        foreach (var i in range)
        {
            var key = "TestAsyncPipeline:" + i;
            await rds.SetAsync(key, i.ToString());
        }

        for (int i = 0; i < 100; i++)
        {
            await Parallel.ForEachAsync(range, async (item, ct) =>
            {
                var key = "TestAsyncPipeline:" + item;
                var ret = await rds.GetAsync(key);

                Assert.True(ret == item.ToString());
            });
        }
    }
    
}