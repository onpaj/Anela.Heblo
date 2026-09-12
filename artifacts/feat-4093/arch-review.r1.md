# Architecture Review: Unit test coverage for `BlobPathValidator`

## Skip Design: true

No UI, no visual components, no API contract changes. Pure test-code addition inside the existing backend test project.

## Architectural Fit Assessment

Textbook fit, zero structural deviation required.

- The class under test (`backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/BlobPathValidator.cs`) is an `internal static class` with a single public static method, no constructor, no fields to arrange, and no I/O. It is as close to a pure function as this codebase gets.
- `Anela.Heblo.Application/AssemblyInfo.cs` already declares `[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]`, so the test project can call `BlobPathValidator.IsValid` directly with no project-file change, no `[InternalsVisibleTo]` addition, and no visibility change to the production type.
- The target test folder `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/` already exists and holds four sibling handler test files (`DownloadExpeditionListHandlerTests.cs`, `ReprintExpeditionListHandlerTests.cs`, `GetExpeditionListsByDateHandlerTests.cs`, `GetExpeditionDatesHandlerTests.cs`). All four consistently use plain xUnit `[Fact]`/`[Theory]` + `Assert.*` — **not** FluentAssertions' `.Should()` syntax — verified by grep: 0 occurrences of `.Should()` across all four files, vs. 6–16 `Assert.*` calls each. This is a deliberate local convention that overrides the org-wide `docs/architecture/testing-strategy.md` recommendation of FluentAssertions (that doc's "Test Structure Pattern" example uses `.Should()`, but this specific folder does not follow it). The new test file must match its immediate neighbours, not the top-level doc — this is exactly the doc's own point that "existing patterns" win in an established codebase area.
- No mocks are needed at all: `Moq` and `NSubstitute` are both present in `Anela.Heblo.Tests.csproj`, but `IsValid` has zero dependencies, so neither package is referenced by the new file.

## Proposed Architecture

### Component Overview

