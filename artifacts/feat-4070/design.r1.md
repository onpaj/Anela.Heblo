# Design: Npgsql connect-path cancellation bursts

## Component Design
- `RetryingHealthCheck : IHealthCheck` (API/HealthChecks). Ctor: `Func<IServiceProvider, IHealthCheck> inner` or inner `IHealthCheck`, `int retries = 1`, `TimeSpan delay = 500ms`, `ILogger`. `CheckHealthAsync`: loop attempts; return on `HealthStatus.Healthy`; catch non-cancellation exceptions as Unhealthy results; if `cancellationToken.IsCancellationRequested` rethrow/stop without retry; delay via `Task.Delay(delay, ct)`.
- Registration: in `AddHealthCheckServices`, replace the direct `AddNpgSql(...)` with a registration of the same Npgsql check wrapped in the decorator, keeping the name `InfrastructureConstants.DATABASE_HEALTH_CHECK` and tags `DB_TAG`, `POSTGRESQL_TAG` so `/health/ready` predicates are unchanged. Set a check timeout (<= 3 s) per attempt.
- `TransientErrorClassifier`: unchanged unless FR-2 tests fail; tests assert `TimeoutRejectedException(inner OperationCanceledException)` => transient, plain caller-cancelled `OperationCanceledException` => not retried by `PollyExecutionStrategy`.
- Docs: KQL query grouping `exceptions` where `outerMethod has "ConnectAsync"` by 15-min bin and `operation_Name`, joined with `DbPoolExhaustionWait`/`DbConnectionFailed` traces by `operation_Id`; decision table (server-side vs pool-side).

## Data Schemas
None (no DB, API or payload changes). Health response JSON shape unchanged.
