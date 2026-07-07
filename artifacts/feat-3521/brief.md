# Issue #3521: [arch-review] InvoiceClassification: InvoiceClassificationService resolves user identity, causing a latent bug in the scheduled job (ADR-005 violation)

## Module
InvoiceClassification

## Finding
`InvoiceClassificationService` injects `ICurrentUserService` and calls `_currentUserService.GetCurrentUser()` at line 34 of `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs` to populate the `processedBy` field in classification history records.

ADR-005 mandates that user identity is resolved **inside MediatR handlers**, not in application services. The `ClassifyInvoicesHandler` (`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`) is the correct location for identity resolution — it currently delegates to `IInvoiceClassificationService` without passing a caller identity.

Beyond the architectural violation, this creates a concrete runtime bug: `InvoiceClassificationJob` (`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Infrastructure/Jobs/InvoiceClassificationJob.cs`) triggers `ClassifyInvoicesHandler` on a schedule, with no HTTP request context. `ICurrentUserService` depends on `IHttpContextAccessor`; called from a background thread, `HttpContext` is `null`, so either the service throws or returns empty/null user data, meaning every scheduled classification run records an empty `ProcessedBy` in history.

## Why it matters
- Violates ADR-005 (accepted decision: identity in handlers only).
- Produces silent data corruption: history records written by the hourly job will have `ProcessedBy = ""` (or throw), making audit history unreliable for automated runs.
- `IInvoiceClassificationService`'s dependency on `ICurrentUserService` is invisible to callers — nothing in the interface signature indicates that an HTTP context is required.

## Suggested fix
1. Add a `string processedBy` parameter to `IInvoiceClassificationService.ClassifyInvoiceAsync` (and the concrete implementation).
2. In `ClassifyInvoicesHandler.Handle`, resolve identity via injected `ICurrentUserService` and pass `currentUser.Name` (or `"system"` when `currentUser` is anonymous/null) to `ClassifyInvoiceAsync`.
3. Remove `ICurrentUserService` from `InvoiceClassificationService`'s constructor and the DI registration.

This is the same pattern already used correctly in `CreateClassificationRuleHandler` and `UpdateClassificationRuleHandler`.

---
_Filed by daily arch-review routine on 2026-07-07._
