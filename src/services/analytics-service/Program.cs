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
        GroupId = "analytics-service-consumer",
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
        serviceName: "analytics-service",
        maxRetries: 3));

builder.Services.AddHostedService(sp =>
    new KafkaConsumerWorker<OutageCreated>(
        sp.GetRequiredService<KafkaConsumerBase<OutageCreated>>(),
        new[] { "analytics.events", "analytics.events.retry" },
        (envelope, _) =>
        {
            Console.WriteLine($"[analytics-service] correlation={envelope.CorrelationId} outage={envelope.Payload.OutageId} region={envelope.Payload.Region}");
            return Task.CompletedTask;
        }));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { Service = "analytics-service", Status = "Healthy" }));

app.Run();
