# Specification: Extract Invoice Import Job Name Prefix Into a Shared Constant

## Summary
`GetRunningInvoiceImportJobsHandler` identifies running/pending invoice import jobs by matching the hardcoded literal `"Import faktur:"` against each Hangfire job's display name, while the actual display name is defined independently via `[DisplayName("Import faktur: {0}")]` on `IInvoiceImportService.ImportInvoicesAsync` and its implementation. This spec replaces the duplicated literal with a single shared constant so the two locations cannot silently drift apart. This is a constant-extraction refactor only — no behavioral or logic changes.

## Background
`GET /api/invoices/import/running-jobs` and the frontend's `InvoiceImportRunningIndicator` both depend on `GetRunningInvoiceImportJobsHandler` correctly identifying which Hangfire jobs are invoice-import jobs. That identification currently works by string-matching the job's display name (produced by Hangfire from the `[DisplayName("Import faktur: {0}")]` attribute) against a separately hardcoded literal in the handler. Because there is no compile-time or shared-constant link between the attribute and the filter, renaming, localizing, or reformatting the display name in one place would silently break the filter in the other — the running-jobs endpoint would start returning an empty list with no exception, no failing test, and no visible error, leaving users unable to see that an import is in progress. This was flagged by the automated architecture review as a maintainability trap and a one-file-scope fix is requested: introduce a shared constant that both the attribute and the handler reference.

## Functional Requirements

### FR-1: Introduce a shared constant for the invoice-import job name prefix
Define a single `public const string` holding the value `"Import faktur:"` (the prefix used for matching, without the `{0}` placeholder) in a location accessible to both `IInvoiceImportService`/`InvoiceImportService` and `GetRunningInvoiceImportJobsHandler`. Use the exact placement and naming suggested in the brief unless a clearer alternative consistent with existing codebase conventions is found during implementation:
- A static class such as `InvoiceImportJobNames` with a member `ImportPrefix`, placed either inside `IInvoiceImportService.cs` or in a new small file in `Anela.Heblo.Application/Features/Invoices/Services/`.

**Acceptance criteria:**
- A single constant exists whose value is `"Import faktur:"`.
- The constant is `public` (or otherwise accessible across the `Invoices` feature slice) and `const`/`static readonly`, consistent with existing constant patterns in the codebase.
- No second copy of the literal string `"Import faktur:"` remains anywhere in the touched files.

### FR-2: Handler references the shared constant instead of a hardcoded literal
Update `GetRunningInvoiceImportJobsHandler.Handle` so the `StartsWith` filter on line 55 uses the new constant instead of the inline string literal `"Import faktur:"`.

**Acceptance criteria:**
- The `.Where(job => job.JobName != null && job.JobName.StartsWith(..., StringComparison.OrdinalIgnoreCase))` clause references the shared constant (e.g. `InvoiceImportJobNames.ImportPrefix`) rather than a literal.
- The comparison still uses `StringComparison.OrdinalIgnoreCase` (unchanged behavior).
- No other logic in `Handle` (caching, exception handling, pending/running job concatenation) is modified.

### FR-3: `[DisplayName]` attribute stays consistent with the constant, documented or interpolated
Ensure the `[DisplayName("Import faktur: {0}")]` attribute on both `IInvoiceImportService.ImportInvoicesAsync` and `InvoiceImportService.ImportInvoicesAsync` is visibly and traceably tied to the same constant, so a future reader (or the compiler, where possible) is warned against changing one without the other.

**Acceptance criteria:**
- Attribute values are interpolated from the constant if C# attribute-argument constraints allow it (attribute arguments must be compile-time constants, so string interpolation via `$"{ConstantName}: {{0}}"` is achievable only if `ConstantName` is itself a `const string` in the same or a referenced assembly — verify this compiles; if the interpolated form is not usable as a compile-time constant in an attribute argument, fall back to the documentation approach below).
- If direct interpolation is not feasible as a compile-time constant, add a one-line code comment directly above each `[DisplayName]` attribute (on both the interface and the implementation) explicitly stating that its literal prefix must match `InvoiceImportJobNames.ImportPrefix`, and add a corresponding comment above the constant's declaration pointing back to both `[DisplayName]` usages.
- Both `[DisplayName]` occurrences (interface and implementation) remain textually identical to each other, as they are today.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected or required; this is a compile-time string-source change with no change to runtime control flow, allocations, or the existing in-memory cache (`GetRunningInvoiceImportJobsHandler.CacheKey`, `RunningJobsCacheSeconds`).

### NFR-2: Security
Not applicable. No change to authentication, authorization, or data sensitivity — this touches only an internal string constant used for in-process job-name matching.

## Data Model
Not applicable. No entities, persistence, or schema are affected. The change is confined to a compile-time string constant and its two consuming call sites.

## API / Interface Design
Not applicable — no public API contract, request/response DTO, or route changes. `GET /api/invoices/import/running-jobs` behavior and response shape (`IList<BackgroundJobInfo>`) are unchanged; this fix only changes where the matching prefix string is sourced from internally.

## Dependencies
- `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/IInvoiceImportService.cs`
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs`
- No external services, packages, or feature flags are involved.

## Out of Scope
- Any change to the actual display-name text, format, or localization of `"Import faktur: {0}"`.
- Any change to the job-matching algorithm itself (e.g. switching from a `StartsWith` string match to a Hangfire job-type/tag-based identification mechanism) — that would be a separate, larger architectural change and is explicitly not requested here.
- Any change to caching behavior, TTL configuration, or the `running-jobs` API contract.
- Adding new automated tests beyond what is needed to confirm the existing behavior is preserved (this is a refactor of a literal, not new functionality); if the project's test suite already covers `GetRunningInvoiceImportJobsHandler`'s filtering behavior, those existing tests must continue to pass unmodified in substance (updating only literal-string references to the new constant is acceptable).
- Any change to `InvoiceImportRunningIndicator` or other frontend code.

## Open Questions
None.

## Status: COMPLETE
