using Agg.Test.DTOs;

namespace Agg.Test.Services
{
    public interface ICacheService
    {
        Task<string?> GetJsonAsync(string key, CancellationToken ct = default);
        Task SetJsonAsync(string key, object value, CancellationToken ct = default);
        Task<string?> SearchAsync(CacheQueryDto query, CancellationToken ct = default);
        Task<string[]> ListKeysAsync(string pattern, CancellationToken ct = default);
    }
}
