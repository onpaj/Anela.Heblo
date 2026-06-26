## Module
Users

## Finding
`GridLayoutsController` declares and assigns `_currentUserService` in its constructor but none of its three action methods (`Get`, `Save`, `Reset`) ever call it.

```csharp
// backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs, lines 18–23
private readonly ICurrentUserService _currentUserService;

public GridLayoutsController(IMediator mediator, ICurrentUserService currentUserService)
{
    _mediator = mediator;
    _currentUserService = currentUserService;   // assigned, never read
}
```

The actual identity resolution happens inside the MediatR handlers (`GetGridLayoutHandler`, `SaveGridLayoutHandler`, `ResetGridLayoutHandler`), each of which already injects `ICurrentUserService` directly.

## Why it matters
Dead constructor dependency. It misleads readers into thinking the controller uses it, adds an unnecessary constructor parameter, and violates YAGNI — the dependency serves no purpose at the controller boundary.

## Suggested fix
Remove the `ICurrentUserService` constructor parameter and the `_currentUserService` field from `GridLayoutsController`. No functional change needed; the handlers already have their own injection.

---
_Filed by daily arch-review routine on 2026-05-25._