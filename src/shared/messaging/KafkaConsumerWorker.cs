using Mcp.Contracts;
using Microsoft.Extensions.Hosting;

namespace Shared.Infrastructure.Messaging;

public sealed class KafkaConsumerWorker<TPayload>(
    KafkaConsumerBase<TPayload> consumer,
    IReadOnlyCollection<string> topics,
    Func<McpEnvelope<TPayload>, CancellationToken, Task> handler) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => consumer.StartAsync(topics, handler, stoppingToken);
}
