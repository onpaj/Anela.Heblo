# Code Review: photobank-schema-drift-health-check

## Summary
The implementation matches the task context's specification verbatim: a read-only `IHealthCheck`
that short-circuits to `Healthy` on non-relational providers and otherwise probes
`information_schema.columns` for drift on the tracked Photobank `DateTime` columns, registered
alongside the sibling `DataQualitySchemaHealthCheck` with matching tags and style. Build and test
verification both pass.

## Review Result: PASS

### task: photobank-schema-drift-health-check
**Status:** PASS

## Docs to Update
(Omit this section entirely if no documentation changes are needed)
- No update required by this task. `docs/development/setup.md`'s post-deployment health-check
  runbook currently documents only `data-quality-schema`; extending it to cover `photobank-schema`
  is explicitly in scope for the separate, still-pending `photobank-drift-runbook-docs` task in this
  feature's task plan, so it is correctly left untouched here.

## Overall Notes
- `PhotobankColumnTypeRow` compiled as a `private sealed` nested class against EF Core 8.0.8's
  `Database.SqlQuery<T>` without needing the public-top-level-class or raw-`NpgsqlCommand` fallback
  the task context anticipated as a contingency.
- Only one test exists (the non-relational-provider skip path), matching the task context's explicit
  acknowledgment that a live-Postgres drift-detection test isn't achievable in this sandbox — the
  same limitation accepted for `DataQualitySchemaHealthCheckTests`' real-DB paths.
- The task context's verification command referenced `backend/Anela.Heblo.sln`, which doesn't exist
  in this repo (the solution file is at the repo root, `Anela.Heblo.sln`); the developer used the
  correct path and both `dotnet test` (1/1 passed) and `dotnet build` (0 errors) succeeded.
