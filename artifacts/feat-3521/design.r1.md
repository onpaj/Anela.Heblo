# Design: Move user-identity resolution out of InvoiceClassificationService (ADR-005 compliance)

## Component Design

**`IInvoiceClassificationService` / `InvoiceClassificationService`**
- Signature change: `ClassifyInvoiceAsync(ReceivedInvoice invoice)` → `ClassifyInvoiceAsync(ReceivedInvoice invoice, string processedBy)`.
- Service becomes context-agnostic: no `ICurrentUserService` field, constructor parameter, or `GetCurrentUser()` call. The caller-supplied `processedBy` is used verbatim at all four existing `RecordClassificationHistory` call sites (no-match, success, ABRA-update-failure, catch block). No other logic changes.

**`ClassifyInvoicesHandler`**
- Identity resolution moves here, per ADR-005 (MediatR handlers are the only layer allowed to consume `ICurrentUserService`). New constructor dependency on `ICurrentUserService`, mirroring `CreateClassificationRuleHandler` / `UpdateClassificationRuleHandler`.
- `Handle` calls `_currentUserService.GetCurrentUser()` exactly once (before the `foreach`, not per invoice) and derives:
  - `processedBy = currentUser.IsAuthenticated ? (currentUser.Name is not null/empty ? currentUser.Name : "system") : "system"`
- The same `processedBy` value is passed into every `_classificationService.ClassifyInvoiceAsync(invoice, processedBy)` call within the batch.

**Callers unaffected**: `InvoiceClassificationJob` (hourly, no HTTP context) and any controller triggering `ClassifyInvoicesRequest` require no changes — they only interact with the unchanged `ClassifyInvoicesRequest`/`ClassifyInvoicesResponse` MediatR contract; the handler resolves identity internally via DI.

## Data Schemas

No schema or contract changes.
- `ClassificationHistory.ProcessedBy` — existing `string` column/property, unchanged shape. Only the source of the value changes (handler-resolved string instead of a service-internal lookup); scheduled-job runs now write `"system"` instead of the misleading `"Anonymous"`.
- `ClassifyInvoicesRequest` / `ClassifyInvoicesResponse` (public MediatR contract) — unchanged.
- `IInvoiceClassificationService.ClassifyInvoiceAsync` — internal application-layer interface, not exposed via HTTP/OpenAPI; no client regeneration needed.
- No database migration required.
