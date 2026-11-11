# Coverage gap — `backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/PollyExecutionStrategy.cs`

**Line coverage:** 19.4% | **CI run:** 41210660028

## What is untested

No test file exists for `PollyExecutionStrategy`.  Both `Execute` and `ExecuteAsync` share identical branching, neither path exercised:

1. **`if (attempt > 1)` branch** — on a successful retry (attempt ≥ 2), records a retry-success metric.  If this branch is broken, every successful retry silently goes unrecorded, making retry dashboards misleading.
2. **Exception catch** — enriches the exception with `ex.Data["Anela.DbRetryAttempts"]` and calls `_metrics.RecordRetryFailure`.  If either side effect is wrong, on-call engineers see exceptions with no retry context and the failure metric is missing.

```csharp
if (attempt > 1)
    _metrics.RecordRetrySuccess(attempt);

catch (Exception ex)
{
    ex.Data["Anela.DbRetryAttempts"] = attempt;
    _metrics.RecordRetryFailure(attempt);
    throw;
}
```

## Suggested tests

1. Single-attempt success — verify no retry metric is recorded.
2. Multi-attempt success (attempt = 2) — verify `RecordRetrySuccess` is called with the correct attempt count.
3. Exception on attempt N — verify `ex.Data["Anela.DbRetryAttempts"]` is set, `RecordRetryFailure` is called, and the exception is re-thrown.
