### task: extract-open-to-quarantine-side-effect

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/OpenToQuarantineSideEffect.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenToQuarantineSideEffectTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class OpenToQuarantineSideEffectTests
{
    private readonly OpenToQuarantineSideEffect _sut = new();

    [Fact]
    public void Supports_OpenedToQuarantine_ReturnsTrue()
    {
        _sut.Supports(TransportBoxState.Opened, TransportBoxState.Quarantine).Should().BeTrue();
    }

    [Fact]
    public void Supports_AnyOtherPair_ReturnsFalse()
    {
        _sut.Supports(TransportBoxState.Opened, TransportBoxState.Reserve).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_AlwaysReturnsNull()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest { BoxId = 1, NewState = TransportBoxState.Quarantine };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify it fails to compile**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~OpenToQuarantineSideEffectTests"`
Expected: Build error — type does not exist.

- [ ] **Step 3: Implement `OpenToQuarantineSideEffect`**

```csharp
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

// No location required for Quarantine — ToQuarantine() clears Location = null.
// Kept as an explicit, registered side effect (rather than omitted from dispatch)
// so future Quarantine-entry behavior has one obvious place to be added, and so
// dispatch-uniqueness tests can assert exactly one strategy handles this pair.
public class OpenToQuarantineSideEffect : ITransportBoxTransitionSideEffect
{
    public bool Supports(TransportBoxState from, TransportBoxState to) =>
        from == TransportBoxState.Opened && to == TransportBoxState.Quarantine;

    public Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult<ChangeTransportBoxStateResponse?>(null);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~OpenToQuarantineSideEffectTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/OpenToQuarantineSideEffect.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenToQuarantineSideEffectTests.cs
git commit -m "feat(logistics): extract OpenToQuarantineSideEffect from ChangeTransportBoxStateHandler"
```

---
