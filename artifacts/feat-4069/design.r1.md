# Design: Extract Invoice Import Job Name Prefix Into a Shared Constant

## Component Design

### New component: `InvoiceImportServiceConstants`
- **Location:** `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportServiceConstants.cs` (new file)
- **Responsibility:** Sole owner of the invoice-import Hangfire job's display-name text. Every other component that needs the prefix or the full display-name format reads it from here instead of holding its own literal.
- **Shape:** `public static class` with two `public const string` members:
  - `ImportPrefix = "Import faktur:"` — the prefix used for `StartsWith` matching.
  - `DisplayNameFormat = $"{ImportPrefix} {{0}}"` — the full Hangfire `[DisplayName]` format string, derived from `ImportPrefix` as a compile-time constant interpolated string (valid under C# 10+/net8.0's default C# 12, verified to compile and to be usable as an attribute argument).
- **Consumers (unchanged responsibilities, only their string *source* changes):**
  - `IInvoiceImportService.ImportInvoicesAsync` — `[DisplayName(InvoiceImportServiceConstants.DisplayNameFormat)]` replaces the literal `[DisplayName("Import faktur: {0}")]`.
  - `InvoiceImportService.ImportInvoicesAsync` — same attribute change, kept textually identical to the interface as today.
  - `GetRunningInvoiceImportJobsHandler.Handle` — the `StartsWith` filter (line 55) reads `InvoiceImportServiceConstants.ImportPrefix` instead of the inline literal `"Import faktur:"`; the `StringComparison.OrdinalIgnoreCase` comparison and all other filtering/caching/exception-handling logic in `Handle` are untouched. Requires adding `using Anela.Heblo.Application.Features.Invoices.Services;` to the handler file (same assembly, no project-reference change).

No component's external contract changes: `IInvoiceImportService`'s method signature, `GetRunningInvoiceImportJobsHandler`'s MediatR request/response types, and the Hangfire job's runtime display name (`"Import faktur: {0}"` rendered with the actual argument) are all identical before and after this change. The only thing that moves is *where the literal text is authored* — from three independent occurrences to one.

## Data Schemas

Not applicable in the conventional sense — this change introduces no database schema, API request/response shape, or event payload. The only "shape" involved is the new constant class's own contract:

```csharp
public static class InvoiceImportServiceConstants
{
    public const string ImportPrefix = "Import faktur:";
    public const string DisplayNameFormat = $"{ImportPrefix} {{0}}"; // -> "Import faktur: {0}"
}
```

`GET /api/invoices/import/running-jobs`'s response shape (`IList<BackgroundJobInfo>`) is unchanged, as is the Hangfire job display-name text rendered to users/operators. No migration, persistence, or contract-versioning concerns apply.
