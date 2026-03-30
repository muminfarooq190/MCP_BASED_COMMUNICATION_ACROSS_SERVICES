# MCP Based Communication Across Services

A .NET mono-repo showing MCP-style event communication across gateway + microservices using Kafka/Redpanda and Redis.

## Monorepo Layout
- `src/gateway` API entrypoint and MCP tool endpoints.
- `src/mcp-server` dedicated MCP server exposing outage event tools.
- `src/mcp-contracts` shared envelope and event contracts.
- `src/services/outage-service`
- `src/services/notification-service`
- `src/services/billing-service`
- `src/services/analytics-service`
- `src/shared` Kafka, Redis, and observability helpers.
- `deploy/docker-compose` and root `docker-compose.yml` local infra stack.
- `docs/architecture` architecture notes.

## MCP server tools (3 exposed)
The MCP server exposes exactly three operational tools:
1. `publish_outage_event`
2. `trace_correlation`
3. `retry_failed_event`

List them:
```bash
curl http://localhost:5100/mcp/tools
```

## Quickstart: run in under 10 minutes

### 1) Prerequisites
- .NET 8 SDK
- Docker Desktop / Docker Engine

### 2) Bootstrap local config
```bash
cp .env.example .env
```

### 3) Start infrastructure
```bash
docker compose up -d
```

### 4) Start apps (6 terminals)
```bash
dotnet run --project src/gateway/Gateway.Api.csproj
```
```bash
dotnet run --project src/mcp-server/Mcp.Server.csproj --urls http://localhost:5100
```
```bash
dotnet run --project src/services/outage-service/OutageService.Api.csproj
```
```bash
dotnet run --project src/services/notification-service/NotificationService.Api.csproj
```
```bash
dotnet run --project src/services/billing-service/BillingService.Api.csproj
```
```bash
dotnet run --project src/services/analytics-service/AnalyticsService.Api.csproj
```

### 5) Send sample curl to create outage
```bash
curl -X POST http://localhost:5000/mcp/tools/create_outage \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId":"tenant-acme",
    "serviceName":"payments",
    "region":"us-east",
    "severity":"critical",
    "description":"payment authorization failures"
  }'
```

Expected response shape (example):
```json
{
  "envelope": {
    "correlationId": "52ab8f...",
    "traceId": "00-...",
    "tenantId": "tenant-acme",
    "sourceService": "gateway",
    "metadata": {
      "eventType": "outage.created",
      "severity": "critical",
      "retry-count": "0"
    },
    "payload": {
      "accountId": "tenant-acme",
      "outageId": "...",
      "priority": "critical",
      "region": "us-east"
    },
    "schemaVersion": "1.0.0"
  },
  "publishedTopic": "outage.events",
  "downstreamTopics": ["notification.events", "billing.events", "analytics.events"]
}
```

### 6) Expected event outputs
- **Kafka topic output**: one `outage.created` envelope on `outage.events`.
- **Downstream fan-out expectation**: notification, billing, and analytics consumers process the same correlation id.
- **MCP trace output**: `trace_correlation` returns ordered hops after events are processed.

## GitHub setup + first stable baseline

Create and push to a new GitHub repo (example: `mcp-kafka-outage-demo`):
```bash
git remote add origin git@github.com:<your-user>/mcp-kafka-outage-demo.git
git push -u origin main
```

Tag the first stable baseline:
```bash
git tag -a v0.1.0 -m "First stable baseline"
git push origin v0.1.0
```

## CI
GitHub Actions workflow at `.github/workflows/ci.yml`:
- restore/build all .NET projects
- lint via `dotnet format --verify-no-changes`
- run unit tests via `dotnet test`
