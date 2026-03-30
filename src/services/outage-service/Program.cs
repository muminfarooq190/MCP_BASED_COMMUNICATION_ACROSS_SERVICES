using Confluent.Kafka;
using Mcp.Contracts;
using Shared.Infrastructure;
using Shared.Infrastructure.Messaging;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(RedisHelper.DefaultConnection));
builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();

builder.Services.AddSingleton(_ =>
{
    var producerConfig = new ProducerConfig { BootstrapServers = KafkaHelper.DefaultBroker, Acks = Acks.All };
    return new ProducerBuilder<string, string>(producerConfig).Build();
});
builder.Services.AddSingleton<KafkaProducerBase>();
builder.Services.AddSingleton<RetryPolicyPublisher>();

builder.Services.AddSingleton(_ =>
{
    var consumerConfig = new ConsumerConfig
    {
        BootstrapServers = KafkaHelper.DefaultBroker,
        GroupId = "outage-service-consumer",
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false
    };

    return new ConsumerBuilder<string, string>(consumerConfig).Build();
});

builder.Services.AddSingleton(sp =>
    new KafkaConsumerBase<OutageCreated>(
        sp.GetRequiredService<IConsumer<string, string>>(),
        sp.GetRequiredService<RetryPolicyPublisher>(),
        sp.GetRequiredService<IIdempotencyStore>(),
        serviceName: "outage-service",
        maxRetries: 3));

builder.Services.AddHostedService(sp =>
    new KafkaConsumerWorker<OutageCreated>(
        sp.GetRequiredService<KafkaConsumerBase<OutageCreated>>(),
        new[] { "outage.events", "outage.events.retry" },
        async (envelope, ct) =>
        {
            var producer = sp.GetRequiredService<KafkaProducerBase>();

            var sharedMetadata = new Dictionary<string, string>(envelope.Metadata)
            {
                ["eventType"] = "outage.forwarded",
                ["retry-count"] = "0"
            };

            await producer.PublishAsync("notification.events", envelope with
            {
                SourceService = "outage-service",
                Metadata = new Dictionary<string, string>(sharedMetadata)
            }, ct);

            await producer.PublishAsync("billing.events", envelope with
            {
                SourceService = "outage-service",
                Metadata = new Dictionary<string, string>(sharedMetadata)
            }, ct);

            await producer.PublishAsync("analytics.events", envelope with
            {
                SourceService = "outage-service",
                Metadata = new Dictionary<string, string>(sharedMetadata)
            }, ct);
        }));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { Service = "outage-service", Status = "Healthy" }));

app.Run();
