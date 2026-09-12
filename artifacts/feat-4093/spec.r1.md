# Specification: Unit test coverage for `BlobPathValidator`

## Summary
Add a complete unit test suite for `BlobPathValidator.IsValid` (currently 0% coverage) that locks in the four sequential guards the method applies: null/whitespace rejection, path-traversal rejection, regex-format rejection, and invalid-date rejection. The security-critical branch — rejecting any path containing `".."` — must be explicitly exercised so a future refactor cannot silently remove it.

## Background
`BlobPathValidator.IsValid` (`backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/BlobPathValidator.cs`) is the sole guard between a user-supplied blob path and the Azure Blob Storage download call in `DownloadExpeditionListHandler` (and is also referenced by `ReprintExpeditionListHandler`). It has zero test coverage today. If the `.Contains("..")` traversal check regresses, an attacker with `ExpeditionListArchive.read` permission could request arbitrary blobs by embedding `..` segments in the path. If the `DateOnly.TryParseExact` check regresses, blobs from paths that are structurally valid but not in the expected `yyyy-MM-dd/filename.pdf` date-folder layout could be served. This was flagged by the weekly automated coverage-gap routine (CI run #33791274852) and is filed as a pure-unit-test task — no mocks, no infrastructure, no production code changes.

The method applies four sequential guards, in order:
1. `string.IsNullOrWhiteSpace(blobPath)` → `false` if null, empty, or whitespace-only.
2. `blobPath.Contains("..")` → `false` if the literal substring `".."` appears anywhere in the path (path-traversal guard).
3. `ValidBlobPathPattern.IsMatch(blobPath)` against `^\d{4}-\d{2}-\d{2}/[^/]+\.pdf$` (case-insensitive) → `false` on any structural mismatch (wrong extension, extra path segment, non-date-shaped prefix, etc.).
4. `DateOnly.TryParseExact(datePart, "yyyy-MM-dd", out _)` on the segment before the first `/` → `false` if the date-shaped prefix does not parse as a real calendar date (e.g. month 13, day 32).

## Functional Requirements

### FR-1: Test — null, empty, and whitespace paths are rejected
`IsValid` returns `false` for `null`, `""`, and a whitespace-only string, without reaching any later guard.

**Acceptance criteria:**
- A theory test in `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/BlobPathValidatorTests.cs` covers at least: `null`, `""`, `"   "`.
- Each case asserts `BlobPathValidator.IsValid(input) == false`.

### FR-2: Test — path traversal via `".."` is rejected (security-critical)
`IsValid` returns `false` whenever the path contains the literal substring `".."`, even when the rest of the path is otherwise structurally valid-looking.

**Acceptance criteria:**
- A dedicated test (not merged into a generic theory with unrelated cases) named to make its security intent obvious, e.g. `IsValid_PathContainsDoubleDot_ReturnsFalse_BlocksPathTraversal`.
- Covers at minimum the brief's traversal example: `"2026-01-01/../../admin.pdf"` → `false`.
- Covers at least one additional traversal shape to guard against a narrowed regex/string-check refactor, e.g. `"..2026-01-01/report.pdf"` (leading `..`) or `"2026-01-01/report..pdf"` (`..` inside the filename segment) → `false`.
- Test name and/or a code comment explicitly states this guards against path traversal / arbitrary blob access, so a future reviewer understands why the case exists even if coverage tooling would be satisfied by a less-targeted test.

### FR-3: Test — happy path is accepted
`IsValid` returns `true` for a well-formed path: four-digit year, two-digit month, two-digit day, a single `/`, a non-empty filename segment, and a `.pdf` extension.

**Acceptance criteria:**
- Test asserts `BlobPathValidator.IsValid("2026-01-01/report.pdf") == true`.
- At least one additional happy-path variant with a case-insensitive extension is covered (e.g. `"2026-01-01/report.PDF"` → `true`), since the regex is compiled with `RegexOptions.IgnoreCase`.

### FR-4: Test — invalid calendar date in an otherwise structurally-valid prefix is rejected
`IsValid` returns `false` when the date-shaped segment matches the regex (`\d{4}-\d{2}-\d{2}`) but does not represent a real calendar date.

**Acceptance criteria:**
- Test asserts `BlobPathValidator.IsValid("2026-13-01/report.pdf") == false` (month 13).
- At least one additional invalid-date case is covered, e.g. `"2026-02-30/report.pdf"` (February 30th does not exist) or `"2026-00-01/report.pdf"` (month 0).

### FR-5: Test — regex/structural mismatches are rejected
`IsValid` returns `false` for inputs that fail the format regex before the date check is ever reached.

**Acceptance criteria:**
- Test asserts `BlobPathValidator.IsValid("notadate/report.pdf") == false` (non-date-shaped prefix).
- Test asserts `BlobPathValidator.IsValid("2026-01-01/subdir/report.pdf") == false` (extra `/` segment — `[^/]+` cannot match a value containing `/`).
- Test asserts `BlobPathValidator.IsValid("2026-01-01/report.xlsx") == false` (wrong extension).
- Test asserts `BlobPathValidator.IsValid("2026-01-01report.pdf") == false` (missing `/` separator) — optional but recommended to round out the regex-mismatch group.

## Non-Functional Requirements

### NFR-1: Test discipline
- Use the project's established test stack: **xUnit** with `[Theory]`/`[InlineData]` for grouped cases and `[Fact]` for the single dedicated security test (FR-2), matching the style of `GetExpeditionDatesHandlerTests.cs` in the same folder (AAA comments, descriptive `MethodUnderTest_GivenCondition_ExpectsBehavior`-style names).
- No mocks, no test doubles, no `Moq` usage — `BlobPathValidator.IsValid` is a pure static method with no dependencies.
- The new test file must build with no warnings under the existing nullable-reference-types settings. Since `IsValid`'s parameter is `string blobPath` (non-nullable) but must be tested with a `null` input (FR-1), suppress or work around the nullable warning the same way sibling test files in this codebase already do when intentionally passing `null` to a non-nullable parameter (e.g. `null!` or `#pragma warning disable CS8625` around the specific call) — verify the codebase's existing convention during implementation and follow it.

### NFR-2: Determinism and isolation
- No real I/O, no clock, no shared mutable state between tests. `BlobPathValidator` is static and stateless (aside from a compiled, immutable `Regex` field), so no per-test setup/teardown is needed.

### NFR-3: Performance
- The full new test class must complete in well under 100 ms — there is no I/O and no async work (`IsValid` is synchronous).

### NFR-4: Coverage
- After this work, line and branch coverage of `BlobPathValidator.IsValid` must be 100% (all four guards, both taken and not-taken on each).

## Data Model
No data model changes. The class under test has no dependencies:
- `BlobPathValidator` (internal static class, `Anela.Heblo.Application.Features.ExpeditionListArchive` namespace) — accessible from `Anela.Heblo.Tests` via the existing `[InternalsVisibleTo("Anela.Heblo.Tests")]` grant on `Anela.Heblo.Application` (confirmed present in `backend/src/Anela.Heblo.Application/AssemblyInfo.cs`); no project file changes are needed.

## API / Interface Design

### Test file
`backend/test/Anela.Heblo.Tests/ExpeditionListArchive/BlobPathValidatorTests.cs` (new file, same folder as the other `ExpeditionListArchive` handler tests).

### Class shape (illustrative — exact grouping/case count decided at implementation time per the FRs above)
```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class BlobPathValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValid_NullOrWhitespace_ReturnsFalse(string? blobPath) { /* FR-1 */ }

    [Fact]
    public void IsValid_PathContainsDoubleDot_ReturnsFalse_BlocksPathTraversal() { /* FR-2 */ }

    [Theory]
    [InlineData("2026-01-01/report.pdf")]
    [InlineData("2026-01-01/report.PDF")]
    public void IsValid_WellFormedPath_ReturnsTrue(string blobPath) { /* FR-3 */ }

    [Theory]
    [InlineData("2026-13-01/report.pdf")]
    [InlineData("2026-02-30/report.pdf")]
    public void IsValid_DateShapedPrefixIsNotARealDate_ReturnsFalse(string blobPath) { /* FR-4 */ }

    [Theory]
    [InlineData("notadate/report.pdf")]
    [InlineData("2026-01-01/subdir/report.pdf")]
    [InlineData("2026-01-01/report.xlsx")]
    public void IsValid_StructuralMismatch_ReturnsFalse(string blobPath) { /* FR-5 */ }
}
```

## Dependencies
- **Project**: `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (already references `xunit`; no new package needed since no mocking is required).
- **Source under test**: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/BlobPathValidator.cs` — no production code changes.
- **Reference test for style**: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs`.

## Out of Scope
- Modifying `BlobPathValidator.cs` or any production code.
- Tests for `DownloadExpeditionListHandler` or `ReprintExpeditionListHandler` (the consumers of `BlobPathValidator`) — already covered by their own existing test files (`DownloadExpeditionListHandlerTests.cs`, `ReprintExpeditionListHandlerTests.cs`) or out of scope for this coverage gap.
- Any change to the regex pattern, date format, or validation rules themselves.
- Integration or end-to-end tests — this is a pure in-process unit test task.

## Open Questions
None.

## Status: COMPLETE
