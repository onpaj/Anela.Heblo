### task: extract-received-side-effect

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ReceivedSideEffect.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ReceivedSideEffectTests.cs`

This moves `HandleReceived`'s body (lines 273–305 of the current handler) unchanged —
including its exact `_logger.LogDebug`/`LogInformation` calls and message templates, and the
`BOX-{box.Id:000000}-{group.ProductCode}` document-number format.

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class ReceivedSideEffectTests
{
    private readonly Mock<ILogisticsStockOperationService> _stockOperationServiceMock = new();
    private readonly ReceivedSideEffect _sut;

    public ReceivedSideEffectTests()
    {
        _stockOperationServiceMock
            .Setup(x => x.StageOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut = new ReceivedSideEffect(_stockOperationServiceMock.Object, NullLogger<ReceivedSideEffect>.Instance);
    }

    [Theory]
    [InlineData(TransportBoxState.InTransit)]
    [InlineData(TransportBoxState.Reserve)]
    [InlineData(TransportBoxState.Quarantine)]
    public void Supports_KnownOriginsToReceived_ReturnsTrue(TransportBoxState from)
    {
        _sut.Supports(from, TransportBoxState.Received).Should().BeTrue();
    }

    [Fact]
    public void Supports_NewToReceived_ReturnsFalse()
    {
        _sut.Supports(TransportBoxState.New, TransportBoxState.Received).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_AggregatesItemsByProductCode_StagesOneOperationPerProduct()
    {
        var box = CreateBoxWithItems(("SKU-1", 2.0), ("SKU-1", 3.0), ("SKU-2", 1.0));
        var request = new ChangeTransportBoxStateRequest { BoxId = box.Id, NewState = TransportBoxState.Received };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().BeNull();
        _stockOperationServiceMock.Verify(x => x.StageOperationAsync(
            $"BOX-{box.Id:000000}-SKU-1", "SKU-1", 5,
            LogisticsStockOperationSource.TransportBox, box.Id, It.IsAny<CancellationToken>()), Times.Once);
        _stockOperationServiceMock.Verify(x => x.StageOperationAsync(
            $"BOX-{box.Id:000000}-SKU-2", "SKU-2", 1,
            LogisticsStockOperationSource.TransportBox, box.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TransportBox CreateBoxWithItems(params (string ProductCode, double Amount)[] items)
    {
        var box = new TransportBox();
        box.Id = 1;
        var itemsField = typeof(TransportBox).GetField("_items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var list = (List<TransportBoxItem>)itemsField.GetValue(box)!;
        foreach (var (productCode, amount) in items)
        {
            list.Add(new TransportBoxItem(productCode, "Product", amount, DateTime.UtcNow, "TestUser", null));
        }
        return box;
    }
}
```

> Note: `TransportBoxItem`'s constructor signature and `TransportBox`'s `_items` backing field
> are taken from `ChangeTransportBoxStateHandlerTests.CreateTestBoxWithMultipleItems` — verify
> against the current `TransportBoxItem.cs` before finalizing if this drifts.

- [ ] **Step 2: Run to verify it fails to compile**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ReceivedSideEffectTests"`
Expected: Build error — type does not exist.

- [ ] **Step 3: Implement `ReceivedSideEffect`**

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class ReceivedSideEffect : ITransportBoxTransitionSideEffect
{
    private readonly ILogisticsStockOperationService _stockOperationService;
    private readonly ILogger<ReceivedSideEffect> _logger;

    public ReceivedSideEffect(ILogisticsStockOperationService stockOperationService, ILogger<ReceivedSideEffect> logger)
    {
        _stockOperationService = stockOperationService;
        _logger = logger;
    }

    public bool Supports(TransportBoxState from, TransportBoxState to) =>
        to == TransportBoxState.Received &&
        (from == TransportBoxState.InTransit || from == TransportBoxState.Reserve || from == TransportBoxState.Quarantine);

    public async Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        var aggregated = box.Items
            .GroupBy(i => i.ProductCode)
            .Select(g => new
            {
                ProductCode = g.Key,
                Amount = (int)Math.Round(g.Sum(i => i.Amount), MidpointRounding.AwayFromZero),
                LineCount = g.Count()
            })
            .ToList();

        foreach (var group in aggregated)
        {
            var documentNumber = $"BOX-{box.Id:000000}-{group.ProductCode}";

            await _stockOperationService.StageOperationAsync(
                documentNumber,
                group.ProductCode,
                group.Amount,
                LogisticsStockOperationSource.TransportBox,
                box.Id,
                cancellationToken);

            _logger.LogDebug("Staged StockUpOperation {DocumentNumber} for product {ProductCode}, amount {Amount} (aggregated from {LineCount} item line(s))",
                documentNumber, group.ProductCode, group.Amount, group.LineCount);
        }

        _logger.LogInformation("Staged {OperationCount} StockUpOperation(s) from {ItemCount} item line(s) for box {BoxId} ({BoxCode})",
            aggregated.Count, box.Items.Count, box.Id, box.Code);

        return null;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ReceivedSideEffectTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ReceivedSideEffect.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ReceivedSideEffectTests.cs
git commit -m "feat(logistics): extract ReceivedSideEffect from ChangeTransportBoxStateHandler"
```

---
