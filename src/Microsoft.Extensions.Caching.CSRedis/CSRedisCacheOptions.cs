using CSRedis;
using Microsoft.Extensions.Caching.Redis;
using Microsoft.Extensions.Options;

namespace Caching.CSRedis
{
    /// <summary>
    /// Configuration options for <see cref="CSRedisCache"/>.
    /// </summary>
    public class CSRedisCacheOptions : IOptions<CSRedisCacheOptions>
    {
        /// <summary>
        /// Redis Client
        /// </summary>
        public CSRedisClient CSRedisClient { get; set; }

        CSRedisCacheOptions IOptions<CSRedisCacheOptions>.Value
        {
            get { return this; }
        }
    }
}
