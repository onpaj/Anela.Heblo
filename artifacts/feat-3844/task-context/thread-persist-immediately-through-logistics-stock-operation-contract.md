### task: thread-persist-immediately-through-logistics-stock-operation-contract

**Goal**

Add the same `bool persistImmediately = true` parameter (after `CancellationToken`) to
`ILogisticsStockOperationService.CreateOperationAsync` and its only implementation,
`LogisticsStockOperationAdapter.CreateOperationAsync`, forwarding the value unchanged into
`IStockUpProcessingService.CreateOperationAsync` (whose new signature was introduced in the
previous task, `add-persist-immediately-and-idempotency-to-stockup-processing-service`, and is now:
`Task CreateOperationAsync(string documentNumber, string productCode, int amount, StockUpSourceType sourceType, int sourceId, CancellationToken ct = default, bool persistImmediately = true)`).

This is a pure pass-through change — no new logic, no idempotency check here (that already lives
inside `StockUpProcessingService`, one level down, and applies uniformly regardless of which
caller reaches it through this adapter).

**Files to touch**

1. `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationService.cs`
2. `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationAdapter.cs`
3. `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsStockOperationAdapterTests.cs`

**Step 1 — write the failing test first**

Open
`backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsStockOperationAdapterTests.cs`.
Its current full content is:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class LogisticsStockOperationAdapterTests
{
    private readonly Mock<IStockUpProcessingService> _service = new();

    private LogisticsStockOperationAdapter CreateAdapter() => new(_service.Object);

    private void SetupServiceReturnsCompleted()
    {
        _service
            .Setup(s => s.CreateOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<StockUpSourceType>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task CreateOperationAsync_WithTransportBoxSource_DelegatesToServiceWithCorrectEnum()
    {
        SetupServiceReturnsCompleted();

        await CreateAdapter().CreateOperationAsync(
            "DOC-1", "PROD-1", 5, LogisticsStockOperationSource.TransportBox, 10);

        _service.Verify(s => s.CreateOperationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            StockUpSourceType.TransportBox,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOperationAsync_WithGiftPackageManufactureSource_DelegatesToServiceWithCorrectEnum()
    {
        SetupServiceReturnsCompleted();

        await CreateAdapter().CreateOperationAsync(
            "DOC-1", "PROD-1", 5, LogisticsStockOperationSource.GiftPackageManufacture, 10);

        _service.Verify(s => s.CreateOperationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            StockUpSourceType.GiftPackageManufacture,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOperationAsync_PassesThroughAllParameters()
    {
        var ct = new CancellationToken(false);
        SetupServiceReturnsCompleted();

        await CreateAdapter().CreateOperationAsync(
            "DOC-42", "SET-99", 7, LogisticsStockOperationSource.TransportBox, 55, ct);

        _service.Verify(s => s.CreateOperationAsync(
            "DOC-42",
            "SET-99",
            7,
            StockUpSourceType.TransportBox,
            55,
            ct), Times.Once);
    }

    [Fact]
    public async Task CreateOperationAsync_WithUnknownSource_ThrowsArgumentOutOfRangeException()
    {
        var unknownSource = (LogisticsStockOperationSource)999;

        var act = () => CreateAdapter().CreateOperationAsync(
            "DOC-1", "PROD-1", 1, unknownSource, 0);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
```

Make two kinds of change to this file:

**(a)** Update `SetupServiceReturnsCompleted` and the three existing `Verify` calls that reference
`CreateOperationAsync` on the inner `_service` mock, to add `It.IsAny<bool>()` as a 7th argument (so
they keep matching regardless of what `persistImmediately` value the adapter forwards — this test
file is about enum-mapping and parameter pass-through, not about the `persistImmediately` value
itself, which gets its own dedicated test below).

Replace:

```csharp
    private void SetupServiceReturnsCompleted()
    {
        _service
            .Setup(s => s.CreateOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<StockUpSourceType>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }
```

with:

```csharp
    private void SetupServiceReturnsCompleted()
    {
        _service
            .Setup(s => s.CreateOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<StockUpSourceType>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Returns(Task.CompletedTask);
    }
```

Replace (in `CreateOperationAsync_WithTransportBoxSource_DelegatesToServiceWithCorrectEnum`):

```csharp
        _service.Verify(s => s.CreateOperationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            StockUpSourceType.TransportBox,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
```

with:

```csharp
        _service.Verify(s => s.CreateOperationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            StockUpSourceType.TransportBox,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<bool>()), Times.Once);
```

Replace (in `CreateOperationAsync_WithGiftPackageManufactureSource_DelegatesToServiceWithCorrectEnum`):

```csharp
        _service.Verify(s => s.CreateOperationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            StockUpSourceType.GiftPackageManufacture,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
```

with:

```csharp
        _service.Verify(s => s.CreateOperationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            StockUpSourceType.GiftPackageManufacture,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<bool>()), Times.Once);
```

Replace (in `CreateOperationAsync_PassesThroughAllParameters`):

```csharp
        _service.Verify(s => s.CreateOperationAsync(
            "DOC-42",
            "SET-99",
            7,
            StockUpSourceType.TransportBox,
            55,
            ct), Times.Once);
```

with:

```csharp
        _service.Verify(s => s.CreateOperationAsync(
            "DOC-42",
            "SET-99",
            7,
            StockUpSourceType.TransportBox,
            55,
            ct,
            It.IsAny<bool>()), Times.Once);
```

**(b)** Add a new dedicated test for the `persistImmediately` pass-through, appended right after
`CreateOperationAsync_PassesThroughAllParameters` (and before
`CreateOperationAsync_WithUnknownSource_ThrowsArgumentOutOfRangeException`):

```csharp
    [Fact]
    public async Task CreateOperationAsync_PersistImmediatelyFalse_ForwardsToService()
    {
        SetupServiceReturnsCompleted();

        await CreateAdapter().CreateOperationAsync(
            "DOC-1", "PROD-1", 5, LogisticsStockOperationSource.TransportBox, 10,
            CancellationToken.None, persistImmediately: false);

        _service.Verify(s => s.CreateOperationAsync(
            "DOC-1",
            "PROD-1",
            5,
            StockUpSourceType.TransportBox,
            10,
            It.IsAny<CancellationToken>(),
            false), Times.Once);
    }
```

**Step 2 — run tests and confirm failure**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LogisticsStockOperationAdapterTests"
```

Expected: compile errors, because neither `ILogisticsStockOperationService.CreateOperationAsync`
nor `LogisticsStockOperationAdapter.CreateOperationAsync` nor the mocked
`IStockUpProcessingService.CreateOperationAsync` calls in this test file accept a 7th `bool`
argument yet on the adapter's own call signature (the mocked `IStockUpProcessingService` already
does, from the previous task, so only the adapter-level calls in the new test and the
`persistImmediately:` named argument will fail to compile).

**Step 3 — implement the interface change**

Replace the full contents of
`backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationService.cs`
(currently):

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public interface ILogisticsStockOperationService
{
    Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default);
}
```

with:

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public interface ILogisticsStockOperationService
{
    /// <param name="persistImmediately">
    /// When true (default), the underlying StockUpOperation is committed to the database
    /// immediately. When false, it is only staged on the shared ApplicationDbContext's
    /// change tracker and flushed later by the caller's own SaveChangesAsync call, so it
    /// commits atomically together with other pending changes in the same request. Placed
    /// after CancellationToken (not before) so existing call sites that pass
    /// cancellationToken positionally as their last argument are unaffected and keep
    /// getting persistImmediately: true. Do not reorder this parameter.
    /// </param>
    Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default,
        bool persistImmediately = true);
}
```

**Step 4 — implement the adapter change**

Replace the full contents of
`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationAdapter.cs`
(currently):

```csharp
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class LogisticsStockOperationAdapter : ILogisticsStockOperationService
{
    private readonly IStockUpProcessingService _stockUpProcessingService;

    public LogisticsStockOperationAdapter(IStockUpProcessingService stockUpProcessingService)
    {
        _stockUpProcessingService = stockUpProcessingService;
    }

    public Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default)
    {
        var mappedSourceType = MapSourceType(sourceType);
        return _stockUpProcessingService.CreateOperationAsync(
            documentNumber,
            productCode,
            amount,
            mappedSourceType,
            sourceId,
            cancellationToken);
    }

    private static StockUpSourceType MapSourceType(LogisticsStockOperationSource sourceType) => sourceType switch
    {
        LogisticsStockOperationSource.TransportBox => StockUpSourceType.TransportBox,
        LogisticsStockOperationSource.GiftPackageManufacture => StockUpSourceType.GiftPackageManufacture,
        _ => throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, null),
    };
}
```

with:

```csharp
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class LogisticsStockOperationAdapter : ILogisticsStockOperationService
{
    private readonly IStockUpProcessingService _stockUpProcessingService;

    public LogisticsStockOperationAdapter(IStockUpProcessingService stockUpProcessingService)
    {
        _stockUpProcessingService = stockUpProcessingService;
    }

    public Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default,
        bool persistImmediately = true)
    {
        var mappedSourceType = MapSourceType(sourceType);
        return _stockUpProcessingService.CreateOperationAsync(
            documentNumber,
            productCode,
            amount,
            mappedSourceType,
            sourceId,
            cancellationToken,
            persistImmediately);
    }

    private static StockUpSourceType MapSourceType(LogisticsStockOperationSource sourceType) => sourceType switch
    {
        LogisticsStockOperationSource.TransportBox => StockUpSourceType.TransportBox,
        LogisticsStockOperationSource.GiftPackageManufacture => StockUpSourceType.GiftPackageManufacture,
        _ => throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, null),
    };
}
```

**Step 5 — run tests again and confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LogisticsStockOperationAdapterTests"
```

All 5 tests (4 original + 1 new `CreateOperationAsync_PersistImmediatelyFalse_ForwardsToService`)
must pass.

**Step 6 — build and format check**

```bash
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
```

**Step 7 — commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationService.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsStockOperationAdapterTests.cs
git commit -m "Thread persistImmediately through ILogisticsStockOperationService/LogisticsStockOperationAdapter"
```

**Acceptance criteria**

- `dotnet build Anela.Heblo.sln` succeeds with no errors.
- `dotnet format Anela.Heblo.sln --verify-no-changes` reports no changes.
- All 5 tests in `LogisticsStockOperationAdapterTests` pass, including the new
  `CreateOperationAsync_PersistImmediatelyFalse_ForwardsToService`.
- `ILogisticsStockOperationService.CreateOperationAsync` has exactly this signature:
  `Task CreateOperationAsync(string documentNumber, string productCode, int amount, LogisticsStockOperationSource sourceType, int sourceId, CancellationToken cancellationToken = default, bool persistImmediately = true)`.
- `LogisticsStockOperationAdapter.CreateOperationAsync` forwards `persistImmediately` unchanged
  into `_stockUpProcessingService.CreateOperationAsync`.
- No other file besides the 3 listed above is modified by this task.

---
