# Development: CustomSamplingTelemetryProcessor is dead code — align docs and code

Implemented plan-01.md / design-01.md exactly as approved by architecture-01.md — no deviations, no scope changes.

## Changes

1. **Deleted** `backend/src/Anela.Heblo.API/Infrastructure/Telemetry/CustomSamplingTelemetryProcessor.cs`
   - Confirmed via `git status` that this is a clean removal (staged as `deleted:`).
   - It was never registered via `AddApplicationInsightsTelemetryProcessor<T>()`, had no DI wiring, and no test referenced it — deletion has zero blast radius beyond the file itself.

2. **Edited** `docs/architecture/observability.md`:
   - **Edit A** (was lines 55-65, "Aggressive Sampling"): replaced the fictional fixed per-type sampling rates (Requests 30% / Dependencies 10% / Traces 5%, always-track failed/slow requests & dependencies) with a description of the adaptive-sampling mechanism that actually runs — `EnableAdaptiveSampling = true`, `UseAdaptiveSampling(maxTelemetryItemsPerSecond: 5 prod / 1 non-prod, excludedTypes: "Exception;Event")`, plus the non-production `UseSampling(10)` flat fallback — matching `ApplicationInsightsExtensions.cs:29,81-89` line-for-line.
   - **Edit B** (was line 387, Implementation Guide code snippet): removed the dead `services.AddApplicationInsightsTelemetryProcessor<CustomSamplingTelemetryProcessor>();` registration line, leaving only the real, active `CostOptimizedTelemetryProcessor` registration.
   - Per design-01.md, the "Environment-Specific Configuration" table (~line 69-72) and the `appsettings.*.json` `SamplingSettings` block were **left untouched** — that's a distinct, already-flagged doc/code mismatch (unread config), explicitly out of scope for this task per plan-01.md's "Dependencies and scope" section.

No other files were touched. `ApplicationInsightsExtensions.cs` (the real adaptive-sampling config) was not modified, as specified — this is a dead-code removal and doc correction only, not a telemetry-pipeline change.

## Verification performed

- `grep -rn "CustomSamplingTelemetryProcessor" backend docs --include="*.cs" --include="*.md"` → **zero matches** (exit code 1). Confirms FR-3 (no remaining references anywhere in the repo).
- `dotnet build` (full solution, `Anela.Heblo.sln`) → **0 errors**, 250 pre-existing warnings (all nullable-reference warnings in test files, unrelated to this change — no new warnings introduced). Confirms FR-1's acceptance criterion (no missing-reference errors after deletion).
- `dotnet format --verify-no-changes` (full solution) → **exit code 0**, no formatting diffs. Confirms the plan's non-functional requirement that `dotnet format` passes cleanly.
- Manual diff review of `observability.md`: confirmed the new "Adaptive Sampling" section matches `ApplicationInsightsExtensions.cs:29,81-89`'s actual configuration values (5 items/sec prod, 1 item/sec non-prod, `Exception;Event` exclusion, 10% non-prod fallback) — no fabricated numbers.
- No test changes were needed: confirmed (both before and after deletion) that no test file in `backend/test/` references `CustomSamplingTelemetryProcessor` by name or by file path — there was no test coverage for the dead class to remove.

## How to verify

```bash
cd backend
export PATH="$HOME/.dotnet:$PATH"   # if dotnet isn't already on PATH
cd ..
dotnet build                         # expect: 0 Error(s)
dotnet format --verify-no-changes    # expect: exit code 0
grep -rn "CustomSamplingTelemetryProcessor" backend docs --include="*.cs" --include="*.md"   # expect: no output
git diff docs/architecture/observability.md   # review the two edits
```

## Files changed

- `backend/src/Anela.Heblo.API/Infrastructure/Telemetry/CustomSamplingTelemetryProcessor.cs` — deleted
- `docs/architecture/observability.md` — corrected (2 edits, 11 insertions / 23 deletions net)
