namespace Shared.Infrastructure.Messaging;

public interface IIdempotencyStore
{
    Task<bool> IsProcessedAsync(string correlationId, string serviceName, CancellationToken cancellationToken = default);
    Task<bool> TryMarkProcessedAsync(string correlationId, string serviceName, TimeSpan ttl, CancellationToken cancellationToken = default);
}
