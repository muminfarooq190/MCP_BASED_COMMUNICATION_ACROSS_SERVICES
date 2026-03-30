using System.Collections.Concurrent;
using System.Diagnostics;
using Mcp.Contracts;
using Shared.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

const string CurrentSchemaVersion = "1.0.0";
var activitySource = new ActivitySource("Mcp.Server.Tools");
var eventPublisher = new InMemoryEventPublisher();
var trailStore = new InMemoryTrailStore();

var mcpTools = new[]
{
    new { Name = "publish_outage_event", Description = "Publishes an outage-created envelope to outage.events." },
    new { Name = "trace_correlation", Description = "Returns ordered service hops for a correlation id." },
    new { Name = "retry_failed_event", Description = "Requeues failed correlation events from retry/DLQ topics." }
};

app.MapGet("/mcp/tools", () => Results.Ok(mcpTools));

app.MapPost("/mcp/tools/publish_outage_event", (PublishOutageEventInput input, HttpContext httpContext) =>
{
    using var activity = activitySource.StartActivity("mcp.tool.publish_outage_event", ActivityKind.Server);
    activity?.SetTag("mcp.tool.name", "publish_outage_event");
    activity?.SetTag("mcp.schema.version", input.SchemaVersion);

    var contextValidation = ToolValidation.ValidateContext(httpContext, input.SchemaVersion, CurrentSchemaVersion);
    if (contextValidation is not null)
    {
        activity?.SetTag("mcp.validation.error", contextValidation.Error);
        return contextValidation.Result;
    }

    var tenantId = httpContext.Request.Headers[ToolValidation.TenantHeader].ToString();
    var correlationId = httpContext.Request.Headers[ToolValidation.CorrelationHeader].ToString();

    var payload = new OutageCreated(input.AccountId, input.OutageId, input.Priority, input.Region);
    var envelope = new McpEnvelope<OutageCreated>
    {
        CorrelationId = correlationId,
        TraceId = ObservabilityHelper.BuildTraceId(),
        TenantId = tenantId,
        SourceService = "mcp-server",
        TimestampUtc = DateTime.UtcNow,
        SchemaVersion = CurrentSchemaVersion,
        Metadata = new Dictionary<string, string>
        {
            ["eventType"] = "outage.created",
            ["priority"] = input.Priority,
            ["kafkaBroker"] = KafkaHelper.DefaultBroker
        },
        Payload = payload
    };

    const string topic = "outage.events";
    var partitionKey = $"{input.AccountId}:{input.Region}";
    eventPublisher.Publish(topic, partitionKey, envelope);
    trailStore.Append(correlationId, "mcp-server", "processed");

    activity?.SetTag("tenant.id", tenantId);
    activity?.SetTag("correlation.id", correlationId);
    activity?.SetTag("messaging.destination", topic);
    activity?.SetTag("messaging.kafka.partition_key", partitionKey);

    return Results.Ok(new
    {
        CorrelationId = correlationId,
        Topic = topic,
        PartitionKey = partitionKey
    });
});

app.MapPost("/mcp/tools/trace_correlation", (TraceCorrelationInput input, HttpContext httpContext) =>
{
    using var activity = activitySource.StartActivity("mcp.tool.trace_correlation", ActivityKind.Server);
    activity?.SetTag("mcp.tool.name", "trace_correlation");
    activity?.SetTag("mcp.schema.version", input.SchemaVersion);

    var contextValidation = ToolValidation.ValidateContext(httpContext, input.SchemaVersion, CurrentSchemaVersion);
    if (contextValidation is not null)
    {
        activity?.SetTag("mcp.validation.error", contextValidation.Error);
        return contextValidation.Result;
    }

    activity?.SetTag("tenant.id", httpContext.Request.Headers[ToolValidation.TenantHeader].ToString());
    activity?.SetTag("correlation.id", input.CorrelationId);
    activity?.SetTag("redis.connection", RedisHelper.DefaultConnection);

    var hops = trailStore.GetOrderedTrail(input.CorrelationId);
    return Results.Ok(new { CorrelationId = input.CorrelationId, Hops = hops });
});

