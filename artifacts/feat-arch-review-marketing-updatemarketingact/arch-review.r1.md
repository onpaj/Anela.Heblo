# Architecture Review: Marketing Module — Consistent DB Save Error Handling for Update and Delete Handlers

## Skip Design: true

Backend-only behavior fix in three MediatR handlers. No new UI, no screens, no visual decisions. The externally visible change is "API returns a structured error envelope instead of HTTP 500" — same envelope shape the rest of the module already produces, so no frontend work is implied.

## Architectural Fit Assessment

This change is a defect repair against an already-established convention, not a new pattern. The Marketing module already has the canonical implementation:

- `CreateMarketingActionHandler` (lines 87–112) wraps `SaveChangesAsync` in try/catch, logs the inconsistency with structured placeholders, attempts an Outlook compensation, and returns `CreateMarketingActionResponse(ErrorCodes.DatabaseError)`.
- `BaseResponse` already exposes the `Success` / `ErrorCode` / `Params` envelope used module-wide; every Marketing response class already has the `(ErrorCodes, Dictionary<string,string>?)` constructor.
- `ErrorCodes.DatabaseError = 0011` exists in `Anela.Heblo.Application.Shared.ErrorCodes` with `HttpStatusCode.InternalServerError` attribute — no new enum value needed.
- The MediatR pipeline + controller layer already translates these envelopes to the correct HTTP status; existing tests in `CreateMarketingActionHandlerTests.Handle_CompensatesOutlookEvent_WhenDbSaveFails` prove the pattern works end-to-end.

The only deviations are in `UpdateMarketingActionHandler.cs:112-113` and `DeleteMarketingActionHandler.cs:75-76`, which let `SaveChangesAsync` / `DeleteSoftAsync` exceptions propagate to MediatR. Fixing them brings these handlers into line with the module's existing Vertical Slice + structured-response convention — there is no architectural debate, only an execution gap.

Two integration points need verification but should be no-ops:

