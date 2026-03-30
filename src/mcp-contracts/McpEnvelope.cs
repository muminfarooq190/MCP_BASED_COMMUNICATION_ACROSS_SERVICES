namespace Mcp.Contracts;

public sealed record McpEnvelope<T>
{
    public required string CorrelationId { get; init; }
    public required string TraceId { get; init; }
    public required string TenantId { get; init; }
    public required string SourceService { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public required Dictionary<string, string> Metadata { get; init; }
    public required T Payload { get; init; }
    public required string SchemaVersion { get; init; }
}
