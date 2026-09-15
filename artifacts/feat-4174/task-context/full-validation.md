### task: full-validation

**Files:** none created or modified — this task only runs the project's standard validation commands (per `CLAUDE.md` "Validation before completion") against the whole backend to confirm the new file compiles cleanly, is correctly formatted, and does not break any other test.

- [ ] **Step 1: Full backend build**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 2: Format check**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`

Expected: exits 0 with no reported files. If it reports the new test file, run `dotnet format backend/Anela.Heblo.sln` (without `--verify-no-changes`) to auto-fix, then re-run the verify command and re-run Step 3 below since formatting may have touched the file.

- [ ] **Step 3: Full FeatureFlags test suite (confirm no collision with existing tests)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.FeatureFlags"`

Expected: all tests pass, including the pre-existing `ClearFlagOverrideHandlerTests`, `UpsertFlagOverrideHandlerTests`, `HebloFeatureProviderTests`, `FeatureFlagRegistryFrontendMirrorTests`, `FeatureFlagsControllerLintTests`, plus the 4 new `ListFlagsHandlerTests` — `Failed: 0`.

- [ ] **Step 4: Commit (only if Step 2 required an auto-fix)**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs
git commit -m "style(feature-flags): apply dotnet format to ListFlagsHandlerTests (#4174)"
```

If Step 2 required no changes, skip this step — there is nothing to commit.

---

## Self-Review

**1. Spec coverage:**
- FR-1 (has-override) → `task: scaffold-and-has-override-test`.
- FR-2 (no-override) → `task: no-override-test`.
- FR-3 (case-sensitive `StringComparer.Ordinal` lookup) → `task: case-mismatch-test`.
- FR-4 (baseline field pass-through, one DTO per registry entry) → `task: baseline-fields-test`.
- NFR-1 (isolation/determinism: mocks only, keys referenced via `FeatureFlagKeys` constants and looked up by key not index) → satisfied by every task's use of `FeatureFlagKeys.LabelPrintingEnabled` and `.Single(f => f.Key == ...)` rather than index access.
- NFR-2 (xUnit/Moq/FluentAssertions, matches `ClearFlagOverrideHandlerTests` style) → satisfied by the scaffold in `task: scaffold-and-has-override-test`.
- NFR-3 (no production code changes) → no task touches any file outside the new test file; `task: full-validation` explicitly runs the full build/format/test suite to confirm nothing else broke.
- No gaps found.

**2. Placeholder scan:** No `TBD`/`TODO`/"add appropriate error handling"/"similar to Task N" patterns present — every step has literal, complete code or a literal, complete command with its expected output.

**3. Type consistency:** `FeatureFlagKeys.LabelPrintingEnabled`, `FeatureFlagRegistry.All`, `FeatureFlagRegistry.ByKey`, `FeatureFlagOverride` (`Key`/`IsEnabled`/`UpdatedBy`/`UpdatedAt`), `FlagStatusDto` (`Key`/`Description`/`CurrentValue`/`IsOverridden`/`DefaultValue`/`UpdatedBy`/`UpdatedAt`), `ListFlagsHandler(IFeatureFlagOverrideRepository, IFeatureFlagChecker)`, `IFeatureFlagChecker.IsEnabledAsync(string, bool, CancellationToken)`, `IFeatureFlagOverrideRepository.GetAllAsync(CancellationToken)` are used identically across all four test tasks and match their actual production signatures as read from `backend/src/Anela.Heblo.Application/Features/FeatureFlags/` and `backend/src/Anela.Heblo.Domain/Features/FeatureFlags/`. No naming drift between tasks.
