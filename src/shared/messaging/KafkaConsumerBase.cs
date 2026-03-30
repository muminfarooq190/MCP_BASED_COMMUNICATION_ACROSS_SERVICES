using System.Text.Json;
using Confluent.Kafka;
using Mcp.Contracts;

namespace Shared.Infrastructure.Messaging;

public class KafkaConsumerBase<TPayload>(
    IConsumer<string, string> consumer,
    RetryPolicyPublisher retryPolicyPublisher,
    IIdempotencyStore idempotencyStore,
    string serviceName,
    int maxRetries,
    JsonSerializerOptions? serializerOptions = null,
    TimeSpan? idempotencyTtl = null)
{
    private readonly JsonSerializerOptions _serializerOptions = serializerOptions ?? new(JsonSerializerDefaults.Web);
    private readonly TimeSpan _idempotencyTtl = idempotencyTtl ?? TimeSpan.FromHours(24);

    public async Task StartAsync(
        IReadOnlyCollection<string> topics,
        Func<McpEnvelope<TPayload>, CancellationToken, Task> processMessage,
        CancellationToken cancellationToken)
    {
        consumer.Subscribe(topics);

        while (!cancellationToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;

            try
            {
                result = consumer.Consume(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (result is null)
            {
                continue;
            }

            var envelope = JsonSerializer.Deserialize<McpEnvelope<TPayload>>(result.Message.Value, _serializerOptions);
            if (envelope is null)
            {
                await MoveToDlqAsync(result, default, "Envelope could not be deserialized", cancellationToken);
                consumer.Commit(result);
                continue;
            }

            if (await idempotencyStore.IsProcessedAsync(envelope.CorrelationId, serviceName, cancellationToken))
            {
                consumer.Commit(result);
                continue;
            }

            try
            {
                await processMessage(envelope, cancellationToken);

                await idempotencyStore.TryMarkProcessedAsync(
                    envelope.CorrelationId,
                    serviceName,
                    _idempotencyTtl,
                    cancellationToken);

                consumer.Commit(result);
            }
            catch (Exception ex)
            {
                await MoveToDlqAsync(result, envelope, ex.Message, cancellationToken);
                consumer.Commit(result);
            }
        }

        consumer.Close();
    }

    private async Task MoveToDlqAsync(
        ConsumeResult<string, string> result,
        McpEnvelope<TPayload>? envelope,
        string reason,
        CancellationToken cancellationToken)
    {
        if (envelope is null)
        {
            var fallbackEnvelope = new McpEnvelope<TPayload>
            {
                CorrelationId = result.Message.Key ?? Guid.NewGuid().ToString("N"),
                TraceId = Guid.NewGuid().ToString("N"),
                TenantId = "unknown",
                SourceService = serviceName,
                TimestampUtc = DateTime.UtcNow,
                Metadata = new Dictionary<string, string>(),
                Payload = default!,
                SchemaVersion = "1.0.0"
            };

            await retryPolicyPublisher.PublishRetryOrDlqAsync(result.Topic, fallbackEnvelope, maxRetries, reason, cancellationToken);
            return;
        }

        await retryPolicyPublisher.PublishRetryOrDlqAsync(result.Topic, envelope, maxRetries, reason, cancellationToken);
    }
}
