# ProcessDailyConsumptionHandler Unit Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add four xUnit characterization tests that lock in the current behavior of `ProcessDailyConsumptionHandler` so its idempotency guard, success/no-invoice messaging, and exception-suppression cannot regress silently.

**Architecture:** A single new test file under `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/` mocks `IConsumptionCalculationService` and `ILogger<ProcessDailyConsumptionHandler>` with Moq, instantiates the handler directly, and asserts response shape + a `LogLevel.Error` log entry via FluentAssertions and `Mock.Verify`. No new packages, no nested folders, no production code touched.

**Tech Stack:** xUnit 2.9.2, FluentAssertions 6.12.0, Moq 4.20.72, `Microsoft.Extensions.Logging.Abstractions` (all already referenced by `Anela.Heblo.Tests.csproj`).

---

## Context

The production code (`backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/`) is **already complete and shipped**. This is **characterization testing**, not classic TDD: each test is written to lock in the handler's *current observable behavior*. Therefore the rhythm is:

1. Add the test.
2. Run it — it should PASS the first time (you are characterizing existing behavior).
3. If it fails, **read the failure carefully** — either the test assertion is wrong about what the handler does, or there is a real handler bug. Fix the test only after confirming what the source actually emits. Never edit the handler in this task.
4. Commit.

### Authoritative source-of-truth references (read before writing tests)

- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs` — handler with the four branches under test.
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionRequest.cs` — `class`, single property `DateOnly ProcessingDate`.
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionResponse.cs` — `class : BaseResponse` with `DateOnly ProcessedDate`, `int MaterialsProcessed`, `string Message`.
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/IConsumptionCalculationService.cs` — the only collaborator we mock.
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ProcessDailyConsumptionResult.cs` — `sealed record (bool WasRun, int MaterialsProcessed)` with **positional constructor**; construct via `new ProcessDailyConsumptionResult(true, 5)`, NOT via object-initializer syntax.
- `backend/test/Anela.Heblo.Tests/Features/Packaging/FillTrackingNumbersJobTests.cs` — reference style for Moq-based handler tests in this repo. Read it before starting Task 1.

### Exact message strings the handler emits (copy from source for assertions)

| Branch | Exact message format |
|---|---|
| `!result.WasRun` | `$"Daily consumption for {request.ProcessingDate} was already processed"` |
| `WasRun=true, MaterialsProcessed > 0` | `$"Daily consumption successfully processed for {request.ProcessingDate}. {result.MaterialsProcessed} materials updated."` |
| `WasRun=true, MaterialsProcessed == 0` | `$"No invoices found for {request.ProcessingDate} — no materials were updated."` (note: that is an em-dash `—`, not a hyphen) |
| Exception caught | `"An unexpected error occurred while processing daily consumption."` (no exception detail leaked) |

Assertions must use **substring `Contain`**, not full-string equality, except for the exception-path message where the literal must be matched and the leaked exception text must be affirmatively absent.

### Exact logger error call to verify (FR-4)

```csharp
_logger.LogError(ex, "Error processing daily consumption for {Date}", request.ProcessingDate);
```

Moq verification target: `LogLevel.Error` was emitted exactly once with the thrown exception instance attached.

---

## File Structure

Create exactly one new file:

```
backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs
```

No new folders, no project-file edits, no production-source edits.

---

## Task 1: Scaffold the test class and add the idempotency test (FR-1)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs`

**FR mapped:** FR-1 (idempotency gate), FR-5 (file location + naming), FR-6 (test-only).

- [ ] **Step 1: Create the file with the test class skeleton, shared SUT builder, and the FR-1 test**

Path: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs`

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Services;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.ProcessDailyConsumption;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class ProcessDailyConsumptionHandlerTests
{
    private static readonly DateOnly TestDate = new(2026, 1, 15);

    private static (
        ProcessDailyConsumptionHandler Sut,
        Mock<IConsumptionCalculationService> Service,
        Mock<ILogger<ProcessDailyConsumptionHandler>> Logger)
        MakeSut()
    {
        var service = new Mock<IConsumptionCalculationService>();
        var logger = new Mock<ILogger<ProcessDailyConsumptionHandler>>();
        var sut = new ProcessDailyConsumptionHandler(service.Object, logger.Object);
        return (sut, service, logger);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenAlreadyProcessed()
    {
        // Arrange
        var (sut, service, _) = MakeSut();
        // MaterialsProcessed=42 on the service result proves the handler ignores it
        // when WasRun=false (it must force MaterialsProcessed to 0 in the response).
        service
            .Setup(s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessDailyConsumptionResult(WasRun: false, MaterialsProcessed: 42));

        var request = new ProcessDailyConsumptionRequest { ProcessingDate = TestDate };

        // Act
        var response = await sut.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.MaterialsProcessed.Should().Be(0);
        response.ProcessedDate.Should().Be(TestDate);
        response.Message.Should().Contain("already processed");
        response.Message.Should().Contain(TestDate.ToString());

        service.Verify(
            s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

- [ ] **Step 2: Build the test project to catch type errors early**

Run from the repo root:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build succeeds. If it fails on `ProcessDailyConsumptionResult`, double-check it is constructed via the positional `record` constructor (`new ProcessDailyConsumptionResult(WasRun: false, MaterialsProcessed: 42)`), not an object-initializer.

- [ ] **Step 3: Run the test and confirm it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProcessDailyConsumptionHandlerTests.Handle_ReturnsFailure_WhenAlreadyProcessed" \
  --no-build
```

