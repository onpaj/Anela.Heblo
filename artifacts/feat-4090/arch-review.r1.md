# Architecture Review: Remove handler-to-handler MediatR dispatch from ChangeTransportBoxStateHandler

## Skip Design: true

## Architectural Fit Assessment
This is a pure application-layer (Clean Architecture "Use Case" layer) internal refactor with no UI, no API contract, and no persistence-schema impact. It fits squarely with an existing, already-proven convention in the very same folder: `AddItemToBoxHandler` and `RemoveItemFromBoxHandler` (`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/{AddItemToBox,RemoveItemFromBox}/*Handler.cs`) both take an `AutoMapper.IMapper` dependency and build their response DTO with `_mapper.Map<TransportBoxDto>(transportBox)` directly against the in-memory, already-updated aggregate — no `IMediator` involved. `ChangeTransportBoxStateHandler` and `UpdateTransportBoxDescriptionHandler` are the two outliers in this folder that instead re-dispatch `GetTransportBoxByIdRequest` through `IMediator`. This review confirms the fix moves `ChangeTransportBoxStateHandler` onto the majority pattern already established and working in this module; there is no competing convention to reconcile.

The mapping infrastructure already exists and is already proven for this exact box→DTO shape: `TransportBoxMappingProfile.cs` (`backend/src/Anela.Heblo.Application/Features/Logistics/TransportBoxMappingProfile.cs`) registers the `TransportBox → TransportBoxDto` map that `GetTransportBoxByIdHandler`, `AddItemToBoxHandler`, and `RemoveItemFromBoxHandler` all already consume via constructor-injected `IMapper`. MediatR's DI registration resolves handler constructors by assembly scanning (standard `AddMediatR(...)` behavior, confirmed by every other handler in this folder taking arbitrary constructor dependencies without any explicit per-handler registration) — adding an `IMapper` constructor parameter to `ChangeTransportBoxStateHandler` requires zero new DI wiring.

No architectural amendment to the spec is needed; `spec.r1.md` already correctly identifies the target shape (`GetTransportBoxByIdResponse { TransportBox = _mapper.Map<TransportBoxDto>(box) }`) and correctly scopes `UpdateTransportBoxDescriptionHandler` out.

## Proposed Architecture

### Component Overview

Before (current):
```
ChangeTransportBoxStateHandler
  ├─ ITransportBoxRepository   (load + save box)
  ├─ IInventoryReservationService
  ├─ IMediator ──────────────► GetTransportBoxByIdHandler
  ├─ ILogger                          │
  ├─ ICurrentUserService              ├─ ITransportBoxRepository (2nd read, same row)
  ├─ ILogisticsStockOperationService  └─ IMapper (box → TransportBoxDto)
  └─ TimeProvider
```

After (target):
```
ChangeTransportBoxStateHandler
  ├─ ITransportBoxRepository   (load + save box)
  ├─ IInventoryReservationService
  ├─ IMapper ─────────────────► box → TransportBoxDto   (in-memory, no re-read)
  ├─ ILogger
  ├─ ICurrentUserService
  ├─ ILogisticsStockOperationService
  └─ TimeProvider
```

`GetTransportBoxByIdHandler` becomes purely a peer of `ChangeTransportBoxStateHandler` again (both independently reachable from the controller), not a dependency of it. No other component in the system depends on `ChangeTransportBoxStateHandler` calling `GetTransportBoxByIdHandler`, so removing the edge is safe in isolation.

### Key Design Decisions

#### Decision 1: Map the in-memory aggregate directly vs. keep the mediator re-fetch
**Options considered:**
1. Keep `_mediator.Send(new GetTransportBoxByIdRequest(...))` (status quo — rejected, this is the finding being fixed).
2. Call `GetTransportBoxByIdHandler` directly as a plain class (bypassing `IMediator` but still depending on the handler type) — rejected: still creates a handler-to-handler dependency, just without going through the mediator; does not address the "hidden coupling" and "testing cost" problems named in the brief, and is not how any other handler in this codebase composes cross-handler reuse.
3. Inject `IMapper` and map the already-in-memory, already-saved `box` directly — **chosen**.

**Rationale:** Option 3 is the only one that removes the coupling entirely and matches the codebase's existing convention (`AddItemToBoxHandler`, `RemoveItemFromBoxHandler`). It also removes the redundant database read and the redundant MediatR pipeline execution, which is the whole point of the finding. `box` is guaranteed up to date at the mapping point: it went through `transition.ChangeStateAsync(...)` and any location/description mutations, then `_repository.UpdateAsync(box, ...)` + `_repository.SaveChangesAsync(...)` — no code path re-reads or replaces `box` after that, so the in-memory reference reflects exactly what was persisted.

