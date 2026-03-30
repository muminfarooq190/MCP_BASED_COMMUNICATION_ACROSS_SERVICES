using StackExchange.Redis;

namespace Shared.Infrastructure.Messaging;

public sealed class RedisIdempotencyStore(IConnectionMultiplexer connectionMultiplexer) : IIdempotencyStore
{
    private readonly IDatabase _db = connectionMultiplexer.GetDatabase();

    public async Task<bool> IsProcessedAsync(string correlationId, string serviceName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _db.KeyExistsAsync(BuildKey(correlationId, serviceName));
    }

    public async Task<bool> TryMarkProcessedAsync(string correlationId, string serviceName, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await _db.StringSetAsync(
            BuildKey(correlationId, serviceName),
            DateTime.UtcNow.ToString("O"),
            expiry: ttl,
            when: When.NotExists);
    }

    private static string BuildKey(string correlationId, string serviceName)
        => $"idempotency:{serviceName}:{correlationId}";
}
