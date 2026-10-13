# Architecture Review: Test coverage for `ListFlagsHandler.IsOverridden` DTO branch

## Skip Design: true
Backend-only unit test addition. No new/changed UI components, screens, layouts, or API surface — nothing for the designer to act on.

## Architectural Fit Assessment
This is a pure coverage-gap fix, not a feature. `ListFlagsHandler` is a standard MediatR request handler in the FeatureFlags vertical slice (`Anela.Heblo.Application.Features.FeatureFlags.UseCases.ListFlags`) and its two collaborators are already interface-abstracted (`IFeatureFlagOverrideRepository` from Domain, `IFeatureFlagChecker` from Application) and already mocked elsewhere in the test suite (`ClearFlagOverrideHandlerTests`, `UpsertFlagOverrideHandlerTests`, `HebloFeatureProviderTests`). There is no missing seam, no missing abstraction, and no production code defect implied by the coverage report — this fits cleanly as "add the missing test file" with zero risk to runtime behavior.

The spec (`spec.r1.md`) is architecturally sound as written. One correction and one clarification are needed before implementation (see Specification Amendments) — both concern test-file conventions, not requirements.

## Proposed Architecture

### Component Overview
```
ListFlagsHandlerTests (NEW)
   │
   ├── mocks IFeatureFlagOverrideRepository.GetAllAsync(ct)
   │        → IReadOnlyList<FeatureFlagOverride>  (Domain type)
   │
   ├── mocks IFeatureFlagChecker.IsEnabledAsync(key, defaultValue, ct)
   │        → bool   (per FeatureFlagRegistry.All entry)
   │
   └── exercises ListFlagsHandler.Handle(ListFlagsRequest, ct)
            → ListFlagsResponse { Flags: List<FlagStatusDto> }
                 - asserts IsOverridden / UpdatedBy / UpdatedAt per FR-1..FR-3
                 - asserts Key/Description/DefaultValue/count per FR-4
```
No new components. `FeatureFlagRegistry.All` (a static, hard-coded 3-entry list — `DeliveredOrderCompletion`, `DeliveredOrderCompletionTestSource`, `LabelPrintingEnabled`) is the fixed source of truth the handler iterates; tests select real keys from it via `FeatureFlagKeys` constants rather than fabricating flag keys, since `Handle` only ever emits one DTO per registry entry — an override for a key that isn't in the registry would be loaded into `overrideMap` but never surface in `Flags` (dead weight in the map, not a DTO), which is why FR-3's case-mismatch test must use a variant of a *real* registered key, not an arbitrary string.

### Key Design Decisions

#### Decision 1: Mock both collaborators (no real registry/checker wiring)
**Options considered:**
- (a) Mock `IFeatureFlagOverrideRepository` and `IFeatureFlagChecker` individually per test (matches `ClearFlagOverrideHandlerTests` pattern).
- (b) Stand up a lightweight in-memory `IFeatureFlagChecker` fake instead of a Moq mock, since `IsEnabledAsync` is called once per registry entry (3 calls) via `Task.WhenAll`.

**Chosen approach:** (a) — Moq for both, matching every other handler test in this file's package.

