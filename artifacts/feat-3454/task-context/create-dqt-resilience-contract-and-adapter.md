### task: create-dqt-resilience-contract-and-adapter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtResilienceService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityResilienceAdapter.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityResilienceAdapterTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:60-62`

This task creates the new DataQuality-owned contract, the Catalog-owned adapter that implements it, registers the adapter in Catalog's DI module, and adds a small delegation test for the adapter (mirroring the existing sibling adapter tests in the same test folder).

- [ ] Step 1: Create the new contract file `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtResilienceService.cs` with this exact content:

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IDqtResilienceService
{
    Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);
}
```

- [ ] Step 2: Create the new adapter file `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityResilienceAdapter.cs` with this exact content:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityResilienceAdapter : IDqtResilienceService
{
    private readonly ICatalogResilienceService _resilienceService;

    public DataQualityResilienceAdapter(ICatalogResilienceService resilienceService)
    {
        _resilienceService = resilienceService;
    }

    public Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default) =>
        _resilienceService.ExecuteWithResilienceAsync(operation, operationName, cancellationToken);
}
```

- [ ] Step 3: Open `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`. Find these two existing lines (currently at lines 60-62):

```csharp
        // DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.
        services.AddScoped<IStockOperationQuery, DataQualityStockOperationQueryAdapter>();
        services.AddScoped<IStockTakingQuery, DataQualityStockTakingQueryAdapter>();
```

Replace them with (adding the new registration immediately after, with its own explanatory comment matching the existing style):

```csharp
        // DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.
        services.AddScoped<IStockOperationQuery, DataQualityStockOperationQueryAdapter>();
        services.AddScoped<IStockTakingQuery, DataQualityStockTakingQueryAdapter>();
        // DataQuality owns the resilience contract; Catalog (this module) provides the adapter implementation.
        services.AddScoped<IDqtResilienceService, DataQualityResilienceAdapter>();
```

Note: `CatalogModule.cs` already has `using Anela.Heblo.Application.Features.DataQuality.Contracts;` at line 21 (used by `IStockOperationQuery`/`IStockTakingQuery`), so no new `using` directive is needed — `IDqtResilienceService` resolves from the same namespace.

- [ ] Step 4: Create the adapter test file `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityResilienceAdapterTests.cs` with this exact content:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class DataQualityResilienceAdapterTests
{
    private readonly Mock<ICatalogResilienceService> _resilienceService = new();

    private DataQualityResilienceAdapter CreateAdapter() => new(_resilienceService.Object);

    [Fact]
    public async Task ExecuteWithResilienceAsync_DelegatesToUnderlyingService_WithSameArgumentsAndReturnValue()
    {
        // Arrange
        Func<CancellationToken, Task<int>> operation = _ => Task.FromResult(42);
        const string operationName = "TestOperation";
        using var cts = new CancellationTokenSource();

        _resilienceService
            .Setup(r => r.ExecuteWithResilienceAsync(operation, operationName, cts.Token))
            .ReturnsAsync(42);

        // Act
        var result = await CreateAdapter().ExecuteWithResilienceAsync(operation, operationName, cts.Token);

        // Assert
        result.Should().Be(42);
        _resilienceService.Verify(
            r => r.ExecuteWithResilienceAsync(operation, operationName, cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_PropagatesException_WhenUnderlyingServiceThrows()
    {
        // Arrange
        Func<CancellationToken, Task<int>> operation = _ => Task.FromResult(0);
        const string operationName = "FailingOperation";

        _resilienceService
            .Setup(r => r.ExecuteWithResilienceAsync(operation, operationName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var act = () => CreateAdapter().ExecuteWithResilienceAsync(operation, operationName, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
```

- [ ] Step 5: From the repository root (`/home/user/worktrees/feature-3454-Arch-Review-Dataquality-Productpairingdqtcomparer`), build the solution to verify the new contract, adapter, and DI registration compile:

```bash
dotnet build Anela.Heblo.sln
```

Confirm the build succeeds with no errors. `ProductPairingDqtComparer` still uses `ICatalogResilienceService` at this point (unchanged), so this step only proves the new types and DI wiring compile alongside the existing code.

- [ ] Step 6: Run the new adapter test to confirm it passes:

```bash
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~DataQualityResilienceAdapterTests"
```

Confirm both tests pass (2 total).

- [ ] Step 7: Commit:

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtResilienceService.cs backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityResilienceAdapter.cs backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityResilienceAdapterTests.cs backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git commit -m "Add IDqtResilienceService contract and Catalog-side adapter"
```

---
