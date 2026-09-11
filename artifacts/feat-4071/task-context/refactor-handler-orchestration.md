### task: refactor-handler-orchestration

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` (path as given by current file: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`)

This is the task that actually removes the `CallBackMap` dictionary and the four private
methods plus `RestoreInventoryForItemsAsync`, and wires in the new collaborators. Do this
task only after all five extraction tasks above are committed — the old private methods stay
in place (dead but harmless, since nothing calls them once dispatch changes) until this step
removes them in one clean diff.

- [ ] **Step 1: Update the constructor and remove `CallBackMap`**

Replace the field/constructor block (current lines 12–54) with:

```csharp
public class ChangeTransportBoxStateHandler : IRequestHandler<ChangeTransportBoxStateRequest, ChangeTransportBoxStateResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly IMediator _mediator;
    private readonly ILogger<ChangeTransportBoxStateHandler> _logger;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;
    private readonly IEnumerable<ITransportBoxTransitionSideEffect> _sideEffects;
    private readonly ITransportBoxInventoryRestorer _inventoryRestorer;

    public ChangeTransportBoxStateHandler(
        ITransportBoxRepository repository,
        IMediator mediator,
        ILogger<ChangeTransportBoxStateHandler> logger,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider,
        IEnumerable<ITransportBoxTransitionSideEffect> sideEffects,
        ITransportBoxInventoryRestorer inventoryRestorer)
    {
        _repository = repository;
        _mediator = mediator;
        _logger = logger;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
        _sideEffects = sideEffects;
        _inventoryRestorer = inventoryRestorer;
    }
```

Remove: the `CallBackMap` static field entirely, and the `IInventoryReservationService` /
`ILogisticsStockOperationService` fields and constructor parameters (they are no longer used
directly by the handler).

- [ ] **Step 2: Replace the dispatch block inside `Handle()`**

Replace:

```csharp
            if (CallBackMap.TryGetValue(new Tuple<TransportBoxState, TransportBoxState>(box.State, request.NewState), out var callbackFactory))
            {
                var callback = callbackFactory(this);
                var callbackResult = await callback(box, request, cancellationToken);
                if (callbackResult != null)
                {
                    return callbackResult;
                }
            }
```

with:

```csharp
            var sideEffect = _sideEffects.FirstOrDefault(s => s.Supports(box.State, request.NewState));
            if (sideEffect != null)
            {
                var sideEffectResult = await sideEffect.ExecuteAsync(box, request, cancellationToken);
                if (sideEffectResult != null)
                {
                    return sideEffectResult;
                }
            }
```

- [ ] **Step 3: Replace the inventory-restore call site**

Replace:

```csharp
            if (itemsToRestore != null)
            {
                await RestoreInventoryForItemsAsync(itemsToRestore, userName, currentTime, box.Id, box.Code, cancellationToken);
            }
```

with:

```csharp
            if (itemsToRestore != null)
            {
                await _inventoryRestorer.RestoreAsync(itemsToRestore, userName, currentTime, box.Id, box.Code, cancellationToken);
            }
```

- [ ] **Step 4: Delete the now-unused private methods**

Delete `HandleNewToOpened`, `HandleOpenToQuarantine`, `HandleOpenToReserve`, `HandleReceived`,
and `RestoreInventoryForItemsAsync` in full (everything from the line
`private async Task<ChangeTransportBoxStateResponse?> HandleNewToOpened(...)` to the closing
brace of `RestoreInventoryForItemsAsync`, i.e. through the end of the class body before the
final `}`).

- [ ] **Step 5: Build**

Run: `cd backend && dotnet build src/Anela.Heblo.Application`
Expected: Build succeeded (existing test project will fail to build until the next task
updates its constructor calls — that's expected and fixed next).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs
git commit -m "refactor(logistics): reduce ChangeTransportBoxStateHandler to orchestration only"
```

---
