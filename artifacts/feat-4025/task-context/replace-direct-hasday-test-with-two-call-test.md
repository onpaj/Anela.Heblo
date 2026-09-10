### task: replace-direct-hasday-test-with-two-call-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs:234-249`

**FR mapped:** FR-2 (test refactor). Must run before `narrow-interface-and-privatize-method` so the old direct-call test is gone before the method it calls becomes inaccessible from outside the class.

- [ ] **Step 1: Add the new replacement test, in place of the old one, at the same location (lines 234-249)**

Replace the entire `HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue` test method shown in Context above with:

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

Note: this is a straight *replacement* of the old method body/name at the same location in the file — do not leave the old test present alongside the new one.

- [ ] **Step 2: Run the test file to verify the new test passes and the old one is gone**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConsumptionCalculationServiceTests"`
Expected: all tests in `ConsumptionCalculationServiceTests` PASS, including the new `ProcessDailyConsumptionAsync_CalledTwiceForSameDate_SecondCallReturnsWasRunFalse`; no test named `HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue` appears in the output.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs
git commit -m "test(packing-materials): verify processing idempotency via ProcessDailyConsumptionAsync instead of calling HasDayAlreadyBeenProcessedAsync directly"
```

---

