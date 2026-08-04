# Design: CustomSamplingTelemetryProcessor is dead code — align docs and code

No UI surface — this is a backend dead-code removal plus a documentation correction. UX/UI section omitted.

## Component design

This change touches exactly two components; nothing new is introduced.

### 1. `CustomSamplingTelemetryProcessor` (delete)

- **File**: `backend/src/Anela.Heblo.API/Infrastructure/Telemetry/CustomSamplingTelemetryProcessor.cs`
- **Action**: delete the file outright.
- **Verified blast radius**: repo-wide grep for `CustomSamplingTelemetryProcessor` (excluding `.git` and this task's own artifacts) returns exactly three hits — the class's own definition/constructor, and the two lines in `observability.md` covered by change #2 below. No DI registration (`AddApplicationInsightsTelemetryProcessor<CustomSamplingTelemetryProcessor>()` is never called), no test file, no other code reference. Deletion is a pure subtraction with no follow-on edits required elsewhere in `ApplicationInsightsExtensions.cs` or the DI container.
- **Why not register it instead**: the real pipeline already satisfies the doc's stated "~60-70% savings" goal via `CostOptimizedTelemetryProcessor` (pre-sampling filtering) + Application Insights adaptive sampling (`EnableAdaptiveSampling = true`, `UseAdaptiveSampling(maxTelemetryItemsPerSecond: 5 prod / 1 non-prod, excludedTypes: "Exception;Event")`, plus `UseSampling(10)` fallback in non-prod) — see `ApplicationInsightsExtensions.cs:29,81-89`. Stacking a second, non-thread-safe (shared instance `Random`, `CustomSamplingTelemetryProcessor.cs:13`) fixed-rate sampler on top would change production telemetry volume/cost behavior with no product need driving it, and would need a thread-safety fix (e.g. `Random.Shared`) plus verification of the combined sampling math — out of proportion to what this finding asks for. Confirmed: delete, don't wire up.

### 2. `docs/architecture/observability.md` (correct)

Two spots reference the deleted class; both are rewritten to describe the adaptive-sampling mechanism that actually ships. No other part of the document is touched — specifically, the "Environment-Specific Configuration" table (`Max Items/sec` / `Sampling %` columns, ~line 69-72) and the `appsettings.*.json` `SamplingSettings` block it mirrors are **left as-is**: that block is unread by `ApplicationInsightsExtensions.cs` today (confirmed by inspection — only `ApplicationInsights:EnableLiveMetrics` is read via `configuration.GetValue`), which is a distinct doc/code mismatch already flagged in `plan-01.md` as an out-of-scope follow-up. Touching it here would expand this task beyond the registered finding.

**Edit A — "Aggressive Sampling" subsection (currently lines 55-65)**

Before:
```
#### 2. Aggressive Sampling
**CustomSamplingTelemetryProcessor** implements:
- Requests: 30% sampling rate
- Dependencies: 10% sampling rate
- Traces: 5% sampling rate
- **Always tracked (100%)**:
  - Exceptions
  - Custom business events
  - Failed requests
  - Slow requests (> 1s)
  - Failed dependencies
```

After:
```
#### 2. Adaptive Sampling
Application Insights' built-in adaptive sampling (`ApplicationInsightsExtensions.cs`), not a custom
processor, governs volume:
- `EnableAdaptiveSampling = true` on the telemetry configuration.
- `UseAdaptiveSampling(maxTelemetryItemsPerSecond: 5)` in Production, `1` in non-production — the SDK
  dynamically adjusts the sampling rate per telemetry type to hit that target throughput, rather than
  using fixed per-type percentages.
- `excludedTypes: "Exception;Event"` — exceptions and custom business events are always tracked (100%),
  never subject to adaptive sampling.
- Non-production only: an additional flat `UseSampling(10)` fallback keeps 10% of all telemetry on top
  of adaptive sampling.
```

This drops the false precision of fixed per-type rates (Requests 30% / Dependencies 10% / Traces 5%) and the false claim that failed/slow requests and dependencies get special-cased — the adaptive sampler makes no such distinction; only Exception/Event types are exempted.

**Edit B — Implementation Guide snippet (currently line 385-387)**

Before:
```csharp
// Extensions/ApplicationInsightsExtensions.cs
services.AddOptimizedApplicationInsights(configuration, environment);
services.AddApplicationInsightsTelemetryProcessor<CostOptimizedTelemetryProcessor>();
services.AddApplicationInsightsTelemetryProcessor<CustomSamplingTelemetryProcessor>();
```

After:
```csharp
// Extensions/ApplicationInsightsExtensions.cs
services.AddOptimizedApplicationInsights(configuration, environment);
services.AddApplicationInsightsTelemetryProcessor<CostOptimizedTelemetryProcessor>();
```

Also update the "Custom Telemetry Processors" row in the Observability Stack table (line 34) only if its ✅ status is read as claiming `CustomSamplingTelemetryProcessor` specifically — it is not (`CostOptimizedTelemetryProcessor` and the four not-found/conflict filters are genuinely implemented and registered), so **no change needed** there.

## Data schemas

N/A. No entities, DTOs, API contracts, or event payloads are introduced or modified.

## Verification plan

1. `git rm backend/src/Anela.Heblo.API/Infrastructure/Telemetry/CustomSamplingTelemetryProcessor.cs`.
2. Apply Edit A and Edit B to `docs/architecture/observability.md`.
3. `grep -rn CustomSamplingTelemetryProcessor .` (excluding `.git`) → expect zero matches anywhere in the repo.
4. `dotnet build` (backend) → succeeds, no missing-type errors.
5. `dotnet format` (backend) → clean, no diff from the deletion.
6. Manual read-through: `observability.md`'s sampling description now matches `ApplicationInsightsExtensions.cs:29,81-89` line-for-line.
