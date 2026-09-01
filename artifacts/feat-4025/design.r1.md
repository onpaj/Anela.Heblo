# Design: Remove `HasDayAlreadyBeenProcessedAsync` from `IConsumptionCalculationService`

## Component Design

### `IConsumptionCalculationService` (interface, unchanged file location)
`backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/IConsumptionCalculationService.cs`

Responsibility: the single public contract for triggering daily packing-material consumption processing, consumed by `ProcessDailyConsumptionHandler`.

Target member list (one method, down from two):
```csharp
public interface IConsumptionCalculationService
{
    Task<ProcessDailyConsumptionResult> ProcessDailyConsumptionAsync(
        DateOnly processingDate,
        CancellationToken cancellationToken = default);
}
```

### `ConsumptionCalculationService` (class, unchanged file location)
`backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs`

Responsibility: unchanged — computes and applies daily packing-material consumption, guarding against reprocessing an already-processed date.

Change: `HasDayAlreadyBeenProcessedAsync` moves from an implicit public interface member to a `private` implementation detail. It keeps its exact current signature and body — only the access modifier changes:

```csharp
private async Task<bool> HasDayAlreadyBeenProcessedAsync(
    DateOnly date,
    CancellationToken cancellationToken = default)
{
    return await _repository.HasDailyProcessingBeenRunAsync(date, cancellationToken);
}
```

The call site inside `ProcessDailyConsumptionAsync` (`if (await HasDayAlreadyBeenProcessedAsync(processingDate, cancellationToken)) { ... }`) requires no code change — it is already an unqualified same-class call, which resolves against the concrete type regardless of the member's access modifier.

### Consumers (unchanged)
- `ProcessDailyConsumptionHandler` — continues to depend only on `IConsumptionCalculationService.ProcessDailyConsumptionAsync`; no code change required in this file.
- `PackingMaterialsModule.AddPackingMaterialsModule()` — DI registration `services.AddScoped<IConsumptionCalculationService, ConsumptionCalculationService>();` is unaffected; binding is by concrete type, not by member list.

### Test doubles
- `ConsumptionCalculationServiceTests` (test file) — the existing test `HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue`, which calls the method directly on the concrete instance, is replaced by a test that verifies the same idempotency guarantee through the public API. See **Data Schemas / test scenario** below for the exact shape.
- `ProcessDailyConsumptionHandlerTests` (test file) — no change. It mocks `IConsumptionCalculationService` via `Mock<IConsumptionCalculationService>` and never references `HasDayAlreadyBeenProcessedAsync`, so it is unaffected by the interface shrink.
- `MockPackingMaterialRepository` (test double) — no change needed. Verified: `SetHasDailyProcessingBeenRun(date, hasRun)` already exists and is the correct seam for the new test — `AddDailyRunAsync` records into `AddedDailyRuns` but does **not** update the internal `_dailyProcessingStatus` dictionary that `HasDailyProcessingBeenRunAsync` reads from, so the replacement test must explicitly call `SetHasDailyProcessingBeenRun(date, true)` between its two `ProcessDailyConsumptionAsync` calls rather than relying on the first call's `AddDailyRunAsync` to implicitly flip that state.

## Data Schemas

No database schema, API request/response shape, or event payload changes. `ProcessDailyConsumptionResult` (`WasRun: bool`, `MaterialsProcessed: int`) is unchanged. `IPackingMaterialRepository` (including `HasDailyProcessingBeenRunAsync` and `AddDailyRunAsync`) is unchanged.

### Replacement test scenario (FR-2)
This is the exact shape the new/adapted test in `ConsumptionCalculationServiceTests.cs` must follow, replacing `HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue`:

```csharp
[Fact]
public async Task ProcessDailyConsumptionAsync_CalledTwiceForSameDate_SecondCallReturnsWasRunFalse()
{
    // Arrange
    var date = new DateOnly(2025, 6, 15);
    var material = new PackingMaterial("Tape", 3m, ConsumptionType.PerDay, 100m);
    var materialRepo = new MockPackingMaterialRepository();
    materialRepo.SetMaterials(new[] { material });
    var invoiceSource = new MockInvoiceConsumptionSource();
    var service = BuildService(materialRepo, invoiceSource, _mockLogger);

    // Act — first call: a genuine, unprocessed run
    var firstResult = await service.ProcessDailyConsumptionAsync(date);

    // The mock's AddDailyRunAsync does not auto-flip HasDailyProcessingBeenRunAsync,
    // so simulate the persisted idempotency state a real repository would now report
    // for this date before the second call.
    materialRepo.SetHasDailyProcessingBeenRun(date, true);

    // Act — second call: same date, should be a no-op
    var secondResult = await service.ProcessDailyConsumptionAsync(date);

    // Assert
    Assert.True(firstResult.WasRun);
    Assert.False(secondResult.WasRun);
    Assert.Equal(0, secondResult.MaterialsProcessed);
}
```

This satisfies the issue's suggested fix verbatim ("call it twice for the same date, assert `WasRun: false` on the second call") while exercising the idempotency guard exclusively through the public `ProcessDailyConsumptionAsync` entry point — no direct call to the now-private `HasDayAlreadyBeenProcessedAsync`.
