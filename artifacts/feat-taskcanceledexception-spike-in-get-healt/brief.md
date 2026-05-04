## Telemetry

4 occurrences of `System.Threading.Tasks.TaskCanceledException` on `GET /health/ready` in the last 24 hours (2026-05-03 22:08 and 22:17 UTC, two bursts of 2), versus a 7-day daily average of ~0.57/day — approximately 7× the baseline.

All 4 share the same problem ID:
```
System.Threading.Tasks.TaskCanceledException at Npgsql.Internal.NpgsqlWriteBuffer+<Flush>d__38.MoveNext
```

## Root Cause

The `/health/ready` endpoint runs three checks tagged `db` or `ready`:

1. **`AddNpgSql`** — registered with a raw connection string (`AddHealthCheckServices` in `ServiceCollectionExtensions.cs:93`). This opens a *new* Npgsql connection on every probe rather than reusing the application pool. During brief DB load spikes or Azure infrastructure jitter, the new connection's first write (the startup packet flush) may time out. The `AspNetCore.Diagnostics.HealthChecks` library re-throws `OperationCanceledException` rather than catching it, so the exception escapes to App Insights as unhandled.

2. **`DataQualitySchemaHealthCheck`** — queries `_db.DqtRuns` with the passed `cancellationToken`. The `catch (Exception ex)` block should handle this, but if cancellation arrives while Npgsql is mid-flush (before the task returns control to the `await` machinery), the exception can propagate before the `catch` intercepts it.

Both bursts are exactly 9 minutes apart (22:08 and 22:17 UTC), suggesting either a recurring background job putting transient load on the DB, or Azure App Service's health probe retrying after a previous unhealthy response.

## Suggested Fix

1. **Register the DB health check via `NpgsqlDataSource`** instead of a raw connection string. This lets the check reuse the existing pool and avoids the expensive "open new connection" step on every probe:

   ```csharp
   // In AddHealthCheckServices, after registering NpgsqlDataSource in DI:
   healthChecksBuilder.AddNpgSql(sp => sp.GetRequiredService<NpgsqlDataSource>(),
       name: ConfigurationConstants.DATABASE_HEALTH_CHECK,
       tags: new[] { ConfigurationConstants.DB_TAG, ConfigurationConstants.POSTGRESQL_TAG });
   ```

2. **Catch `OperationCanceledException` explicitly** in `DataQualitySchemaHealthCheck` so cancellations return `Degraded` instead of an unhandled exception:

   ```csharp
   catch (OperationCanceledException)
   {
       return HealthCheckResult.Degraded("DataQuality probe was cancelled");
   }
   catch (Exception ex)
   {
       return HealthCheckResult.Unhealthy("DataQuality probe failed", ex);
   }
   ```

3. Consider adding a short explicit timeout (e.g. 5 s) to the health check pipeline so probes fail fast and predictably rather than waiting for the full DB connection timeout.

## Affected Files

- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (line 93 — `AddNpgSql`)
- `backend/src/Anela.Heblo.API/HealthChecks/DataQuality/DataQualitySchemaHealthCheck.cs`