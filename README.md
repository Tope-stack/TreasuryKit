# TreasuryKit

Treasury tooling for African businesses operating across borders. Phase 1 focuses on multi-bank statement reconciliation. See [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).

## Run locally

Prerequisites: .NET SDK 10.0.400+, Docker.

```bash
docker compose up -d                                   # PostgreSQL on localhost:5433
dotnet run --project src/Host/TreasuryKit.Api          # http://localhost:5080 (applies migrations)
dotnet run --project src/Host/TreasuryKit.Worker       # outbox dispatcher
```

- Health: `GET /health`
- API reference (Development): http://localhost:5080/scalar
- Get a token (Development only):

```bash
curl -s -X POST localhost:5080/dev/tenants -H 'content-type: application/json' -d '{"name":"Acme Ltd"}'
curl -s -X POST localhost:5080/dev/token -H 'content-type: application/json' \
  -d '{"tenantId":"<id>","userId":"alice","roles":["Preparer"]}'
```

## Test

```bash
dotnet test   # integration tests start their own PostgreSQL container
```
