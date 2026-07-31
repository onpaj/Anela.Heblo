# Architecture review: CustomSamplingTelemetryProcessor dead-code cleanup

## Verdict: approved, no changes to plan-01.md / design-01.md required

Re-verified every factual claim in the design against the current working tree (not trusted from the artifacts alone):

- `CustomSamplingTelemetryProcessor.cs` exists exactly as described — per-instance `Random` field (line 13), fixed per-type sampling table (Request 30% / Dependency 10% / Trace 5% / PageView 50%, Event/Exception/Metric 100%), always-track branches for failed/slow requests and dependencies. Matches the design's description of what it does.
- Repo-wide grep for `CustomSamplingTelemetryProcessor` (excluding `.git` and this task's own `.artifacts/`) returns exactly the three hits the design claims: the class's own definition/constructor, and `docs/architecture/observability.md:56` and `:387`. No DI registration, no test file, no other reference anywhere in `backend/` or `frontend/`.
- Confirmed there is no reflection/assembly-scanning registration path for `ITelemetryProcessor` that could pick this class up implicitly — every processor in the pipeline (`CostOptimizedTelemetryProcessor`, the four not-found/conflict filters) is registered explicitly by type in `ApplicationInsightsExtensions.cs`. `CustomSamplingTelemetryProcessor` is absent from that list. Deletion has zero hidden blast radius.
- `ApplicationInsightsExtensions.cs:29,81-89` confirmed line-for-line as quoted: `EnableAdaptiveSampling = true`, `UseAdaptiveSampling(maxTelemetryItemsPerSecond: environment.IsProduction() ? 5 : 1, excludedTypes: "Exception;Event")`, plus non-production `UseSampling(10)` fallback. This is the real, active sampling mechanism.
- `observability.md` lines 55-65 and 386-387 confirmed to still contain the fictional per-type rates and the dead registration line, at the exact line numbers the design targets. Design's Edit A and Edit B apply cleanly against current doc content.
- Namespace check: all sibling files in `Infrastructure/Telemetry/` (`CostOptimizedTelemetryProcessor`, the filters) declare `namespace Anela.Heblo.API.Telemetry;` despite living in an `Infrastructure/Telemetry` folder — `CustomSamplingTelemetryProcessor` follows the same (pre-existing, unrelated) folder/namespace mismatch convention. Not a new issue, not something this change should touch.

## Alignment with codebase invariants

- **Surgical changes** (CLAUDE.md): the design touches exactly the two files implicated by the finding and explicitly declines to touch the unrelated `SamplingSettings`/table mismatch in `appsettings.*.json` and the Environment-Specific Configuration table, flagging it as a separate follow-up instead of scope-creeping. Correct call — that mismatch is a distinct finding with its own blast radius.
- **DTOs are classes, not records**: N/A, no DTOs involved.
- **No architectural changes without consulting docs first**: this task *is* the doc correction; `observability.md` was read in full context before deciding what to change.
- **Decide-one-source-of-truth framing**: plan-01.md correctly frames this as a binary (register vs. delete) and justifies deletion — the adaptive-sampling pipeline already delivers the stated "~60-70% savings" goal, and reviving a second, non-thread-safe fixed-rate sampler on top would be an unrequested production telemetry-volume change. Registering it would also require fixing the shared-`Random` thread-safety bug and re-deriving the combined sampling math with the adaptive sampler — real work with no product driver. Delete-and-document-reality is the lower-risk, correctly-scoped choice for a solo-maintained codebase.
- No test file references the class (confirmed via grep for both class name and its file path across `backend/`), so FR's "no test changes needed" claim holds.

## Risks / residual concerns

None blocking. Two minor notes for the implementer, already correctly deferred by the plan:

1. The `appsettings.Production.json`/`appsettings.Staging.json` `SamplingSettings` block and the doc's "Environment-Specific Configuration" table (lines 67-74) describe a third, apparently-also-unread configuration surface. Confirmed by inspection that `ApplicationInsightsExtensions.cs` never calls `configuration.GetValue`/`GetSection` for anything under `SamplingSettings` — only `ApplicationInsights:EnableLiveMetrics` is read from config for this pipeline. Left out of scope per plan-01.md; agreed this should be a separate arch-review finding rather than folded into this one.
2. After deletion, `dotnet build` and `dotnet format` must still be run as the plan's verification steps 4-5 specify — no reason to expect failures given the confirmed zero inbound references, but this review does not substitute for actually running them during implementation.

No changes requested to plan-01.md or design-01.md. Proceed to implementation as designed.
