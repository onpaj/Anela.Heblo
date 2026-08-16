# Implementation: photobank-schema-drift-health-check

## What was implemented
Added a read-only ASP.NET Core `IHealthCheck` (`PhotobankSchemaHealthCheck`) that detects Postgres
column-type drift on the Photobank tables' `DateTime` columns. It queries
`information_schema.columns` for the tracked `(table, column)` pairs on `Photos`,
`PhotobankIndexRoots`, and `PhotoTags`, compares each actual `data_type` against the expected
`"timestamp without time zone"`, and returns `Unhealthy` with a `driftedColumns` data payload when
any column doesn't match. Registered the check in the existing `AddHealthCheckServices` builder
chain immediately after `DataQualitySchemaHealthCheck`, tagged `ready`, `db`, `schema` — consistent
with the sibling check's tags.

Followed TDD: wrote the failing test first (compile failure — `PhotobankSchemaHealthCheck` did not
exist), then added the implementation and registration, then reran to green.

## Files created/modified
- `backend/src/Anela.Heblo.API/HealthChecks/Photobank/PhotobankSchemaHealthCheck.cs` — new health
  check; short-circuits to `Healthy` when `_db.Database.IsRelational()` is false (in-memory
  provider), otherwise runs the `information_schema.columns` probe via `Database.SqlQuery<T>` and
  reports drift/missing columns as `Unhealthy` data. Cancellation → `Degraded`; unexpected
  exception → `Unhealthy` with the exception attached.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — added
  `using Anela.Heblo.API.HealthChecks.Photobank;` and one `.AddCheck<PhotobankSchemaHealthCheck>(...)`
  entry to the existing `healthChecksBuilder` chain in `AddHealthCheckServices`, matching the
  `DataQualitySchemaHealthCheck` registration's style (name `photobank-schema`, `failureStatus:
  HealthStatus.Unhealthy`, tags `ready`, `db`, `schema`).
- `backend/test/Anela.Heblo.Tests/API/HealthChecks/Photobank/PhotobankSchemaHealthCheckTests.cs` —
  new test asserting the non-relational (in-memory provider) short-circuit path returns `Healthy`
  with the exact skip description.

## Tests
- `PhotobankSchemaHealthCheckTests.CheckHealthAsync_WhenProviderNotRelational_ReturnsHealthyAndSkips`
  — covers the only path testable without a live Postgres connection in this sandbox (same
  limitation the task-context and `DataQualitySchemaHealthCheckTests` note/accept). No local/CI
  Postgres instance was available in this environment, so the relational drift-detected /
  drift-free assertions described as optional in the task context were not added.

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankSchemaHealthCheckTests"
dotnet build Anela.Heblo.sln   # from repo root; note: task context said `backend/Anela.Heblo.sln`,
                                # but the .sln actually lives at the repo root
```
Both ran clean: 1/1 test passed; full solution build succeeded, 0 errors (13 pre-existing warnings
unrelated to this change, including a pre-existing `MSB3073`/exit-134 warning from the
`AccessMatrixGen` prebuild step that was also present before this change).

## Notes
- `Database.SqlQuery<PhotobankColumnTypeRow>` compiled as written against EF Core 8.0.8 with
  `PhotobankColumnTypeRow` as a `private sealed` nested class — the fallback (public top-level class,
  or raw `NpgsqlCommand`) noted in the task context as a contingency was not needed.
- Deviated from the task context's literal build command path (`backend/Anela.Heblo.sln`) only
  because that path doesn't exist in this repo layout — the solution file is at the repo root
  (`Anela.Heblo.sln`). No source changes follow from this; it only affected how verification was
  run.

## PR Summary
Added `PhotobankSchemaHealthCheck`, a read-only health check that flags Postgres column-type drift
on the Photobank tables' `DateTime` columns (`Photos`, `PhotobankIndexRoots`, `PhotoTags`) by
comparing `information_schema.columns` against the expected `timestamp without time zone` type, and
registered it in the existing health-check pipeline alongside `DataQualitySchemaHealthCheck`.

### Changes
- `backend/src/Anela.Heblo.API/HealthChecks/Photobank/PhotobankSchemaHealthCheck.cs` — new health check
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — registers the new check
- `backend/test/Anela.Heblo.Tests/API/HealthChecks/Photobank/PhotobankSchemaHealthCheckTests.cs` — non-relational-provider skip test

## Status
DONE
