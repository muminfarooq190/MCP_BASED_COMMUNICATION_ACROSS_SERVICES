using Confluent.Kafka;
using Mcp.Contracts;
using Shared.Infrastructure;
using Shared.Infrastructure.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(_ =>
{
    var config = new ProducerConfig
    {
        BootstrapServers = KafkaHelper.DefaultBroker,
        Acks = Acks.All
    };

    return new ProducerBuilder<string, string>(config).Build();
});
builder.Services.AddSingleton<KafkaProducerBase>();

var app = builder.Build();

var mcpTools = new[]
{
    new { Name = "create_outage", Description = "Creates an outage command envelope and triggers service fan-out." },
    new { Name = "calculate_bill_impact", Description = "Estimates billing credit impact for the outage duration." },
    new { Name = "summarize_incident", Description = "Builds a customer-safe incident summary for notifications." }
};

app.MapGet("/mcp/tools", () => Results.Ok(mcpTools));

app.MapPost("/mcp/tools/create_outage", async (OutageRequest request, KafkaProducerBase producer, CancellationToken cancellationToken) =>
{
    var envelope = new McpEnvelope<OutageCreated>
    {
        CorrelationId = Guid.NewGuid().ToString("N"),
        TraceId = ObservabilityHelper.BuildTraceId(),
        TenantId = request.TenantId,
        SourceService = "gateway",
        TimestampUtc = DateTime.UtcNow,
        Metadata = new Dictionary<string, string>
        {
            ["eventType"] = "outage.created",
            ["severity"] = request.Severity,
            ["retry-count"] = "0"
        },
        Payload = new OutageCreated(
            request.TenantId,
            Guid.NewGuid().ToString("N"),
            request.Severity,
            request.Region),
        SchemaVersion = "1.0.0"
    };

    await producer.PublishAsync("outage.events", envelope, cancellationToken);

    return Results.Accepted("/events/outage", new
    {
        Envelope = envelope,
        PublishedTopic = "outage.events",
        DownstreamTopics = new[] { "notification.events", "billing.events", "analytics.events" }
    });
});

app.MapPost("/mcp/tools/calculate_bill_impact", (BillImpactInput input) =>
{
    var estimatedCredit = Math.Round(input.AffectedAccounts * input.MinutesDown * 0.02m, 2);
    return Results.Ok(new { input.TenantId, input.MinutesDown, EstimatedCredit = estimatedCredit });
});

app.MapPost("/mcp/tools/summarize_incident", (IncidentSummaryInput input) =>
{
    var message = $"[{input.Region}] Service disruption for {input.ServiceName}. Teams are actively restoring service.";
    return Results.Ok(new { Summary = message, input.Severity });
});

app.Run();

public sealed record OutageRequest(string TenantId, string ServiceName, string Region, string Severity, string Description);
public sealed record BillImpactInput(string TenantId, int AffectedAccounts, int MinutesDown);
public sealed record IncidentSummaryInput(string ServiceName, string Region, string Severity);
