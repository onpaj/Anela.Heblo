# Architecture Review: Extract Transport Box State-Transition Side Effects from ChangeTransportBoxStateHandler

## Skip Design: true

This is a backend-only internal refactor of a MediatR handler. No new or changed UI components,
screens, layouts, or visual design decisions are involved — `spec.r1.md` confirms no frontend
impact and the request/response DTOs are unchanged.

## Architectural Fit Assessment

This fits cleanly as a Vertical Slice, Application-layer-internal refactor. It touches only
`Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState` plus one new
services subfolder and `LogisticsModule`'s DI registration — no module boundary is crossed,
no contract (`Anela.Heblo.Application.Features.Logistics.Contracts`) changes, and no domain
type (`Anela.Heblo.Domain.Features.Logistics.Transport.*`) is touched.

The codebase already has a directly analogous, working precedent for "one of several
side-effect implementations, selected at runtime by a predicate, injected as a collection":
`Anela.Heblo.Application.Features.KnowledgeBase.Services.IIndexingStrategy`, consumed by
`DocumentIndexingService` as:

```csharp
private readonly IEnumerable<IIndexingStrategy> _strategies;
...
var strategy = _strategies.FirstOrDefault(s => s.Supports(document.DocumentType))
    ?? throw new NotSupportedException(...);
```

This refactor follows that exact convention rather than inventing a new dispatch idiom. It
replaces `ChangeTransportBoxStateHandler`'s `private static readonly Dictionary<Tuple<...>,
Func<ChangeTransportBoxStateHandler, Func<...>>>` (a dictionary of `this`-bound delegates —
itself an unusual, hard-to-test shape) with the same "inject `IEnumerable<IFoo>`, resolve by
`Supports(...)`" pattern the KnowledgeBase module already uses successfully.

## Proposed Architecture

### Component Overview

```
ChangeTransportBoxStateHandler (orchestration only)
    │
    │  box, request  →  resolve strategy for (box.State, request.NewState)
    ▼
IEnumerable<ITransportBoxTransitionSideEffect>  (constructor-injected)
    ├── NewToOpenedSideEffect            (From=New,       To=Opened)
    ├── OpenToReserveSideEffect          (From=Opened,    To=Reserve)
    ├── OpenToQuarantineSideEffect       (From=Opened,    To=Quarantine)
    └── ReceivedSideEffect               (From={InTransit,Reserve,Quarantine}, To=Received)
