### task: extract-open-to-reserve-side-effect

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/OpenToReserveSideEffect.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenToReserveSideEffectTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class OpenToReserveSideEffectTests
{
    private readonly OpenToReserveSideEffect _sut = new();

    [Fact]
    public void Supports_OpenedToReserve_ReturnsTrue()
    {
        _sut.Supports(TransportBoxState.Opened, TransportBoxState.Reserve).Should().BeTrue();
    }

    [Fact]
    public void Supports_AnyOtherPair_ReturnsFalse()
    {
        _sut.Supports(TransportBoxState.Opened, TransportBoxState.Quarantine).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_MissingLocation_ReturnsRequiredFieldMissing()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest { BoxId = 1, NewState = TransportBoxState.Reserve };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().NotBeNull();
        result!.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
        result.Params.Should().Contain("field", "Location");
    }

    [Fact]
    public async Task ExecuteAsync_LocationProvided_ReturnsNull()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1, NewState = TransportBoxState.Reserve, Location = "A1"
        };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify it fails to compile**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~OpenToReserveSideEffectTests"`
Expected: Build error — type does not exist.

- [ ] **Step 3: Implement `OpenToReserveSideEffect`**

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class OpenToReserveSideEffect : ITransportBoxTransitionSideEffect
{
    public bool Supports(TransportBoxState from, TransportBoxState to) =>
        from == TransportBoxState.Opened && to == TransportBoxState.Reserve;

    public Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Location))
        {
            return Task.FromResult<ChangeTransportBoxStateResponse?>(new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.RequiredFieldMissing,
                Params = new Dictionary<string, string> { { "field", "Location" } }
            });
        }

        return Task.FromResult<ChangeTransportBoxStateResponse?>(null);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~OpenToReserveSideEffectTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/OpenToReserveSideEffect.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenToReserveSideEffectTests.cs
git commit -m "feat(logistics): extract OpenToReserveSideEffect from ChangeTransportBoxStateHandler"
```

---
