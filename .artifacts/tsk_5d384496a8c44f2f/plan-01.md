# Plan: CustomSamplingTelemetryProcessor is dead code — align docs and code

## Summary

`CustomSamplingTelemetryProcessor` is a fully-implemented but never-registered `ITelemetryProcessor`.
`docs/architecture/observability.md` documents it as the live sampling strategy (per-type fixed rates),
but production sampling is actually governed by Application Insights adaptive sampling configured in
`ApplicationInsightsExtensions.cs`. This plan removes the dead class and corrects the doc to describe
what actually ships, rather than wiring up unused code.

## Context

Two sampling mechanisms exist side by side: the real one (`EnableAdaptiveSampling` +
`UseAdaptiveSampling(...)` + `UseSampling(10)` fallback in non-prod, `ApplicationInsightsExtensions.cs:29,81-89`)
and a decoy one (`CustomSamplingTelemetryProcessor`, never passed to
`AddApplicationInsightsTelemetryProcessor<T>()`). `observability.md` documents only the decoy's rates
(Requests 30%, Dependencies 10%, Traces 5%) as the active cost-control strategy, which is wrong and
would mislead anyone reasoning about telemetry volume/cost or "completing" the class by wiring it up.

**Decision: delete the dead class, correct the doc.** Registering it instead would stack a second,
non-thread-safe (shared `Random`, `CustomSamplingTelemetryProcessor.cs:13`) sampling layer on top of
adaptive sampling that is already meeting the documented "~60-70% savings" goal, for no stated product
need, in a solo-maintained codebase where surgical/minimal-blast-radius changes are the stated norm.
Fixing thread-safety and activating a second sampling layer would be a behavior change to production
telemetry volume with no requester driving it — out of proportion to what this finding calls for.

## Functional requirements

- **FR-1**: Delete `backend/src/Anela.Heblo.API/Infrastructure/Telemetry/CustomSamplingTelemetryProcessor.cs`.
  - Acceptance: file no longer exists; `dotnet build` succeeds with no missing-reference errors.
- **FR-2**: Update `docs/architecture/observability.md` so it describes only the sampling mechanism that
  actually runs:
  - Rewrite `#### 2. Aggressive Sampling` (lines 55-65) to describe adaptive sampling
    (`UseAdaptiveSampling(maxTelemetryItemsPerSecond: 5 prod / 1 non-prod, excludedTypes: "Exception;Event")`
    plus the `UseSampling(10)` fixed-rate fallback applied only in non-production), not fixed per-type
    percentages.
  - Remove the `services.AddApplicationInsightsTelemetryProcessor<CustomSamplingTelemetryProcessor>();`
    line from the Implementation Guide code block (line 387).
  - Acceptance: `grep -rn CustomSamplingTelemetryProcessor docs/` returns no matches; the doc's described
    behavior matches `ApplicationInsightsExtensions.cs` line-for-line for sampling.
- **FR-3**: Confirm no other code or docs reference the removed class.
  - Acceptance: `grep -rn CustomSamplingTelemetryProcessor .` (excluding `.git`) returns no matches anywhere
    in the repo after the change.

## Non-functional requirements

- No production behavior change — this is a dead-code removal plus a documentation correction, not a
  telemetry-pipeline change. `ApplicationInsightsExtensions.cs` itself is not touched.
- `dotnet build` and `dotnet format` must pass per repo validation rules.

## Data model

N/A — no entities, no persistence involved.

## Interfaces

N/A — no API/endpoint/UI surface. Touches one backend source file (deleted) and one architecture doc.

## Dependencies and scope

- Depends on / touches: `backend/src/Anela.Heblo.API/Infrastructure/Telemetry/CustomSamplingTelemetryProcessor.cs`
  (delete), `docs/architecture/observability.md` (correct).
- Explicitly untouched: `ApplicationInsightsExtensions.cs` (the real adaptive-sampling config stays as-is),
  `CostOptimizedTelemetryProcessor` and the four filter processors (already correctly documented and wired).
- Out of scope: `appsettings.Production.json` / `appsettings.Staging.json` contain a `SamplingSettings`
  block (`InitialSamplingPercentage`, `MinSamplingPercentage`, `MaxSamplingPercentage`, etc.) that
  `ApplicationInsightsExtensions.cs` does not appear to read at all — it hardcodes
  `maxTelemetryItemsPerSecond` instead. That is a second, separate potential doc/code mismatch; noted
  here but not addressed by this task to avoid scope creep. Worth a follow-up arch-review finding.

## Rough plan

1. Delete `CustomSamplingTelemetryProcessor.cs`.
2. Edit `observability.md`:
   a. Rewrite the "Aggressive Sampling" subsection to describe the real adaptive-sampling configuration
      and its actual per-environment numbers (prod: 5 items/sec adaptive, excluding Exception/Event;
      non-prod: 1 item/sec adaptive + flat 10% fallback).
   b. Drop the `CustomSamplingTelemetryProcessor` registration line from the Implementation Guide snippet.
3. Grep the repo to confirm zero remaining references to the deleted class name.
4. Run `dotnet build` and `dotnet format` on the backend to confirm the removal is clean.
5. No tests exist for this class (confirmed — no test file references it), so no test changes needed.

## Open questions

- Whether the `SamplingSettings` values in `appsettings.Production.json` / `appsettings.Staging.json`
  are genuinely dead config (unread by code) is left as a follow-up observation, not resolved here —
  default: out of scope for this task, flag separately if confirmed.
