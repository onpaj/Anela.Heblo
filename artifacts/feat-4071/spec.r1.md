# Specification: Extract Transport Box State-Transition Side Effects from ChangeTransportBoxStateHandler

## Summary
`ChangeTransportBoxStateHandler` (Logistics module) currently combines transition orchestration with five distinct per-transition side-effect behaviors inside one 330-line class, dispatched through a hand-rolled `CallBackMap` dictionary of private methods. This refactor extracts each side-effect into an independently testable, independently extensible strategy component, leaving the handler responsible only for orchestration (fetch box, validate transition, apply mutations, dispatch strategy, persist, respond). No externally observable behavior, API contract, or database schema changes.

## Background
The handler implements `IRequestHandler<ChangeTransportBoxStateRequest, ChangeTransportBoxStateResponse>` and is invoked whenever a transport box's state changes (New→Opened, Opened→Reserve, Opened→Quarantine, InTransit/Reserve/Quarantine→Received). Beyond the ~156-line `Handle()` method, it embeds four private per-transition methods (`HandleNewToOpened`, `HandleOpenToQuarantine`, `HandleOpenToReserve`, `HandleReceived`) plus a private helper (`RestoreInventoryForItemsAsync`), selected via a static `Dictionary<Tuple<TransportBoxState, TransportBoxState>, Func<...>>`.

This shape has two costs, per the filed finding (`artifacts/feat-4071/brief.md`):
1. **Testability** — the private methods are hidden branch points; today they can only be exercised indirectly through `Handle()`, requiring a full set of unrelated mocks (repository, mediator, inventory service, stock operation service) to test any single transition's side effect.
2. **Open/Closed violation** — adding a new state transition with a side effect requires editing the handler class (new private method + new `CallBackMap` entry), rather than adding a new, independent unit.

This is a pure internal refactor of an existing, already-shipped code path. It must preserve behavior byte-for-byte from the caller's perspective; the only change is internal structure.

## Functional Requirements

### FR-1: Per-transition side effects become independently testable strategy units
Each of the four existing transition side effects — `HandleNewToOpened` (box-code uniqueness check + closing stale `Stocked` boxes sharing the code), `HandleOpenToQuarantine` (no-op today, kept as an explicit strategy for symmetry and future extension), `HandleOpenToReserve` (required-location validation), `HandleReceived` (aggregate items by product code, stage one `StockUpOperation` per product) — is extracted into its own class implementing a shared strategy interface, e.g. `ITransportBoxTransitionSideEffect` (naming is an architecture decision, see arch-review).

**Acceptance criteria:**
- Each strategy class has a single public entry point taking exactly the inputs its current private-method signature takes today (`TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken`) and returning `Task<ChangeTransportBoxStateResponse?>` (non-null = short-circuit with a failure response; null = continue).
- Each strategy class can be unit-tested in isolation, constructed directly with only the dependencies that specific transition actually needs (e.g. the `HandleReceived` strategy needs only `ILogisticsStockOperationService` + `ILogger`; it must not need `IInventoryReservationService`).
- Existing behavior for each of the four transitions is unchanged: same error codes, same params, same side effects, same log messages/levels.

### FR-2: Handler retains only orchestration responsibility
`ChangeTransportBoxStateHandler.Handle()` keeps: fetching the box, assigning box code/location, resolving the transition via `TransitionNode.GetTransition`, evaluating `transition.Condition`, applying location/description updates, dispatching to the resolved side-effect strategy (if any is registered for the `(currentState, newState)` pair), executing `transition.ChangeStateAsync`, performing the existing inventory-restore special case (Opened→New), persisting via the repository, and building the response — plus the existing top-level exception-to-error-code mapping (`catch` blocks for `TransportBoxCodeRequiredException`, `TransportBoxCodeFormatException`, `TransportBoxEmptyException`, `TransportBoxInvalidStateTransitionException`, `ValidationException`, generic `Exception`).

**Acceptance criteria:**
- No per-transition business logic (product-code aggregation, code-uniqueness check, location requiredness check) remains inline in `Handle()` or as a private method on the handler after the refactor.
- The dispatch mechanism (dictionary keyed by `(TransportBoxState, TransportBoxState)`, or its replacement) is resolved via constructor-injected dependencies, not a `static readonly` dictionary capturing `this`-bound delegates.
- `Handle()`'s cyclomatic/line footprint is materially reduced; exact target line count is not prescribed, but no single method should re-embed the extracted business logic inline.

