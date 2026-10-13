# Specification: Test coverage for `ListFlagsHandler.IsOverridden` DTO branch

## Summary
`ListFlagsHandler` (FeatureFlags module) merges the live flag values from `IFeatureFlagChecker` with override rows from `IFeatureFlagOverrideRepository` and populates `IsOverridden`, `UpdatedBy`, and `UpdatedAt` on each `FlagStatusDto` based on whether an override exists for that flag's key. This branch currently has no unit test coverage (16.7% line coverage on the file vs. a 60% threshold). This spec adds a dedicated unit test class covering the "has override", "no override", and case-mismatch paths so the branch is verified and coverage clears the threshold.

## Background
The feature flags admin UI (`frontend/src/pages/FeatureFlagsAdminPage.tsx`) reads `IsOverridden` to show operators which flags have been manually overridden versus running on their config/registry default. `ListFlagsHandler.Handle` builds this per-flag by:
1. Loading all override rows via `_repo.GetAllAsync(ct)` and indexing them into a `Dictionary<string, FeatureFlagOverride>` keyed by `Key`, using `StringComparer.Ordinal`.
2. Concurrently evaluating every registered flag's current value via `_checker.IsEnabledAsync(def.Key, def.DefaultValue, ct)`.
3. Zipping the two together per `FeatureFlagRegistry.All` entry: `overrideMap.TryGetValue(def.Key, out var entity)`, then setting `IsOverridden = overrideMap.ContainsKey(def.Key)`, `UpdatedBy = entity?.UpdatedBy`, `UpdatedAt = entity?.UpdatedAt`.

No existing test exercises this handler at all (`backend/test/Anela.Heblo.Tests/Features/FeatureFlags/` has tests for `ClearFlagOverrideHandler`, `UpsertFlagOverrideHandler`, `HebloFeatureProvider`, and two lint/mirror tests, but none for `ListFlagsHandler`). A regression here (e.g. an accidental case-insensitive comparer swap, or dropping the `entity?.` null-conditional) would silently misreport override state in the admin UI — flags could show as non-overridden when an override is actually in effect, hiding an operator's manual customization without raising any error.

## Functional Requirements

### FR-1: Cover the "has override" path
For a flag whose key has a matching entry in the override repository's result, the produced `FlagStatusDto` must have `IsOverridden = true` and `UpdatedBy` / `UpdatedAt` populated from that override entity (not null, and equal to the seeded override's values).

**Acceptance criteria:**
- Given a fake/mocked `IFeatureFlagOverrideRepository.GetAllAsync` returning one `FeatureFlagOverride` whose `Key` exactly matches a real key from `FeatureFlagRegistry.All` (e.g. `FeatureFlagKeys.LabelPrintingEnabled`), the resulting `FlagStatusDto` for that key has `IsOverridden == true`.
- That same DTO's `UpdatedBy` equals the seeded override's `UpdatedBy`.
- That same DTO's `UpdatedAt` equals the seeded override's `UpdatedAt`.

### FR-2: Cover the "no override" path
For a flag with no matching override row, the produced `FlagStatusDto` must have `IsOverridden = false` and both `UpdatedBy` and `UpdatedAt` null.