Expected: `Passed: 1`. If it fails, re-read the handler's `!result.WasRun` branch and adjust the assertion to match the actual emitted text — do **not** change the handler.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs
git commit -m "test(packingmaterials): cover ProcessDailyConsumption idempotency gate"
```

---

## Task 2: Add success-with-materials-updated test (FR-2)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs`

**FR mapped:** FR-2.

- [ ] **Step 1: Append the FR-2 test method to the existing `ProcessDailyConsumptionHandlerTests` class**

Insert this method directly below `Handle_ReturnsFailure_WhenAlreadyProcessed`, inside the same class:

```csharp
    [Fact]
    public async Task Handle_ReturnsSuccess_WhenMaterialsUpdated()
    {
        // Arrange
        const int materialsUpdated = 5;
        var (sut, service, _) = MakeSut();
        service
            .Setup(s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessDailyConsumptionResult(WasRun: true, MaterialsProcessed: materialsUpdated));

        var request = new ProcessDailyConsumptionRequest { ProcessingDate = TestDate };

        // Act
        var response = await sut.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.MaterialsProcessed.Should().Be(materialsUpdated);
        response.ProcessedDate.Should().Be(TestDate);
        response.Message.Should().Contain(TestDate.ToString());
        response.Message.Should().Contain(materialsUpdated.ToString());
        response.Message.Should().Contain("materials updated");

        service.Verify(
            s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run the new test and confirm it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProcessDailyConsumptionHandlerTests.Handle_ReturnsSuccess_WhenMaterialsUpdated"
```

Expected: `Passed: 1`. If `Contain("materials updated")` fails, inspect the exact handler message (`$"Daily consumption successfully processed for {request.ProcessingDate}. {result.MaterialsProcessed} materials updated."`) — the substring is lower-case and includes the trailing word "updated".

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs
git commit -m "test(packingmaterials): cover ProcessDailyConsumption success with N materials"
```

---

## Task 3: Add success-with-no-invoices test (FR-3)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs`

**FR mapped:** FR-3.

- [ ] **Step 1: Append the FR-3 test method to the existing class**

Insert directly below the FR-2 test:

```csharp
    [Fact]
    public async Task Handle_ReturnsSuccessWithZeroCount_WhenNoInvoicesFound()
    {
        // Arrange
        var (sut, service, _) = MakeSut();
        service
            .Setup(s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessDailyConsumptionResult(WasRun: true, MaterialsProcessed: 0));

        var request = new ProcessDailyConsumptionRequest { ProcessingDate = TestDate };

        // Act
        var response = await sut.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.MaterialsProcessed.Should().Be(0);
        response.ProcessedDate.Should().Be(TestDate);
        response.Message.Should().Contain("No invoices");
        response.Message.Should().Contain(TestDate.ToString());

        service.Verify(
            s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run the new test and confirm it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProcessDailyConsumptionHandlerTests.Handle_ReturnsSuccessWithZeroCount_WhenNoInvoicesFound"
```

Expected: `Passed: 1`. The handler emits `$"No invoices found for {date} — no materials were updated."`, so `Contain("No invoices")` is a stable, casing-correct substring.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs
git commit -m "test(packingmaterials): cover ProcessDailyConsumption no-invoices success path"
```

---

## Task 4: Add exception-handling test with non-leaking message + log verification (FR-4)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs`

**FR mapped:** FR-4, NFR-2 (no exception detail leaks).

- [ ] **Step 1: Append the FR-4 test method AND a small Moq logger-verification helper**

Insert below the FR-3 test, still inside the same class:

```csharp
    [Fact]
    public async Task Handle_ReturnsGenericError_WhenServiceThrows()
    {
        // Arrange
        const string secretDetail = "secret database connection string";
        var thrown = new InvalidOperationException(secretDetail);

        var (sut, service, logger) = MakeSut();
        service
            .Setup(s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);

        var request = new ProcessDailyConsumptionRequest { ProcessingDate = TestDate };

        // Act
        var response = await sut.Handle(request, CancellationToken.None);

        // Assert — response shape
        response.Success.Should().BeFalse();
        response.MaterialsProcessed.Should().Be(0);
        response.ProcessedDate.Should().Be(TestDate);
        response.Message.Should().Be("An unexpected error occurred while processing daily consumption.");

        // Defense-in-depth: the secret must not leak into the message
        response.Message.Should().NotContain(secretDetail);
        response.Message.Should().NotContain(nameof(InvalidOperationException));

        // Logger contract: an Error-level entry was emitted with the same exception instance
        VerifyErrorLogged(logger, thrown);
    }

    private static void VerifyErrorLogged(
        Mock<ILogger<ProcessDailyConsumptionHandler>> logger,
        Exception expected)
    {
        logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                expected,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

- [ ] **Step 2: Build to catch any Moq generic-argument typos in the verify expression**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build succeeds. If you see `CS0411` ("type arguments cannot be inferred") on the `logger.Verify(...)` call, the `It.IsAnyType` lines are wrong — copy them verbatim from the snippet above.

- [ ] **Step 3: Run the new test and confirm it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProcessDailyConsumptionHandlerTests.Handle_ReturnsGenericError_WhenServiceThrows"
```

