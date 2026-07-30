# Architecture assessment: reconcile frontend feature-flag mirror with backend registry

## Verdict

**Approved as designed, no changes required.** This is a same-shape deletion
(3 frontend consts, 3 appsettings lines) plus one new backend unit test. I
independently re-verified every factual claim the plan and design make
against the current repo state (not just the diffs shown in those docs) —
all checked out exactly. Nothing here conflicts with an existing invariant,
module boundary, or documented convention.

## What I verified against the live codebase

| Claim in plan/design | Verified against | Result |
|---|---|---|
| Frontend has 4 keys, only `LabelPrinting` matches the registry | `frontend/src/features/feature-flags/featureFlags.ts` | Confirmed byte-for-byte |
| Registry (`FeatureFlagRegistry.All`) has exactly 3 entries | `FeatureFlagRegistry.cs` + `FeatureFlagKeys.cs` | Confirmed — `DeliveredOrderCompletion`, `DeliveredOrderCompletionTestSource`, `LabelPrintingEnabled` |
| `appsettings.json` base file has the 3 orphaned keys + the 3 real ones (6 total) | `backend/src/Anela.Heblo.API/appsettings.json:2-9` | Confirmed |
| `appsettings.Staging.json` only carries `is-label-printing-enabled`, no orphans | `appsettings.Staging.json:2-4` | Confirmed |
| `appsettings.Test.json` / `.Conductor.json` / `.Development.json` / `.Production.json` have no `FeatureManagement` section at all | grepped all four | Confirmed — makes FR-5's "exact match on base file only" assumption safe; no overlay carries orphaned keys either |
| The 3 orphaned keys (`FeatureFlagKeys.TransportBoxTracking` / `.StockTaking` / `.BackgroundRefresh`, and their string values) are unreferenced anywhere outside `featureFlags.ts` and `appsettings.json` | repo-wide grep for the exact constant-access pattern and exact kebab-case strings | Confirmed zero live call sites (only hits: the two files being edited, one historical planning doc, and this task's own artifacts) |
| `EvaluateFlagsForClientHandler` builds its response purely from `FeatureFlagRegistry.All` | `EvaluateFlagsForClientHandler.cs` | Confirmed — iterates `FeatureFlagRegistry.All`, zero coupling to appsettings orphans or frontend file |
| The two precedent test patterns the design cites exist and work as described | `LocalizationCoverageTests.cs` (walk-up-from-assembly-location technique), `AccessMatrixJsonTests.cs` (fixed `../../../../../..` relative path technique) | Both confirmed present, both confirmed to match the design's description of them |
| `FeatureFlagsControllerLintTests.cs` is the existing sibling for the new test's location | `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/` | Confirmed — folder already holds `FeatureFlagsControllerLintTests.cs` and `HebloFeatureProviderTests.cs`; new file fits the existing namespace (`Anela.Heblo.Tests.Features.FeatureFlags`) |
| `docs/development/feature-flags.md` lifecycle rules match what plan/design cite | Read in full | Confirmed — "Flag lifecycle" section (delete constant → registry entry → appsettings line → DB override → call sites) and "Step 3 — Mirror in frontend" both match |
| `FeatureFlagDefinition` is a record, not touched by this change | `FeatureFlagDefinition.cs` | Confirmed record, but it's an internal application type never serialized through an OpenAPI DTO — doesn't trigger the project's "DTOs must be classes" rule, and this task doesn't touch it anyway |

No discrepancy found between what the plan/design assert and what the code actually does.

## Alignment with existing patterns

- **Test technique choice (walk-up vs. fixed relative path).** Both patterns are already live in the test suite (`LocalizationCoverageTests` walks up; `AccessMatrixJsonTests` uses a fixed 6-level `../..` chain). The design picked walk-up and gave a reason (resilience to build-output path changes). Either would have been acceptable since both are proven; no objection to the choice made.
- **Regex-based TS extraction instead of a TS parser.** Consistent with `LocalizationCoverageTests`, which already reads `frontend/src/i18n.ts` as plain text with `Regex.IsMatch`. Keeps the backend test suite free of a TS toolchain dependency. Correct call.
- **One-directional subset assertion (frontend ⊆ registry), not exact-match.** Correct given `FeatureFlagsController`'s existing asymmetry: backend-only flags (`DeliveredOrderCompletion*`) are a legitimate steady state with no UI, same as how `IFeatureFlagChecker`/`[FeatureGate]` are consumed purely server-side today. An exact-match test here would force speculative frontend mirrors for job-control flags, which is the same anti-pattern this task removes, just inverted.
- **appsettings scope for FR-5 (base file only, no overlay handling).** Verified safe — none of the 5 environment overlay files declare a `FeatureManagement` section, so there's no overlay-inheritance semantics to reason about. FR-5 can do a straight exact-match on the base file with zero risk of a false positive from environment-specific subsetting.
- **`FeatureFlagKeys` naming vs. mirror value.** Frontend key name `LabelPrinting` vs. backend constant name `LabelPrintingEnabled` already differ today (only the string *value* has to match, not the identifier) — the design correctly treats value-matching, not name-matching, as the invariant. No change needed to align this; it's already the existing convention (see `docs/development/feature-flags.md` Step 3 example, which also doesn't require identical names).

