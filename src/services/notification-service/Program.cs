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
        GroupId = "notification-service-consumer",
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
        serviceName: "notification-service",
        maxRetries: 3));

builder.Services.AddHostedService(sp =>
    new KafkaConsumerWorker<OutageCreated>(
        sp.GetRequiredService<KafkaConsumerBase<OutageCreated>>(),
        new[] { "notification.events", "notification.events.retry" },
        (envelope, _) =>
        {
            Console.WriteLine($"[notification-service] correlation={envelope.CorrelationId} region={envelope.Payload.Region} priority={envelope.Payload.Priority}");
            return Task.CompletedTask;
        }));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { Service = "notification-service", Status = "Healthy" }));

app.Run();
