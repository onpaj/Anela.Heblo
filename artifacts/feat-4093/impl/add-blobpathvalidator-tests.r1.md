# Implementation: add-blobpathvalidator-tests

## What was implemented

Added a new xUnit test class covering `BlobPathValidator.IsValid` (internal static class in
`Anela.Heblo.Application.Features.ExpeditionListArchive`), exercising all four guards in the
method: null/whitespace input, path-traversal (`..`) rejection, regex structural mismatch, and
semantic date validity (`DateOnly.TryParseExact`).

Before writing the file, the real implementation was read and each of the task context's proposed
test cases was manually traced against it:

```csharp
public static bool IsValid(string blobPath)
{
    if (string.IsNullOrWhiteSpace(blobPath)) return false;
    if (blobPath.Contains("..")) return false;
    if (!ValidBlobPathPattern.IsMatch(blobPath)) return false; // ^\d{4}-\d{2}-\d{2}/[^/]+\.pdf$ (IgnoreCase)
    var datePart = blobPath.Split('/')[0];
    return DateOnly.TryParseExact(datePart, "yyyy-MM-dd", out _);
}
```

Every proposed case's expected result matches the real behavior exactly — no corrections were
needed (see Notes for the full case-by-case trace summary). The proposed test file from the task
context was used verbatim.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/BlobPathValidatorTests.cs` — new test class with 6 test methods (2 `[Fact]`, 4 `[Theory]`) covering 13 individual cases across all four `IsValid` guards, plain `Assert.*` style, matching the sibling test files' AAA-comment convention.

## Tests

- `dotnet test ... --filter "FullyQualifiedName~BlobPathValidatorTests"` → **13/13 passed**, 0 failed.
- `dotnet test ... --filter "FullyQualifiedName~ExpeditionListArchive"` (full folder, includes the 3 sibling handler test classes plus the new one) → **36/36 passed**, 0 failed.
- Full-solution test suite (task context Step 5) was **deliberately skipped** per pipeline instructions due to concurrent-worktree `dotnet test` contention risk (known to hang at 0% CPU); deferred to CI.

## How to verify

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~BlobPathValidatorTests"
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/test/Anela.Heblo.Tests/ExpeditionListArchive/BlobPathValidatorTests.cs
grep -c "Assert\." backend/test/Anela.Heblo.Tests/ExpeditionListArchive/BlobPathValidatorTests.cs   # 6, > 0
grep -c "\.Should()" backend/test/Anela.Heblo.Tests/ExpeditionListArchive/BlobPathValidatorTests.cs # 0
```

## Notes

- **Verification of proposed expectations against real code** (all confirmed correct, no changes needed):
  - Null/empty/whitespace → `IsNullOrWhiteSpace` guard → false. Matches.
  - `"2026-01-01/../../admin.pdf"` contains `".."` as a literal substring → false via guard 2. Matches.
  - `"2026-01-01/report..pdf"` — the substring `".."` appears between `report` and `pdf` (`report` + `.` + `.` + `pdf`) → caught by the same `Contains("..")` guard before the regex even runs. Matches.
  - `"2026-01-01/report.pdf"` / `"2026-01-01/report.PDF"` — well-formed, regex is case-insensitive (`RegexOptions.IgnoreCase`), date parses → true. Matches.
  - `"2026-13-01/..."` and `"2026-02-30/..."` — both are digit-shaped enough to pass the regex (`\d{4}-\d{2}-\d{2}`) but fail `DateOnly.TryParseExact` (month 13, Feb 30 don't exist) → false via guard 4. Matches.
  - `"notadate/report.pdf"` (no date-shaped prefix), `"2026-01-01/subdir/report.pdf"` (extra `/` defeats `[^/]+`), `"2026-01-01/report.xlsx"` (wrong extension), `"2026-01-01report.pdf"` (missing `/` separator) — all fail the regex match → false via guard 3. Matches.
- Confirmed `internal static class BlobPathValidator` is reachable from the test project via `[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]` in `Anela.Heblo.Application/AssemblyInfo.cs` — no accessibility issue.
- Build produced 252 pre-existing warnings across the test project, none attributable to the new file (verified via `grep -i "BlobPathValidatorTests"` against the build output — no matches). The `null!` usage in the `[InlineData(null!)]` theory case does not introduce a CS8625 warning, as expected.
- Full-solution test suite run (task context Step 5) was deferred per explicit pipeline instruction, due to known `dotnet test` hang-at-0%-CPU behavior under concurrent sibling-worktree contention. Ran the filtered `BlobPathValidatorTests` (13/13 pass) and the full `ExpeditionListArchive` folder (36/36 pass) instead.
- No production code was touched — this is a test-only, coverage-gap change.
- Committed locally in the working directory (commit optional per pipeline instructions but harmless); the pipeline will commit+push on top as usual.

## PR Summary

Adds unit test coverage for `BlobPathValidator.IsValid`, a previously-untested internal helper in the ExpeditionListArchive feature that guards blob path access against traversal attacks and malformed paths. The new test class covers all four validation branches: null/whitespace rejection, `..` path-traversal rejection (including a `..` embedded inside a filename segment, not just as a standalone path segment), structural regex mismatches (wrong prefix shape, extra path segments, wrong extension, missing separator), and semantic date validity (date-shaped-but-invalid values like month 13 or February 30th). Each proposed test expectation was independently traced against the real implementation before being committed to the suite — all matched, so no corrections were required. Production code was not modified (coverage-gap task only).

### Changes
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/BlobPathValidatorTests.cs` — new test file, 6 test methods / 13 cases, 100% line and branch coverage of `BlobPathValidator.IsValid`.

## Status
DONE
