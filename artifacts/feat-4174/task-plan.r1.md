# ListFlagsHandler IsOverridden Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add unit test coverage for `ListFlagsHandler`'s override-merge branch (`IsOverridden`/`UpdatedBy`/`UpdatedAt`), which currently has zero test coverage.

**Architecture:** Add one new xUnit test class, `ListFlagsHandlerTests`, under `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/`, mirroring the production `UseCases/ListFlags/` folder and the `ClearFlagOverrideHandlerTests` mocking pattern (Moq for `IFeatureFlagOverrideRepository` and `IFeatureFlagChecker`). No production code changes — `ListFlagsHandler.cs` is already correct; this closes a pure coverage gap.

**Tech Stack:** .NET 8, xUnit 2.9.2, Moq 4.20.72, FluentAssertions 6.12.0 (all already referenced by `Anela.Heblo.Tests.csproj`).

---

## Note on TDD phrasing in this plan

Every other plan produced by this pipeline follows red→green: write a failing test, watch it fail, implement, watch it pass. Here there is nothing to implement — `ListFlagsHandler.Handle` is already correct (confirmed by reading `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandler.cs`), and the spec (`spec.r1.md`, NFR-3) explicitly forbids production code changes. So each task below is "write the test, run it, confirm it passes" — the run step is not ceremony, it's the actual verification that the test asserts what it claims (a test that never ran red even once as a sanity check should at minimum be watched to see it actually executes, actually calls the mocked methods, and actually passes against the real handler — not silently skipped or mistyped into an always-true assertion).

---

### task: scaffold-and-has-override-test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs`

This task creates the test file with its scaffold (mocks, constructor stub, `CreateHandler` helper) plus the first test, covering spec FR-1 (has-override path).

- [ ] **Step 1: Write the test file with the class scaffold and the has-override test**

Create `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.FeatureFlags.UseCases.ListFlags;
using Anela.Heblo.Domain.Features.FeatureFlags;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.FeatureFlags.UseCases.ListFlags;

public class ListFlagsHandlerTests
{
    private readonly Mock<IFeatureFlagOverrideRepository> _repoMock = new();
    private readonly Mock<IFeatureFlagChecker> _checkerMock = new();

    public ListFlagsHandlerTests()
    {
        // Every registry entry is evaluated via Task.WhenAll inside Handle, so every
        // key must resolve. CurrentValue is irrelevant to the IsOverridden branch under
        // test here (see spec.r1.md FR-4), so a single any-key stub covers all of them.
        _checkerMock
            .Setup(c => c.IsEnabledAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, bool defaultValue, CancellationToken ct) => defaultValue);
    }

    private ListFlagsHandler CreateHandler() => new(_repoMock.Object, _checkerMock.Object);

    [Fact]
    public async Task Handle_FlagHasMatchingOverride_SetsIsOverriddenTrueAndPopulatesAuthorAndDate()
    {
        var updatedAt = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);
        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeatureFlagOverride>
            {
                new()
                {
                    Key = FeatureFlagKeys.LabelPrintingEnabled,
                    IsEnabled = false,
                    UpdatedBy = "jane@example.com",
                    UpdatedAt = updatedAt,
                },
            });

        var response = await CreateHandler().Handle(new ListFlagsRequest(), CancellationToken.None);

        var dto = response.Flags.Single(f => f.Key == FeatureFlagKeys.LabelPrintingEnabled);
        dto.IsOverridden.Should().BeTrue();
        dto.UpdatedBy.Should().Be("jane@example.com");
        dto.UpdatedAt.Should().Be(updatedAt);
    }
}
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListFlagsHandlerTests"`

Expected: build succeeds, `Passed! - Failed: 0, Passed: 1, Skipped: 0` (exactly one test collected and passing so far).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs
git commit -m "test(feature-flags): cover ListFlagsHandler has-override path (#4174)"
```

---

### task: no-override-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` (add one test method to the class created in the previous task)

Covers spec FR-2 (no-override path).

- [ ] **Step 1: Add the no-override test**

Add this method to the `ListFlagsHandlerTests` class (after `Handle_FlagHasMatchingOverride_SetsIsOverriddenTrueAndPopulatesAuthorAndDate`):

