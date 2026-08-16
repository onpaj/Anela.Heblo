# Code Review: extract-pif-folder-prefix-builder

## Summary
The implementation is a clean, minimal extraction that matches the task spec and prescribed code almost verbatim — a new pure static helper `PifFolderPrefixBuilder.Build(string productCode)` in `Features/CatalogDocuments/Infrastructure/`, consumed identically by both `UploadPifDocumentHandler` and `ListPifDocumentsHandler`, with no leftover inline duplication anywhere in the codebase. Build succeeds with 0 errors, `dotnet format` shows no issues in the touched files, and the full `CatalogDocuments` test suite (34 tests, including the 4 new `PifFolderPrefixBuilderTests`) passes with 0 failures.

## Review Result: PASS

### task: extract-pif-folder-prefix-builder
**Status:** PASS

## Overall Notes
- **FR-1** (shared helper): `PifFolderPrefixBuilder.Build` is a pure static function (no I/O, no DI, no logging), placed in `Infrastructure/` alongside `MaterialFilenameBuilder`, and reproduces the exact truncation rule (first 6 chars, or whole code if shorter, + `"__"`) — verified by tracing all four boundary cases (>6, ==6, <6, empty) against the four new unit tests, all of which pass.
- **FR-2 / FR-3** (handler call sites): Both `UploadPifDocumentHandler.Handle` and `ListPifDocumentsHandler.Handle` now call `PifFolderPrefixBuilder.Build(request.ProductCode)` in place of the inline `shortCode`/`prefix` computation. Confirmed via `git show d3a5b89` and direct file reads — the diff is a pure line-for-line substitution; `prefix` is still passed to `FindFolderAsync` and used in the `NotFound`/`ExpectedPrefix` payloads exactly as before. A repo-wide grep for the old inline pattern (`ProductCode[..6]` / `ProductCode.Length >= 6`) confirms no duplicate logic remains anywhere.
- **FR-4** (tests): `PifFolderPrefixBuilderTests.cs` covers all four required cases (length > 6, == 6, < 6, and empty), styled consistently with the sibling `MaterialFilenameBuilderTests` (xUnit `[Fact]`, FluentAssertions, same namespace convention). Existing handler-adjacent and `MaterialFilenameBuilderTests` tests are untouched and still pass (34/34 total in the `CatalogDocuments` filter).
- **Architecture adherence**: Matches the `MaterialFilenameBuilder` pattern exactly as directed by `arch-review.r1.md` — same folder, same static-class/static-method shape, same call convention (`ClassName.Build(...)`, no interface, no DI registration).
- **Correctness**: No logic errors. This is byte-for-byte the same computation as the original inline code (confirmed by reading the diff — the extracted method body is a literal copy of the removed inline lines). No new error handling is needed since none existed before and behavior is unchanged (NFR-1).
- **Scope discipline**: `MaterialFilenameBuilder` and the Materials handlers are untouched, as required. No public interface, DTO, or error code changed.
- **Verification performed directly**: `dotnet build` (Application project) → 0 errors; `dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` → 0 errors; `dotnet test --filter "FullyQualifiedName~CatalogDocuments"` → 34 passed, 0 failed; `dotnet format --verify-no-changes` → no issues reported for any PIF/CatalogDocuments file. The pre-existing `AccessMatrixGen` post-build warning and the pre-existing Overtime test formatting issue both reproduced exactly as documented and were not counted against this task.
