## Module
Manufacture

## Finding
`ManufactureOrder` (`backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs`) exposes `State` as a plain public setter with no invariant protection. The allowed state transition table lives in `UpdateManufactureOrderStatusHandler.IsValidStateTransition` (lines 162–174 in `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`):

```csharp
private bool IsValidStateTransition(ManufactureOrderState fromState, ManufactureOrderState toState)
{
    return fromState switch
    {
        ManufactureOrderState.Draft => toState is ManufactureOrderState.Planned or ManufactureOrderState.Cancelled,
        ManufactureOrderState.Planned => toState is ManufactureOrderState.Draft or ...,
        ...
        ManufactureOrderState.Cancelled => false,
        _ => false
    };
}
```

Because the entity has no guard, any handler that acquires the aggregate can silently assign any state directly (`order.State = arbitrary;`) without going through this validation.

## Why it matters
State transition rules are a domain invariant — the aggregate itself should enforce what transitions are legal. Keeping the rule only in the Application handler:
- Violates _business logic belongs in the domain_ (CLAUDE.md, `development_guidelines.md`)
- Leaves no protection when other code paths mutate `order.State` directly (e.g. a future handler, or ad-hoc repository update)
- The check can never be unit-tested against the entity in isolation — only against the handler

## Suggested fix
Add a method to `ManufactureOrder`:

```csharp
public bool CanTransitionTo(ManufactureOrderState newState) => State switch
{
    ManufactureOrderState.Draft => newState is ManufactureOrderState.Planned or ManufactureOrderState.Cancelled,
    ManufactureOrderState.Planned => newState is ManufactureOrderState.Draft or ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Cancelled or ManufactureOrderState.Completed,
    ManufactureOrderState.SemiProductManufactured => newState is ManufactureOrderState.Planned or ManufactureOrderState.Completed or ManufactureOrderState.Cancelled,
    ManufactureOrderState.Completed => newState is ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Cancelled or ManufactureOrderState.Planned,
    ManufactureOrderState.Cancelled => false,
    _ => false
};
```

Then replace `IsValidStateTransition(oldState, request.NewState)` in the handler with `!order.CanTransitionTo(request.NewState)` and delete the private method. The `ManufactureOrderStateTransitionTests` (already present in the test suite) can be moved/extended to test the entity method directly.

---
_Filed by daily arch-review routine on 2026-06-14._
