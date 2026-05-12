using BffService.DTOs;
using SharedKernel.Caching;

namespace BffService.Clients;

public class CachedUserClient : IUserClient
{
    private readonly IUserClient _inner;
    private readonly ICacheService _cache;
    private readonly ILogger<CachedUserClient> _logger;
    private readonly TimeSpan _ttl;

    private const string KeyPrefix = "bff:user-summary:";

    public CachedUserClient(
        IUserClient inner,
        ICacheService cache,
        ILogger<CachedUserClient> logger,
        IConfiguration configuration)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
        _ttl = TimeSpan.FromSeconds(configuration.GetValue<int>("Cache:BffUserSummaryTtlSeconds", 300));
    }

    public async Task<UserSummaryDto> GetUserSummaryAsync(int userId)
    {
        var key = $"{KeyPrefix}{userId}";
        var cached = await _cache.GetAsync<UserSummaryDto>(key);
        if (cached != null) return cached;

        var result = await _inner.GetUserSummaryAsync(userId);
        await _cache.SetAsync(key, result, _ttl);
        return result;
    }
}
