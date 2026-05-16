# Architecture Review: Move User ID Resolution Out of LogisticsController

## Skip Design: true

Backend-only refactor. No UI, no new visual components, no design decisions.

## Architectural Fit Assessment

The intent is sound and aligned with `docs/architecture/development_guidelines.md` (ADR-003, "Business logic in Controller class" forbidden). The current code violates the rule.

**However, exploration reveals the spec is built on a false premise.** The user-id GUID being resolved in the controller is **dead code**:

- `CreateGiftPackageManufactureRequest.UserId` and `DisassembleGiftPackageRequest.UserId` are written by the controller but **read by nothing**.
- `CreateGiftPackageManufactureHandler` does not reference `request.UserId` (see `CreateGiftPackageManufactureHandler.cs:15-22`).
- `DisassembleGiftPackageHandler` does not reference `request.UserId` (see `DisassembleGiftPackageHandler.cs:16-23`).
- The downstream `GiftPackageManufactureService` already injects `ICurrentUserService` and uses `_currentUserService.GetCurrentUser().Name ?? "System"` (see `GiftPackageManufactureService.cs:191, 272`) — a **string Name**, not a GUID.
- The domain `GiftPackageManufactureLog.CreatedBy` is a `string` (`GiftPackageManufactureLog.cs:11`); no `UserId Guid` column is persisted.
- The frontend already sends a placeholder `userId: "00000000-0000-0000-0000-000000000000"` with the comment "This will be overridden by the backend from current user context" (`frontend/src/components/pages/GiftPackageManufacturing/index.tsx:79`).
- Confirmed via grep: `request.UserId` is referenced nowhere in the Logistics application/domain/persistence layers; only the two controller assignments exist.

**Implication:** FR-2's acceptance criterion — "the resolved GUID flows into the same downstream calls that previously consumed `request.UserId`" — is unfulfillable, because no downstream call consumes it today. FR-3's `UserIdResolver` helper resolves a value nobody reads. The "byte-for-byte identical behavior" in FR-5 is satisfied trivially by **deleting the dead resolution entirely**.

The existing pattern for GUID resolution in this same module (`CreateNewTransportBoxHandler.cs:43`, `OpenOrResumeBoxByCodeHandler.cs:72`) uses `Guid.TryParse(currentUser.Id, out var userId) ? userId : null` (nullable, no system-user sentinel). The sentinel GUID `00000000-0000-0000-0000-000000000001` is unique to `LogisticsController` and not referenced elsewhere — there is no shared convention to preserve.

The correct architectural move is **delete-and-simplify**, not extract-and-relocate.

## Proposed Architecture

### Component Overview

```
Before:
  LogisticsController ──► [GUID parse + sentinel fallback] ──► request.UserId (unread) ──► Handler ──► Service
       │
       └── injects ICurrentUserService (unused elsewhere in controller)

After:
  LogisticsController ──► Handler ──► Service ──► ICurrentUserService.GetCurrentUser().Name
  (one-line action)              (unchanged)        (unchanged — already there)
```

No new components. No helper class. No handler-level resolver. The service already owns the only real user-context dependency (the `Name` used for audit).

### Key Design Decisions

#### Decision 1: Delete the resolution, do not relocate it

**Options considered:**
- (A) Spec's proposal: extract `UserIdResolver` helper, inject into both handlers, fall back to sentinel GUID `00000000-0000-0000-0000-000000000001`.
- (B) Move the resolution block verbatim into each handler.
- (C) Delete the resolution and the DTO `UserId` properties entirely; no handler changes beyond DTO field removal.

**Chosen approach:** (C).

**Rationale:**
- The GUID is never read. Relocating dead code does not improve the system — it just hides the deadness one layer deeper.
- The sentinel `00000000-0000-0000-0000-000000000001` is a single-use literal with no semantic meaning to any downstream consumer; preserving it is not "byte-for-byte identical behavior," it is "byte-for-byte identical noise."
- YAGNI: extracting `UserIdResolver` for a non-consumer creates an abstraction that exists only to satisfy a spec acceptance criterion, not a real call site.
- If a future audit feature adds a `UserId Guid` column to `GiftPackageManufactureLog`, that work will require a migration, domain change, and service change — at that point the resolver belongs in the service (next to the existing `_currentUserService` usage), aligned with the TransportBox pattern (`Guid.TryParse(...) ? userId : null`, no sentinel).

#### Decision 2: No handler-level `ICurrentUserService` dependency