**Acceptance criteria:**
- Given `GetAllAsync` returns an empty list (or a list that does not contain a given flag's key), the `FlagStatusDto` for that flag has `IsOverridden == false`.
- `UpdatedBy` is null for that DTO.
- `UpdatedAt` is null for that DTO.

### FR-3: Cover the case-sensitive (`StringComparer.Ordinal`) lookup contract
A key whose casing differs from the registry key must not be treated as a match — the override must be ignored for the purposes of `IsOverridden`/`UpdatedBy`/`UpdatedAt` on that flag.

**Acceptance criteria:**
- Given `GetAllAsync` returns one override whose `Key` is a case-differing variant of a real registry key (e.g. uppercased), the `FlagStatusDto` for the real (correctly-cased) key has `IsOverridden == false`, `UpdatedBy == null`, `UpdatedAt == null` — i.e. the mismatched-case override is not applied to it.
- This also implicitly guards the `StringComparer.Ordinal` argument to `ToDictionary` in `Handle`: swapping it for a case-insensitive comparer would make this test fail (duplicate-key `ArgumentException` when two variants collide with an existing key, or a false-positive match), which is the specific regression this coverage gap calls out.

### FR-4: Preserve currently-untested but reachable behavior (regression safety net, not new requirements)
While adding coverage, the following existing behaviors of `Handle` must continue to hold and should be observable (directly or incidentally) through the new tests, so a future change to unrelated parts of the method doesn't silently break them:
- `CurrentValue` on each DTO comes from `_checker.IsEnabledAsync(def.Key, def.DefaultValue, ct)`, independent of override state (an overridden flag can still have any `CurrentValue`, since `IFeatureFlagChecker` is the one responsible for consulting overrides when evaluating live value — `ListFlagsHandler` does not derive `CurrentValue` from the override row itself).
- `Key`, `Description`, `DefaultValue` on each DTO are copied straight from the matching `FeatureFlagDefinition` in `FeatureFlagRegistry.All`.
- The response contains exactly one `FlagStatusDto` per entry in `FeatureFlagRegistry.All` (no flags dropped or duplicated), returned inside `ListFlagsResponse.Flags`.

**Acceptance criteria:**
- Test setup asserts (via `Should().HaveCount(FeatureFlagRegistry.All.Count)` or equivalent) that the response has one DTO per registered flag.
- At least one assertion checks `Key`/`Description`/`DefaultValue` on a DTO match the corresponding `FeatureFlagRegistry` entry.

## Non-Functional Requirements

### NFR-1: Test isolation and determinism
Tests must not depend on `FeatureFlagRegistry.All`'s exact contents changing over time in ways that break the test — reference flags by their `FeatureFlagKeys` constants and look up the corresponding registry entry/DTO by key rather than by list index, so the tests remain valid if the registry gains or reorders flags. Tests must not hit a real database, OpenFeature provider, or the filesystem; both `IFeatureFlagOverrideRepository` and `IFeatureFlagChecker` are mocked with Moq, matching the existing pattern in `ClearFlagOverrideHandlerTests` / `UpsertFlagOverrideHandlerTests`.

### NFR-2: Style/framework consistency
Use xUnit `[Fact]`, `Moq`, and `FluentAssertions`, matching every other test in `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/` (see `ClearFlagOverrideHandlerTests.cs` for the established constructor-mock-handler pattern). New test class is a plain C# class, not a record (project convention for DTOs applies to production types; this is a test class, but the codebase style/using-conventions should still be matched — no records for anything acting as a data holder used across the test).

### NFR-3: No production code changes required
This is a coverage-only gap: `ListFlagsHandler.cs` is already logically correct per the issue's description. Nothing in `Handle`, `FlagStatusDto`, `FeatureFlagOverride`, or `IFeatureFlagOverrideRepository` needs to change to satisfy these requirements — only a new test file is added. (If test-writing surfaces an actual behavioral bug, that is an Open Question, not an assumed requirement — see below.)

## Data Model
No new or changed data model. Existing types used by the tests:
- `Anela.Heblo.Domain.Features.FeatureFlags.FeatureFlagOverride` — `Key` (string), `IsEnabled` (bool), `UpdatedAt` (DateTime), `UpdatedBy` (string).
- `Anela.Heblo.Application.Features.FeatureFlags.Contracts.FlagStatusDto` — `Key`, `Description`, `CurrentValue`, `IsOverridden`, `DefaultValue`, `UpdatedBy` (nullable), `UpdatedAt` (nullable).
- `Anela.Heblo.Application.Features.FeatureFlags.FeatureFlagRegistry.All` — the fixed list of `FeatureFlagDefinition`s the handler iterates; `FeatureFlagKeys` supplies the string constants.

## API / Interface Design
No API surface changes. This is a backend unit test addition only, targeting:
- Handler under test: `Anela.Heblo.Application.Features.FeatureFlags.UseCases.ListFlags.ListFlagsHandler`
- Mocked collaborators: `IFeatureFlagOverrideRepository` (mock `GetAllAsync`), `IFeatureFlagChecker` (mock `IsEnabledAsync(string, bool, CancellationToken)` to return a value per flag — e.g. echo back `defaultValue`, or set up per-key `It.Is<string>(k => k == ...)` returns as needed per test).
- New test file location: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` (mirrors the existing `UseCases/ClearFlagOverride/ClearFlagOverrideHandlerTests.cs` folder convention — note `UpsertFlagOverrideHandlerTests.cs` currently sits one level up, directly under `Features/FeatureFlags/`, so there is a slight existing inconsistency; this spec follows the more specific/newer `UseCases/<UseCaseName>/` convention used by `ClearFlagOverride`).

## Dependencies
- Existing test infrastructure only: `Xunit`, `Moq`, `FluentAssertions` (already referenced by the test project, as seen in `ClearFlagOverrideHandlerTests.cs`).
- No new NuGet packages, no new external services.

## Out of Scope
- Any change to `ListFlagsHandler.cs`, `FlagStatusDto`, `FeatureFlagOverride`, `IFeatureFlagOverrideRepository`, or `IFeatureFlagChecker` production code.
- Testing `IFeatureFlagChecker`'s own override-resolution logic (that belongs to `HebloFeatureProviderTests.cs` / the checker's own implementation, not `ListFlagsHandler`).
- Testing the controller (`FeatureFlagsController.cs`) or the frontend admin page — this coverage gap is scoped to the handler only.
- Raising the file's line coverage to a specific target percentage as an explicit metric goal — the acceptance criteria are behavioral (the three branches below); coverage crossing the 60% threshold is the expected natural consequence of testing `Handle`'s only nontrivial branch, not a separately measured requirement.

## Open Questions
None.
