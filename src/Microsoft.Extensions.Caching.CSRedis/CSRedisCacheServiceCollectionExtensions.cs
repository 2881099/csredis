using Caching.CSRedis;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Redis;
using System;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class CSRedisCacheServiceCollectionExtensions
    {
        /// <summary>
        /// Adds Redis distributed caching services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
        /// <param name="setupAction">An <see cref="Action{CSRedisCacheOptions}"/> to configure the provided
        /// <see cref="CSRedisCacheOptions"/>.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection AddCSRedisCache(this IServiceCollection services, Action<CSRedisCacheOptions> setupAction)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (setupAction == null)
            {
                throw new ArgumentNullException(nameof(setupAction));
            }

            services.AddOptions();
            services.Configure(setupAction);
            services.Add(ServiceDescriptor.Singleton<IDistributedCache, CSRedisCache>());

            return services;
        }
    }
}
