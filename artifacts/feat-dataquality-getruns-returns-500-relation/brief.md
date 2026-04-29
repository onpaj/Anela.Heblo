## Telemetry

`GET DataQuality/GetRuns` (mapped to `GET /api/data-quality/runs`) is returning HTTP 500 errors at a rate of **8 failures in the last 24h** vs a 7-day baseline of ~1.1/day — a **7× spike**.

The underlying exception is:
```
Npgsql.PostgresException: 42P01: relation "dqt_runs" does not exist
POSITION: 27
```

The endpoint is also appearing in slow-request telemetry (4 hits at avg **3 716 ms**), consistent with query timeouts or error-path latency before the 500 is returned.

## Root Cause

The DataQuality feature was added in migration `20260424060451_AddDataQualityTables`, which creates the table as `dqt_runs` (snake_case) in the public schema. A later migration, `20260424142720_StandardizeTableNamingToPascalCase`, renames it to `DqtRuns` (PascalCase), which is what `DqtRunConfiguration` maps to:

```csharp
// backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunConfiguration.cs
builder.ToTable("DqtRuns", "public");
```

However, production is still generating queries against `dqt_runs`, which suggests one of:
1. **Migration not yet applied on production** — `StandardizeTableNamingToPascalCase` ran in CI/staging but not on the production database, so the table still exists as `dqt_runs` while the deployed code expects `DqtRuns`.
2. **Deployment out of sync** — A previous version of the application code (before the rename) is still being served by some instances that mapped the entity to `dqt_runs`, but the DB has already been renamed.

## Suggested Fix

1. Verify the migration history on the production database:
   ```sql
   SELECT "MigrationId" FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE %DataQuality% OR "MigrationId" LIKE %StandardizeTable%;
   ```
2. If `StandardizeTableNamingToPascalCase` is missing, apply the pending migration.
3. Confirm no old application instances are still running — perform a full rolling restart if needed.
4. If the migration ordering caused any confusion, add an integration smoke test that queries `DqtRuns` via EF Core as part of deployment health checks.