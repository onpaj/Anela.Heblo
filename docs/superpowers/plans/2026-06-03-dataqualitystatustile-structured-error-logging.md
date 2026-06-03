# DataQualityStatusTile Structured Error Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the silent bare `catch` in `DataQualityStatusTile.LoadDataAsync` with structured `ILogger.LogError`, matching the sibling `DqtYesterdayStatusTile` pattern so failures are observable.

**Architecture:** Inject `ILogger<DataQualityStatusTile>` via constructor (appended last per sibling convention), capture `Exception ex` in the catch block, and log with the structured `{TestType}` property before returning the existing degraded payload. Public surface (return shape, status semantics) is unchanged. DI works automatically — `RegisterTile<T>()` already resolves all constructor params through the container, and `ILogger<T>` is provided by the default host. The only caller is the test class, which is updated to pass `NullLogger<T>.Instance` for the three existing tests; one new test verifies `LogError` is invoked using a Moq-mocked logger.

**Tech Stack:** .NET 8, C#, xUnit, FluentAssertions, Moq, `Microsoft.Extensions.Logging.Abstractions` (already a transitive dependency).

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs` | Modify | Add `_logger` field, accept `ILogger<DataQualityStatusTile>` in constructor, replace bare `catch` with `catch (Exception ex)` + `LogError`. |
| `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs` | Modify | Pass `NullLogger<DataQualityStatusTile>.Instance` to the constructor in the existing fixture; add one new `[Fact]` that verifies `LogError` is invoked exactly once with the thrown exception. |

No other files change. `DataQualityModule.cs` is not edited — `RegisterTile<DataQualityStatusTile>()` already wires the new constructor through the container.

---

### Task 1: Add Logger Verification Test (RED)

**Goal:** Write a failing test asserting that `LoadDataAsync` calls `ILogger.LogError` exactly once with the original exception when the repository throws. Test fails because the production class has neither a logger field nor an invocation.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs`

- [ ] **Step 1: Add the using directives for the logger types**

Open `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs`. After the existing `using Anela.Heblo.Domain.Features.DataQuality;` line, ensure these usings are present:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
```

Place them in alphabetical order with the existing usings so the final block reads:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
```

- [ ] **Step 2: Add a `Mock<ILogger<DataQualityStatusTile>>` field and update the constructor wiring**

Replace the existing field declarations and constructor block:

```csharp
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly DataQualityStatusTile _tile;

    public DataQualityStatusTileTests()
    {
        _tile = new DataQualityStatusTile(_repositoryMock.Object);
    }
```

With:

```csharp
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<ILogger<DataQualityStatusTile>> _loggerMock = new();
    private readonly DataQualityStatusTile _tile;

    public DataQualityStatusTileTests()
    {
        _tile = new DataQualityStatusTile(_repositoryMock.Object, _loggerMock.Object);
    }
```

Note: this temporarily breaks compilation because `DataQualityStatusTile`'s constructor still takes only one parameter. Task 2 fixes that. The whole-test-project compile success comes in Step 5 of Task 2.

- [ ] **Step 3: Add the new logger-verification test**

Append this `[Fact]` just below the existing `LoadDataAsync_RepositoryThrows_ReturnsErrorWithRouteKey` method and above the `private static DqtRun CreateCompletedRun(...)` helper:

```csharp
    [Fact]
    public async Task LoadDataAsync_RepositoryThrows_LogsErrorOnce()
    {
        var thrown = new InvalidOperationException("db down");

        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAsync(
                DqtTestType.IssuedInvoiceComparison, It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);

        await _tile.LoadDataAsync();

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                thrown,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

Why this shape: `ILogger.LogError(ex, message, args)` is an extension method that forwards to `ILogger.Log(LogLevel.Error, ...)`. Moq cannot verify extension methods directly, so the verification targets the underlying `Log` call. Matching on `thrown` ensures the exception we threw is the one logged.

- [ ] **Step 4: Run the new test to verify it does not yet compile or fails as expected**

Run from the repo root:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build **fails** in `Anela.Heblo.Tests` with an error like:

```
error CS1729: 'DataQualityStatusTile' does not contain a constructor that takes 2 arguments
```

This is the "RED" signal. Do **not** commit yet — Task 2 makes the test compile and pass.

---

### Task 2: Inject Logger and Replace Bare Catch (GREEN)

**Goal:** Update `DataQualityStatusTile` to accept `ILogger<DataQualityStatusTile>`, capture `Exception ex` in the catch block, and call `_logger.LogError(...)` with the structured `{TestType}` property. After this task, the new test from Task 1 passes and the three pre-existing tests continue to pass.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs`

- [ ] **Step 1: Add the `Microsoft.Extensions.Logging` using directive**

Open `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs`. The current usings are:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Xcc.Services.Dashboard;
```

Change them to (alphabetical with the new entry):

```csharp
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;
```

- [ ] **Step 2: Add the `_logger` field next to `_repository`**

Replace:

```csharp
    private readonly IDqtRunRepository _repository;
```

With:

```csharp
    private readonly IDqtRunRepository _repository;
    private readonly ILogger<DataQualityStatusTile> _logger;
```

- [ ] **Step 3: Update the constructor to accept and store the logger**

Replace the existing constructor:

```csharp
    public DataQualityStatusTile(IDqtRunRepository repository)
    {
        _repository = repository;
    }
```

With (logger appended last, matching sibling `DqtYesterdayStatusTile`):

```csharp
    public DataQualityStatusTile(
        IDqtRunRepository repository,
        ILogger<DataQualityStatusTile> logger)
    {
        _repository = repository;
        _logger = logger;
    }