app.MapPost("/mcp/tools/retry_failed_event", (RetryFailedEventInput input, HttpContext httpContext) =>
{
    using var activity = activitySource.StartActivity("mcp.tool.retry_failed_event", ActivityKind.Server);
    activity?.SetTag("mcp.tool.name", "retry_failed_event");
    activity?.SetTag("mcp.schema.version", input.SchemaVersion);

    var contextValidation = ToolValidation.ValidateContext(httpContext, input.SchemaVersion, CurrentSchemaVersion);
    if (contextValidation is not null)
    {
        activity?.SetTag("mcp.validation.error", contextValidation.Error);
        return contextValidation.Result;
    }

    var retryState = trailStore.IncrementRetry(input.CorrelationId);
    trailStore.Append(input.CorrelationId, "mcp-server", retryState.NewRetryCount > 1 ? "retry" : "processed");

    activity?.SetTag("tenant.id", httpContext.Request.Headers[ToolValidation.TenantHeader].ToString());
    activity?.SetTag("correlation.id", input.CorrelationId);
    activity?.SetTag("messaging.destination", retryState.DestinationTopic);
    activity?.SetTag("retry.previous_count", retryState.PreviousRetryCount);
    activity?.SetTag("retry.new_count", retryState.NewRetryCount);

    return Results.Ok(retryState);
});

app.Run();

public sealed record PublishOutageEventInput(
    string AccountId,
    string OutageId,
    string Priority,
    string Region,
    string SchemaVersion);

public sealed record TraceCorrelationInput(string CorrelationId, string SchemaVersion);

public sealed record RetryFailedEventInput(string CorrelationId, string SchemaVersion);

internal static class ToolValidation
{
    internal const string TenantHeader = "x-tenant-id";
    internal const string CorrelationHeader = "x-correlation-id";

    internal static ValidationError? ValidateContext(HttpContext context, string schemaVersion, string currentSchemaVersion)
    {
        if (string.IsNullOrWhiteSpace(context.Request.Headers[TenantHeader]))
        {
            return new ValidationError("missing_tenant_context", Results.BadRequest(new { Error = "Missing tenant context header x-tenant-id." }));
        }

        if (string.IsNullOrWhiteSpace(context.Request.Headers[CorrelationHeader]))
        {
            return new ValidationError("missing_correlation_context", Results.BadRequest(new { Error = "Missing correlation context header x-correlation-id." }));
        }

        if (!string.Equals(schemaVersion, currentSchemaVersion, StringComparison.Ordinal))
        {
            return new ValidationError("stale_schema_version", Results.BadRequest(new { Error = $"Unsupported schemaVersion '{schemaVersion}'. Expected '{currentSchemaVersion}'." }));
        }

        return null;
    }
}

internal sealed record ValidationError(string Error, IResult Result);

internal sealed class InMemoryEventPublisher
{
    private readonly ConcurrentDictionary<string, List<object>> _topicMessages = new();

    public void Publish<T>(string topic, string partitionKey, McpEnvelope<T> envelope)
    {
        var messages = _topicMessages.GetOrAdd(topic, _ => new List<object>());
        lock (messages)
        {
            messages.Add(new { PartitionKey = partitionKey, Envelope = envelope });
        }
    }
}

internal sealed class InMemoryTrailStore
{
    private readonly ConcurrentDictionary<string, List<HopEntry>> _trail = new();
    private readonly ConcurrentDictionary<string, int> _retryCount = new();

    public void Append(string correlationId, string service, string status)
    {
        var hops = _trail.GetOrAdd(correlationId, _ => new List<HopEntry>());
        lock (hops)
        {
            hops.Add(new HopEntry(service, DateTime.UtcNow, status));
        }
    }

    public IReadOnlyList<HopEntry> GetOrderedTrail(string correlationId)
    {
        if (!_trail.TryGetValue(correlationId, out var hops))
        {
            return [];
        }

        lock (hops)
        {
            return hops.OrderBy(h => h.TimestampUtc).ToArray();
        }
    }

    public RetryResult IncrementRetry(string correlationId)
    {
        var previous = _retryCount.GetOrAdd(correlationId, 0);
        var next = previous + 1;
        _retryCount[correlationId] = next;

        var destinationTopic = next >= 3 ? "outage.events.retry" : "outage.events";
        if (next >= 3)
        {
            Append(correlationId, "retry-worker", "dlq");
        }

        return new RetryResult(previous, next, destinationTopic);
    }
}

public sealed record HopEntry(string Service, DateTime TimestampUtc, string Status);
public sealed record RetryResult(int PreviousRetryCount, int NewRetryCount, string DestinationTopic);
