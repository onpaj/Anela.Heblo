### task: extract-invoice-import-prefix-constant
**Goal:** Introduce `InvoiceImportServiceConstants` (`ImportPrefix` + compiler-derived `DisplayNameFormat`) and make the two `[DisplayName]` attributes and the `GetRunningInvoiceImportJobsHandler` filter all reference it, eliminating every duplicate copy of the literal `"Import faktur:"`.

**Files:**
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportServiceConstants.cs` — create: new `public static class InvoiceImportServiceConstants` with `public const string ImportPrefix = "Import faktur:";` and `public const string DisplayNameFormat = $"{ImportPrefix} {{0}}";` (namespace `Anela.Heblo.Application.Features.Invoices.Services`, matching the sibling files in that folder). Include the doc-comments shown in `design.r1.md` explaining that `DisplayNameFormat` and both `[DisplayName]` usages derive from `ImportPrefix` and must not be hand-edited independently.
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/IInvoiceImportService.cs` — modify: change `[DisplayName("Import faktur: {0}")]` on `ImportInvoicesAsync` to `[DisplayName(InvoiceImportServiceConstants.DisplayNameFormat)]`. No `using` needed — same namespace.
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs` — modify: change the matching `[DisplayName("Import faktur: {0}")]` on `ImportInvoicesAsync` (line 37) to `[DisplayName(InvoiceImportServiceConstants.DisplayNameFormat)]`. No `using` needed — same namespace.
- `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs` — modify: add `using Anela.Heblo.Application.Features.Invoices.Services;`, then change the `.Where(job => job.JobName != null && job.JobName.StartsWith("Import faktur:", StringComparison.OrdinalIgnoreCase))` clause (line ~55) to use `InvoiceImportServiceConstants.ImportPrefix` in place of the string literal. Leave the code comment above it, the `StringComparison.OrdinalIgnoreCase` argument, and all other logic (caching, exception handling, pending/running concatenation) untouched.

**Steps:**
1. Create `InvoiceImportServiceConstants.cs` with the two `const string` members and doc-comments as specified above.
2. Update the `[DisplayName]` attribute on `IInvoiceImportService.ImportInvoicesAsync` to reference `InvoiceImportServiceConstants.DisplayNameFormat`.
3. Update the `[DisplayName]` attribute on `InvoiceImportService.ImportInvoicesAsync` to reference `InvoiceImportServiceConstants.DisplayNameFormat`, keeping it textually identical to the interface's attribute.
4. Add the `using Anela.Heblo.Application.Features.Invoices.Services;` import to `GetRunningInvoiceImportJobsHandler.cs` and replace the inline `"Import faktur:"` literal in the `StartsWith` filter with `InvoiceImportServiceConstants.ImportPrefix`.
5. Grep the three modified files (and the new file) to confirm no literal occurrence of `"Import faktur:"` remains outside the new constants file.
6. Do not modify any test file — `InvoiceImportServiceTests.cs` and `GetRunningInvoiceImportJobsHandlerTests.cs` assert the resolved runtime string value (`"Import faktur: {0}"` / job names starting with `"Import faktur:"`), which is unchanged by this refactor and must keep passing unmodified.

**Validation:**
- `cd backend && dotnet build` — must succeed (also confirms the const-interpolated-string attribute argument compiles as verified in the architecture review).
- `cd backend && dotnet format --verify-no-changes` — must report no formatting violations (or run `dotnet format` and confirm it makes no unexpected changes beyond the touched lines).
- `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~InvoiceImportServiceTests"` — all tests in this class pass, including `InvoiceImportService_HasCorrectDisplayNameAttribute` (asserts `attribute.DisplayName == "Import faktur: {0}"`).
- `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~GetRunningInvoiceImportJobsHandlerTests"` — all tests in this class pass, including the ones asserting jobs named `"Import faktur: ..."` are correctly identified as running/pending.
- Manual check: `grep -rn "Import faktur:" backend/src/` should show exactly one occurrence — inside `InvoiceImportServiceConstants.cs`'s `ImportPrefix` declaration (the `DisplayNameFormat` line references `ImportPrefix` via interpolation, not a second literal).