## Implementation guidance (confirms design, no additions needed)

1. `frontend/src/features/feature-flags/featureFlags.ts` — delete 3 lines, leave `LabelPrinting` and the `FeatureFlagKey` type alias untouched. Exactly as designed.
2. `backend/src/Anela.Heblo.API/appsettings.json` — delete the same 3 keys from `FeatureManagement`. Exactly as designed. Do not touch any of the 5 overlay files — none of them have the orphaned keys.
3. New test file `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/FeatureFlagRegistryFrontendMirrorTests.cs`, namespace `Anela.Heblo.Tests.Features.FeatureFlags`, following the `LocalizationCoverageTests` walk-up-to-repo-root technique, targeting `frontend/src/features/feature-flags/featureFlags.ts`. Assert frontend values ⊆ `FeatureFlagRegistry.All.Select(d => d.Key)`.
4. FR-5 (appsettings exact-match test) may be added in the same PR if cheap — confirmed safe in scope (base file only, `System.Text.Json`, no overlay logic needed) — but is a stretch goal, not a blocker.
5. Order of operations for a clean bisectable history: land the test first against `main` (should fail, proving it catches the real drift), then land the deletions in the same PR (test goes green). This is a one-PR change; sequencing only matters for the commit-by-commit narrative if the developer wants to demonstrate the test actually catches the bug it's meant to catch — optional, not required by any project rule.

## Risks and mitigations

- **Risk: an override row exists in the `FeatureFlagOverrides` Postgres table for one of the 3 removed keys.** Not a build/test risk (the plan already scopes DB-row cleanup out of this PR, correctly — orphaned override rows for unregistered keys are inert, nothing reads them via `FeatureFlagRegistry.ByKey`-keyed lookups). Mitigation already captured in the plan: flag it for a manual dev/staging DB check, don't block the PR on it. No architectural change needed.
- **Risk: regex extraction in the new test is fragile to formatting changes in `featureFlags.ts`.** Low — the file is small, single-purpose, and the existing `LocalizationCoverageTests` precedent has apparently been stable enough to not need parser-level robustness. If `featureFlags.ts` ever grows multi-line values or nested objects, the regex will need revisiting, but that's speculative and out of scope now.
- **Risk: none of the 5 environment overlay appsettings files were checked before this review — could FR-5 introduce a false failure in CI for an environment this review didn't inspect.** Mitigated by verification performed in this step: all 5 overlays checked directly, none carry a `FeatureManagement` section, so FR-5's base-file-only exact-match is safe as designed.

## Prerequisites before implementation begins

None outstanding. All assumptions the plan and design relied on (zero live call sites, appsettings overlay shape, precedent test patterns, handler behavior) are now independently confirmed against the current `main` state of the repo. Implementation can proceed directly per the design's before/after diffs.
