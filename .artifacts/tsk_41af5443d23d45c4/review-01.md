# Review: Reconcile frontend feature-flag mirror with backend registry

## Diff reviewed

```
backend/src/Anela.Heblo.API/appsettings.json                                       |  3 -
backend/test/.../FeatureFlags/FeatureFlagRegistryFrontendMirrorTests.cs (new)      | 84 ++++
frontend/src/features/feature-flags/featureFlags.ts                               |  3 -
```

## Conformance to spec (plan-01.md / design-01.md / architecture-01.md)

- **FR-1** (remove orphaned frontend keys): done — `TransportBoxTracking`, `StockTaking`, `BackgroundRefresh` deleted from `FeatureFlagKeys`; only `LabelPrinting` remains, matching `FeatureFlagRegistry.All`.
- **FR-2** (prune matching `appsettings.json` entries): done — same three keys removed from the base `appsettings.json` `FeatureManagement` section. Confirmed no other appsettings overlay (`Staging`/`Test`/`Conductor`/`Development`/`Production`) carried these keys, so no further pruning was needed.
- **FR-3** (no speculative frontend mirror for `DeliveredOrderCompletion*`): correctly left as a non-change; confirmed via repo-wide grep that neither key is referenced anywhere in `frontend/src`.
- **FR-4** (drift guard test): `FrontendMirror_AllKeys_ExistInBackendRegistry` implemented — regex-extracts `featureFlags.ts`'s `FeatureFlagKeys` values and asserts they're a subset of `FeatureFlagRegistry.All`. Matches the file-walk-to-repo-root technique design called out from `LocalizationCoverageTests`.
- **FR-5** (stretch — appsettings exact-match): implemented as `AppSettings_FeatureManagement_ExactlyMatchesBackendRegistry`, scoped to the base `appsettings.json` only, as the plan required if attempted.
- Out-of-scope items (codegen, DB override cleanup, stale planning doc) correctly left untouched.

## Correctness / verification performed in this review

- `dotnet test --filter FullyQualifiedName~FeatureFlagRegistryFrontendMirrorTests` → **2/2 pass**.
- `dotnet format test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes --include <new file>` → clean, no formatting diffs.
- Repo-wide grep for the three removed kebab-case keys (`is-transport-box-tracking-enabled`, `is-stock-taking-enabled`, `is-background-refresh-enabled`) across `*.ts/*.tsx/*.json/*.cs` (excluding `node_modules`/`bin`/`obj`) → zero remaining references.
- Repo-wide grep for `FeatureFlagKeys.` usage in frontend `src` → no call sites at all (consistent with the plan's claim these were dead mirrors).
- Checked all backend `appsettings*.json` files for a `FeatureManagement` section — only the base file and `appsettings.Staging.json` have one; `Staging.json` already only contained `is-label-printing-enabled` and needed no change, matching the plan's assumption.
- `npx tsc --noEmit` shows pre-existing `react-i18next`/TypeScript version-mismatch errors unrelated to this change (matches the dev notes' documented pre-existing `npm ci` peer-conflict issue); not caused by this diff, and `npm run build`/`npm run lint` (per development-01.md) were reported clean for the touched files.
- Test logic itself is sound: subset check for the frontend mirror, exact-set equality for `appsettings.json` vs. registry, both keyed off `FeatureFlagRegistry.All` as the single source of truth — correctly modeling the two failure directions (orphaned mirror vs. orphaned appsettings default).

## Assessment

No functional requirement is unmet, the implementation matches the approved architecture with no deviations, the required regression test was added and passes, and no correctness issues were found. Changes are minimal and surgical (six lines of deletion + one new test file), matching the "surgical changes" project convention.

## Outcome: done