**Rationale:** Consistency with `ClearFlagOverrideHandlerTests.cs` / `UpsertFlagOverrideHandlerTests.cs` (both instantiate `Mock<T>` per dependency in the constructor). `IsEnabledAsync(string key, bool defaultValue, CancellationToken ct)` is called via `Task.WhenAll` across `FeatureFlagRegistry.All`, so the mock setup must handle all three keys — use `_checker.Setup(c => c.IsEnabledAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync((string key, bool defaultValue, CancellationToken _) => defaultValue)` as a single catch-all default (returns the registry default for every flag, which is irrelevant to `IsOverridden`/`UpdatedBy`/`UpdatedAt` per FR-4's own observation that `CurrentValue` is independent of override state). Only add a per-key `Setup` when a test specifically needs to assert `CurrentValue`.

#### Decision 2: One override-repo mock per test, not a shared fixture
**Options considered:**
- (a) A single `[Theory]`/`[InlineData]`-driven test parametrizing override presence.
- (b) Three separate `[Fact]` methods, one per FR (has-override / no-override / case-mismatch).

**Chosen approach:** (b) — separate `[Fact]`s.

**Rationale:** `GetAllAsync`'s return shape (a list of `FeatureFlagOverride` with different key casings/contents) doesn't parametrize cleanly into `[InlineData]` primitives, and the codebase's existing handler tests (`ClearFlagOverrideHandlerTests`, `UpsertFlagOverrideHandlerTests`) universally use discrete `[Fact]` methods with descriptive `Handle_<Scenario>_<Expectation>` names rather than theories. Matches house style; keeps each test's arrange block self-contained and readable.

#### Decision 3: Test file location — `UseCases/ListFlags/` subfolder
**Options considered:**
- (a) `backend/test/.../Features/FeatureFlags/ListFlagsHandlerTests.cs` (flat, matching `UpsertFlagOverrideHandlerTests.cs`'s current location).
- (b) `backend/test/.../Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` (mirrors production source layout and matches `ClearFlagOverrideHandlerTests.cs`'s location).

**Chosen approach:** (b).

**Rationale:** The production handler lives at `Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandler.cs`. `ClearFlagOverrideHandlerTests.cs` already established the "mirror the production `UseCases/<Name>/` folder in the test tree" convention for this same directory, and it's the more recently added test (the flat `UpsertFlagOverrideHandlerTests.cs` placement predates it and is the outlier, not the convention to keep spreading). Mirroring source layout is also the project-wide default per `docs/architecture/filesystem.md`.

## Implementation Guidance

### Directory / Module Structure
Create exactly one new file:
```
backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs
```
No other files change. Namespace: `Anela.Heblo.Tests.Features.FeatureFlags.UseCases.ListFlags` (matches `ClearFlagOverrideHandlerTests`'s `Anela.Heblo.Tests.Features.FeatureFlags.UseCases.ClearFlagOverride` pattern exactly).

### Interfaces and Contracts
No new or changed interfaces. Tests consume exactly what already exists:
- `Anela.Heblo.Application.Features.FeatureFlags.UseCases.ListFlags.ListFlagsHandler` (constructor: `(IFeatureFlagOverrideRepository repo, IFeatureFlagChecker checker)`)
- `Anela.Heblo.Application.Features.FeatureFlags.UseCases.ListFlags.ListFlagsRequest` / `ListFlagsResponse`
- `Anela.Heblo.Application.Features.FeatureFlags.Contracts.FlagStatusDto`
- `Anela.Heblo.Domain.Features.FeatureFlags.FeatureFlagOverride` (`Key`, `IsEnabled`, `UpdatedAt`, `UpdatedBy` — all settable properties, easy to construct with object initializers in test arrange blocks)
- `Anela.Heblo.Application.Features.FeatureFlags.FeatureFlagRegistry` / `FeatureFlagKeys` (read-only statics, safe to reference directly from tests — do not duplicate flag key strings, always go through `FeatureFlagKeys.*`)
- `Anela.Heblo.Application.Features.FeatureFlags.IFeatureFlagOverrideRepository` — note this is a `global using` alias to `Anela.Heblo.Domain.Features.FeatureFlags.IFeatureFlagOverrideRepository`; either the Domain or Application namespace works for the mock's generic type argument since it's the same type, but match `ClearFlagOverrideHandlerTests.cs`'s existing `using Anela.Heblo.Domain.Features.FeatureFlags;` + `Mock<IFeatureFlagOverrideRepository>` style for consistency.
- `Anela.Heblo.Application.Features.FeatureFlags.IFeatureFlagChecker` (mock `IsEnabledAsync(string, bool, CancellationToken)` — the 3-arg overload is the one `ListFlagsHandler` calls; the 2-arg overload is unused by this handler and does not need a setup)

### Data Flow
1. Test arranges `_repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(overrides)` where `overrides` is an `IReadOnlyList<FeatureFlagOverride>` (empty for FR-2, one matching entry for FR-1, one case-differing entry for FR-3).
2. Test arranges `_checkerMock.Setup(...)` catch-all as in Decision 1.
3. Test constructs `new ListFlagsHandler(_repoMock.Object, _checkerMock.Object)` and calls `await handler.Handle(new ListFlagsRequest(), CancellationToken.None)`.
4. Test locates the DTO(s) of interest via `response.Flags.Single(f => f.Key == FeatureFlagKeys.LabelPrintingEnabled)` (or similar) and asserts per the FR's acceptance criteria with FluentAssertions (`.Should().BeTrue()`, `.Should().Be(...)`, `.Should().BeNull()`).
5. No cache, no HTTP, no DB — fully in-memory, matching the existing suite's speed/isolation profile (test pyramid: this stays a fast unit test per `docs/architecture/testing-strategy.md`).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `IsEnabledAsync` mock setup doesn't cover all 3 registry keys, causing `Task.WhenAll` to throw `MockException` (strict-by-default Moq unmatched call) or return `default(bool)` unexpectedly | Low | Use the `It.IsAny<string>()` catch-all setup from Decision 1 so every registry key is covered regardless of how many entries `FeatureFlagRegistry.All` has now or later. |
| `FeatureFlagRegistry.All` gains/loses entries in a future change, shifting which keys exist | Low | Tests already reference flags via `FeatureFlagKeys` constants + `.Single(f => f.Key == ...)` lookups (never by list index), per spec NFR-1 — no change needed here, just confirming the guidance holds. |
| Case-mismatch test (FR-3) accidentally throws `ArgumentException` from `ToDictionary` if the mismatched-case key happens to collide with a second entry already in the override list | Low | Keep the FR-3 override list to a single entry (the case-mismatched one) — no risk of a `ToDictionary` duplicate-key collision since there is nothing else in the list for it to collide with. |
| None of these tests touch production code, so there is no risk of behavior change or regression from this work itself | N/A | N/A — confirms NFR-3 in the spec: no production code changes are required or expected. |

## Specification Amendments
1. **API / Interface Design section, file path convention:** The spec correctly flags the existing inconsistency between `ClearFlagOverrideHandlerTests.cs`'s `UseCases/<Name>/` placement and `UpsertFlagOverrideHandlerTests.cs`'s flat placement, and picks the `UseCases/ListFlags/` convention. This review confirms that choice (Decision 3) — no change to the spec's own conclusion, just formalizing it as the binding decision for implementation.
2. **NFR-2 wording tweak:** Drop the sentence "New test class is a plain C# class, not a record" — it's a category error carried over from the DTO-specific project rule ("DTOs are classes, never records") into a context (a test class with `[Fact]` methods) where the record/class distinction was never in question; xUnit test classes are always plain classes in this codebase and there is no ambiguity to guard against. No functional impact either way — purely a spec clarity fix, safe to leave as-is if preferred, but the developer should not read it as implying test *data* (e.g. a local override-builder record) is disallowed — nothing in this task needs one, but if the developer chooses a tiny local `record` for arranging test fixtures inside the test file only (not shared, not a DTO), that's fine and outside the DTO-records-forbidden rule's intent.
3. **Mock setup detail for `IsEnabledAsync`:** The spec's API/Interface Design section says "mock `IsEnabledAsync(string, bool, CancellationToken)` ... echo back `defaultValue`, or set up per-key returns as needed" — this review makes that concrete (Decision 1's catch-all lambda) since `Task.WhenAll` over all 3 registry entries means every entry must have a matching setup or the test fails for reasons unrelated to what's being tested.

## Prerequisites
None. All required types, interfaces, and test infrastructure (xUnit, Moq, FluentAssertions) already exist and are already referenced by the test project. No migrations, no config, no new packages.
