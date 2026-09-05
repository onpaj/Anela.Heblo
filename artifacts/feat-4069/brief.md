[arch-review] Invoices: GetRunningInvoiceImportJobsHandler filters by hardcoded magic string with no compile-time link to [DisplayName]

## Module
Invoices

## Finding
`backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs` (line 55) identifies invoice import jobs by matching a hardcoded string:

```csharp
.Where(job => job.JobName != null &&
              job.JobName.StartsWith("Import faktur:", StringComparison.OrdinalIgnoreCase))
```

This string must match the prefix of the `[DisplayName("Import faktur: {0}")]` attribute declared on `IInvoiceImportService.ImportInvoicesAsync` (`Services/IInvoiceImportService.cs:9`) and on the concrete `InvoiceImportService.ImportInvoicesAsync` (`Services/InvoiceImportService.cs:37`). There is no compile-time link between the two: renaming or localizing the display name silently causes the filter to return an empty list (running jobs become invisible to users) with no error or test failure.

## Why it matters
The filter is the only mechanism that feeds the "running jobs" endpoint (`GET /api/invoices/import/running-jobs`) and the frontend's `InvoiceImportRunningIndicator`. A silent mismatch produces a broken UI indicator with no observable exception — a maintenance trap as the codebase evolves.

## Suggested fix
Expose the display-name prefix as a constant so the handler can reference it rather than duplicating the literal:

```csharp
// In IInvoiceImportService.cs or a new InvoiceImportServiceConstants.cs
public static class InvoiceImportJobNames
{
    public const string ImportPrefix = "Import faktur:";
}
```

Then in the handler:
```csharp
job.JobName.StartsWith(InvoiceImportJobNames.ImportPrefix, StringComparison.OrdinalIgnoreCase)
```

And update the `[DisplayName]` attribute to interpolate the constant if desired (or at minimum leave a code comment linking the two). This is a one-file constant extraction — no logic changes.

---
_Filed by daily arch-review routine on 2026-09-04._
