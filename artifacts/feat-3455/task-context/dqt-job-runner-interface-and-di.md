### task: dqt-job-runner-interface-and-di

## Goal

Introduce a shared `IDqtJobRunner` interface implemented by both `InvoiceDqtJobRunner` and `DriftDqtJobRunner`, register it additively in DI, and add a new `ErrorCodes.DqtUnsupportedTestType = 2204` entry that a later task will consume. This task is purely additive: no existing method body changes, no handler changes. The build must remain green and all existing tests must keep passing unmodified after this task.

## Context

### Current file: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IInvoiceDqtJobRunner.cs`
```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Services;

/// <summary>
/// Runs the invoice DQT comparison for a given DQT run ID.
/// </summary>
public interface IInvoiceDqtJobRunner
{
    Task RunAsync(Guid dqtRunId, CancellationToken cancellationToken = default);
}
```

### Current file: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDriftDqtJobRunner.cs`
```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Services;

public interface IDriftDqtJobRunner
{
    Task RunAsync(Guid runId, CancellationToken ct = default);
}
```

### Current file: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtJobRunner.cs` (full)
```csharp
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class InvoiceDqtJobRunner : IInvoiceDqtJobRunner
{
    private readonly IDqtRunRepository _repository;
    private readonly IInvoiceDqtComparer _comparer;
    private readonly ILogger<InvoiceDqtJobRunner> _logger;

    public InvoiceDqtJobRunner(
        IDqtRunRepository repository,
        IInvoiceDqtComparer comparer,
        ILogger<InvoiceDqtJobRunner> logger)
    {
        _repository = repository;
        _comparer = comparer;
        _logger = logger;
    }

    public async Task RunAsync(Guid dqtRunId, CancellationToken cancellationToken = default)
    {
        var run = await _repository.GetByIdAsync(dqtRunId, cancellationToken);
        if (run == null)
        {
            _logger.LogWarning("DQT run {DqtRunId} not found", dqtRunId);
            return;
        }

        _logger.LogInformation("Starting DQT run {DqtRunId} ({TestType}) for {DateFrom} to {DateTo}",
            dqtRunId, run.TestType, run.DateFrom, run.DateTo);

        try
        {
            var result = await _comparer.CompareAsync(run.DateFrom, run.DateTo, cancellationToken);

            var resultEntities = result.Mismatches
                .Select(m => InvoiceDqtResult.Create(run.Id, m.InvoiceCode, m.MismatchType, m.ShoptetValue, m.FlexiValue, m.Details))
                .ToList();

            foreach (var entity in resultEntities)
                run.Results.Add(entity);

            // Explicitly register with EF — FindAsync without Include() does not set up
            // a change-tracking collection, so items added to run.Results are invisible
            // to the context until explicitly added here.
            await _repository.AddResultsAsync(resultEntities, cancellationToken);

            run.Complete(result.TotalChecked, result.Mismatches.Count);

            _logger.LogInformation("DQT run {DqtRunId} completed: {Checked} checked, {Mismatches} mismatches",
                dqtRunId, result.TotalChecked, result.Mismatches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DQT run {DqtRunId} failed", dqtRunId);
            run.Fail(ex.Message);
        }
        finally
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
    }
}
```

### Current file: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs` (full)
```csharp
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class DriftDqtJobRunner : IDriftDqtJobRunner
{
    private readonly IDqtRunRepository _repository;
    private readonly IEnumerable<IDriftDqtComparer> _comparers;
    private readonly ILogger<DriftDqtJobRunner> _logger;

    public DriftDqtJobRunner(
        IDqtRunRepository repository,
        IEnumerable<IDriftDqtComparer> comparers,
        ILogger<DriftDqtJobRunner> logger)
    {
        _repository = repository;
        _comparers = comparers;
        _logger = logger;
    }

    public async Task RunAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _repository.GetByIdAsync(runId, ct);
        if (run == null)
        {
            _logger.LogWarning("Drift DQT run {RunId} not found", runId);
            return;
        }

        _logger.LogInformation("Starting drift DQT run {RunId} ({TestType}) for {DateFrom} to {DateTo}",
            runId, run.TestType, run.DateFrom, run.DateTo);

        try
        {
            var comparer = _comparers.SingleOrDefault(c => c.TestType == run.TestType)
                ?? throw new InvalidOperationException(
                    $"No IDriftDqtComparer registered for {run.TestType}");

            var result = await comparer.CompareAsync(run.DateFrom, run.DateTo, ct);

            var entities = result.Mismatches
                .Select(m => DqtDriftResult.Create(
                    run.Id, run.TestType, m.EntityKey, m.MismatchCode,
                    m.HebloValue, m.ShoptetValue, m.Details))
                .ToList();

            await _repository.AddDriftResultsAsync(entities, ct);
            run.Complete(result.TotalChecked, result.Mismatches.Count);

            _logger.LogInformation("Drift DQT run {RunId} completed: {Checked} checked, {Mismatches} mismatches",
                runId, result.TotalChecked, result.Mismatches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Drift DQT run {RunId} ({TestType}) failed", runId, run.TestType);
            run.Fail(ex.Message);
        }
        finally
        {
            await _repository.SaveChangesAsync(ct);
        }
    }
}
```

