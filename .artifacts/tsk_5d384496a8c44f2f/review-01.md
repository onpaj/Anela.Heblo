# Review: CustomSamplingTelemetryProcessor dead-code cleanup

## Verdict: done

## What was checked

Re-verified the implementation independently against plan-01.md / design-01.md / architecture-01.md and against the finding itself, not just trusting the prior steps' self-reports.

- **Diff matches the approved design exactly.** `git show HEAD` touches only two files: deletion of `backend/src/Anela.Heblo.API/Infrastructure/Telemetry/CustomSamplingTelemetryProcessor.cs` and a two-part edit to `docs/architecture/observability.md` (Edit A: "Aggressive Sampling" → "Adaptive Sampling" section; Edit B: drop the dead registration line from the Implementation Guide snippet). No unrelated files touched, no scope creep beyond what plan-01.md/design-01.md specified.
- **File is actually gone**: `ls backend/src/Anela.Heblo.API/Infrastructure/Telemetry/` no longer lists `CustomSamplingTelemetryProcessor.cs`.
- **Zero remaining references**: `grep -rn "CustomSamplingTelemetryProcessor" . --include="*.cs" --include="*.md"` (excluding `.artifacts/`) returns nothing. Satisfies FR-2 and FR-3 of plan-01.md.
- **Doc now matches code line-for-line**: read `ApplicationInsightsExtensions.cs:29` (`EnableAdaptiveSampling = true`) and `:81-89` (`UseAdaptiveSampling(maxTelemetryItemsPerSecond: environment.IsProduction() ? 5 : 1, excludedTypes: "Exception;Event")` plus non-production `UseSampling(10)` fallback) directly — the new "Adaptive Sampling" section in `observability.md` states these exact values (5 prod / 1 non-prod, `Exception;Event` exclusion, 10% non-prod fallback) with no fabricated numbers.
- **`dotnet build Anela.Heblo.sln`** (full solution, run fresh in this review, not reused from development-01.md's claim): **0 errors**, 250 pre-existing nullable-reference warnings in test files only — no new warnings, no missing-reference errors from the deletion.
- **`dotnet format Anela.Heblo.sln --verify-no-changes`**: exit code 0, clean.

## Assessment against the finding

The original finding asked to resolve the contradiction between the dead `CustomSamplingTelemetryProcessor` class and `observability.md`'s claim that it's the active sampling strategy. The chosen resolution (delete the unregistered class, correct the doc to describe the real adaptive-sampling mechanism) is well-justified in plan-01.md/design-01.md: registering it instead would require fixing a non-thread-safe shared `Random` and would change production telemetry volume with no product driver, for a decoy that duplicates work adaptive sampling already does. This is the lower-risk, correctly-scoped choice and matches the project's "surgical changes" convention.

The unrelated `SamplingSettings` config-table mismatch was correctly identified and deliberately left out of scope as a separate follow-up, avoiding scope creep.

No functional requirement is unmet, no architecture conflict, no missing tests (none existed for the dead class), no correctness bug in the change itself.

```json
{"outcome": "done", "summary": "Verified independently: CustomSamplingTelemetryProcessor.cs is deleted, zero repo-wide references remain, observability.md's new Adaptive Sampling section matches ApplicationInsightsExtensions.cs line-for-line, dotnet build succeeds with 0 errors, and dotnet format --verify-no-changes is clean. Diff is scoped exactly to plan-01.md/design-01.md with no scope creep. Approved."}
```
