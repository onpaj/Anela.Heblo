# Remove HasDayAlreadyBeenProcessedAsync from IConsumptionCalculationService Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove `HasDayAlreadyBeenProcessedAsync` from `IConsumptionCalculationService` (ISP cleanup), make it a private implementation detail of `ConsumptionCalculationService`, and replace the one test that calls it directly with a test that verifies the same idempotency guarantee through the public `ProcessDailyConsumptionAsync` entry point.

**Architecture:** Two existing production files are edited in place (interface narrowed by one member, method access modifier changed from implicit-public to `private`); one existing test file is edited in place (one test replaced by an equivalent, stronger test). No new files, no DI changes, no behavior change. See `arch-review.r1.md` Decision 1 and Decision 2 for the rationale behind not extracting a separate abstraction and for the two-call test shape.

**Tech Stack:** C# / .NET 8, xUnit, the existing `MockPackingMaterialRepository` and `MockInvoiceConsumptionSource` test doubles already used by `ConsumptionCalculationServiceTests`. No new packages.

---

## Context

This is a pure refactor of already-shipped, working code — not new behavior. The correct rhythm per task is:

1. Add the replacement test first, while the old method is still public — it must PASS immediately (it characterizes existing idempotent behavior, it does not change it).
2. Delete the old test that calls the method directly (it would stop compiling once the method goes private).
3. Run the full `ConsumptionCalculationServiceTests` suite — confirm green with the old test gone and the new one in place.
4. Only then narrow the interface and privatize the method (Task 2) — by that point nothing outside the class still needs public access, so this step is a same-commit, no-surprises change.
5. Rebuild the whole solution and rerun the full test suite (including `ProcessDailyConsumptionHandlerTests`, which mocks the interface but never touches this member) to prove nothing else broke.

### Authoritative source-of-truth references (read before starting)

- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/IConsumptionCalculationService.cs` — interface to narrow.
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs` — method to privatize; note the call site at the top of `ProcessDailyConsumptionAsync` (`if (await HasDayAlreadyBeenProcessedAsync(processingDate, cancellationToken))`) needs **no edit** — it is already an unqualified same-class call.
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs` — contains the test to replace, `HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue` (around line 234–249), plus the `BuildService` helper and existing `MakeHeader` helper to reuse.
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs` — confirmed: `AddDailyRunAsync` records into `AddedDailyRuns` but does **not** update the internal `_dailyProcessingStatus` dictionary read by `HasDailyProcessingBeenRunAsync`. The replacement test must call `SetHasDailyProcessingBeenRun(date, true)` explicitly between its two `ProcessDailyConsumptionAsync` calls — do not assume the mock tracks this automatically.
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs` — confirmed unaffected (mocks `IConsumptionCalculationService`, never references `HasDayAlreadyBeenProcessedAsync`); no edit needed, but rerun it in Task 2's verification step as a regression check.

### Exact current code being touched (copy from source for the diff steps below)

`IConsumptionCalculationService.cs` (current, full file):
```csharp
namespace Anela.Heblo.Application.Features.PackingMaterials.Services;

public interface IConsumptionCalculationService
{
    Task<ProcessDailyConsumptionResult> ProcessDailyConsumptionAsync(
        DateOnly processingDate,
        CancellationToken cancellationToken = default);

    Task<bool> HasDayAlreadyBeenProcessedAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
}
```

`ConsumptionCalculationService.cs`, current method to privatize (lines 95–100):
```csharp
    public async Task<bool> HasDayAlreadyBeenProcessedAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await _repository.HasDailyProcessingBeenRunAsync(date, cancellationToken);
    }
```

`ConsumptionCalculationServiceTests.cs`, current test to delete (lines 234–249):
```csharp
    [Fact]
    public async Task HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue()
    {
        // Arrange
        var materialRepo = new MockPackingMaterialRepository();
        var invoiceSource = new MockInvoiceConsumptionSource();
        var service = BuildService(materialRepo, invoiceSource, _mockLogger);
        var date = DateOnly.FromDateTime(DateTime.Today);
        materialRepo.SetHasDailyProcessingBeenRun(date, true);

        // Act
        var result = await service.HasDayAlreadyBeenProcessedAsync(date);

        // Assert
        Assert.True(result);
    }
```

---

## File Structure

No new files. Three existing files are edited in place:

```
backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/IConsumptionCalculationService.cs   (narrow interface)
backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs    (privatize method)
backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs             (replace one test)
```

---

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
