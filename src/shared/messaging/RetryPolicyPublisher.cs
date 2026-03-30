using System.Text.Json;
using Confluent.Kafka;
using Mcp.Contracts;

namespace Shared.Infrastructure.Messaging;

public sealed class RetryPolicyPublisher(
    KafkaProducerBase producer,
    JsonSerializerOptions? serializerOptions = null)
{
    private readonly JsonSerializerOptions _serializerOptions = serializerOptions ?? new(JsonSerializerDefaults.Web);

    public async Task PublishRetryOrDlqAsync<TPayload>(
        string consumedTopic,
        McpEnvelope<TPayload> envelope,
        int maxRetries,
        string? error = null,
        CancellationToken cancellationToken = default)
    {
        var baseTopic = NormalizeBaseTopic(consumedTopic);

        var retryCount = GetRetryCount(envelope.Metadata);
        var nextRetryCount = retryCount + 1;

        var metadata = new Dictionary<string, string>(envelope.Metadata)
        {
            ["retry-count"] = nextRetryCount.ToString()
        };

        if (!string.IsNullOrWhiteSpace(error))
        {
            metadata["last-error"] = error;
        }

        var outgoing = envelope with { Metadata = metadata };

        var targetTopic = nextRetryCount <= maxRetries
            ? $"{baseTopic}.retry"
            : $"{baseTopic}.dlq";

        await producer.PublishAsync(targetTopic, outgoing, cancellationToken);
    }

    public static int GetRetryCount(IReadOnlyDictionary<string, string> metadata)
        => metadata.TryGetValue("retry-count", out var raw) && int.TryParse(raw, out var count)
            ? count
            : 0;

    public static string NormalizeBaseTopic(string consumedTopic)
    {
        if (consumedTopic.EndsWith(".retry", StringComparison.OrdinalIgnoreCase))
        {
            return consumedTopic[..^".retry".Length];
        }

        if (consumedTopic.EndsWith(".dlq", StringComparison.OrdinalIgnoreCase))
        {
            return consumedTopic[..^".dlq".Length];
        }

        return consumedTopic;
    }
}
