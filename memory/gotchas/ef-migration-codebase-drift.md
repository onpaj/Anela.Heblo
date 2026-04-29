# EF Core migration / codebase drift causes 42P01 in production

## Symptom

`GET /api/data-quality/runs` returns HTTP 500 at multiples of baseline rate. Application logs show:

```
Npgsql.PostgresException: 42P01: relation "dqt_runs" does not exist
```

Slow-request telemetry on the same endpoint shows latencies in the 3,000–4,000 ms range (timeout-adjacent), suggesting the query never reaches the row-fetch phase.

## Root cause class

Drift between the deployed application image's EF Core mapping and the physical schema in the production PostgreSQL database. In this repo, database migrations are **manual** — they are not applied by the deployment pipeline. Two failure modes are possible:

1. The dependent migration was never applied to production while the dependent application code was deployed.
2. The migration was applied but one or more Azure App Service container instances continued serving an older image whose mapping referenced the pre-rename table name.

Either mode produces the same `42P01` symptom.

## Concrete instance

- Migration `20260424060451_AddDataQualityTables` created the table as `dqt_runs` (snake_case).
- Migration `20260424142720_StandardizeTableNamingToPascalCase` renamed it to `DqtRuns` (PascalCase).
- The deployed `DqtRunConfiguration` maps to `DqtRuns`. Production was observed to either (a) not have applied the rename, or (b) be serving stale application instances with the pre-rename mapping.

## Diagnostic procedure

Use the read-only SQL pair documented in `docs/development/setup.md` under "Diagnostic SQL for suspected schema drift". The pair queries `__EFMigrationsHistory` and `information_schema.tables` to determine which of three states (A: code/DB consistent, B: migration unapplied, C: anomalous) actually holds.

## Durable safeguard

`DataQualitySchemaHealthCheck` is registered under `/health/ready` with the `ready`, `db`, and `schema` tags. It executes `_db.DqtRuns.AsNoTracking().AnyAsync(ct)` on every readiness poll. If the relation is missing (Postgres SQLSTATE `42P01`), the check returns `HealthStatus.Unhealthy` with structured `data` (`entity`, `expectedTable`, `schema`, `sqlState`), Azure App Service removes the instance from rotation, and on-call sees a structured drift signal instead of a 500 spike.

## Known limitation of the safeguard

The probe currently covers `DqtRuns` only. It does NOT cover the other tables touched by `StandardizeTableNamingToPascalCase`. Broader coverage is tracked as a follow-up; do not assume the probe protects against drift on any other entity.

## Operator-filled incident record

Fill in below once FR-1 through FR-4 are complete:

- Diagnosis state (A / B / C):
- Migration apply timestamp (UTC), if any:
- Rolling restart timestamp (UTC), if any:
- Verification window start (UTC):
- Verification window end (UTC):
- Final 500 count on `GET /api/data-quality/runs` during verification window: 0 (required) / actual:
- Final `42P01` count in logs during verification window: 0 (required) / actual:
- Slow-request p95 on the endpoint during verification window (target < 1,000 ms):
- Pre-restart container image digest:
- Image digest verified to match the commit containing `DqtRunConfiguration.ToTable("DqtRuns", "public")`: yes / no
- Telemetry / dashboard links (no credentials):

## Lesson

Manual migrations + traffic-serving deployments require an explicit deploy-time gate, not a runtime gate. The readiness-probe safeguard is the minimum correct response: it does not automate migrations (out of scope, larger initiative) but it converts a user-facing 500 into a platform-managed instance removal. The next migration with a similar shape (rename, schema move) MUST be paired with the pre-deploy checklist in `docs/development/setup.md`.
