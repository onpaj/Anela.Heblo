# Design: Unit test coverage for `BlobPathValidator`

## Component Design

### `BlobPathValidatorTests` (new)
- **Location:** `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/BlobPathValidatorTests.cs`
- **Namespace:** `Anela.Heblo.Tests.ExpeditionListArchive`
- **Responsibility:** Exercise all four sequential guards inside `BlobPathValidator.IsValid`, with one dedicated, security-labeled test for the path-traversal guard.
- **Collaborators:** None. No mocks, no fixtures, no shared state. Calls the static `BlobPathValidator.IsValid(string)` directly.
- **Test groups** (per `arch-review.r1.md` Decision 2 and Decision 3):

| Group | Kind | Method name | Cases | Expected |
|---|---|---|---|---|
| 1. Null/whitespace | `[Theory]` | `IsValid_NullOrWhitespace_ReturnsFalse` | `null!`, `""`, `"   "` | `false` |
| 2. Path traversal (security) | `[Fact]` (+ optional 2nd `[Fact]`) | `IsValid_PathContainsDoubleDot_ReturnsFalse_BlocksPathTraversal` | `"2026-01-01/../../admin.pdf"`; optionally a 2nd fact `IsValid_PathContainsDoubleDot_InFilenameSegment_ReturnsFalse` for `"2026-01-01/report..pdf"` | `false` |
| 3. Happy path | `[Theory]` | `IsValid_WellFormedPath_ReturnsTrue` | `"2026-01-01/report.pdf"`, `"2026-01-01/report.PDF"` | `true` |
| 4. Invalid calendar date | `[Theory]` | `IsValid_DateShapedPrefixIsNotARealDate_ReturnsFalse` | `"2026-13-01/report.pdf"`, `"2026-02-30/report.pdf"` | `false` |
| 5. Regex/structural mismatch | `[Theory]` | `IsValid_StructuralMismatch_ReturnsFalse` | `"notadate/report.pdf"`, `"2026-01-01/subdir/report.pdf"`, `"2026-01-01/report.xlsx"`, `"2026-01-01report.pdf"` | `false` |

- **Assertion style:** Plain xUnit `Assert.True(...)` / `Assert.False(...)`, matching the four sibling files in the same folder (no FluentAssertions — see arch-review Decision 1).
- **Null handling:** The `null` case in Group 1 is passed as `[InlineData(null!)]` with the theory parameter typed `string?` (or `string` with `null!` suppressing CS8625 at the call site) — no file-wide pragma, no change to `IsValid`'s signature.

### No changes to `BlobPathValidator` itself
The design makes no production-code changes. `BlobPathValidator.IsValid`'s existing signature, guard order, and regex are treated as fixed inputs to the test design.

## Data Schemas

Not applicable — no persistence, no API request/response shapes, no event payloads are involved. The only "shape" in this design is the test's own input/output pairing:

```
Input:  string? blobPath
Output: bool    (true = accepted, false = rejected by one of the 4 guards)
```

No DTOs, no database schema, no serialization concerns.