```

- [ ] **Step 4: Replace the bare catch with `catch (Exception ex)` + structured `LogError`**

Replace the existing catch block (currently lines ~65–73):

```csharp
        catch
        {
            return new
            {
                status = "error",
                data = (object?)null,
                drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
            };
        }
```

With:

```csharp
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load DataQuality status tile for {TestType}",
                DqtTestType.IssuedInvoiceComparison);

            return new
            {
                status = "error",
                data = (object?)null,
                drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
            };
        }
```

The returned anonymous object's shape, field order, and values are byte-for-byte identical to the previous block. Only the catch signature and the logging call are new.

- [ ] **Step 5: Build the solution**

Run from the repo root:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: **build succeeds** with no errors. Warnings unrelated to this change are acceptable.

- [ ] **Step 6: Run the DataQualityStatusTile test class**

Run from the repo root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DataQualityStatusTileTests" \
  --no-build
```

Expected: **all 4 tests pass** — the three pre-existing tests (`LoadDataAsync_NoRun_ReturnsNoDataWithRouteKey`, `LoadDataAsync_RunWithoutMismatches_ReturnsSuccessWithRouteKey`, `LoadDataAsync_RepositoryThrows_ReturnsErrorWithRouteKey`) plus the new `LoadDataAsync_RepositoryThrows_LogsErrorOnce`.

- [ ] **Step 7: Run `dotnet format` to apply analyzer/style fixes**

Run from the repo root:

```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs
```

Expected: command completes with exit code 0. Re-run `dotnet build` if formatter changed anything to confirm it still builds.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs \
        backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs
git commit -m "feat: log exceptions in DataQualityStatusTile.LoadDataAsync

Inject ILogger<DataQualityStatusTile> via constructor and replace the bare
catch in LoadDataAsync with catch (Exception ex) + structured LogError,
matching the DqtYesterdayStatusTile pattern. Adds a unit test verifying
LogError is invoked once on the exception path."
```

---

### Task 3: Final Validation

**Goal:** Confirm the full test project still passes (no collateral breakage outside the touched tile) and the solution builds clean. No code changes in this task.

**Files:** none.

- [ ] **Step 1: Run the full DataQuality test slice**

Run from the repo root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.DataQuality" \
  --no-build
```

Expected: **all tests pass** (DataQuality tile tests + any other DataQuality unit tests in the project). No new failures.

- [ ] **Step 2: Build the solution one more time**

Run from the repo root:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds with no new errors.

- [ ] **Step 3: Confirm working tree is clean**

```bash
git status
```

Expected: `nothing to commit, working tree clean`. If anything is unexpectedly modified, investigate before continuing.

---

## Self-Review

**Spec coverage:**
- FR-1 (inject `ILogger<DataQualityStatusTile>` into the constructor, store in `_logger`) → Task 2 Steps 2–3.
- FR-2 (replace bare catch with `catch (Exception ex)` and log at Error level before returning the degraded payload) → Task 2 Step 4.
- FR-3 (purely additive from caller perspective, return shape unchanged) → Task 2 Step 4 preserves the anonymous-object literal byte-for-byte; Task 2 Step 6 reruns the existing tests that assert the shape.
- NFR-1 (no perf impact) → covered by leaving the happy path untouched (verified by existing `NoRun` and `RunWithoutMismatches` tests in Task 2 Step 6).
- NFR-2 (no PII / secret leakage) → only `DqtTestType` enum value is logged as a structured property, plus the exception itself. No user identifiers, no connection strings.
- NFR-3 (consistency with sibling) → Task 2 mirrors `DqtYesterdayStatusTile` for field name (`_logger`), constructor ordering (logger last), log level (`Error`), and structured-property style (`{TestType}`).
- NFR-4 (testability) → Task 1 Step 3 adds the explicit Moq-based `LogError` verification.
- Arch-review Amendment 1 (use structured `{TestType}` property instead of fixed string) → Task 2 Step 4 uses `"Failed to load DataQuality status tile for {TestType}"`.
- Arch-review Amendment 2 (keep existing tests on NullLogger / Moq mirror, add a dedicated `LogError` verification test) → Task 1 Step 2 swaps to `Mock<ILogger<T>>` (acceptable because Moq's mock acts as a no-op for the three existing return-shape tests, equivalent to NullLogger behavior for them) and Task 1 Step 3 adds the dedicated verification test. Note: arch-review preferred `NullLogger` for the three pre-existing tests; using a Moq mock that is silently called by them is functionally equivalent and avoids duplicate logger fields. The verification test (`Times.Once`) still passes because the three other tests do not trigger the catch path.
- Arch-review Amendment 3 (no `DataQualityModule.cs` edit required) → reflected in File Structure (`DataQualityModule.cs` intentionally not modified).

**Placeholder scan:** No "TBD", "TODO", "implement later", "add appropriate error handling", "similar to Task N", or empty test stubs. Every code step has full source. Every command has expected output described.

**Type consistency:**
- `ILogger<DataQualityStatusTile>` is used identically in production (Task 2 Step 2 field, Step 3 constructor) and tests (Task 1 Step 2 field).
- `_logger` field name matches sibling and is used consistently.
- `DqtTestType.IssuedInvoiceComparison` matches the enum value already used by the repository call on line 32 of the production file and by every existing test fixture.
- Constructor signature `(IDqtRunRepository repository, ILogger<DataQualityStatusTile> logger)` is the same in production declaration (Task 2 Step 3) and test instantiation (Task 1 Step 2).
- Test method name `LoadDataAsync_RepositoryThrows_LogsErrorOnce` follows the existing `LoadDataAsync_*` xUnit naming convention in the same file.
