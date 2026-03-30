using Mcp.Contracts;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { Service = "outage-service", Status = "Healthy" }));

app.MapPost("/events", (McpEnvelope<object> envelope) =>
{
    return Results.Ok(new
    {
        Service = "outage-service",
        Received = envelope.Metadata.TryGetValue("eventType", out var eventType) ? eventType : "unknown",
        envelope.CorrelationId,
        envelope.TraceId,
        envelope.TimestampUtc
    });
});

app.Run();
