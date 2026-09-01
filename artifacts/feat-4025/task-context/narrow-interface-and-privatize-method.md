### task: narrow-interface-and-privatize-method

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/IConsumptionCalculationService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs:95-100`

**FR mapped:** FR-1 (interface narrowing + privatization). Depends on `replace-direct-hasday-test-with-two-call-test` having already removed the only external caller of the method outside `ConsumptionCalculationService`.

- [ ] **Step 1: Confirm no remaining external reference before touching source**

Run: `grep -rn "HasDayAlreadyBeenProcessedAsync" backend/ --include="*.cs"`
Expected: exactly two matches — the interface declaration in `IConsumptionCalculationService.cs` and the method declaration + its same-class call site in `ConsumptionCalculationService.cs`. No matches in `backend/test/`.

- [ ] **Step 2: Remove the method from the interface**

Edit `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/IConsumptionCalculationService.cs` to:

```csharp
namespace Anela.Heblo.Application.Features.PackingMaterials.Services;

public interface IConsumptionCalculationService
{
    Task<ProcessDailyConsumptionResult> ProcessDailyConsumptionAsync(
        DateOnly processingDate,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Make the method private on the concrete class**

In `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs`, change lines 95–100 from:

```csharp
    public async Task<bool> HasDayAlreadyBeenProcessedAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await _repository.HasDailyProcessingBeenRunAsync(date, cancellationToken);
    }
```

to:

```csharp
    private async Task<bool> HasDayAlreadyBeenProcessedAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await _repository.HasDailyProcessingBeenRunAsync(date, cancellationToken);
    }
```

No other line in this file changes — the call site at line 28 (`if (await HasDayAlreadyBeenProcessedAsync(processingDate, cancellationToken))`) already compiles unchanged against a private same-class member.

- [ ] **Step 4: Build the solution to confirm no compile-time break**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: `Build succeeded.` with 0 errors. (If any file outside this slice fails to compile referencing `HasDayAlreadyBeenProcessedAsync` through the interface, Step 1's grep should already have caught it — this build is the final safety net.)

- [ ] **Step 5: Run the full PackingMaterials test suite to confirm no regression**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterials"`
Expected: all tests in `ConsumptionCalculationServiceTests` and `ProcessDailyConsumptionHandlerTests` PASS (and any other `PackingMaterials`-namespaced tests present in the suite).

- [ ] **Step 6: Run `dotnet format` on the touched files**

Run: `dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/IConsumptionCalculationService.cs backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs --verify-no-changes`
Expected: no formatting violations reported (exit code 0). If it reports violations, run without `--verify-no-changes` to apply them, then re-run Step 5.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/IConsumptionCalculationService.cs backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs
git commit -m "refactor(packing-materials): remove HasDayAlreadyBeenProcessedAsync from IConsumptionCalculationService (ISP)"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (remove from interface, make private) → covered by `narrow-interface-and-privatize-method`, Steps 2–3.
- FR-2 (refactor the direct test to verify indirectly, two-call sequence, `WasRun: false` on second call) → covered by `replace-direct-hasday-test-with-two-call-test`, Step 1, matching the issue's suggested fix verbatim.
- NFR-1 (no behavior change) → enforced by ordering (test-first, no production logic edits beyond the access modifier) and Step 4/5 build+test verification.
- NFR-2 (build/format/test integrity) → covered by `narrow-interface-and-privatize-method` Steps 4, 5, 6.

**2. Placeholder scan:** No "TBD"/"TODO"/"handle appropriately" language present; every step shows exact code, exact file paths and line ranges, and exact commands with expected output.

**3. Type consistency:** `ProcessDailyConsumptionResult(WasRun, MaterialsProcessed)` positional-record usage in the new test matches its existing usage throughout `ConsumptionCalculationServiceTests.cs` (e.g. `Assert.True(result.WasRun)` pattern used by six other tests in the same file). `PackingMaterial`, `ConsumptionType.PerDay`, `MockPackingMaterialRepository.SetMaterials`/`SetHasDailyProcessingBeenRun` are all used with the exact same signatures as the surrounding pre-existing tests in the file — no new helper or type introduced.
