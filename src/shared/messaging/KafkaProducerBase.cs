using System.Text.Json;
using Confluent.Kafka;
using Mcp.Contracts;

namespace Shared.Infrastructure.Messaging;

public class KafkaProducerBase(IProducer<string, string> producer, JsonSerializerOptions? serializerOptions = null)
{
    private readonly JsonSerializerOptions _serializerOptions = serializerOptions ?? new(JsonSerializerDefaults.Web);

    public Task<DeliveryResult<string, string>> PublishAsync<TPayload>(string topic, McpEnvelope<TPayload> envelope, CancellationToken cancellationToken = default)
    {
        var message = new Message<string, string>
        {
            Key = envelope.CorrelationId,
            Value = JsonSerializer.Serialize(envelope, _serializerOptions)
        };

        return producer.ProduceAsync(topic, message, cancellationToken);
    }

    public Task<DeliveryResult<string, string>> PublishRawAsync(string topic, string key, string payload, CancellationToken cancellationToken = default)
        => producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = payload }, cancellationToken);
}