Expected: `Passed: 1`. If `VerifyErrorLogged` fails with "expected invocation on the mock once, but was 0 times", confirm the handler's `catch` block still calls `_logger.LogError(ex, ...)`.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs
git commit -m "test(packingmaterials): cover ProcessDailyConsumption exception path (no leak, error log)"
```

---

## Task 5: Final validation — format, full-class run, sanity-check the production source is untouched

**Files:**
- Modify (formatting only): `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs` (if `dotnet format` rewrites whitespace).

**FR mapped:** FR-5 (naming + AAA), FR-6 (no prod changes), NFR-3 (deterministic), NFR-5 (full 4-branch coverage).

- [ ] **Step 1: Run `dotnet format` against the test project to apply analyzer fixes**

```bash
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: exit 0; either no changes or whitespace-only changes in the new test file.

- [ ] **Step 2: Run the full `ProcessDailyConsumptionHandlerTests` class**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProcessDailyConsumptionHandlerTests"
```

Expected: `Passed: 4, Failed: 0, Skipped: 0`. Total wall-clock under 1 second per NFR-1.

- [ ] **Step 3: Sanity-check that no production file was modified**

```bash
git diff --name-only main...HEAD -- 'backend/src/**'
```

Expected: empty output. If anything in `backend/src/` shows up, FR-6 has been violated — revert those changes before continuing.

- [ ] **Step 4: Run the full PackingMaterials test folder to confirm no regressions**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.PackingMaterials"
```

Expected: all tests pass (the new four plus every pre-existing PackingMaterials test).

- [ ] **Step 5: Commit any formatter changes (only if `git status` is non-empty)**

```bash
git status --short
```

If `git status` shows changes:

```bash
git add backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs
git commit -m "chore: apply dotnet format to ProcessDailyConsumptionHandlerTests"
```

If `git status` is clean, skip this step.

---

## Self-Review Notes (from the plan author)

- **Spec FR-1 coverage** — Task 1 asserts `Success=false`, `MaterialsProcessed=0`, `ProcessedDate==input`, `Message` contains "already processed", and verifies the mock was called exactly once with the exact `DateOnly` and any `CancellationToken`. ✅
- **Spec FR-2 coverage** — Task 2 uses `materialsUpdated=5`, asserts the count is propagated and the message contains the date, the number, and "materials updated". ✅
- **Spec FR-3 coverage** — Task 3 asserts the success-with-zero-count semantics and contains "No invoices" (matches the handler's exact casing). ✅
- **Spec FR-4 coverage** — Task 4 asserts the exact generic message string, affirmatively asserts the secret does **not** leak (NFR-2), and verifies a single `LogLevel.Error` call carrying the same exception instance. ✅
- **Spec FR-5 coverage** — File at flat `Features/PackingMaterials/` path matching every other handler test in the folder; class `ProcessDailyConsumptionHandlerTests`; methods named per the spec; AAA layout. The architecture review's Decision 2 overrides the spec's nested `UseCases/ProcessDailyConsumption/` suggestion. ✅
- **Spec FR-6 coverage** — Task 5 Step 3 explicitly diffs `backend/src/` against `main` and fails the task if anything appears. ✅
- **NFR-1 (perf)** — Pure in-memory mocks; the full filter run should comfortably stay under 1 second. ✅
- **NFR-2 (security)** — `secretDetail` and `nameof(InvalidOperationException)` are both asserted absent from the response message. ✅
- **NFR-3 (isolation)** — `MakeSut()` constructs a fresh handler + fresh mocks per test; no shared mutable state; `TestDate` is a `const`-equivalent `static readonly`. ✅
- **NFR-4 (maintainability)** — Single mocking library (Moq), already in `Anela.Heblo.Tests.csproj`. No NuGet additions. ✅
- **NFR-5 (coverage)** — Four tests = four handler branches: `!WasRun` early return, `MaterialsProcessed > 0` arm, `MaterialsProcessed == 0` arm, `catch` block. 100% branch coverage of `ProcessDailyConsumptionHandler.Handle`. ✅
- **Placeholder scan** — Every step shows the exact code / command / expected output. No "TBD", no "add appropriate handling", no "similar to". ✅
- **Type consistency** — Throughout the plan: `DateOnly` (not `DateTime`), `ProcessDailyConsumptionResult` constructed via positional `record` syntax (not object initializer), `Mock<ILogger<ProcessDailyConsumptionHandler>>` (not `MockLogger<T>` no-op), `It.IsAnyType` in the verify expression. All consistent across Tasks 1–5. ✅