### FR-3: Extending the transition set does not require modifying the handler
Registering a new `(fromState, toState)` → side-effect mapping must be achievable by adding one new strategy class plus a DI registration line in `LogisticsModule`, without editing `ChangeTransportBoxStateHandler`'s `Handle()` body.

**Acceptance criteria:**
- The dispatch mechanism resolves the applicable strategy generically (e.g. by iterating injected `IEnumerable<ITransportBoxTransitionSideEffect>` matched on declared `(From, To)`, or via a keyed registration), not via a switch/dictionary literal hard-coded and maintained inside the handler file itself. (Final mechanism is an architecture decision — see arch-review.)

### FR-4: `RestoreInventoryForItemsAsync` extraction (in scope but stays orchestration-adjacent)
`RestoreInventoryForItemsAsync` is only invoked from the Opened→New rollback path directly in `Handle()`, is not part of the `CallBackMap` dispatch, and is a single, small, focused private helper. It may remain a private helper on the handler, or move into a small dedicated inventory-restoration collaborator — this is left to the architecture review to decide based on whether it should be independently unit-testable outside `Handle()`.

**Acceptance criteria:**
- Its existing behavior (restore each item with a non-null `SourceInventoryId`, skip items without one) is unchanged.
- Whatever the final placement, it does not need to duplicate any of the four extracted strategies' dependencies.

## Non-Functional Requirements

### NFR-1: Performance
No performance regression. Strategy resolution must not introduce additional database round-trips, N+1 queries, or materially more allocations per request than the current dictionary lookup (a handful of small object allocations for a rarely-hot-path admin/warehouse operation is acceptable).

### NFR-2: Security
No change. No new external inputs, no new authorization surface — this is a pure internal restructuring of already-validated, already-authorized request handling.

### NFR-3: Backward compatibility
`ChangeTransportBoxStateRequest` and `ChangeTransportBoxStateResponse` (the MediatR contract, also the API contract, per `docs/architecture/development_guidelines.md`'s DTO rules) are unchanged. This is an internal-only refactor; no OpenAPI client regeneration is expected.

### NFR-4: Test coverage
The existing test suite (`ChangeTransportBoxStateHandlerTests.cs`, `ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs`) must continue to pass without modification to its assertions (constructor signature changes to the handler, if any, are allowed and must be updated in the test file, but observable behavior asserted by existing tests must not change). New unit tests are added per extracted strategy class.

## Data Model
No changes. `TransportBox`, `TransportBoxItem`, `TransportBoxState`, `TransportBoxTransition`, `TransportBoxStateNode` (all in `Anela.Heblo.Domain.Features.Logistics.Transport`) are unchanged by this refactor — this is an Application-layer (`Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState`) internal restructuring only.

## API / Interface Design
No change to the public MediatR request/response contract or any HTTP endpoint. New internal interface(s) are introduced purely inside the Application layer for the strategy abstraction (exact shape is an architecture decision — see arch-review.r1.md). New DI registrations are added to `LogisticsModule.AddLogisticsModule()` following its existing factory/`AddScoped`/`AddTransient` conventions.

## Dependencies
- Existing services already injected into the handler: `ITransportBoxRepository`, `IInventoryReservationService`, `IMediator`, `ILogger<T>`, `ICurrentUserService`, `ILogisticsStockOperationService`, `TimeProvider`.
- No new external dependencies, packages, or infrastructure.

## Out of Scope
- Any change to `TransportBoxTransition`, `TransportBoxStateNode`, `TransportBoxStateRules`, or the state machine's allowed-transition definitions.
- Any change to the box-code uniqueness rule itself, the inventory restoration business rule, or the stock-operation staging business rule — only where that logic lives, not what it does.
- Any change to `ChangeTransportBoxStateRequest`/`Response` shapes, error codes, or HTTP-facing behavior.
- Any new state transitions or new business capability.
- Frontend changes (none are needed — this is a backend-only internal refactor; confirmed no UI impact).

## Open Questions
None.

## Status: COMPLETE