```

`ReceivedSideEffect` is registered once but must answer `Supports` for three distinct
`From` states (see Decision 2) — it is one class, not three, because the current
`HandleReceived` body has zero dependency on which state the box came from.

`RestoreInventoryForItemsAsync` is not one of the dispatched side effects (it is never in
`CallBackMap` — it runs unconditionally on the Opened→New path, directly from `Handle()`) and
is relocated to its own small collaborator, `TransportBoxInventoryRestorer`, injected into the
handler directly (not through the `ITransportBoxTransitionSideEffect` collection) — see
Decision 3.

### Key Design Decisions

#### Decision 1: Strategy interface shape

**Options considered:**
- (a) Keyed dictionary of strategies registered by `(TransportBoxState, TransportBoxState)` tuple key, resolved via `IReadOnlyDictionary` built from `IEnumerable<T>` at construction.
- (b) `IEnumerable<ITransportBoxTransitionSideEffect>` with a `Supports(TransportBoxState from, TransportBoxState to)` predicate method, resolved via `FirstOrDefault` — mirroring `IIndexingStrategy`.
- (c) MediatR notification/pipeline behavior per transition.

**Chosen approach:** (b).

**Rationale:** (b) is the codebase's existing, proven idiom for this exact shape of problem
(one of N side-effect implementations, selected by a runtime predicate over a small closed
set of inputs) — see `IIndexingStrategy`/`DocumentIndexingService` above. It requires no new
infrastructure, no custom DI keyed-service wiring, and reviewers already recognize the pattern.
(a) adds a manual dictionary-construction step that (b) doesn't need and doesn't match any
existing convention in this codebase. (c) is architecturally heavier than warranted — this is
synchronous, in-request-scope side-effect selection, not a fan-out event; MediatR notifications
would also make "did a side effect run" implicit/inferred rather than visible in the handler.

```csharp
namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public interface ITransportBoxTransitionSideEffect
{
    bool Supports(TransportBoxState from, TransportBoxState to);

    Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box,
        ChangeTransportBoxStateRequest request,
        CancellationToken cancellationToken);
}
```

Contract preserved exactly from the current private methods: return `null` to continue the
transition, return a non-null `ChangeTransportBoxStateResponse` to short-circuit with failure.

#### Decision 2: One `Supports` predicate can match multiple `From` states

**Options considered:**
- (a) One `ReceivedSideEffect` class whose `Supports` checks `to == Received && (from == InTransit || from == Reserve || from == Quarantine)`.
- (b) Three separate classes (`InTransitToReceivedSideEffect`, `ReserveToReceivedSideEffect`, `QuarantineToReceivedSideEffect`) that all delegate to one shared internal helper.

**Chosen approach:** (a).

**Rationale:** The current `HandleReceived` body is identical regardless of which state the
box transitioned from — duplicating it into three classes (or three classes forwarding to a
fourth) adds files and indirection without adding testability or extensibility; FR-3 only
requires that *adding a new mapping* not touch the handler, not that every mapping be a
distinct class. If a future transition needs `Received`-adjacent behavior that differs by
origin state, split then — YAGNI today.

#### Decision 3: `RestoreInventoryForItemsAsync` becomes its own collaborator, not a side-effect strategy

**Options considered:**
- (a) Leave it a private method on the handler (status quo for this one piece).
- (b) Extract to `TransportBoxInventoryRestorer` (or similarly named class), injected directly into the handler via constructor, independent of the `ITransportBoxTransitionSideEffect` collection.
- (c) Force it into the `ITransportBoxTransitionSideEffect` shape for consistency.

**Chosen approach:** (b).

**Rationale:** It is not a member of `CallBackMap` and is not selected by `(from, to)` dispatch
— it runs unconditionally whenever `Handle()` detects the specific Opened→New rollback
condition, orthogonal to the strategy dispatch table. Forcing it into the strategy interface (c)
would misrepresent it as transition-dispatched when it isn't, and would give it a `Supports`
method that can never be exercised through the strategy-resolution path (dead branch). Leaving
it inline (a) fails FR-1's testability goal for this remaining ~15-line block, which today
still requires the full handler's dependency set to exercise. Extracting it as
`ITransportBoxInventoryRestorer` gives it the same isolated-unit-test benefit as the four
extracted strategies, with a natural, minimal dependency (`IInventoryReservationService` only).

## Implementation Guidance

### Directory / Module Structure

New files, all under
`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/`
(kept vertical-slice-local — these types have no other consumer):

- `ITransportBoxTransitionSideEffect.cs` — the interface (Decision 1).
- `NewToOpenedSideEffect.cs` — body of current `HandleNewToOpened`. Depends on: `ITransportBoxRepository`, `ICurrentUserService`, `TimeProvider`.
- `OpenToReserveSideEffect.cs` — body of current `HandleOpenToReserve`. No dependencies beyond the method inputs.
- `OpenToQuarantineSideEffect.cs` — body of current `HandleOpenToQuarantine` (no-op today; kept as an explicit class per spec FR-1, so the transition is documented and extensible without resurrecting a `CallBackMap`-shaped structure later).
- `ReceivedSideEffect.cs` — body of current `HandleReceived`. Depends on: `ILogisticsStockOperationService`, `ILogger<ReceivedSideEffect>`.
- `ITransportBoxInventoryRestorer.cs` / `TransportBoxInventoryRestorer.cs` — body of current `RestoreInventoryForItemsAsync`. Depends on: `IInventoryReservationService`.

`ChangeTransportBoxStateHandler.cs` is modified in place (not moved) — it keeps its existing
constructor dependencies for orchestration (`ITransportBoxRepository`, `IMediator`,
`ILogger<ChangeTransportBoxStateHandler>`, `ICurrentUserService`, `TimeProvider`) and gains two
new constructor parameters: `IEnumerable<ITransportBoxTransitionSideEffect> sideEffects` and
`ITransportBoxInventoryRestorer inventoryRestorer`. It **drops** `IInventoryReservationService`
and `ILogisticsStockOperationService` as direct dependencies — those move to the two
collaborators that now own that logic exclusively.

### Interfaces and Contracts

```csharp
public interface ITransportBoxTransitionSideEffect
{
    bool Supports(TransportBoxState from, TransportBoxState to);
    Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken);
}

