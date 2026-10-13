### task: fix-process-daily-consumption-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs:20-67`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs:108-151`

- [ ] **Step 1: Rewrite the existing exception test to expect propagation instead of a swallowed response**

Replace the entire `Handle_ReturnsGenericError_WhenServiceThrows` test and its now-unused `VerifyErrorLogged` helper (lines 108-151 of `ProcessDailyConsumptionHandlerTests.cs`) with:

```csharp
    [Fact]
    public async Task Handle_PropagatesException_WhenServiceThrows()
    {
        // Arrange
        var thrown = new InvalidOperationException("secret database connection string");

        var (sut, service, _) = MakeSut();
        service
            .Setup(s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);

        var request = new ProcessDailyConsumptionRequest { ProcessingDate = TestDate };

        // Act
        Func<Task> act = () => sut.Handle(request, CancellationToken.None);

        // Assert — the handler no longer swallows the exception into a Success=false response.
        // DailyConsumptionJob's own catch/throw relies on this to make Hangfire's retry
        // contract work (see DailyConsumptionJobTests.cs, task: add-daily-consumption-job-failure-test).
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(thrown);
    }
```

This deletes the private `VerifyErrorLogged` static method too (its only caller was the test just replaced) — leave the file ending at the new test's closing `}` followed by the class's closing `}` (no trailing helper method).

- [ ] **Step 2: Run the test to confirm it fails against the current (unfixed) handler**

Run: `dotnet test --filter "FullyQualifiedName~ProcessDailyConsumptionHandlerTests.Handle_PropagatesException_WhenServiceThrows"`
Expected: FAIL — the current handler still catches the exception and returns a `Success = false` response, so `act.Should().ThrowAsync<InvalidOperationException>()` fails because nothing was thrown.

- [ ] **Step 3: Remove the catch-and-swallow block from the handler**

Replace the full `Handle` method body in `ProcessDailyConsumptionHandler.cs` (lines 20-67) with:

```csharp
    public async Task<ProcessDailyConsumptionResponse> Handle(
        ProcessDailyConsumptionRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing daily consumption for {Date}", request.ProcessingDate);

        var result = await _consumptionService.ProcessDailyConsumptionAsync(request.ProcessingDate, cancellationToken);

        if (!result.WasRun)
        {
            return new ProcessDailyConsumptionResponse
            {
                Success = false,
                ProcessedDate = request.ProcessingDate,
                MaterialsProcessed = 0,
                Message = $"Daily consumption for {request.ProcessingDate} was already processed"
            };
        }

        _logger.LogInformation("Successfully processed daily consumption for {Date}", request.ProcessingDate);

        var message = result.MaterialsProcessed > 0
            ? $"Daily consumption successfully processed for {request.ProcessingDate}. {result.MaterialsProcessed} materials updated."
            : $"No invoices found for {request.ProcessingDate} — no materials were updated.";

        return new ProcessDailyConsumptionResponse
        {
            Success = true,
            ProcessedDate = request.ProcessingDate,
            MaterialsProcessed = result.MaterialsProcessed,
            Message = message
        };
    }
```

This removes the `try`/`catch (Exception ex) { ... }` wrapper entirely — there is no longer any exception-handling code in this method, only the two existing informational log calls and the two existing return branches, now unindented one level.

- [ ] **Step 4: Run the whole test file to confirm the new test passes and no existing test regressed**

Run: `dotnet test --filter "FullyQualifiedName~ProcessDailyConsumptionHandlerTests"`
Expected: PASS — all 4 tests (`Handle_ReturnsFailure_WhenAlreadyProcessed`, `Handle_ReturnsSuccess_WhenMaterialsUpdated`, `Handle_ReturnsSuccessWithZeroCount_WhenNoInvoicesFound`, `Handle_PropagatesException_WhenServiceThrows`) pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs
git commit -m "fix(packing-materials): propagate exceptions from ProcessDailyConsumptionHandler instead of swallowing them"
```

---

