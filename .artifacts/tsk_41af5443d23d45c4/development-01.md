# Development: Reconcile frontend feature-flag mirror with backend registry

## Summary

Implemented exactly what plan-01.md / design-01.md / architecture-01.md specified, with no deviations:

1. Deleted the 3 orphaned frontend flag keys (`TransportBoxTracking`, `StockTaking`, `BackgroundRefresh`) from `FeatureFlagKeys`, leaving only `LabelPrinting`.
2. Deleted the matching 3 orphaned entries from the base `appsettings.json` `FeatureManagement` section.
3. Added a new backend test file with two guard tests: the FR-4 subset check (frontend ⊆ registry) and the FR-5 stretch goal (`appsettings.json` `FeatureManagement` exact-match against the registry), since FR-5 turned out cheap to add in the same PR as anticipated by architecture-01.md.
4. Did **not** add frontend mirrors for `DeliveredOrderCompletion` / `DeliveredOrderCompletionTestSource` (FR-3, deliberate non-change — no UI consumer).

## Files changed

- `frontend/src/features/feature-flags/featureFlags.ts` — removed 3 orphaned constants; `LabelPrinting` and the `FeatureFlagKey` type alias untouched.
- `backend/src/Anela.Heblo.API/appsettings.json` — removed the same 3 keys from `FeatureManagement`. Left the unrelated `"BackgroundRefresh"` top-level config section (line ~309, background-job scheduling config, not a feature flag) untouched — verified it's a distinct, unrelated JSON key before editing.
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/FeatureFlagRegistryFrontendMirrorTests.cs` (new) — two `[Fact]`s:
  - `FrontendMirror_AllKeys_ExistInBackendRegistry` — reads `featureFlags.ts` as text (walk-up-to-repo-root technique from `LocalizationCoverageTests`, regex-extracts the `FeatureFlagKeys` object literal's string values), asserts every value exists in `FeatureFlagRegistry.All`.
  - `AppSettings_FeatureManagement_ExactlyMatchesBackendRegistry` — reads the base `appsettings.json` (same walk-up technique, `JsonDocument.Parse` with `CommentHandling.Skip`/`AllowTrailingCommas` since the file has inline `//` comments), asserts the `FeatureManagement` key set is exactly `FeatureFlagRegistry.All`'s key set (no more, no fewer).

## Verification performed

- Repo-wide grep for the three removed kebab-case flag strings and `FeatureFlagKeys.TransportBoxTracking|StockTaking|BackgroundRefresh` — zero live references outside build output (`bin/`, rebuilt on next build).
- `dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — succeeds, 0 errors (pre-existing warnings only, unrelated to this change).
- `dotnet test --filter FullyQualifiedName~FeatureFlagRegistryFrontendMirrorTests` — 2/2 pass.
- `dotnet test --filter FullyQualifiedName~Features.FeatureFlags` — 8/8 pass (new 2 + existing 6 in the same folder, no regression).
- `dotnet format test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --include <new test file>` — clean, no changes needed.
- `npm install --legacy-peer-deps` (fresh `node_modules`, needed since none was present in this checkout) + `npm run lint` — 175 pre-existing errors in unrelated test files (Testing Library rule violations, an import-order issue, etc., none in `featureFlags.ts` or any file touched by this change); confirmed via grep that zero lint findings reference `featureFlags`.
- `CI=false npm run build` — compiles successfully, bundle emitted.

### Pre-existing, unrelated issues observed (not touched)

- `npm ci`/`npm install` fails without `--legacy-peer-deps` due to a `react-i18next` (wants TypeScript 5) vs. root `typescript@^4.9.5` peer conflict — pre-existing in `package.json`, unrelated to this task.
- The backend build's `AccessMatrixGen` pre-build step throws a `JsonException` ("'/' is an invalid start of a value") reading `access-matrix.json`'s JSONC comments in this environment, but the build step itself completes ("Access matrix generation completed") and the overall build/test succeeds — confirmed via `git status` that this is not caused by any file this task touches.

## How to verify

```bash
# Backend
cd backend
dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.FeatureFlags"

# Frontend
cd frontend
npm install --legacy-peer-deps   # only needed if node_modules is missing
npm run lint
npm run build
```

To confirm the new test actually catches drift (not just passes vacuously), temporarily re-add one of the deleted `FeatureFlagKeys` entries (e.g. `StockTaking: "is-stock-taking-enabled"`) and re-run the filtered test — `FrontendMirror_AllKeys_ExistInBackendRegistry` fails with a clear message naming the orphaned mirror. Revert before committing.
