### task: fix-get-daily-consumption-breakdown-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownHandler.cs:22-65`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetDailyConsumptionBreakdownHandlerTests.cs:165-185`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs` (add a throw-hook for `GetConsumptionsByDateAsync`, mirroring the existing `SetSaveChangesException`/`_saveChangesException` pattern already in this file)

- [ ] **Step 1: Add a throw-hook to the shared repository fake**

`MockPackingMaterialRepository` already has this exact pattern for `SaveChangesAsync` (`_saveChangesException` field + `SetSaveChangesException` method, lines 11, 28-31, 140-143). Add the equivalent for `GetConsumptionsByDateAsync`, which currently has no way to simulate a repository failure.

In `MockPackingMaterialRepository.cs`, add a new field next to `_saveChangesException` (line 11):

```csharp
    private Exception? _saveChangesException;
    private Exception? _getConsumptionsByDateException;
```

Add a new method next to `SetSaveChangesException` (after line 31):

```csharp
    public void SetGetConsumptionsByDateException(Exception ex)
    {
        _getConsumptionsByDateException = ex;
    }
```

Change `GetConsumptionsByDateAsync` (line 193-194) from:

```csharp
    public Task<IEnumerable<PackingMaterialConsumption>> GetConsumptionsByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<PackingMaterialConsumption>>(ConsumptionRowsByDate.TryGetValue(date, out var rows) ? rows : new List<PackingMaterialConsumption>());
```

to:

```csharp
    public Task<IEnumerable<PackingMaterialConsumption>> GetConsumptionsByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        if (_getConsumptionsByDateException != null)
            throw _getConsumptionsByDateException;
        return Task.FromResult<IEnumerable<PackingMaterialConsumption>>(ConsumptionRowsByDate.TryGetValue(date, out var rows) ? rows : new List<PackingMaterialConsumption>());
    }
```

- [ ] **Step 2: Rewrite the existing out-of-range-enum test to expect propagation, and add a repository-throws test**

Replace `GroupBy_OutOfRangeEnumValue_ReturnsFailureResponse` (lines 165-185 of `GetDailyConsumptionBreakdownHandlerTests.cs`) with:

```csharp
    [Fact]
    public async Task GroupBy_OutOfRangeEnumValue_PropagatesException()
    {
        // Arrange: an out-of-range enum value can only occur via an unchecked cast — ASP.NET Core's
        // model binder can never produce one for a real HTTP request, but the handler's switch must
        // still fail loudly (not silently succeed) if it ever receives one, e.g. from a future
        // internal caller. The discard arm of the switch throws ArgumentOutOfRangeException, which
        // now propagates out of Handle instead of being converted into a Success=false response —
        // ASP.NET Core's existing ArgumentExceptionHandler (registered globally) maps ArgumentException
        // and its subclasses to a 400 automatically, so this is still a client-facing 400, just via
        // the standard exception pipeline instead of this handler building the response by hand.
        var repo = BuildRepo(Array.Empty<PackingMaterial>(), new[] { MakeConsumption(1, 5m, invoiceId: "INV-1") });
        var handler = BuildHandler(repo);
        var outOfRangeGroupBy = (ConsumptionGroupBy)99;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            handler.Handle(
                new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = outOfRangeGroupBy },
                CancellationToken.None));

        Assert.Contains("Unhandled GroupBy value", exception.Message);
    }

    [Fact]
    public async Task Handle_PropagatesException_WhenRepositoryThrows()
    {
        // Arrange
        var repo = new MockPackingMaterialRepository();
        var thrown = new InvalidOperationException("database unreachable");
        repo.SetGetConsumptionsByDateException(thrown);
        var handler = BuildHandler(repo);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = ConsumptionGroupBy.Material },
                CancellationToken.None));

        Assert.Same(thrown, exception);
    }
```

- [ ] **Step 3: Run the test file to confirm both new/changed tests fail against the current (unfixed) handler**

Run: `dotnet test --filter "FullyQualifiedName~GetDailyConsumptionBreakdownHandlerTests"`
Expected: FAIL on `GroupBy_OutOfRangeEnumValue_PropagatesException` and `Handle_PropagatesException_WhenRepositoryThrows` — the current handler still catches both exceptions and returns a `Success = false` response, so `Assert.ThrowsAsync` fails because nothing was thrown. All other tests in the file still pass.

- [ ] **Step 4: Remove the catch-and-swallow block from the handler**

Replace the full `Handle` method body in `GetDailyConsumptionBreakdownHandler.cs` (lines 22-65) with:

```csharp
    public async Task<GetDailyConsumptionBreakdownResponse> Handle(
        GetDailyConsumptionBreakdownRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading daily consumption breakdown for {Date} grouped by {GroupBy}", request.Date, request.GroupBy);

        var consumptions = (await _repository.GetConsumptionsByDateAsync(request.Date, cancellationToken)).ToList();

        if (consumptions.Count == 0)
            return new GetDailyConsumptionBreakdownResponse { Success = true, Date = request.Date, GroupBy = request.GroupBy.ToString() };

        var materials = (await _repository.GetAllWithAllocationsAsync(cancellationToken)).ToList();

        var groups = request.GroupBy switch
        {
            ConsumptionGroupBy.Material => BuildGroupByMaterial(consumptions, materials),
            ConsumptionGroupBy.Product => BuildGroupByProduct(consumptions, materials),
            ConsumptionGroupBy.Order => BuildGroupByOrder(consumptions, materials),
            _ => throw new ArgumentOutOfRangeException(nameof(request.GroupBy), request.GroupBy, "Unhandled GroupBy value.")
        };

        return new GetDailyConsumptionBreakdownResponse
        {
            Success = true,
            Date = request.Date,
            GroupBy = request.GroupBy.ToString(),
            Groups = groups
        };
    }
```

This removes the `try`/`catch (Exception ex) { ... }` wrapper entirely — the three private `BuildGroupBy*` helper methods below it are untouched.

- [ ] **Step 5: Run the whole test file to confirm everything passes**

Run: `dotnet test --filter "FullyQualifiedName~GetDailyConsumptionBreakdownHandlerTests"`
Expected: PASS — every test in the file passes, including the two rewritten/added ones.

- [ ] **Step 6: Full backend validation**

Run, from the repository root (where `Anela.Heblo.sln` lives):

```bash
dotnet build
dotnet format --verify-no-changes
dotnet test --filter "FullyQualifiedName~PackingMaterials"
```

Expected: `dotnet build` succeeds with no errors; `dotnet format --verify-no-changes` reports no files need formatting (if it does, run `dotnet format` and re-stage before committing); `dotnet test` reports all PackingMaterials-scoped tests passing, including every test touched or added across all three tasks in this plan.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownHandler.cs backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetDailyConsumptionBreakdownHandlerTests.cs backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs
git commit -m "fix(packing-materials): propagate exceptions from GetDailyConsumptionBreakdownHandler instead of swallowing them"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (`ProcessDailyConsumptionHandler` propagates) → `fix-process-daily-consumption-handler`, Steps 1-4.
- FR-2 (`DailyConsumptionJob` retry contract, test-only) → `add-daily-consumption-job-failure-test`, all steps.
- FR-3 (`GetDailyConsumptionBreakdownHandler` propagates) → `fix-get-daily-consumption-breakdown-handler`, Steps 1-5 (Step 1's repository fake extension is a direct prerequisite for the Step 2 test spec calls for).
- FR-4 (API surfaces 5xx) → no code change required per arch-review.r1.md's confirmed finding that `app.UseExceptionHandler()` + `AddProblemDetails()` already covers this controller; verified by FR-3's own propagation test per arch-review.r1.md's Specification Amendment #3. No separate task needed.
- FR-5 (logging preserved) → satisfied by construction: Steps 3/4 (`fix-process-daily-consumption-handler`) and Step 4 (`fix-get-daily-consumption-breakdown-handler`) keep every pre-existing `_logger.LogInformation` call and only remove the `_logger.LogError` calls that lived inside the deleted catch blocks, per spec FR-5's explicit instruction not to add new handler-level logging.
- NFR-1 (no behavior change on success/legitimate-false paths) → every pre-existing passing test for those paths is left untouched in both handler test files; only the two exception-path tests are rewritten.
- NFR-2 (Hangfire retry policy itself unchanged) → no task touches Hangfire configuration; `add-daily-consumption-job-failure-test` only proves the *path* to the retry policy is restored, per spec.
- Open Questions from spec.r1.md are resolved by arch-review.r1.md, not by new tasks: global exception handling coverage is confirmed to already exist (no task needed); `GetDailyConsumptionBreakdownResponse.Error` is kept as-is, unmodified (no task needed — the spec's Out of Scope already covers this); no broader sweep of other handlers is performed (out of scope, unchanged).

**Placeholder scan:** No "TBD"/"TODO"/"add appropriate error handling" phrases; every step shows complete, copy-pasteable code or an exact shell command with its expected output.

**Type consistency:** `ProcessDailyConsumptionRequest`, `ProcessDailyConsumptionResponse`, `GetDailyConsumptionBreakdownRequest`, `GetDailyConsumptionBreakdownResponse`, `ConsumptionGroupBy`, `IRecurringJobStatusChecker.IsJobEnabledAsync(string, CancellationToken, bool)`, and `MockPackingMaterialRepository`'s existing method/field naming conventions are used identically to how they appear in the current source and test files read during architecture review and planning — no invented signatures.