### Current file: `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs` (full)
```csharp
using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Persistence.DataQuality;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.DataQuality;

public static class DataQualityModule
{
    public static IServiceCollection AddDataQualityModule(this IServiceCollection services)
    {
        services.AddScoped<IDqtRunRepository, DqtRunRepository>();
        services.AddScoped<IInvoiceDqtComparer, InvoiceDqtComparer>();
        services.AddScoped<IInvoiceDqtJobRunner, InvoiceDqtJobRunner>();
        services.AddScoped<IDriftDqtJobRunner, DriftDqtJobRunner>();
        services.AddScoped<IDriftDqtComparer, ProductPairingDqtComparer>();
        services.AddScoped<IDriftDqtComparer, StockWriteBackDqtComparer>();

        // Register dashboard tiles
        services.RegisterTile<DataQualityStatusTile>();
        services.RegisterTile<DqtYesterdayStatusTile>();

        return services;
    }
}
```

### Current file: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — relevant excerpt (DataQuality `22XX` block, exact lines as they exist today)
```csharp
    // DataQuality module errors (22XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    DqtRunNotFound = 2201,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    DqtInvalidDateRange = 2202,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    DqtExternalServiceError = 2203,

    // Marketing Calendar errors (23XX)
```
The file starts with `using System.Net;` and the enum is `public enum ErrorCodes` in namespace `Anela.Heblo.Application.Shared`. Every entry has an `[HttpStatusCode(...)]` attribute immediately above it (this attribute type is already defined elsewhere in the codebase and does not need to be created).

## Files to create/modify

- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtJobRunner.cs` — **create new file**.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtJobRunner.cs` — add `IDqtJobRunner` to the class's implemented-interfaces list and add a `CanHandle` method.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs` — add `IDqtJobRunner` to the class's implemented-interfaces list and add a `CanHandle` method.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs` — add two additive `AddScoped<IDqtJobRunner, ...>()` registrations.
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add one new enum entry.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DataQualityModuleTests.cs` — **create new file** (DI registration test, see Tests to write).

## Implementation steps

1. Create `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtJobRunner.cs` with exactly:
   ```csharp
   using Anela.Heblo.Domain.Features.DataQuality;

   namespace Anela.Heblo.Application.Features.DataQuality.Services;

   public interface IDqtJobRunner
   {
       bool CanHandle(DqtTestType testType);
       Task RunAsync(Guid runId, CancellationToken ct = default);
   }
   ```

2. In `InvoiceDqtJobRunner.cs`, change the class declaration line from
   ```csharp
   public class InvoiceDqtJobRunner : IInvoiceDqtJobRunner
   ```
   to
   ```csharp
   public class InvoiceDqtJobRunner : IInvoiceDqtJobRunner, IDqtJobRunner
   ```
   and add this method to the class body (placement: anywhere in the class, e.g. immediately before the existing `RunAsync` method):
   ```csharp
   public bool CanHandle(DqtTestType testType) => testType == DqtTestType.IssuedInvoiceComparison;
   ```
   No other changes to this file. The existing `RunAsync(Guid dqtRunId, CancellationToken cancellationToken = default)` method already satisfies `IDqtJobRunner.RunAsync(Guid runId, CancellationToken ct = default)` — C# does not require parameter names to match between an interface and its implementation.

3. In `DriftDqtJobRunner.cs`, change the class declaration line from
   ```csharp
   public class DriftDqtJobRunner : IDriftDqtJobRunner
   ```
   to
   ```csharp
   public class DriftDqtJobRunner : IDriftDqtJobRunner, IDqtJobRunner
   ```
   and add this method to the class body:
   ```csharp
   public bool CanHandle(DqtTestType testType) => _comparers.Any(c => c.TestType == testType);
   ```
   This delegates to the already-injected `IEnumerable<IDriftDqtComparer> _comparers` field — no new dependency needed. No other changes to this file.

