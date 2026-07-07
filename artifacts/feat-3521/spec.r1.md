# Specification: Move user-identity resolution out of InvoiceClassificationService (ADR-005 compliance)

## Summary
`InvoiceClassificationService` currently resolves the caller's identity itself (via `ICurrentUserService`) to stamp `ProcessedBy` on classification history records. This violates ADR-005, which mandates that identity resolution happens only inside MediatR handlers, and it causes the hourly `InvoiceClassificationJob` — which runs with no HTTP context — to write history records with a misleading `ProcessedBy` value instead of a real caller name. This spec moves identity resolution into `ClassifyInvoicesHandler` and makes `ProcessedBy` an explicit, required input to the service.

## Background
`ClassifyInvoicesHandler.Handle` calls `IInvoiceClassificationService.ClassifyInvoiceAsync(invoice)` for each invoice. Inside that service, `ClassifyInvoiceAsync` calls `_currentUserService.GetCurrentUser()` (line 34 of `InvoiceClassificationService.cs`) purely to obtain a display name for the `processedBy` field passed into `RecordClassificationHistory`.

Two problems result:
1. **Architectural**: `ICurrentUserService` is an HTTP-request-scoped concern. ADR-005 requires it be consumed only in MediatR handlers, not in application services further down the call stack, so that services remain context-agnostic and testable without HTTP concerns. `CreateClassificationRuleHandler` and `UpdateClassificationRuleHandler` already follow the correct pattern (resolve `currentUser` in the handler, pass the resolved name into the aggregate/service call).
2. **Runtime bug**: `InvoiceClassificationJob` is a scheduled `IRecurringJob` invoked hourly by the job scheduler, with no HTTP request in flight. It calls `_mediator.Send(new ClassifyInvoicesRequest(...))`, which reaches `ClassifyInvoicesHandler` and then `InvoiceClassificationService.ClassifyInvoiceAsync`. At that point `IHttpContextAccessor.HttpContext` is `null`. Per the concrete `CurrentUserService.GetCurrentUser()` implementation (`backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs`), a null `HttpContext` does not throw — it falls through to `user = null`, `isAuthenticated = false`, and `name = "Anonymous"`. So in practice every hourly job run writes `ClassificationHistory.ProcessedBy = "Anonymous"` rather than throwing or leaving it empty; the brief's characterization of the symptom (empty/throwing) does not exactly match the current fallback string, but the underlying defect — audit history for automated runs never reflects that the job (not a person) performed the classification — is confirmed and is exactly the bug this fix addresses.

Fixing this also removes a hidden dependency: `IInvoiceClassificationService`'s interface gives no indication that an HTTP context is required to call it, which makes it easy for future callers (e.g., other background jobs) to hit the same bug.

## Functional Requirements

### FR-1: `IInvoiceClassificationService.ClassifyInvoiceAsync` takes an explicit `processedBy` parameter
Change the interface signature from:
```csharp
Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoice invoice);
```
to:
```csharp
Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoice invoice, string processedBy);
```
Update `InvoiceClassificationService.ClassifyInvoiceAsync` to use the passed-in `processedBy` parameter (instead of `_currentUserService.GetCurrentUser().Name`) everywhere it currently passes `currentUser.Name` into `RecordClassificationHistory` (the "no matching rule" branch, the success branch, the ABRA-update-failure branch, and the catch block).

**Acceptance criteria:**
- `IInvoiceClassificationService.ClassifyInvoiceAsync` has signature `Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoice invoice, string processedBy)`.
- All four `RecordClassificationHistory` call sites in `InvoiceClassificationService` pass the `processedBy` parameter value as the `processedBy`/`currentUser.Name` argument.
- No behavior other than the source of the `processedBy` value changes (matching logic, ABRA update calls, exception handling, and returned `InvoiceClassificationResult` are unchanged).

### FR-2: `InvoiceClassificationService` no longer depends on `ICurrentUserService`
Remove the `ICurrentUserService _currentUserService` field, the constructor parameter, and the `_currentUserService.GetCurrentUser()` call from `InvoiceClassificationService`.

**Acceptance criteria:**
- `InvoiceClassificationService`'s constructor no longer accepts `ICurrentUserService`.
- `InvoiceClassificationService.cs` no longer references `ICurrentUserService`, `_currentUserService`, or `Anela.Heblo.Domain.Features.Users` (unless still needed for another type — verify and remove the `using` if unused).
- `InvoiceClassificationModule.AddInvoiceClassificationModule` requires no change for `ICurrentUserService` registration (it was never registered there — `ICurrentUserService` is registered at the API composition-root level and continues to be used by other handlers); confirm no dangling/unused registration exists for this service that was specific to `InvoiceClassificationService`.

### FR-3: `ClassifyInvoicesHandler` resolves identity and passes it down
Inject `ICurrentUserService` into `ClassifyInvoicesHandler`. In `Handle`, resolve `var currentUser = _currentUserService.GetCurrentUser();` once per `Handle` invocation (not once per invoice), and compute a `processedBy` string:
- If `currentUser.IsAuthenticated` is `true`, use `currentUser.Name` (falling back to `"system"` if `Name` is null or empty even though authenticated, to avoid ever writing a null/empty `ProcessedBy`).
- If `currentUser.IsAuthenticated` is `false` (the case for the scheduled job, since there is no HTTP context), use the literal string `"system"`.

