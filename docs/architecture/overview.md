# MCP-Based Outage Processing Architecture

## Services
- **gateway**: API ingress + MCP tool surface.
- **outage-service**: validates outage requests.
- **notification-service**: customer and internal fan-out notifications.
- **billing-service**: computes credits and billing adjustments.
- **analytics-service**: aggregates metrics and trend analysis.

## Shared packages
- **mcp-contracts**: common envelope and event contract definitions.
- **shared**: Kafka, Redis, and observability helpers.

## Event flow
1. Gateway receives outage request.
2. Gateway wraps request into `McpEnvelope<T>`.
3. Gateway emits fan-out event to service subscribers.
4. Services process event and publish status updates.