**Options considered:**
- (A) Add `ICurrentUserService` to both handlers (spec's FR-2).
- (B) Leave handlers untouched.

**Chosen approach:** (B).

**Rationale:** Handlers in this slice are thin orchestrators over `IGiftPackageManufactureService`. The service is the layer that needs and already has `ICurrentUserService`. Adding the dependency to handlers would either duplicate the service's usage or pass the resolved GUID into a service method that does not accept it. Neither is justified by a real consumer.

#### Decision 3: Remove `UserId` from request DTOs

Aligned with the spec (FR-4). The property has no server-side reader and the only client sender (`GiftPackageManufacturing/index.tsx:79`) sets it to a sentinel that the backend was already overriding. Removing it from the DTO removes the field from the OpenAPI-generated TypeScript client, which is a net improvement to the contract.

## Implementation Guidance

### Directory / Module Structure

No new files. No new modules. No DI registration changes.

Files modified:
- `backend/src/Anela.Heblo.API/Controllers/LogisticsController.cs` — remove lines 79–90 and 104–115; remove `ICurrentUserService` field, constructor parameter, and `using Anela.Heblo.Domain.Features.Users;`.
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureRequest.cs` — remove `UserId` property.
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/DisassembleGiftPackage/DisassembleGiftPackageRequest.cs` — remove `UserId` property.
- `frontend/src/components/pages/GiftPackageManufacturing/index.tsx` — remove the `userId` field from the request body at line 79 (TypeScript build will require this once the regenerated client drops the field).
- `frontend/src/api/hooks/__tests__/useGiftPackageManufacturing.test.ts` — remove `userId` from mock request literals at lines 72, 310, 333.

Files NOT touched:
- `CreateGiftPackageManufactureHandler.cs` — no change.
- `DisassembleGiftPackageHandler.cs` — no change.
- `GiftPackageManufactureService.cs` — no change.
- `GiftPackageManufactureModule.cs` — no change.
- `LogisticsModule.cs` — no change.
- Backend handler/service tests — no change (none reference `request.UserId`).

### Interfaces and Contracts

- **HTTP contract:** `POST /api/logistics/gift-packages/manufacture` and `POST /api/logistics/gift-packages/disassemble` request bodies lose the `userId` field. Response shapes unchanged. Status codes unchanged.
- **OpenAPI client:** Regenerates on `dotnet build`; the TypeScript request interfaces lose the `userId` property.
- **`ICurrentUserService`:** Untouched.
- **Domain types:** Untouched.

### Data Flow

For both endpoints, the post-refactor flow is:

```
HTTP request ──► Controller (one-line _mediator.Send) ──► Handler ──► IGiftPackageManufactureService
                                                                       │
                                                                       └── _currentUserService.GetCurrentUser().Name → CreatedBy (string)
```

Persisted `CreatedBy` value is identical to today. No GUID is persisted today and none after the refactor.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Frontend `index.tsx` still ships `userId` in the body after the OpenAPI client drops the field, causing a TypeScript build break. | High | Update the frontend call site in the same PR as the DTO removal. The `npm run build` gate catches it. |
| Frontend Jest mocks at `useGiftPackageManufacturing.test.ts:72, 310, 333` reference `userId` and will fail to type-check after client regeneration. | Medium | Strip `userId` from those literals in the same PR. |
| Reviewer assumes the spec is correct and asks why no `UserIdResolver` was created. | Low | Document in the PR description that grep confirms `request.UserId` has zero readers; relocating dead code adds no value. Link to this review. |
| Dashboard controller (`DashboardController.cs:49`) has the same dead-code pattern. Out of scope per spec, but reviewers may flag it. | Low | Explicitly note "out of scope, identical pattern exists in `DashboardController`" in the PR description; do not touch it. |
| Future requirement adds a real `UserId Guid` audit column. | Low | Document in `memory/decisions/` that the audit column should be added at the service layer alongside existing `_currentUserService` usage, using the nullable-Guid pattern from `CreateNewTransportBoxHandler.cs:43` (no sentinel). |
| Latent third-party consumer relies on the `userId` field in the request body. | Low | The field is server-overridden today and documented as such in the frontend comment; no external API consumers exist (single-tenant app). Acceptable risk. |

## Specification Amendments

The spec must be amended to reflect codebase reality before implementation begins. Required changes:

1. **FR-2 (Handlers resolve the acting user's ID): REMOVE.** No handler needs to resolve a GUID because no downstream code consumes it. The audit string (`CreatedBy`) is already resolved by `GiftPackageManufactureService` via its existing `ICurrentUserService` dependency.

2. **FR-3 (Shared resolution helper): REMOVE.** No helper is needed; there is no resolution to share. The sentinel GUID literal is removed entirely, not relocated — satisfying NFR-4's "appears exactly once" by reducing the count to zero.

3. **FR-4 (Remove `UserId` from DTOs): KEEP, with added frontend scope.** Explicitly add to acceptance criteria:
   - Remove `userId` from the request body in `frontend/src/components/pages/GiftPackageManufacturing/index.tsx` (line 79).
   - Remove `userId` from mock request literals in `frontend/src/api/hooks/__tests__/useGiftPackageManufacturing.test.ts` (lines 72, 310, 333).

4. **FR-5 (Behavior preservation): CLARIFY.** "Byte-for-byte identical" means: identical HTTP responses, identical persisted `CreatedBy` string, no new persisted columns. The sentinel GUID was never persisted and is not preserved.

5. **NFR-3 (Testability): REMOVE.** No new handler-level resolution exists to test. Existing service tests (`GiftPackageManufactureServiceTests`) already cover `ICurrentUserService` mocking.

6. **Out of Scope: ADD entry.** Adding a real `UserId Guid` audit column to `GiftPackageManufactureLog` (would require schema migration, domain change, service change; align with `TransportBox` nullable-Guid pattern when undertaken).

## Prerequisites

None. No migrations, no config changes, no infrastructure work.

Build/test gates that must pass:
- `dotnet build` (regenerates the OpenAPI TypeScript client without the `userId` fields).
- `dotnet format`.
- `dotnet test` for affected projects.
- `npm run build` in `frontend/` (will fail loudly if any consumer still sends `userId`).
- `npm run lint` in `frontend/`.
- `npm test` in `frontend/` (catches the three mock literals listed above).