Pass this single `processedBy` value into every `_classificationService.ClassifyInvoiceAsync(invoice, processedBy)` call inside the `foreach` loop.

**Acceptance criteria:**
- `ClassifyInvoicesHandler`'s constructor accepts `ICurrentUserService` (added as a new constructor parameter, following the same DI pattern as `CreateClassificationRuleHandler`).
- `Handle` calls `_currentUserService.GetCurrentUser()` exactly once per invocation, not inside the per-invoice loop.
- When invoked with no HTTP context (`IsAuthenticated == false`, as happens for `InvoiceClassificationJob`), every `ClassificationHistory` row written during that run has `ProcessedBy == "system"`.
- When invoked from an authenticated HTTP request (e.g., a manual "reclassify" trigger from the UI, if one exists, or any future authenticated caller), `ProcessedBy` is set to the authenticated user's `Name`.
- All invoices processed within a single `Handle` call receive the same `processedBy` value (it is not re-resolved per invoice).

### FR-4: Update existing unit tests to match the new signatures
`ClassifyInvoicesHandlerTests` and `InvoiceClassificationServiceTests` currently construct `ClassifyInvoicesHandler` without `ICurrentUserService` and `InvoiceClassificationService` with `ICurrentUserService`; both must be updated to compile and to verify the new behavior.

**Acceptance criteria:**
- `InvoiceClassificationServiceTests`: `InvoiceClassificationService` is constructed without a `ICurrentUserService` mock; each test calls `_sut.ClassifyInvoiceAsync(invoice, processedBy)` with an explicit `processedBy` test value and asserts `capturedHistory.ProcessedBy` equals that value (replacing the current `currentUser.Name` assertions).
- `ClassifyInvoicesHandlerTests`: `ClassifyInvoicesHandler` is constructed with a `Mock<ICurrentUserService>`. At least one test asserts that when the mocked `GetCurrentUser()` returns `IsAuthenticated == false` (simulating the background-job scenario), `_classificationServiceMock` receives `ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>(), "system")`. At least one test covers the authenticated case, asserting `ClassifyInvoiceAsync` is called with the authenticated user's `Name`.
- All existing assertions unrelated to identity resolution (invoice fetching, parallelism, error counting) continue to pass unmodified.
- `dotnet build` and the full `Anela.Heblo.Tests` suite (at minimum the `InvoiceClassification` test files) pass after the change.

## Non-Functional Requirements

### NFR-1: Performance
N/A — this change replaces one `GetCurrentUser()` call per invoice (inside the service, in the per-invoice path) with one `GetCurrentUser()` call per `Handle` invocation (in the handler, outside the per-invoice loop). This is a net reduction in calls to `ICurrentUserService`, not a regression.

### NFR-2: Security
No change to authentication/authorization. `ICurrentUserService` continues to be resolved only where an `IHttpContextAccessor`-backed implementation is meaningful (inside the MediatR handler, which is invoked either from an authenticated HTTP request or from the internal scheduler). No new identity data is exposed; the fix makes an existing audit field (`ProcessedBy`) more accurate, which is a net data-integrity improvement for audit history.

## Data Model
No schema changes. `ClassificationHistory.ProcessedBy` (existing `string` field) continues to be populated the same way structurally; only the source of the value changes (handler-resolved `processedBy` string instead of a service-internal `ICurrentUserService` lookup). No migration required.

## API / Interface Design
- **Changed internal interface**: `IInvoiceClassificationService.ClassifyInvoiceAsync(ReceivedInvoice invoice)` → `IInvoiceClassificationService.ClassifyInvoiceAsync(ReceivedInvoice invoice, string processedBy)`. This is an internal application-layer interface (not exposed via HTTP/OpenAPI), so no client regeneration or contract-versioning concerns apply.
- No changes to `ClassifyInvoicesRequest`/`ClassifyInvoicesResponse` (the public MediatR contract used by controllers and `InvoiceClassificationJob`) — identity resolution stays fully internal to `ClassifyInvoicesHandler.Handle` and does not need to be threaded through the request DTO.
- No HTTP endpoint, route, or UI changes.

## Dependencies
- `ICurrentUserService` (`Anela.Heblo.Domain.Features.Users`) — already registered at the API composition root; only the consumer moves from `InvoiceClassificationService` to `ClassifyInvoicesHandler`.
- No new external dependencies.

## Out of Scope
- Changing `ADR-005` itself or auditing other modules for the same violation (this fix addresses only the `InvoiceClassification` module, per the filed issue).
- Adding a caller-identity parameter to `ClassifyInvoicesRequest` (unnecessary — the handler already has direct access to `ICurrentUserService` via DI).
- Backfilling or correcting historical `ClassificationHistory` rows that already have `ProcessedBy = "Anonymous"` from prior scheduled runs.
- Any change to `InvoiceClassificationJob` itself — it is already correctly context-agnostic; the fix is entirely within the handler/service layer it calls into.

## Open Questions
None.

## Status: COMPLETE
