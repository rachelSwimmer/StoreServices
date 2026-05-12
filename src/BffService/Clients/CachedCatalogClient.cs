using BffService.DTOs;
using SharedKernel.Caching;

namespace BffService.Clients;

public class CachedCatalogClient : ICatalogClient
{
    private readonly ICatalogClient _inner;
    private readonly ICacheService _cache;
    private readonly ILogger<CachedCatalogClient> _logger;
    private readonly TimeSpan _ttl;

    private const string KeyPrefix = "bff:product-stock:";

    public CachedCatalogClient(
        ICatalogClient inner,
        ICacheService cache,
        ILogger<CachedCatalogClient> logger,
        IConfiguration configuration)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
        _ttl = TimeSpan.FromSeconds(configuration.GetValue<int>("Cache:BffProductStockTtlSeconds", 30));
    }

    public async Task<ProductStockDto> GetProductStockAsync(int productId)
    {
        var key = $"{KeyPrefix}{productId}";
        var cached = await _cache.GetAsync<ProductStockDto>(key);
        if (cached != null) return cached;

        var result = await _inner.GetProductStockAsync(productId);
        await _cache.SetAsync(key, result, _ttl);
        return result;
    }
}
