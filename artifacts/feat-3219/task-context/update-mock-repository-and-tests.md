### task: update-mock-repository-and-tests

**Goal:** Update `MockPackingMaterialRepository` to implement the new `Task<bool>` signature for `AddDailyRunAsync`, add a per-date configurability mechanism, and update `ConsumptionCalculationServiceTests` to exercise the duplicate path via the new mock API.

**Files to change:**
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs` — change `AddDailyRunAsync` return type; add `_addDailyRunResults` dictionary; add `SetAddDailyRunReturns` method
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs` — update `ProcessDailyConsumptionAsync_PropagatesOtherDbUpdateExceptions` test; add new test for mock-driven duplicate path

**Implementation steps:**

*In `MockPackingMaterialRepository.cs`:*

1. Add a private field after `_saveChangesException`:
   ```csharp
   private readonly Dictionary<DateOnly, bool> _addDailyRunResults = new();
   ```
2. Add a public configuration method after `SetSaveChangesException`:
   ```csharp
   public void SetAddDailyRunReturns(DateOnly date, bool result)
   {
       _addDailyRunResults[date] = result;
   }
   ```
3. Replace the current `AddDailyRunAsync` implementation:
   ```csharp
   public Task<bool> AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default)
   {
       AddedDailyRuns.Add(run);
       var result = !_addDailyRunResults.TryGetValue(run.Date, out var configured) || configured;
       return Task.FromResult(result);
   }
   ```
   The default (no configuration for the date) is `true` (insertion succeeded), preserving existing test behaviour.

*In `ConsumptionCalculationServiceTests.cs`:*

4. Update `ProcessDailyConsumptionAsync_PropagatesOtherDbUpdateExceptions`: the exception is now thrown by the second-phase `SaveChangesAsync` (for consumption rows + quantity updates), not the daily-run phase. The test setup and assertion remain identical — `SetSaveChangesException` on the mock still causes `SaveChangesAsync` to throw, which now fires during the consumption-row phase. Update the inline comment to reflect this:
   ```csharp
   // Set up a non-duplicate DbUpdateException (no inner PostgresException).
   // After the refactor, AddDailyRunAsync returns true (mock default), so execution
   // reaches the second SaveChangesAsync (consumption rows + quantity updates), where
   // this exception is thrown.
   ```
5. Add a new test `ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAddDailyRunReturnsFalse` that uses `SetAddDailyRunReturns(date, false)` to simulate a concurrent duplicate at the persistence level:
   ```csharp
   [Fact]
   public async Task ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAddDailyRunReturnsFalse()
   {
       // Arrange — simulate concurrent duplicate: AddDailyRunAsync returns false
       var date = new DateOnly(2025, 6, 15);
       var material = new PackingMaterial("Tape", 3m, ConsumptionType.PerDay, 100m);
       var materialRepo = new MockPackingMaterialRepository();
       materialRepo.SetMaterials(new[] { material });
       materialRepo.SetAddDailyRunReturns(date, false);
       var invoiceSource = new MockInvoiceConsumptionSource();

       var service = BuildService(materialRepo, invoiceSource, _mockLogger);

       // Act
       var result = await service.ProcessDailyConsumptionAsync(date);

       // Assert
       Assert.False(result.WasRun);
       Assert.Equal(0, result.MaterialsProcessed);
       // Consumption rows must NOT have been persisted after duplicate detected
       Assert.Empty(materialRepo.AddedConsumptionRows);
   }
   ```

**Acceptance criteria:**
- `MockPackingMaterialRepository.AddDailyRunAsync` returns `Task<bool>`.
- Default return (no `SetAddDailyRunReturns` call for the date) is `true`.
- `SetAddDailyRunReturns(date, false)` causes the mock to return `false` for that date.
- All existing tests in `ConsumptionCalculationServiceTests` pass without modification to their assertions.
- `ProcessDailyConsumptionAsync_PropagatesOtherDbUpdateExceptions` still passes and now includes a comment explaining the exception fires from the consumption-row save phase.
- New test `ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAddDailyRunReturnsFalse` passes.
- `dotnet build` on the Tests project succeeds.

**Notes:**
- Depends on `update-repository-interface`, `implement-persistence-duplicate-detection`, and `refactor-consumption-calculation-service` all completing first.
- The existing test `ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAlreadyProcessed` (line 140) exercises the `HasDailyProcessingBeenRunAsync` pre-check path and does NOT need modification — it remains the primary guard path test.
- `AddedDailyRuns.Add(run)` must still run before checking the configured result, so the assertion `Assert.Single(materialRepo.AddedDailyRuns)` in other tests continues to work. In the new duplicate-path test, `AddedDailyRuns` will contain the run even when `false` is returned, mirroring the persistence implementation which does add the entity before the save attempt.
