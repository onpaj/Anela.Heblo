## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes
Two-file diff: `GetRunningInvoiceImportJobsHandler.cs` (predicate `Contains("InvoiceImport", ...)` → `StartsWith("Import faktur:", StringComparison.OrdinalIgnoreCase)`, plus an updated inline comment) and `GetRunningInvoiceImportJobsHandlerTests.cs` (mocked job names updated to the real `"Import faktur: ..."` format, with a regression case for the old broken string). Confirmed via `grep` that `[DisplayName("Import faktur: {0}")]` appears only on `InvoiceImportService.ImportInvoicesAsync` / `IInvoiceImportService`, so the new prefix match has no false-positive collision risk with any other background job's display name in this codebase. Independently verified `dotnet build` (0 errors) and `dotnet test --filter FullyQualifiedName~GetRunningInvoiceImportJobsHandlerTests` (5/5 passed) during the developer-loop review. No cross-cutting or scope concerns — the change matches the spec (FR-1, FR-2) and stays confined to the two files that needed it.
