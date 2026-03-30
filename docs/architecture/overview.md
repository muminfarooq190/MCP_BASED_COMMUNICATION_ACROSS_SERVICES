# MCP-Based Outage Processing Architecture

## High-level diagram

```mermaid
flowchart LR
    C[Client / SRE] --> G[Gateway API\nMCP Tools]
    G --> K[(Kafka / Redpanda)]
    G --> R[(Redis)]

    K --> O[Outage Service]
    K --> N[Notification Service]
    K --> B[Billing Service]
    K --> A[Analytics Service]

    O --> K
    N --> K
    B --> K
    A --> K

    M[MCP Server] --> K
    M --> R

    G -.shared contracts.-> S[(mcp-contracts + shared infra)]
    M -.shared contracts.-> S
    O -.shared contracts.-> S
    N -.shared contracts.-> S
    B -.shared contracts.-> S
    A -.shared contracts.-> S
```

## Services
- **gateway**: API ingress + MCP tool surface.
- **mcp-server**: dedicated MCP server with tool endpoints for publish/trace/retry workflows.
- **outage-service**: validates outage requests.
- **notification-service**: customer and internal fan-out notifications.
- **billing-service**: computes credits and billing adjustments.
- **analytics-service**: aggregates metrics and trend analysis.

## Shared packages
- **mcp-contracts**: common envelope and event contract definitions.
- **shared**: Kafka, Redis, and observability helpers.

## Event flow
1. Gateway receives outage request and wraps it in `McpEnvelope<T>`.
2. Gateway publishes `outage.created` to Kafka.
3. Downstream services consume, process, and optionally publish derived events.
4. MCP server tools support operational workflows: publish, trace correlation, and retry failed events.
