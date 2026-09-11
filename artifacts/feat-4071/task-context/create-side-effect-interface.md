### task: create-side-effect-interface

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ITransportBoxTransitionSideEffect.cs`

- [ ] **Step 1: Create the interface**

```csharp
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

/// <summary>
/// One state-transition's side effect, dispatched by ChangeTransportBoxStateHandler.
/// Return null to let the transition continue; return a populated response to
/// short-circuit Handle() with a failure result — identical contract to the
/// private methods this interface replaces.
/// </summary>
public interface ITransportBoxTransitionSideEffect
{
    bool Supports(TransportBoxState from, TransportBoxState to);

    Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box,
        ChangeTransportBoxStateRequest request,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Application`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ITransportBoxTransitionSideEffect.cs
git commit -m "feat(logistics): add ITransportBoxTransitionSideEffect interface"
```

---