1. **Repository contract** — `IMarketingActionRepository.DeleteSoftAsync` performs its own `SaveChangesAsync` internally (brief.md states this and the existing handler has no follow-up save call). The catch must therefore wrap the `DeleteSoftAsync` call itself, not a separate save.
2. **MediatR pipeline behaviors** — There is no global exception-to-envelope behavior in this module (otherwise the current code would not produce 500s, and the existing Create handler's try/catch would be redundant). The fix must happen inside each handler.

## Proposed Architecture

### Component Overview

```
Controller (existing, unchanged)
    │
    ▼
MediatR pipeline (existing, unchanged)
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│ MarketingAction handlers (per-use-case)                     │
│                                                             │
│  ┌──────────────────────┐ ┌──────────────────────┐ ┌──────┐ │
│  │ CreateMarketingActi… │ │ UpdateMarketingActi… │ │ Dele…│ │
│  │ (reference impl —    │ │ (add guarded save)   │ │ (add │ │
│  │  unchanged)          │ │                      │ │ guar…│ │
│  └─────────┬────────────┘ └─────────┬────────────┘ └──┬───┘ │
│            │                        │                 │     │
│            └────────────────────────┴─────────────────┘     │
│                              │                              │
│             try { repo save } catch (Exception)            │
│             → log {ActionId},{EventId}, "out of sync"      │
│             → return Response(ErrorCodes.DatabaseError)     │
└─────────────────────────────────────────────────────────────┘
    │                                            │
    ▼                                            ▼
IMarketingActionRepository                IOutlookCalendarSync
(UpdateAsync, SaveChangesAsync,          (CreateEventAsync,
 DeleteSoftAsync)                         UpdateEventAsync,
                                          DeleteEventAsync)
```

The dashed boundary above is the only thing changing. No new components, interfaces, services, packages, configuration, DI registration, controller routes, or database objects.

### Key Design Decisions

#### Decision 1: Inline try/catch in each handler vs. extracted helper

**Options considered:**
- (A) Add three near-identical try/catch blocks inline (one in each handler).
- (B) Extract a shared helper / base class / MediatR `IPipelineBehavior` that runs the DB save and produces a `DatabaseError` response.

**Chosen approach:** (A) — inline in each handler.

**Rationale:** The catch bodies are not identical: the Create handler also runs a compensating Outlook delete; the Update handler logs "may now be out of sync"; the Delete handler logs "Outlook event already deleted". The compensation logic, log phrasing, and response *type* all differ. Spec §Out of Scope explicitly says "three near-identical try/catch blocks are acceptable" and NFR-4 ("a future developer can copy any of them as a template") is satisfied by symmetry, not by abstraction. A pipeline behavior cannot inject the per-handler log message or response type without ambient state, and would also catch errors thrown by *non-Outlook* operations (e.g. `GetByIdAsync`) we do not want to remap to `DatabaseError`. YAGNI: keep the surface area small.

#### Decision 2: What to catch — `Exception` vs. a narrower type

**Options considered:**
- (A) `catch (Exception)` — matches the existing Create handler precedent.
- (B) `catch (DbUpdateException)` / `catch (DbException)` — narrower, only intercepts persistence-layer errors.

**Chosen approach:** (A) `catch (Exception)` — match the Create handler exactly.

**Rationale:** Consistency with the established reference implementation (FR-4) is explicitly required, and the spec's stated goal is to ensure *any* DB-save failure surfaces as `DatabaseError` rather than 500. Narrowing the catch would re-introduce inconsistency and risk missing wrapped exceptions (`DbUpdateConcurrencyException`, broker-level timeouts wrapped by EF, etc.). `OperationCanceledException` from a caller-cancelled token will be caught and mapped to `DatabaseError`; this is acceptable because a cancellation after a successful Outlook write still represents an out-of-sync state the operator needs to know about — and that is what the log message says.

#### Decision 3: Where the catch goes in `DeleteMarketingActionHandler`

**Options considered:**
- (A) Wrap only the `DeleteSoftAsync` call (which internally does `SaveChangesAsync`).
- (B) Add an explicit `SaveChangesAsync` after `DeleteSoftAsync` and wrap both.

**Chosen approach:** (A).

**Rationale:** `IMarketingActionRepository.DeleteSoftAsync` already commits internally — the current handler never calls `SaveChangesAsync` separately. Adding a second save would either be a no-op or change repository semantics. Wrap the single call.

#### Decision 4: Update handler — wrap save only, or wrap update+save together

**Options considered:**
- (A) Wrap both `UpdateAsync` and `SaveChangesAsync` in one try/catch.
- (B) Wrap only `SaveChangesAsync`.

**Chosen approach:** (A) — mirror the Create handler, which has `AddAsync` outside the try but only because `AddAsync` is purely in-memory tracking. In EF Core both `UpdateAsync` and `SaveChangesAsync` *can* throw (`UpdateAsync` may trigger change-tracker work; in some repository implementations it touches the context). The spec's `FR-1` says "wrap the `UpdateAsync` + `SaveChangesAsync` calls".

**Rationale:** Defer to the spec. Cost of including `UpdateAsync` in the try block is zero on the happy path.

#### Decision 5: Log severity and structured fields

**Chosen approach:** `LogError` with structured placeholders `{ActionId}` and `{EventId}`, and the literal phrase from the spec:
- Update: `"DB save failed after Outlook update for MarketingAction {ActionId}; Outlook event {EventId} may now be out of sync"`
- Delete: `"DB soft-delete failed after Outlook delete for MarketingAction {ActionId}; Outlook event {EventId} already deleted — DB row still present"`

**Rationale:** FR-3 requires structured placeholders (NOT interpolation) and the greppable phrases "may now be out of sync" / "already deleted". NFR-2 forbids logging marketing-action payload (title/description); only IDs are logged. The exception object is the first argument to `LogError`, matching the Create handler.

## Implementation Guidance

### Directory / Module Structure

No new directories, no new files. Touch exactly three production files and update two test files:

```
backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/
  UpdateMarketingAction/UpdateMarketingActionHandler.cs   ← modify lines 112–113
  DeleteMarketingAction/DeleteMarketingActionHandler.cs   ← modify lines 75–76
  CreateMarketingAction/CreateMarketingActionHandler.cs   ← unchanged (FR-4)

backend/test/Anela.Heblo.Tests/Application/Marketing/
  UpdateMarketingActionHandlerTests.cs   ← add DB-failure test
  DeleteMarketingActionHandlerTests.cs   ← add DB-failure test
  CreateMarketingActionHandlerTests.cs   ← unchanged
```

### Interfaces and Contracts

No interface changes. The following already exist and must be reused as-is:

- `IMarketingActionRepository.UpdateAsync(MarketingAction, CancellationToken)` and `SaveChangesAsync(CancellationToken)` (verified in `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs` via inheritance from `IRepository<MarketingAction,int>`).
- `IMarketingActionRepository.DeleteSoftAsync(int id, string userId, string username, CancellationToken)` — performs its own commit.
- `UpdateMarketingActionResponse(ErrorCodes, Dictionary<string,string>?)` constructor (verified `UpdateMarketingActionRequest.cs:41`).
- `DeleteMarketingActionResponse(ErrorCodes, Dictionary<string,string>?)` constructor (verified `DeleteMarketingActionRequest.cs:19`).
- `ErrorCodes.DatabaseError` with `[HttpStatusCode(HttpStatusCode.InternalServerError)]` (verified `ErrorCodes.cs:35`).

API behavior change (not a contract change): `PUT` and `DELETE` endpoints will return `500 InternalServerError` carrying `{ success: false, errorCode: "DatabaseError" }` instead of an unhandled 500 page. The HTTP status is unchanged because `DatabaseError` itself maps to 500 — only the body shape becomes structured.

### Data Flow

#### Update — failure path after this fix
```
Client → PUT /api/marketing-actions/{id}
  → UpdateMarketingActionHandler.Handle
    → GetByIdAsync                              [unchanged]
    → mutate action fields                       [unchanged]
    → IOutlookCalendarSync.UpdateEventAsync      [succeeds — Outlook now updated]
    → try {
        UpdateAsync(action)
        SaveChangesAsync()                       [throws e.g. DbUpdateException]
      } catch (Exception ex) {
        _logger.LogError(ex, "...{ActionId}...{EventId}...may now be out of sync", id, eventId)
        return new UpdateMarketingActionResponse(ErrorCodes.DatabaseError)
      }
  → controller serializes envelope → HTTP 500 + structured body
```

#### Delete — failure path after this fix
```
Client → DELETE /api/marketing-actions/{id}
  → DeleteMarketingActionHandler.Handle
    → GetByIdAsync                              [unchanged]
    → IOutlookCalendarSync.DeleteEventAsync     [succeeds — Outlook event gone]
    → try {
        _repository.DeleteSoftAsync(id, ...)    [throws]
      } catch (Exception ex) {
        _logger.LogError(ex, "...{ActionId}...{EventId}...already deleted", id, eventId)
        return new DeleteMarketingActionResponse(ErrorCodes.DatabaseError)
      }
  → controller serializes envelope → HTTP 500 + structured body
```

#### Happy paths
Unchanged. No new branches in success paths.

### Testing Approach

The existing test fixtures (`xUnit` + `Moq` + `FluentAssertions`, `Mock<ILogger<T>>` already in test classes) cover everything needed. Each new test:

1. Reuses the existing constructor's default mocks.
2. Overrides `SaveChangesAsync` / `DeleteSoftAsync` to `ThrowsAsync(new Exception("DB unavailable"))`.
3. Asserts `result.Success == false`, `result.ErrorCode == ErrorCodes.DatabaseError`.
4. Verifies the logger's `LogError` was called via `_logger.Verify(...)` matching on the message-template fragment (`"may now be out of sync"` / `"already deleted"`) and that `ActionId` and `EventId` appear in the structured state. The Create test (`Handle_CompensatesOutlookEvent_WhenDbSaveFails`) does *not* assert the log content today, but spec FR-1/FR-2 explicitly require asserting an error-level log — see Specification Amendments below for the recommended `Mock<ILogger<T>>.Verify` pattern.
5. For Update: verifies Outlook update was attempted but **not** reverted (out of scope per spec).
6. For Delete: verifies Outlook delete was attempted but **not** recreated.

API-level integration test for HTTP 500 body shape is **NOT** added in this change unless there is an existing integration-test harness for Marketing endpoints — the search did not find one. Spec FR-1/FR-2 say "API integration test… returns the structured error envelope". Treat that as aspirational unless an existing pattern exists; the unit tests are the hard gate. See Specification Amendments.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `Mock<ILogger<T>>.Verify` for structured log messages is fragile (matches on the message template, not the rendered string) | Low | Use the documented `It.Is<It.IsAnyType>((v,t) => v.ToString()!.Contains("may now be out of sync"))` pattern; this is already in use elsewhere in the test suite — check `backend/test/Anela.Heblo.Tests/` for prior examples before writing new boilerplate. |
| Catching `Exception` swallows `OperationCanceledException` and converts caller cancellation into `DatabaseError` | Low | Acceptable: a cancellation after a successful Outlook write *is* a divergence the operator needs to learn about. Matches the existing Create handler. If team disagrees, add `catch (OperationCanceledException) { throw; }` ahead of the general catch — note this would deviate from the Create reference. |
| Update handler logs `action.OutlookEventId` which may be **freshly set** by `MarkOutlookSynced` on this request (line 77/82). If the Outlook step created a new event (`else` branch at 81), the logged ID is the new one — correct. | None | This is the desired behavior; the new ID is what's now in Outlook. |
| Spec asks for the "Outlook event ID" but Update handler may not have one (push disabled). | Low | When `_options.CurrentValue.PushEnabled == false`, no Outlook write happened, so there is no drift on DB failure — the log message can still be emitted (`{EventId}` will be `null`/empty), and an operator who sees a null `EventId` knows Outlook was not the issue. No code branch needed. |
| Catching after Update sets `action.MarkOutlookSynced` — the in-memory mutation has already happened on the now-stale tracked entity. EF context state may be poisoned for the next request if the repository is scoped per-request. | None | MediatR handlers in this codebase are scoped per-request (standard ASP.NET Core DI). The `DbContext` is discarded at request end. No leak between requests. |
| Compensation parity question: should Update handler also attempt Outlook revert (mirroring Create)? | None | Explicitly out of scope per spec §Out of Scope. Do not add. |
| Tests written today fail tomorrow when team adopts a global MediatR exception-handling behavior. | Low | The structured envelope shape (`{ Success, ErrorCode, Params }`) is the contract; a future global behavior would have to produce the same envelope. Tests assert against the envelope, not the absence of a behavior. Acceptable. |

## Specification Amendments

1. **FR-1 / FR-2 — clarify log assertion mechanism.** The spec says "asserts that an error-level log entry is emitted containing the action ID and the Outlook event ID." With `Mock<ILogger<T>>` this is done by `_logger.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v,_) => v.ToString()!.Contains("...")), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType,Exception?,string>>()))`. Recommend the spec's acceptance criteria be read as "assertion against the rendered message template substring," not literal field-by-field structured-state introspection.

2. **FR-1 / FR-2 — API integration test.** The spec lists this as a hard acceptance criterion. The codebase does not appear to have a `WebApplicationFactory`-based integration harness for Marketing endpoints (search did not surface one). Recommend amending acceptance criteria to "unit tests prove handler returns the structured envelope; an integration test is added *only if* an existing harness covers similar endpoints." If no harness exists, do not introduce one in this change — that is a separate, much larger effort.

3. **FR-3 — exact phrase for Delete handler.** The spec says "Outlook event already deleted" should be greppable. The Delete handler's Outlook step already logs `"Outlook event {EventId} already deleted (404)"` (line 62-64) for the *successful* 404 case. To avoid grep collisions, the DB-failure log should use a more specific phrase. Recommend: `"DB soft-delete failed after Outlook delete; Outlook event {EventId} already deleted — DB row {ActionId} still present"`. This keeps the "already deleted" greppable token while making the DB-failure case unambiguous.

4. **FR-4 — clarify "no functional change."** The spec says no functional change to `CreateMarketingActionHandler`. This explicitly forbids extracting a shared helper that the Create handler would also call. Accepted as-is — Decision 1 above reinforces this.

## Prerequisites

None. All required infrastructure exists:

- ✅ `ErrorCodes.DatabaseError = 0011` exists with the correct HTTP status mapping.
- ✅ `UpdateMarketingActionResponse` and `DeleteMarketingActionResponse` both have the `(ErrorCodes, Dictionary<string,string>?)` constructor.
- ✅ `ILogger<UpdateMarketingActionHandler>` and `ILogger<DeleteMarketingActionHandler>` are already injected.
- ✅ Repository methods (`UpdateAsync`, `SaveChangesAsync`, `DeleteSoftAsync`) exist on `IMarketingActionRepository`.
- ✅ Test fixtures with `Mock<ILogger<T>>`, `Mock<IMarketingActionRepository>`, `TestOptionsMonitor<MarketingCalendarOptions>` already wired up for both handlers.
- ✅ No new packages, no DI registration, no config keys, no DB migration, no Key Vault secret.

Implementation can start immediately. Total expected diff: ~30 LOC of production code + ~50 LOC of tests.