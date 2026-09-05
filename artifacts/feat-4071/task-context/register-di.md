### task: register-di

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs`

- [ ] **Step 1: Add the new registrations**

In `AddLogisticsModule()`, immediately after the existing
`services.AddTransient<ITransportBoxCompletionService, TransportBoxCompletionService>();` line,
add:

```csharp
        // Register transport box state-transition side effects (dispatched by
        // ChangeTransportBoxStateHandler via IEnumerable<ITransportBoxTransitionSideEffect>)
        services.AddTransient<UseCases.ChangeTransportBoxState.ITransportBoxTransitionSideEffect, UseCases.ChangeTransportBoxState.NewToOpenedSideEffect>();
        services.AddTransient<UseCases.ChangeTransportBoxState.ITransportBoxTransitionSideEffect, UseCases.ChangeTransportBoxState.OpenToReserveSideEffect>();
        services.AddTransient<UseCases.ChangeTransportBoxState.ITransportBoxTransitionSideEffect, UseCases.ChangeTransportBoxState.OpenToQuarantineSideEffect>();
        services.AddTransient<UseCases.ChangeTransportBoxState.ITransportBoxTransitionSideEffect, UseCases.ChangeTransportBoxState.ReceivedSideEffect>();
        services.AddTransient<UseCases.ChangeTransportBoxState.ITransportBoxInventoryRestorer, UseCases.ChangeTransportBoxState.TransportBoxInventoryRestorer>();
```

(Fully-qualify the types as shown, or add a
`using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;` at the top
of the file and drop the `UseCases.ChangeTransportBoxState.` prefix — match whichever style the
file's existing `using` block favors; the file currently has no `using` for this namespace, so
either is acceptable, but prefer adding the `using` for readability since five types are
referenced.)

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build src/Anela.Heblo.Application`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs
git commit -m "feat(logistics): register transition side-effect and inventory-restorer DI"
```

---
