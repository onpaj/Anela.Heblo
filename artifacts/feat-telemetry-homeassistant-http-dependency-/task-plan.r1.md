Plan saved to `docs/superpowers/plans/2026-06-13-resilient-homeassistant-dependency.md`.

**Summary of the 16-task plan:**

1. **Domain enum** — append `Stale = 4` to `ConditionsReadingSource`.
2. **PDF consumer** — add `Stale => " (Starší údaje)"` switch arm in `ManufactureProtocolDocument`.
3. **Settings knobs** — `RetryCount`, `RetryBaseDelayMilliseconds`, `RetryMaxDelaySeconds`, `StaleSnapshotMaxAgeMinutes`, `LiveSnapshotMaxAgeMinutes`.
4. **NuGet packages** — `Microsoft.Extensions.Http.Resilience` 8.10.0, `Microsoft.Extensions.Diagnostics.HealthChecks` 8.0.0, `Microsoft.ApplicationInsights` 2.22.0.
5. **`HomeAssistantSnapshotCoordinator`** (TDD) — singleton with single-flight gate, last-observed, last-known-good `Live`.
6. **`HomeAssistantSnapshotMetrics`** — `Meter`-backed `homeassistant.snapshot.source` counter.
7. **Transient-error predicate** — centralizes "what counts as retryable".
8. **Retry-activity tagging `DelegatingHandler`** — tags `Activity.Current` with `ha.retry-suppress` on transient send failures.
9. **`HomeAssistantDependencyTelemetryFilter`** (TDD) — `ITelemetryProcessor` that drops tagged dependencies.
10. **`HomeAssistantConditionsHealthCheck`** (TDD) — coordinator-only, never calls HA.
11. **Provider rewrite** (TDD) — single-flight, stale fallback, structured `Information` summary, demoted final-failure warning without exception object.
12. **DI wiring** — `AddResilienceHandler` + `HttpClient.Timeout = Infinite` + tagging handler + singletons.
13. **End-to-end retry pipeline tests** — recovery from one transient, exhausted retries → `Unavailable`.
14. **API health check registration** — tags `{ "homeassistant", "ready" }`.
15. **Conditional AI processor registration** — alongside existing `CostOptimizedTelemetryProcessor`.
16. **Final build / format / test sweep**.

Self-review confirms full FR/NFR coverage, type consistency across tasks (`Coordinator.Gate`, `SuppressTagName`, predicate shape, provider ctor), and no placeholders.