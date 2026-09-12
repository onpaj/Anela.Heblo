# Implementation Plan: Unit test coverage for `BlobPathValidator`

## Overview
Add a single new test file, `BlobPathValidatorTests.cs`, covering all four guards of `BlobPathValidator.IsValid` (null/whitespace, path traversal, invalid calendar date, regex/structural mismatch) plus the happy path, with a dedicated named test for the security-critical `".."` traversal check. No production code changes. One task is sufficient given the scope: a single new file with no dependencies to wire up.

### task: add-blobpathvalidator-tests
**Goal:** Create `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/BlobPathValidatorTests.cs` with full coverage of `BlobPathValidator.IsValid`'s four guards, following the plain-`Assert.*` style of the three sibling test files in the same folder, and confirm it builds, formats clean, and passes.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/BlobPathValidatorTests.cs`
- Reference (read-only, do not modify): `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/BlobPathValidator.cs`
- Reference (read-only, style model): `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class BlobPathValidatorTests
{
    [Theory]
    [InlineData(null!)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValid_NullOrWhitespace_ReturnsFalse(string? blobPath)
    {
        // Act
        var result = BlobPathValidator.IsValid(blobPath!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_PathContainsDoubleDot_ReturnsFalse_BlocksPathTraversal()
    {
        // Arrange — a path that would otherwise look structurally valid if the
        // traversal guard were removed or narrowed. This is the security-critical
        // check: if it regresses, a caller could reach blobs outside the expected
        // date-folder layout (see issue #4093).
        const string blobPath = "2026-01-01/../../admin.pdf";

        // Act
        var result = BlobPathValidator.IsValid(blobPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_PathContainsDoubleDot_InFilenameSegment_ReturnsFalse_BlocksPathTraversal()
    {
        // Arrange — ".." embedded inside the filename segment rather than as a
        // standalone path component; guards against a narrowed check that only
        // looks for "/../" instead of the literal substring "..".
        const string blobPath = "2026-01-01/report..pdf";

        // Act
        var result = BlobPathValidator.IsValid(blobPath);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("2026-01-01/report.pdf")]
    [InlineData("2026-01-01/report.PDF")]
    public void IsValid_WellFormedPath_ReturnsTrue(string blobPath)
    {
        // Act
        var result = BlobPathValidator.IsValid(blobPath);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("2026-13-01/report.pdf")]  // month 13 does not exist
    [InlineData("2026-02-30/report.pdf")]  // February 30th does not exist
    public void IsValid_DateShapedPrefixIsNotARealDate_ReturnsFalse(string blobPath)
    {
        // Act
        var result = BlobPathValidator.IsValid(blobPath);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("notadate/report.pdf")]             // prefix is not date-shaped
    [InlineData("2026-01-01/subdir/report.pdf")]     // extra "/" segment
    [InlineData("2026-01-01/report.xlsx")]           // wrong extension
    [InlineData("2026-01-01report.pdf")]             // missing "/" separator
    public void IsValid_StructuralMismatch_ReturnsFalse(string blobPath)
    {
        // Act
        var result = BlobPathValidator.IsValid(blobPath);

        // Assert
        Assert.False(result);
    }
}
```

- [ ] **Step 2: Run the new test class to verify all cases pass**

Run: `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~BlobPathValidatorTests"`
Expected: all tests in `BlobPathValidatorTests` pass (13 individual cases: 3 + 1 + 1 + 2 + 2 + 4).

- [ ] **Step 3: Run `dotnet format` and confirm no unrelated changes**

Run: `dotnet format --verify-no-changes` (from repo root, or `dotnet format Anela.Heblo.sln --verify-no-changes`)
Expected: no formatting violations in the new file. If violations are reported, run `dotnet format` and confirm the diff touches only `BlobPathValidatorTests.cs`.

- [ ] **Step 4: Full solution build**

Run: `dotnet build Anela.Heblo.sln`
Expected: build succeeds with no new warnings introduced by the new file (in particular, no CS8625 nullable-warning regression from the `null!` usage in Step 1 — `null!` is the standard suppression idiom and must not produce a warning).

- [ ] **Step 5: Full backend test suite**

Run: `dotnet test Anela.Heblo.sln`
Expected: full suite passes, including the new `BlobPathValidatorTests` class and all previously-passing tests (no regressions).

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/ExpeditionListArchive/BlobPathValidatorTests.cs
git commit -m "test: add BlobPathValidator unit tests covering all four validation guards

Closes #4093"
```

**Validation:**
- `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~BlobPathValidatorTests"` — all cases pass.
- `dotnet build Anela.Heblo.sln` — succeeds, no new warnings.
- `dotnet format --verify-no-changes` — clean (or a `dotnet format` diff limited to the new file).
- `dotnet test Anela.Heblo.sln` — full suite green, no regressions.
- Manual check: `grep -c "Assert\." backend/test/Anela.Heblo.Tests/ExpeditionListArchive/BlobPathValidatorTests.cs` returns a number > 0 and `grep -c "\.Should()" backend/test/Anela.Heblo.Tests/ExpeditionListArchive/BlobPathValidatorTests.cs` returns `0`, confirming the plain-`Assert.*` style convention (arch-review Decision 1) was followed.
- Coverage of `BlobPathValidator.IsValid` is 100% line and branch (all four guards, taken and not-taken).
