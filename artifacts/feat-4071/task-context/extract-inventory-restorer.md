### task: extract-inventory-restorer

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ITransportBoxInventoryRestorer.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/TransportBoxInventoryRestorer.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxInventoryRestorerTests.cs`

This moves `RestoreInventoryForItemsAsync`'s body (lines 307–328 of the current handler)
unchanged into its own collaborator — not a `ITransportBoxTransitionSideEffect` (see
arch-review Decision 3: it is not dispatched by `(from, to)`, it runs unconditionally on the
Opened→New rollback path).

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class TransportBoxInventoryRestorerTests
{
    private readonly Mock<IInventoryReservationService> _inventoryReservationServiceMock = new();
    private readonly TransportBoxInventoryRestorer _sut;

    public TransportBoxInventoryRestorerTests()
    {
        _sut = new TransportBoxInventoryRestorer(_inventoryReservationServiceMock.Object);
    }

    [Fact]
    public async Task RestoreAsync_ItemWithSourceInventoryId_CallsRestore()
    {
        var item = new TransportBoxItem("SKU-1", "Product", 3.0, DateTime.UtcNow, "user", null) { SourceInventoryId = 42 };
        var timestamp = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        await _sut.RestoreAsync(new[] { item }, "tester", timestamp, boxId: 7, boxCode: "B001", CancellationToken.None);

        _inventoryReservationServiceMock.Verify(x => x.RestoreAsync(
            42, 3.0m, "tester", timestamp, 7, "B001", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreAsync_ItemWithoutSourceInventoryId_SkipsRestore()
    {
        var item = new TransportBoxItem("SKU-1", "Product", 3.0, DateTime.UtcNow, "user", null);

        await _sut.RestoreAsync(new[] { item }, "tester", DateTime.UtcNow, boxId: 7, boxCode: "B001", CancellationToken.None);

        _inventoryReservationServiceMock.Verify(x => x.RestoreAsync(
            It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

> Note: verify `TransportBoxItem.SourceInventoryId` is settable (init/property setter) against
> the real type — if it is constructor-only or internal-set, adjust the test's item
> construction to match rather than the illustrative object-initializer above.

- [ ] **Step 2: Run to verify it fails to compile**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~TransportBoxInventoryRestorerTests"`
Expected: Build error — types do not exist.

- [ ] **Step 3: Implement the interface and class**

```csharp
// ITransportBoxInventoryRestorer.cs
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public interface ITransportBoxInventoryRestorer
{
    Task RestoreAsync(
        IReadOnlyList<TransportBoxItem> items,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        CancellationToken cancellationToken);
}
```

```csharp
// TransportBoxInventoryRestorer.cs
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class TransportBoxInventoryRestorer : ITransportBoxInventoryRestorer
{
    private readonly IInventoryReservationService _inventoryReservationService;

    public TransportBoxInventoryRestorer(IInventoryReservationService inventoryReservationService)
    {
        _inventoryReservationService = inventoryReservationService;
    }

    public async Task RestoreAsync(
        IReadOnlyList<TransportBoxItem> items,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (item.SourceInventoryId == null) continue;

            await _inventoryReservationService.RestoreAsync(
                inventoryId: item.SourceInventoryId.Value,
                amount: (decimal)item.Amount,
                userName: userName,
                timestamp: timestamp,
                boxId: boxId,
                boxCode: boxCode,
                cancellationToken: cancellationToken);
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~TransportBoxInventoryRestorerTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ITransportBoxInventoryRestorer.cs backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/TransportBoxInventoryRestorer.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxInventoryRestorerTests.cs
git commit -m "feat(logistics): extract TransportBoxInventoryRestorer from ChangeTransportBoxStateHandler"
```

---