```csharp
    [Fact]
    public async Task Handle_FlagHasNoOverride_SetsIsOverriddenFalseAndNullsAuthorAndDate()
    {
        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeatureFlagOverride>());

        var response = await CreateHandler().Handle(new ListFlagsRequest(), CancellationToken.None);

        var dto = response.Flags.Single(f => f.Key == FeatureFlagKeys.LabelPrintingEnabled);
        dto.IsOverridden.Should().BeFalse();
        dto.UpdatedBy.Should().BeNull();
        dto.UpdatedAt.Should().BeNull();
    }
```

- [ ] **Step 2: Run the tests to verify both pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListFlagsHandlerTests"`

Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs
git commit -m "test(feature-flags): cover ListFlagsHandler no-override path (#4174)"
```

---

### task: case-mismatch-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` (add one test method)

Covers spec FR-3: the `StringComparer.Ordinal` lookup contract — a case-differing override key must not match.

- [ ] **Step 1: Add the case-mismatch test**

Add this method to the `ListFlagsHandlerTests` class (after `Handle_FlagHasNoOverride_SetsIsOverriddenFalseAndNullsAuthorAndDate`):

```csharp
    [Fact]
    public async Task Handle_OverrideKeyCaseDiffersFromRegistryKey_IsTreatedAsNoMatch()
    {
        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeatureFlagOverride>
            {
                new()
                {
                    Key = FeatureFlagKeys.LabelPrintingEnabled.ToUpperInvariant(),
                    IsEnabled = false,
                    UpdatedBy = "jane@example.com",
                    UpdatedAt = DateTime.UtcNow,
                },
            });

        var response = await CreateHandler().Handle(new ListFlagsRequest(), CancellationToken.None);

        var dto = response.Flags.Single(f => f.Key == FeatureFlagKeys.LabelPrintingEnabled);
        dto.IsOverridden.Should().BeFalse();
        dto.UpdatedBy.Should().BeNull();
        dto.UpdatedAt.Should().BeNull();
    }
```

Note: `FeatureFlagKeys.LabelPrintingEnabled` is `"is-label-printing-enabled"` — already all-lowercase, so `.ToUpperInvariant()` reliably produces a differently-cased string (`"IS-LABEL-PRINTING-ENABLED"`) that cannot equal the original under any comparer. If a future edit changes that constant's value, `.ToUpperInvariant()` on the constant itself still guarantees a mismatch as long as the key contains at least one letter — it does not hard-code the literal string.

- [ ] **Step 2: Run the tests to verify all three pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListFlagsHandlerTests"`

Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs
git commit -m "test(feature-flags): cover ListFlagsHandler case-mismatch override lookup (#4174)"
```

---

### task: baseline-fields-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` (add one test method)

Covers spec FR-4: the response has exactly one DTO per registered flag, with `Key`/`Description`/`DefaultValue` copied from the registry definition.

- [ ] **Step 1: Add the baseline-fields test**

Add this method to the `ListFlagsHandlerTests` class (after `Handle_OverrideKeyCaseDiffersFromRegistryKey_IsTreatedAsNoMatch`):

```csharp
    [Fact]
    public async Task Handle_AlwaysReturnsOneDtoPerRegisteredFlag_WithFieldsCopiedFromDefinition()
    {
        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeatureFlagOverride>());

        var response = await CreateHandler().Handle(new ListFlagsRequest(), CancellationToken.None);

        response.Flags.Should().HaveCount(FeatureFlagRegistry.All.Count);

        var definition = FeatureFlagRegistry.ByKey[FeatureFlagKeys.LabelPrintingEnabled];
        var dto = response.Flags.Single(f => f.Key == FeatureFlagKeys.LabelPrintingEnabled);
        dto.Description.Should().Be(definition.Description);
        dto.DefaultValue.Should().Be(definition.DefaultValue);
    }
```

- [ ] **Step 2: Run the tests to verify all four pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListFlagsHandlerTests"`

Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs
git commit -m "test(feature-flags): cover ListFlagsHandler baseline DTO field mapping (#4174)"
```

---

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