public interface ITransportBoxInventoryRestorer
{
    Task RestoreAsync(
        IReadOnlyList<TransportBoxItem> items, string userName, DateTime timestamp,
        int boxId, string? boxCode, CancellationToken cancellationToken);
}
```

Handler's dispatch replaces the `CallBackMap.TryGetValue(...)` block with:

```csharp
var sideEffect = _sideEffects.FirstOrDefault(s => s.Supports(box.State, request.NewState));
if (sideEffect != null)
{
    var result = await sideEffect.ExecuteAsync(box, request, cancellationToken);
    if (result != null)
    {
        return result;
    }
}
```

DI registration in `LogisticsModule.AddLogisticsModule()` (append near the existing
`ITransportBoxCompletionService` registration, matching its `AddTransient` convention since
these are stateless per-call collaborators with no captured mutable state):

```csharp
services.AddTransient<ITransportBoxTransitionSideEffect, NewToOpenedSideEffect>();
services.AddTransient<ITransportBoxTransitionSideEffect, OpenToReserveSideEffect>();
services.AddTransient<ITransportBoxTransitionSideEffect, OpenToQuarantineSideEffect>();
services.AddTransient<ITransportBoxTransitionSideEffect, ReceivedSideEffect>();
services.AddTransient<ITransportBoxInventoryRestorer, TransportBoxInventoryRestorer>();
```

### Data Flow

Unchanged end-to-end: controller → `IMediator.Send(ChangeTransportBoxStateRequest)` →
`ChangeTransportBoxStateHandler.Handle()` → (new) strategy resolution replaces the dictionary
lookup, calling into one of the four extracted classes → same transition/persist/response flow
as today. The Opened→New inventory-restore call site inside `Handle()` changes from
`await RestoreInventoryForItemsAsync(...)` (private method call) to
`await _inventoryRestorer.RestoreAsync(...)` (collaborator call) — same arguments, same
placement in the flow.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Multiple strategies could accidentally both return `true` from `Supports` for the same `(from, to)` pair, making dispatch order (registration order via `FirstOrDefault`) silently significant | Medium | Add one unit test per pair in the spec's transition table asserting exactly one registered strategy supports it (iterate the real DI-resolved `IEnumerable<ITransportBoxTransitionSideEffect>` and assert `Count(s => s.Supports(from, to)) == 1` for each of the four known pairs) |
| Behavior drift during extraction (e.g. dropping the `_logger.LogDebug` call inside `HandleReceived`, or changing exception handling around the extracted code) | Medium | Existing `ChangeTransportBoxStateHandlerTests.cs` and `ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs` must pass unmodified in assertions (constructor wiring only may change); each extraction is a mechanical move, not a rewrite |
| `OpenToQuarantineSideEffect` being a no-op today makes it tempting to skip registering it, silently reintroducing an implicit "no side effect for this pair" case that's indistinguishable from "pair not handled" | Low | Register and test it explicitly per Implementation Guidance — this preserves FR-1's documentation/symmetry intent and gives future maintainers one obvious place to add Quarantine-entry behavior |
| Constructor dependency list churn breaks other callers of `ChangeTransportBoxStateHandler`'s constructor directly (e.g. any test or factory not going through DI) | Low | Grep confirms the only direct constructor call sites are `ChangeTransportBoxStateHandlerTests.cs` and `ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs`; both are in scope to update per NFR-4 |

## Specification Amendments

None required — `spec.r1.md`'s FR-1 through FR-4 map directly onto Decisions 1–3 above without
contradiction. One clarification for the planner: FR-4 leaves `RestoreInventoryForItemsAsync`'s
placement "to the architecture review" — Decision 3 resolves that: it is extracted to
`ITransportBoxInventoryRestorer`/`TransportBoxInventoryRestorer`, not left inline and not folded
into the `ITransportBoxTransitionSideEffect` family.

## Prerequisites

None. No migrations, no config, no infrastructure changes — this is a same-assembly,
same-module code restructuring buildable and testable entirely from the existing solution.
