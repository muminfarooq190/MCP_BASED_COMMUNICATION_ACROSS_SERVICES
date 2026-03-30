namespace Mcp.Contracts;

public sealed record OutageCreated(
    string AccountId,
    string OutageId,
    string Priority,
    string Region);
