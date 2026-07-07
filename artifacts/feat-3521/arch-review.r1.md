# Architecture Review: InvoiceClassification ADR-005 Identity-Resolution Fix

## Skip Design: true

No UI, contract (`Contracts/`), controller, or HTTP-facing type changes. `IInvoiceClassificationService` is an internal application-layer interface not exposed via OpenAPI; `ClassifyInvoicesRequest`/`ClassifyInvoicesResponse` (the public MediatR contract) are explicitly unchanged per the spec. This is a pure internal signature change plus a call-site move, confirmed against both the spec's "API / Interface Design" section and the actual code below.

## Architectural Fit Assessment

This is a textbook ADR-005 convergence fix, not a new pattern. The codebase already has two correct reference implementations in the same module — `CreateClassificationRuleHandler` and `UpdateClassificationRuleHandler` — both of which inject `ICurrentUserService`, call `GetCurrentUser()` once in `Handle`, and pass `currentUser.Name` down into domain/service calls. `ClassifyInvoicesHandler` and `InvoiceClassificationService` are the only two files in this vertical slice that deviate from that shape.

Verified in code:
- `InvoiceClassificationService.cs:13,21,28,34` — constructor takes `ICurrentUserService currentUserService`, and `ClassifyInvoiceAsync` (line 34) calls `_currentUserService.GetCurrentUser()` once per invoice, inside the service.
- `ClassifyInvoicesHandler.cs` — no `ICurrentUserService` dependency at all today; `Handle` calls `_classificationService.ClassifyInvoiceAsync(invoice)` per invoice in a `foreach` loop (line 72) with no identity passed.
- `InvoiceClassificationJob.cs` sends `ClassifyInvoicesRequest` via `IMediator` on an hourly cron (`"0 * * * *"`) with no HTTP request in flight, confirming the runtime-bug half of the finding.
- `InvoiceClassificationModule.cs` registers `IInvoiceClassificationService` but never registers `ICurrentUserService` itself (it's registered once, module-wide, by `UsersModule.AddUsersModule()` at the API composition root) — so removing the dependency from `InvoiceClassificationService` requires no DI registration change in this module, only removal of the constructor parameter.

No new components, no new module boundaries, no schema changes. The fix is a mechanical application of an already-accepted, already-precedented pattern.

## Proposed Architecture

### Component Overview

```
Before:
  ClassifyInvoicesHandler.Handle
      └─> IInvoiceClassificationService.ClassifyInvoiceAsync(invoice)
              └─> ICurrentUserService.GetCurrentUser()   [WRONG LAYER: called from job with no HttpContext]
              └─> RecordClassificationHistory(..., currentUser.Name)

After:
  ClassifyInvoicesHandler.Handle
      ├─> ICurrentUserService.GetCurrentUser()           [resolved ONCE, in the handler]
      ├─> compute processedBy ("system" if unauthenticated, else Name)
      └─> IInvoiceClassificationService.ClassifyInvoiceAsync(invoice, processedBy)   [per invoice, in the loop]
              └─> RecordClassificationHistory(..., processedBy)
```

This mirrors `CreateClassificationRuleHandler.Handle` / `UpdateClassificationRuleHandler.Handle`, which resolve `currentUser` once at the top of `Handle` and pass `currentUser.Name` into the domain call.

### Key Design Decisions

#### Decision 1: Resolve identity once per `Handle` call, not per invoice
**Options considered:**
- (a) Resolve `GetCurrentUser()` inside the `foreach` loop, once per invoice.
- (b) Resolve it once before the loop and reuse the same value for every invoice in the batch.

**Chosen approach:** (b), per spec FR-3.

**Rationale:** The caller identity cannot change mid-`Handle` (it's fixed by the inbound HTTP request or the fact that there is none). Resolving once avoids redundant `IHttpContextAccessor`/claims-chain work per invoice and matches the existing single-resolution pattern in `CreateClassificationRuleHandler`/`UpdateClassificationRuleHandler`. It's also a net reduction in `ICurrentUserService` calls versus today (was 1/invoice inside the service; becomes 1/batch inside the handler).

#### Decision 2: Explicit `processedBy` parameter vs. threading identity through the request DTO
**Options considered:**
- (a) Add a `CallerName`/`ProcessedBy` field to `ClassifyInvoicesRequest` and have `InvoiceClassificationJob`/controllers populate it.
- (b) Keep `ClassifyInvoicesRequest` untouched; resolve identity fully inside `ClassifyInvoicesHandler.Handle` via DI, and pass the resolved string as an explicit method parameter to `IInvoiceClassificationService.ClassifyInvoiceAsync`.

**Chosen approach:** (b), per spec FR-1/FR-3 and the "API / Interface Design" section.

**Rationale:** Option (a) would violate ADR-005's spoofing-hole rule ("Request DTOs must not carry client-settable `UserId`/`ModifiedBy`") and would force `InvoiceClassificationJob` to know about identity resolution, which it correctly does not today. The handler already has direct DI access to `ICurrentUserService`; there is no reason to route identity through the MediatR request payload.

#### Decision 3: Fallback string for unauthenticated/background callers
**Options considered:**
- (a) Let `ProcessedBy` be empty/null when there's no HTTP context (today's de facto behavior, modulo the "Anonymous" string produced by `CurrentUserService`'s null-`HttpContext` fallback).
- (b) Use a fixed literal `"system"` whenever `IsAuthenticated == false`, and also fall back to `"system"` if `IsAuthenticated == true` but `Name` is unexpectedly empty.

**Chosen approach:** (b), per spec FR-3.

**Rationale:** `"system"` is self-describing in audit history (distinguishes automated runs from a blank/misleading "Anonymous"), and the double fallback (`IsAuthenticated == true` + empty `Name`) closes a latent null-string edge case that would otherwise resurface as a future arch-review finding. This is a value/interface-layer decision, not an architectural one — flagged here only because it affects the `IInvoiceClassificationService` contract's implicit guarantee ("caller always gets a non-empty `processedBy`").

## Implementation Guidance

### Directory / Module Structure
No new files or folders. All changes are edits to existing files, all within the `InvoiceClassification` module:
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/IInvoiceClassificationService.cs` — signature change.
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs` — remove `ICurrentUserService` field/ctor param/call; use the new `processedBy` parameter at all four `RecordClassificationHistory` call sites (lines 44-45, 60-61, 74-75, 89-90).
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs` — add `ICurrentUserService` ctor param; resolve `currentUser`/`processedBy` once before the `foreach` (before line 68); pass `processedBy` into the `ClassifyInvoiceAsync` call (line 72).
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationModule.cs` — no change expected (confirmed: it does not register `ICurrentUserService` today, so nothing to remove there); verify at implementation time that this stays true.
- Test files: `InvoiceClassificationServiceTests` and `ClassifyInvoicesHandlerTests` (exact paths under `backend/test/Anela.Heblo.Tests/...InvoiceClassification/...` — not read in this review; locate via the existing test project structure mirrored from `Application/Features/InvoiceClassification`).

### Interfaces and Contracts
```csharp
// IInvoiceClassificationService.cs — changed
Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoice invoice, string processedBy);
```
No other public interface changes. `ClassifyInvoicesRequest`/`ClassifyInvoicesResponse` (the MediatR contract used by controllers and `InvoiceClassificationJob`) are unchanged — confirmed no controller or job code needs to change.

### Data Flow
1. HTTP request (authenticated) or `InvoiceClassificationJob` (hourly, unauthenticated) → `IMediator.Send(ClassifyInvoicesRequest)`.
2. `ClassifyInvoicesHandler.Handle` resolves `currentUser = _currentUserService.GetCurrentUser()` exactly once.
3. Handler computes `processedBy`: `currentUser.IsAuthenticated ? (currentUser.Name ?? "system") : "system"`.
4. For each invoice in the batch, handler calls `_classificationService.ClassifyInvoiceAsync(invoice, processedBy)` — same `processedBy` value for every invoice in the run.
5. `InvoiceClassificationService.ClassifyInvoiceAsync` uses the passed-in `processedBy` at all four `RecordClassificationHistory` call sites (no-match, success, ABRA-failure, exception-catch branches) — no other logic changes.
6. `ClassificationHistory.ProcessedBy` is persisted with either the real authenticated user's name or the literal `"system"`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed call site: one of the four `RecordClassificationHistory` calls in `InvoiceClassificationService` still references a removed `currentUser` variable, causing a compile error (not a silent bug, since it won't build) | Low | Compile immediately after edit; `dotnet build` will catch any stale reference — no `currentUser` local should remain in the file. |
| Existing unit tests fail to compile because `ClassifyInvoicesHandlerTests`/`InvoiceClassificationServiceTests` construct the handlers/services with the old constructor signatures | Medium | Update both test files in the same change per spec FR-4; this is required for the build to pass, not optional follow-up. |
| Historical `ClassificationHistory` rows already have `ProcessedBy = "Anonymous"` from prior scheduled runs, and a reviewer might expect a backfill | Low | Explicitly out of scope per spec — no data migration needed; call this out in the PR description so it isn't mistaken for a partial fix. |

## Specification Amendments
None. The specification is implementation-ready as written: FR-1 through FR-4 fully cover the interface change, the dependency removal, the handler-side resolution, and the required test updates, and they match the actual code read during this review (line numbers, call-site count, module registration state, and the `CurrentUser` record shape — `Id`/`Name`/`Email`/`IsAuthenticated` — all verified).

## Prerequisites
None. No migrations, no config, no infrastructure changes, no other module needs to change first. This can be implemented directly against `main`/the current branch.
