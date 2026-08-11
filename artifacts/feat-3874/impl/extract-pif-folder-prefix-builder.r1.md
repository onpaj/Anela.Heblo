# Implementation: extract-pif-folder-prefix-builder

## What was implemented
Extracted the duplicated PIF folder short-code/prefix derivation logic from `UploadPifDocumentHandler` and `ListPifDocumentsHandler` into a shared static helper `PifFolderPrefixBuilder.Build(string productCode)`, matching the existing `MaterialFilenameBuilder` convention in the same `Infrastructure` folder. Both handlers now call the shared helper instead of independently duplicating the truncation rule.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/PifFolderPrefixBuilder.cs` — new static helper, pure function, no DI registration needed
- `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/PifFolderPrefixBuilderTests.cs` — new unit tests covering >6, ==6, <6, and empty product codes
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadPifDocument/UploadPifDocumentHandler.cs` — replaced inline `shortCode`/`prefix` derivation with `PifFolderPrefixBuilder.Build(request.ProductCode)`
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListPifDocuments/ListPifDocumentsHandler.cs` — same substitution

## Tests
- `PifFolderPrefixBuilderTests` (4 new tests): product code longer than 6 chars truncates to first 6 + `__`; exactly 6 chars uses whole code; shorter than 6 uses whole code; empty string returns `__`.
- Full `CatalogDocuments` test suite (34 tests total, including the new ones and the pre-existing `MaterialFilenameBuilderTests`) passes with 0 failures.

## How to verify
```
cd backend
dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj   # 0 errors (pre-existing AccessMatrixGen post-build warning is unrelated, see Notes)
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~CatalogDocuments"
cd .. && dotnet format --verify-no-changes   # no errors in changed files (pre-existing unrelated errors in Overtime tests, see Notes)
```

## Notes
- `dotnet build`/`dotnet test` must be invoked from the `backend/` directory (project-level) or repo root (`dotnet format`, which needs `Anela.Heblo.sln` at the repo root) — there is no `.sln` inside `backend/` itself.
- The API project's post-build target invokes `tools/Anela.Heblo.AccessMatrixGen`, which throws an unhandled `JsonException` due to a pre-existing argument-order mismatch between the `.csproj` `Exec` command and `Program.cs`'s expected args. This reproduces identically on the unmodified `main` checkout and is a pre-existing, unrelated issue — MSBuild treats it as a non-fatal warning (`MSB3073`), and the build still reports `0 Error(s)`. Out of scope for this task.
- `dotnet format --verify-no-changes` reports pre-existing whitespace errors in `backend/test/Anela.Heblo.Tests/Application/Overtime/GetMonthlyStatementsHandlerTests.cs`, unrelated to this change (no PIF/CatalogDocuments files touched by this task have any formatting issues).
- No public interface, DTO, or error code changed. `MaterialFilenameBuilder` and the Materials handlers are untouched.

## Status
DONE