4. In `DataQualityModule.cs`, add two lines directly beneath the existing `AddScoped<IInvoiceDqtJobRunner, InvoiceDqtJobRunner>()` / `AddScoped<IDriftDqtJobRunner, DriftDqtJobRunner>()` lines, so the block reads:
   ```csharp
   services.AddScoped<IInvoiceDqtJobRunner, InvoiceDqtJobRunner>();
   services.AddScoped<IDriftDqtJobRunner, DriftDqtJobRunner>();
   services.AddScoped<IDqtJobRunner, InvoiceDqtJobRunner>();
   services.AddScoped<IDqtJobRunner, DriftDqtJobRunner>();
   ```
   Do not remove or modify the two pre-existing lines — this is additive only.

5. In `ErrorCodes.cs`, insert a new entry immediately after `DqtExternalServiceError = 2203,` (still inside the `// DataQuality module errors (22XX)` block, before the `// Marketing Calendar errors (23XX)` comment):
   ```csharp
   [HttpStatusCode(HttpStatusCode.InternalServerError)]
   DqtUnsupportedTestType = 2204,
   ```

## Tests to write

- Create `backend/test/Anela.Heblo.Tests/Features/DataQuality/DataQualityModuleTests.cs` with a test that verifies the new DI registrations by inspecting `ServiceDescriptor`s on a fresh `IServiceCollection` (do **not** call `BuildServiceProvider()`/resolve instances — `AddDataQualityModule()` also registers a `DbContext`-backed repository and other services that are not trivially constructible in a unit test; inspecting descriptors avoids needing to satisfy all of that). Follow the lightweight pattern already used in `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` (which asserts on `services.Single(s => s.ServiceType == typeof(...))` without building the provider). Exact test file content:
  ```csharp
  using Anela.Heblo.Application.Features.DataQuality;
  using Anela.Heblo.Application.Features.DataQuality.Services;
  using Microsoft.Extensions.DependencyInjection;
  using Xunit;

  namespace Anela.Heblo.Tests.Features.DataQuality;

  public class DataQualityModuleTests
  {
      [Fact]
      public void AddDataQualityModule_RegistersBothRunnersUnderIDqtJobRunner()
      {
          // Arrange
          var services = new ServiceCollection();

          // Act
          services.AddDataQualityModule();

          // Assert
          var dqtJobRunnerDescriptors = services
              .Where(s => s.ServiceType == typeof(IDqtJobRunner))
              .ToList();

          Assert.Equal(2, dqtJobRunnerDescriptors.Count);
          Assert.Contains(dqtJobRunnerDescriptors, d => d.ImplementationType == typeof(InvoiceDqtJobRunner));
          Assert.Contains(dqtJobRunnerDescriptors, d => d.ImplementationType == typeof(DriftDqtJobRunner));
      }

      [Fact]
      public void AddDataQualityModule_RetainsExistingNarrowInterfaceRegistrations()
      {
          // Arrange
          var services = new ServiceCollection();

          // Act
          services.AddDataQualityModule();

          // Assert — narrow interfaces are retained, additive-only change
          Assert.Contains(services, d => d.ServiceType == typeof(IInvoiceDqtJobRunner) && d.ImplementationType == typeof(InvoiceDqtJobRunner));
          Assert.Contains(services, d => d.ServiceType == typeof(IDriftDqtJobRunner) && d.ImplementationType == typeof(DriftDqtJobRunner));
      }
  }
  ```
  Note: `AddDataQualityModule` is an extension method in namespace `Anela.Heblo.Application.Features.DataQuality` (the `using` above brings it into scope).

## Acceptance criteria

- `dotnet build` succeeds for the whole solution.
- `dotnet format` reports no changes needed (or is run to apply formatting) for all touched files.
- All pre-existing tests in `Anela.Heblo.Tests` continue to pass unmodified (this task does not touch `RunDqtHandler.cs`, `GetDqtRunDetailHandler.cs`, `RunDqtHandlerTests.cs`, or `GetDqtRunDetailHandlerTests.cs`).
- The two new tests in `DataQualityModuleTests.cs` pass.
- `InvoiceDqtJobRunner.CanHandle(DqtTestType.IssuedInvoiceComparison)` returns `true`; `CanHandle` for `ProductPairing` or `StockWriteBackReconciliation` returns `false`.
- `DriftDqtJobRunner.CanHandle` returns `true` for `ProductPairing` and `StockWriteBackReconciliation`, `false` for `IssuedInvoiceComparison`.
- `ErrorCodes.DqtUnsupportedTestType` exists with value `2204` and `[HttpStatusCode(HttpStatusCode.InternalServerError)]`.