#### Decision 2: Response shape — keep `GetTransportBoxByIdResponse` wrapper vs. flatten to `TransportBoxDto`
**Options considered:**
1. Change `ChangeTransportBoxStateResponse.UpdatedBox` to `TransportBoxDto` directly — rejected: this is a public contract change. `ChangeTransportBoxStateResponse` is a DTO consumed by the MVC controller and, through OpenAPI generation, by the TypeScript client; changing its shape is out of scope for an internal-coupling fix and would force frontend changes with no functional benefit.
2. Keep `UpdatedBox: GetTransportBoxByIdResponse` and populate it by constructing `new GetTransportBoxByIdResponse { TransportBox = _mapper.Map<TransportBoxDto>(box) }` — **chosen**.

**Rationale:** Preserves the existing public response contract exactly (per this repo's DTO-stability rule: DTOs are classes with generated OpenAPI clients depending on stable shapes). `GetTransportBoxByIdResponse` is a plain data-holder class (extends `BaseResponse`, one nullable `TransportBoxDto?` property) — constructing it directly with `new` is not "calling the handler," it is just populating a DTO, which is architecturally clean.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. All changes are confined to two existing files:
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`

Verify (do not assume) whether `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs` references `IMediator` or `GetTransportBoxByIdRequest` in a way that depends on the handler's current dispatch — if it does, update it too; if (as expected for an integration test against real persistence) it does not, leave it untouched.

### Interfaces and Contracts
- Constructor signature change on `ChangeTransportBoxStateHandler`: remove `IMediator mediator`, add `AutoMapper.IMapper mapper` (place it adjacent to the other injected services, following the ordering style already used in `AddItemToBoxHandler`'s constructor: repository/domain services first, then `ILogger`, then `IMapper`, then `TimeProvider` — match whatever exact order the existing file uses for the parameters being removed/added rather than reordering unrelated parameters).
- `ChangeTransportBoxStateResponse.UpdatedBox` keeps its declared type `GetTransportBoxByIdResponse?` — unchanged.
- No new interfaces. No changes to `ITransportBoxRepository`, `GetTransportBoxByIdRequest`, or `GetTransportBoxByIdResponse`.

### Data Flow
1. Controller receives `ChangeTransportBoxStateRequest` → dispatches via `IMediator` (composition-root usage, unaffected by this change) → `ChangeTransportBoxStateHandler.Handle`.
2. Handler loads `box` via `_repository.GetByIdWithDetailsAsync`, mutates it, runs the state-specific callback (`CallBackMap`), executes the transition, persists via `_repository.UpdateAsync` + `SaveChangesAsync`.
3. **Changed step:** handler maps `box` → `TransportBoxDto` directly via injected `IMapper`, wraps it in a new `GetTransportBoxByIdResponse`, and returns it as `ChangeTransportBoxStateResponse.UpdatedBox`. No second repository read, no second MediatR dispatch, no invocation of `GetTransportBoxByIdHandler`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Mapped DTO diverges from what `GetTransportBoxByIdHandler` would have produced for the same box (e.g. if some field were populated by a DB round-trip side effect, like a computed column or trigger) | Low | `box` is the same tracked entity instance that was just saved via `UpdateAsync`/`SaveChangesAsync` on the same repository/DbContext; both this handler and `GetTransportBoxByIdHandler` use the identical `IMapper` profile (`TransportBoxMappingProfile`) against a `TransportBox` domain object, so output is structurally guaranteed identical absent a computed-column/trigger scenario. No such scenario exists in the current schema (confirm during implementation by inspecting `TransportBoxMappingProfile.cs` for any field sourced from something other than in-memory entity state). |
| Existing unit tests (`ChangeTransportBoxStateHandlerTests.cs`) fail to compile/pass after the constructor signature change, since the file has ~14 references to `_mediatorMock` across many test methods | Medium | FR-4 in the spec already scopes this: replace `Mock<IMediator>` with `Mock<IMapper>` (mirroring `AddItemToBoxHandlerTests`/`RemoveItemFromBoxHandlerTests`), remove all `GetTransportBoxByIdRequest`-specific mediator setups and the two `Times.Once` verifies on `_mediator.Send`. This is mechanical, not risky, but must be done fully in one pass or the build breaks. |
| A future contributor reintroduces the same anti-pattern in `UpdateTransportBoxDescriptionHandler` (which still has it) without realizing this fix set a precedent | Low | Out of scope for this issue by explicit instruction; not mitigated here. Left as a natural candidate for a follow-up arch-review finding, not part of this change. |

## Specification Amendments
None. `spec.r1.md` already correctly scopes the change, correctly identifies the exact code to change, correctly excludes `UpdateTransportBoxDescriptionHandler`, and correctly accounts for the `GetTransportBoxByIdResponse` wrapper shape. No amendment needed.

## Prerequisites
None. `IMapper` and the `TransportBox → TransportBoxDto` AutoMapper profile are already registered application-wide and already consumed successfully by three other handlers in this same folder (`GetTransportBoxByIdHandler`, `AddItemToBoxHandler`, `RemoveItemFromBoxHandler`). No migration, config, or infrastructure change is required before implementation can start.
