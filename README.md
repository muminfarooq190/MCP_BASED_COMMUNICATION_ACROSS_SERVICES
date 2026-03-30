# MCP Based Communication Across Services

## Monorepo Layout
- `src/gateway` API entrypoint and MCP server tool endpoints.
- `src/mcp-server` dedicated MCP server exposing outage event tools.
- `src/mcp-contracts` shared envelope and event contracts.
- `src/services/outage-service`
- `src/services/notification-service`
- `src/services/billing-service`
- `src/services/analytics-service`
- `src/shared` Kafka, Redis, and observability helpers.
- `deploy/docker-compose` local infra stack.
- `docs/architecture` architecture notes.

## Single Command Flow
1. **Start infra containers**
   ```bash
   docker compose -f deploy/docker-compose/docker-compose.yml up -d
   ```
2. **Run gateway + services** (open one shell per solution)
   ```bash
   dotnet run --project src/gateway/Gateway.Api.csproj
   dotnet run --project src/mcp-server/Mcp.Server.csproj
   dotnet run --project src/services/outage-service/OutageService.Api.csproj
   dotnet run --project src/services/notification-service/NotificationService.Api.csproj
   dotnet run --project src/services/billing-service/BillingService.Api.csproj
   dotnet run --project src/services/analytics-service/AnalyticsService.Api.csproj
   ```
3. **Send one outage request**
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
4. **Observe fan-out events**
   - Inspect gateway response for `FanOutEvents`.
   - POST the returned envelope to each service `/events` endpoint and verify service receipts.