```
                ┌─────────────────────────────────────────────┐
                │ BlobPathValidatorTests   (xUnit class)      │
                │                                              │
                │  No constructor, no fields — static SUT.    │
                │                                              │
                │  [Theory]/[Fact] groups, one per guard:     │
                │   1. null/whitespace                        │
                │   2. path traversal (dedicated [Fact])      │
                │   3. happy path                             │
                │   4. invalid calendar date                  │
                │   5. regex/structural mismatch              │
                └───────────────────┬──────────────────────────┘
                                    │ calls (static, no DI)
                                    ▼
                ┌─────────────────────────────────────────────┐
                │ BlobPathValidator.IsValid(string)           │
                │ (internal static, Application layer)        │
                └─────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Plain `Assert.*`, not FluentAssertions
**Options considered:**
- (A) FluentAssertions `.Should().BeTrue()/.BeFalse()`, per the org-wide testing-strategy doc.
- (B) Plain xUnit `Assert.True`/`Assert.False`, matching every existing file in this folder.

**Chosen approach:** (B).

**Rationale:** All four sibling test files in `ExpeditionListArchive/` use plain `Assert.*` with zero FluentAssertions usage. Introducing the only FluentAssertions-based file in this folder would create local inconsistency for no benefit — the assertions here are trivial boolean checks (`Assert.True(...)` / `Assert.False(...)`) where FluentAssertions offers no readability advantage over the xUnit primitive. Follow the folder, not the top-level doc.

#### Decision 2: One dedicated `[Fact]` for the traversal guard, not folded into a generic `[Theory]`
**Options considered:**
- (A) Fold the `".."` traversal case(s) into the same `[Theory]` used for other rejected/structural-mismatch inputs.
- (B) Give the traversal check its own `[Fact]` (or its own small `[Theory]`) with an explicit, security-worded test name.

**Chosen approach:** (B), per spec FR-2.

**Rationale:** This is called out in the issue itself as "the security-critical branch." A named, standalone test (e.g. `IsValid_PathContainsDoubleDot_ReturnsFalse_BlocksPathTraversal`) makes intent legible in a test-run report and in `git blame`/PR diffs in a way a generic `[InlineData("2026-01-01/../../admin.pdf", false)]` row buried in a larger theory would not. A future refactor that accidentally weakens or removes the `.Contains("..")` check produces a failure with a name that says exactly what broke, rather than an anonymous theory-row failure.

#### Decision 3: Group remaining guards into `[Theory]`/`[InlineData]` blocks by guard, not one big flat theory
**Options considered:**
- (A) One single `[Theory]` with `(input, expected)` pairs for every case across all four guards.
- (B) One `[Theory]` per guard (null/whitespace, happy-path, invalid-date, structural-mismatch), each returning a fixed expected value implicit in the method name, per spec's illustrative class shape.

**Chosen approach:** (B).

**Rationale:** Since three of the four guard groups have a uniform expected outcome (`false`) and one has `true`, per-guard theories let each test name state the invariant directly (`..._ReturnsFalse`) rather than requiring a second `expected` parameter on every row. This also means a coverage/branch report maps cleanly: one theory ≈ one guard ≈ one line in `BlobPathValidator.IsValid`.

## Implementation Guidance

### Directory / Module Structure

Single new file, no project file change, no production code change:

```
backend/test/Anela.Heblo.Tests/ExpeditionListArchive/
└── BlobPathValidatorTests.cs   ← NEW (sibling of the four existing handler test files)
```

Namespace: `Anela.Heblo.Tests.ExpeditionListArchive` (matches all four sibling files).

### Interfaces and Contracts

No new interfaces or types. The test depends on exactly one production symbol:

| Symbol | Location | Use in test |
|---|---|---|
| `BlobPathValidator.IsValid(string blobPath)` | `Application/Features/ExpeditionListArchive/BlobPathValidator.cs` | SUT — called directly, no instantiation, no DI |

Confirmed signature and behavior by reading the source (reproduced from `BlobPathValidator.cs`):
```csharp
internal static class BlobPathValidator
{
    private static readonly Regex ValidBlobPathPattern =
        new(@"^\d{4}-\d{2}-\d{2}/[^/]+\.pdf$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsValid(string blobPath)
    {
        if (string.IsNullOrWhiteSpace(blobPath)) return false;
        if (blobPath.Contains("..")) return false;
        if (!ValidBlobPathPattern.IsMatch(blobPath)) return false;
        var datePart = blobPath.Split('/')[0];
        return DateOnly.TryParseExact(datePart, "yyyy-MM-dd", out _);
    }
}
```
Note the parameter type is non-nullable `string`, but guard 1 must still be tested with a literal `null` argument (per brief and spec FR-1) — this compiles under nullable-reference-types with a CS8625 warning at the call site unless suppressed. Verified precedent: `GetExpeditionDatesHandlerTests.cs` and its siblings in this same folder do not currently pass `null` to a non-nullable parameter, so there is no exact local precedent to copy; use the standard, minimally-invasive suppression — `null!` at the specific `[InlineData(null!)]` call site (the common idiom already used more broadly in this codebase for intentional-null test scenarios) — rather than a file-wide `#pragma warning disable`. Do not weaken nullability project-wide to accommodate this one test.

### Data Flow

Representative per-guard flow (FR-2, the security case):
```
Arrange: (none — no fixture, no mock, no setup)
Act:     var result = BlobPathValidator.IsValid("2026-01-01/../../admin.pdf");
Assert:  Assert.False(result);
```

Representative per-guard flow (FR-4, invalid calendar date — demonstrates guard ordering matters):
```
Act:     var result = BlobPathValidator.IsValid("2026-13-01/report.pdf");
Assert:  Assert.False(result);
         // "2026-13-01" DOES match \d{4}-\d{2}-\d{2} (guard 3 passes) —
         // this test only fails guard 4 (DateOnly.TryParseExact), confirming
         // guard 4 is reached and actually enforced, not dead code shadowed
         // by an over-eager regex.
```

All five test groups follow this same three-line shape (no Arrange step beyond parameter values, since there is no state and no collaborator).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A single flat `[Theory]` mixing all guards could pass even if guard ordering is subtly wrong (e.g. traversal check moved after regex check) | Low | Per-guard grouping (Decision 3) plus the FR-4 case specifically chosen because it is regex-valid-but-date-invalid, which only passes if guard 4 actually runs after guard 3 — this exposes a guard-reordering regression that a less careful test selection would miss. |
| `[InlineData(null!)]` suppression pattern diverges from a codebase-wide convention not yet observed in this folder | Trivial | Grep the broader `Anela.Heblo.Tests` project for the existing `null!`-in-`InlineData` idiom during implementation and match it exactly; if none is found, `null!` is the minimal, standard, and self-explanatory choice — no further discussion needed. |
| Coverage tooling might consider the traversal guard "covered" by an unrelated case that happens to touch the same line without asserting the security property | Low | FR-2's dedicated, explicitly-named `[Fact]` (Decision 2) exists specifically so the security intent is asserted, not just incidentally exercised. |

## Specification Amendments

None required — the spec (`spec.r1.md`) is implementation-ready as written. It already anticipates the `null!`/nullable-warning question in NFR-1 and defers the exact suppression idiom to "the codebase's existing convention," which this review resolves as: use `null!` at the call site (no file-wide pragma), confirmed via Decision/Risk above.

## Prerequisites

None. Everything required exists today:
- `Anela.Heblo.Application/AssemblyInfo.cs` already grants `InternalsVisibleTo("Anela.Heblo.Tests")` — no change needed.
- `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` already references `xunit` — no new package needed (no Moq/NSubstitute/FluentAssertions required for this file).
- The target folder `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/` already exists.

Implementation can begin immediately; the deliverable is a single new file with roughly 5 test groups (~10–14 individual cases across `[Fact]`/`[Theory]`), expected to build clean and execute in well under 100 ms.
