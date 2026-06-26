# Persist Scheduled DQT Run Records Before Execution — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `await _repository.SaveChangesAsync(cancellationToken)` between `AddAsync` and `_jobRunner.RunAsync` in all three scheduled DQT job classes (`InvoiceDqtJob`, `ProductPairingDqtJob`, `StockWriteBackDqtJob`) so the `Running` audit row is durably committed before the runner executes, achieving parity with the manual trigger path (`RunDqtHandler`).

**Architecture:** Strict additive change — one extra line per job file, no new types, no interface changes, no DB migration. Test-first: each job gets a new `<JobName>Tests.cs` file (none exist today) using xUnit + Moq, asserting (a) call order `AddAsync → SaveChangesAsync → RunAsync`, (b) early-return when the job is disabled — no persistence calls, (c) `cancellationToken` propagation. The runner contract is unchanged; pre-saving the row means the runner's `GetByIdAsync` returns the already-tracked entity and its terminal `SaveChangesAsync` issues an UPDATE instead of an INSERT — exactly the manual-path behaviour already in production.

**Tech Stack:** .NET 8 · xUnit · Moq · `Microsoft.Extensions.Logging.Abstractions.NullLogger<T>` · EF Core (mocked at the `IDqtRunRepository` boundary).

---

## File Structure

**Modify (one line per file, between existing `AddAsync` and `RunAsync` calls):**

- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/InvoiceDqtJob.cs` — insert after line 49
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs` — insert after line 49
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/StockWriteBackDqtJob.cs` — insert after line 49

**Create (new test files — none exist today for these job classes):**

- `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtJobTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtJobTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/StockWriteBackDqtJobTests.cs`

**Reference (do not modify — these are the conventions to match):**

- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs:40-41` — the manual-path sequence the scheduled jobs must mirror.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtJobRunnerTests.cs` — the closest-neighbour test file; matches its xUnit + Moq + `NullLogger` + plain `Assert` style (no FluentAssertions).
- `backend/src/Anela.Heblo.Domain/Features/DataQuality/IDqtRunRepository.cs` — repository interface; `AddAsync` and `SaveChangesAsync` are inherited from `IRepository<DqtRun, Guid>` and need no extension.
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobStatusChecker.cs` — the gate that must short-circuit before any persistence occurs.

**No changes to:** `src/` aside from the three job files, DI registrations, EF migrations, configuration, frontend, OpenAPI client.

---

## Conventions to Follow

- **Test style:** match `InvoiceDqtJobRunnerTests.cs` — xUnit `[Fact]`, Moq, plain `Assert.*`, `NullLogger<T>.Instance`. Do **not** introduce FluentAssertions here.
- **Mock setup:** `_repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>())).ReturnsAsync((DqtRun run, CancellationToken _) => run);` — same pattern as `RunDqtHandlerTests.cs:41-42`.
- **Call-order verification:** use a `List<string> calls` plus `.Callback(() => calls.Add("..."))` on each mock setup, then `Assert.Equal(new[] { "AddAsync", "SaveChangesAsync", "RunAsync" }, calls)`. This is the cleanest readable expression of a sequencing requirement and avoids Moq's stricter `MockSequence` machinery.
- **Cancellation token in production code:** the inserted `SaveChangesAsync` uses the same `cancellationToken` parameter already in scope (FR-5) — never `CancellationToken.None`.
- **Placement of the new line:** strictly between `await _repository.AddAsync(...)` and `await _jobRunner.RunAsync(...)`. Never above the `IsJobEnabledAsync` check (would persist a `Running` row for a disabled job — see arch-review risks table).
- **Commit messages:** conventional commits style, type `fix:` (this is a reliability bug fix, not a new feature).

---

## Task 1: `InvoiceDqtJob` — TDD cycle

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtJobTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/InvoiceDqtJob.cs` (insert after line 49)

### Step 1.1 — Write the failing tests

- [ ] **Step 1.1: Create `InvoiceDqtJobTests.cs` with the three tests**

Create `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtJobTests.cs` with the following content:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class InvoiceDqtJobTests
{
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<IInvoiceDqtJobRunner> _jobRunnerMock = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusCheckerMock = new();
    private readonly InvoiceDqtJob _sut;

    public InvoiceDqtJobTests()
    {
        _sut = new InvoiceDqtJob(
            _repositoryMock.Object,
            _jobRunnerMock.Object,
            _statusCheckerMock.Object,
            NullLogger<InvoiceDqtJob>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_JobEnabled_PersistsRunBeforeInvokingRunner()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var calls = new List<string>();

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("AddAsync"))
            .ReturnsAsync((DqtRun run, CancellationToken _) => run);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("SaveChangesAsync"))
            .ReturnsAsync(1);

        _jobRunnerMock
            .Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("RunAsync"))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ExecuteAsync(CancellationToken.None);

        // Assert — sequence is exactly AddAsync, then SaveChangesAsync, then RunAsync
        Assert.Equal(new[] { "AddAsync", "SaveChangesAsync", "RunAsync" }, calls);

        // And the persisted run has the expected scheduled-trigger metadata
        _repositoryMock.Verify(
            r => r.AddAsync(
                It.Is<DqtRun>(run =>
                    run.TestType == DqtTestType.IssuedInvoiceComparison &&
                    run.TriggerType == DqtTriggerType.Scheduled &&
                    run.Status == DqtRunStatus.Running),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_JobDisabled_DoesNotPersistOrInvokeRunner()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _sut.ExecuteAsync(CancellationToken.None);

        // Assert — short-circuit: no repository writes, no runner invocation
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _jobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationTokenToSaveChanges()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun run, CancellationToken _) => run);
        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _jobRunnerMock
            .Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        await _sut.ExecuteAsync(token);

        // Assert — the same token threads through SaveChangesAsync (FR-5)
        _repositoryMock.Verify(r => r.SaveChangesAsync(token), Times.Once);
    }
}
```

### Step 1.2 — Run the failing test

- [ ] **Step 1.2: Run new tests; verify they fail**

Run from repository root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~InvoiceDqtJobTests" \
  --logger "console;verbosity=normal"
```

Expected: `ExecuteAsync_JobEnabled_PersistsRunBeforeInvokingRunner` and `ExecuteAsync_PropagatesCancellationTokenToSaveChanges` both **FAIL**. The call-order assertion will fail with a sequence that contains only `AddAsync` and `RunAsync` (no `SaveChangesAsync`). The cancellation-token verification will fail with "Expected invocation on the mock once, but was 0 times."

`ExecuteAsync_JobDisabled_DoesNotPersistOrInvokeRunner` should **PASS** even before the fix (the short-circuit already exists).

### Step 1.3 — Apply the one-line fix

- [ ] **Step 1.3: Insert `SaveChangesAsync` call into `InvoiceDqtJob`**

In `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/InvoiceDqtJob.cs`, change lines 48-51 from:

```csharp
        var run = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, yesterday, yesterday, DqtTriggerType.Scheduled);
        await _repository.AddAsync(run, cancellationToken);

        await _jobRunner.RunAsync(run.Id, cancellationToken);
```

to:

```csharp
        var run = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, yesterday, yesterday, DqtTriggerType.Scheduled);
        await _repository.AddAsync(run, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _jobRunner.RunAsync(run.Id, cancellationToken);
```

### Step 1.4 — Run the tests; verify they pass

- [ ] **Step 1.4: Re-run tests; verify all pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~InvoiceDqtJobTests" \
  --logger "console;verbosity=normal"
```

Expected: all three tests **PASS**.

### Step 1.5 — Commit

- [ ] **Step 1.5: Commit task 1**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/InvoiceDqtJob.cs \
        backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtJobTests.cs

git commit -m "fix(dqt): persist InvoiceDqtJob run row before runner executes

The scheduled InvoiceDqtJob omitted SaveChangesAsync between AddAsync and
RunAsync, so a process crash mid-run left no audit row. Bring the scheduled
path into parity with RunDqtHandler (manual trigger) by committing the
Running row before handing off to the runner. Adds the first unit-test file
for this job class, asserting the AddAsync -> SaveChangesAsync -> RunAsync
call order, the disabled-job short-circuit, and cancellation-token
propagation."
```

---

## Task 2: `ProductPairingDqtJob` — TDD cycle

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtJobTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs` (insert after line 49)

### Step 2.1 — Write the failing tests

- [ ] **Step 2.1: Create `ProductPairingDqtJobTests.cs`**

Create `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtJobTests.cs` with the following content:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class ProductPairingDqtJobTests
{
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<IDriftDqtJobRunner> _jobRunnerMock = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusCheckerMock = new();
    private readonly ProductPairingDqtJob _sut;

    public ProductPairingDqtJobTests()
    {
        _sut = new ProductPairingDqtJob(
            _repositoryMock.Object,
            _jobRunnerMock.Object,
            _statusCheckerMock.Object,
            NullLogger<ProductPairingDqtJob>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_JobEnabled_PersistsRunBeforeInvokingRunner()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var calls = new List<string>();

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("AddAsync"))
            .ReturnsAsync((DqtRun run, CancellationToken _) => run);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("SaveChangesAsync"))
            .ReturnsAsync(1);

        _jobRunnerMock
            .Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("RunAsync"))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ExecuteAsync(CancellationToken.None);

        // Assert
        Assert.Equal(new[] { "AddAsync", "SaveChangesAsync", "RunAsync" }, calls);

        _repositoryMock.Verify(
            r => r.AddAsync(
                It.Is<DqtRun>(run =>
                    run.TestType == DqtTestType.ProductPairing &&
                    run.TriggerType == DqtTriggerType.Scheduled &&
                    run.Status == DqtRunStatus.Running),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_JobDisabled_DoesNotPersistOrInvokeRunner()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _sut.ExecuteAsync(CancellationToken.None);

        // Assert
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _jobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationTokenToSaveChanges()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun run, CancellationToken _) => run);
        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _jobRunnerMock
            .Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        await _sut.ExecuteAsync(token);

        // Assert
        _repositoryMock.Verify(r => r.SaveChangesAsync(token), Times.Once);
    }
}
```

### Step 2.2 — Run the failing tests

- [ ] **Step 2.2: Run new tests; verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProductPairingDqtJobTests" \
  --logger "console;verbosity=normal"
```

Expected: `ExecuteAsync_JobEnabled_PersistsRunBeforeInvokingRunner` and `ExecuteAsync_PropagatesCancellationTokenToSaveChanges` **FAIL** with the same kind of mismatch as Task 1. The disabled-job test **PASSES**.

### Step 2.3 — Apply the one-line fix

- [ ] **Step 2.3: Insert `SaveChangesAsync` call into `ProductPairingDqtJob`**

In `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs`, change lines 48-51 from:

```csharp
        var run = DqtRun.Start(DqtTestType.ProductPairing, today, today, DqtTriggerType.Scheduled);
        await _repository.AddAsync(run, cancellationToken);

        await _jobRunner.RunAsync(run.Id, cancellationToken);
```

to:

```csharp
        var run = DqtRun.Start(DqtTestType.ProductPairing, today, today, DqtTriggerType.Scheduled);
        await _repository.AddAsync(run, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _jobRunner.RunAsync(run.Id, cancellationToken);
```

### Step 2.4 — Run the tests; verify they pass

- [ ] **Step 2.4: Re-run tests; verify all pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProductPairingDqtJobTests" \
  --logger "console;verbosity=normal"
```

Expected: all three tests **PASS**.

### Step 2.5 — Commit

- [ ] **Step 2.5: Commit task 2**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs \
        backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtJobTests.cs

git commit -m "fix(dqt): persist ProductPairingDqtJob run row before runner executes

Mirror the InvoiceDqtJob fix: commit the Running DqtRun row before invoking
the drift runner so a mid-run crash still leaves an audit trail. Adds the
first unit-test file for ProductPairingDqtJob covering call order,
disabled-job short-circuit, and cancellation-token propagation."
```

---

## Task 3: `StockWriteBackDqtJob` — TDD cycle

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/DataQuality/StockWriteBackDqtJobTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/StockWriteBackDqtJob.cs` (insert after line 49)

### Step 3.1 — Write the failing tests

- [ ] **Step 3.1: Create `StockWriteBackDqtJobTests.cs`**

Create `backend/test/Anela.Heblo.Tests/Features/DataQuality/StockWriteBackDqtJobTests.cs` with the following content:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class StockWriteBackDqtJobTests
{
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<IDriftDqtJobRunner> _jobRunnerMock = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusCheckerMock = new();
    private readonly StockWriteBackDqtJob _sut;

    public StockWriteBackDqtJobTests()
    {
        _sut = new StockWriteBackDqtJob(
            _repositoryMock.Object,
            _jobRunnerMock.Object,
            _statusCheckerMock.Object,
            NullLogger<StockWriteBackDqtJob>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_JobEnabled_PersistsRunBeforeInvokingRunner()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var calls = new List<string>();

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("AddAsync"))
            .ReturnsAsync((DqtRun run, CancellationToken _) => run);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("SaveChangesAsync"))
            .ReturnsAsync(1);

        _jobRunnerMock
            .Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("RunAsync"))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ExecuteAsync(CancellationToken.None);

        // Assert
        Assert.Equal(new[] { "AddAsync", "SaveChangesAsync", "RunAsync" }, calls);

        _repositoryMock.Verify(
            r => r.AddAsync(
                It.Is<DqtRun>(run =>
                    run.TestType == DqtTestType.StockWriteBackReconciliation &&
                    run.TriggerType == DqtTriggerType.Scheduled &&
                    run.Status == DqtRunStatus.Running),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_JobDisabled_DoesNotPersistOrInvokeRunner()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _sut.ExecuteAsync(CancellationToken.None);

        // Assert
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _jobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationTokenToSaveChanges()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun run, CancellationToken _) => run);
        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _jobRunnerMock
            .Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        await _sut.ExecuteAsync(token);

        // Assert
        _repositoryMock.Verify(r => r.SaveChangesAsync(token), Times.Once);
    }
}
```

### Step 3.2 — Run the failing tests

- [ ] **Step 3.2: Run new tests; verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~StockWriteBackDqtJobTests" \
  --logger "console;verbosity=normal"
```

Expected: `ExecuteAsync_JobEnabled_PersistsRunBeforeInvokingRunner` and `ExecuteAsync_PropagatesCancellationTokenToSaveChanges` **FAIL**; disabled-job test **PASSES**.

### Step 3.3 — Apply the one-line fix

- [ ] **Step 3.3: Insert `SaveChangesAsync` call into `StockWriteBackDqtJob`**

In `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/StockWriteBackDqtJob.cs`, change lines 48-51 from:

```csharp
        var run = DqtRun.Start(DqtTestType.StockWriteBackReconciliation, yesterday, yesterday, DqtTriggerType.Scheduled);
        await _repository.AddAsync(run, cancellationToken);

        await _jobRunner.RunAsync(run.Id, cancellationToken);
```

to:

```csharp
        var run = DqtRun.Start(DqtTestType.StockWriteBackReconciliation, yesterday, yesterday, DqtTriggerType.Scheduled);
        await _repository.AddAsync(run, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _jobRunner.RunAsync(run.Id, cancellationToken);
```

### Step 3.4 — Run the tests; verify they pass

- [ ] **Step 3.4: Re-run tests; verify all pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~StockWriteBackDqtJobTests" \
  --logger "console;verbosity=normal"
```

Expected: all three tests **PASS**.

### Step 3.5 — Commit

- [ ] **Step 3.5: Commit task 3**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/StockWriteBackDqtJob.cs \
        backend/test/Anela.Heblo.Tests/Features/DataQuality/StockWriteBackDqtJobTests.cs

git commit -m "fix(dqt): persist StockWriteBackDqtJob run row before runner executes

Final scheduled-DQT-job parity fix. All three scheduled jobs now match
RunDqtHandler: AddAsync -> SaveChangesAsync -> RunAsync. Adds the first
unit-test file for StockWriteBackDqtJob covering call order, disabled-job
short-circuit, and cancellation-token propagation."
```

---

## Task 4: Final validation

**Files:** none (verification only).

### Step 4.1 — Confirm regression-suite green and code formatted

- [ ] **Step 4.1: Build, format-check, and run the full DataQuality test slice**

Run each command sequentially. None must fail.

```bash
# 1. Solution builds clean
dotnet build backend/Anela.Heblo.sln

# 2. Code style/format check (CLAUDE.md validation gate)
dotnet format backend/Anela.Heblo.sln --verify-no-changes

# 3. All DataQuality tests pass — old runner tests AND the three new job tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DataQuality" \
  --logger "console;verbosity=normal"
```

Expected:
- `dotnet build` finishes with `Build succeeded.` and zero errors.
- `dotnet format --verify-no-changes` exits 0 (no formatting drift introduced).
- Test run reports zero failures; the three new test files contribute 9 tests total (3 per job).

If `dotnet format --verify-no-changes` reports drift, run `dotnet format backend/Anela.Heblo.sln`, inspect the diff, and amend the most recent task commit (`git commit --amend --no-edit`) only if the drift is in files this PR touched. Do not blanket-format unrelated files.

### Step 4.2 — Run the full backend test suite as a final guard

- [ ] **Step 4.2: Run the entire `Anela.Heblo.Tests` project to catch any unforeseen ripple**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --logger "console;verbosity=normal"
```

Expected: zero failures. (The existing `InvoiceDqtJobRunnerTests`, `DriftDqtJobRunnerTests`, `RunDqtHandlerTests`, etc., must remain green — they exercise the runner and manual-trigger paths which were not touched.)

### Step 4.3 — Final commit (only if `dotnet format` made changes)

- [ ] **Step 4.3: Commit any formatter-only changes (skip if none)**

If `dotnet format` produced any changes in step 4.1, stage and commit them:

```bash
git status
git add -p          # review hunks before staging
git commit -m "chore: dotnet format"
```

If `git status` is clean after step 4.1, skip this step entirely — no empty commit.

---

## Self-Review

**1. Spec coverage**

| Spec item | Plan task |
|---|---|
| FR-1: persist `InvoiceDqtJob` run before execution | Task 1, Step 1.3 |
| FR-2: persist `ProductPairingDqtJob` run before execution | Task 2, Step 2.3 |
| FR-3: persist `StockWriteBackDqtJob` run before execution | Task 3, Step 3.3 |
| FR-4: parity with manual `RunDqtHandler` path | Sequence `AddAsync → SaveChangesAsync → RunAsync` verified by call-order assertion in Steps 1.1 / 2.1 / 3.1; matches `RunDqtHandler.cs:40-41` exactly |
| FR-5: cancellation token propagation | `ExecuteAsync_PropagatesCancellationTokenToSaveChanges` test in each of Steps 1.1 / 2.1 / 3.1 |
| NFR-1 performance | No new round trips beyond the single `SaveChangesAsync`; spec already accepts this |
| NFR-2 reliability | Achieved by FR-1/2/3 — `Running` row durable before runner runs |
| NFR-3 backwards compatibility | No schema, API, or DI changes — verified by Step 4.1 (`dotnet build`) and Step 4.2 (full test suite) |
| NFR-4 test coverage | Three new test files in Tasks 1-3; each contains call-order, disabled-job, and cancellation tests — exceeds "retain current coverage" (which was zero for these classes) |
| Arch-review amendment 1: introduce missing test files | Tasks 1-3 each create the first test file for the respective job |
| Arch-review amendment 2: precise `DqtTestType` per job | `IssuedInvoiceComparison`, `ProductPairing`, `StockWriteBackReconciliation` — all asserted in the call-order tests via `It.Is<DqtRun>(...)` |

No gaps.

**2. Placeholder scan**

Scanned for: TBD, TODO, "fill in", "appropriate error handling", "similar to task N", "implement later", "write tests for the above". None present. Every code block is complete and ready to paste.

**3. Type consistency**

- `IDqtRunRepository.AddAsync(DqtRun, CancellationToken)` and `.SaveChangesAsync(CancellationToken)` — verified against `IRepository<DqtRun, Guid>` inherited from `BaseRepository.cs:57,97`.
- `IInvoiceDqtJobRunner.RunAsync(Guid, CancellationToken)` — verified against `IInvoiceDqtJobRunner.cs:8`.
- `IDriftDqtJobRunner.RunAsync(Guid, CancellationToken)` — verified against `IDriftDqtJobRunner.cs:5`.
- `IRecurringJobStatusChecker.IsJobEnabledAsync(string, CancellationToken)` — verified against `IRecurringJobStatusChecker.cs:5`.
- `DqtTestType` enum values used in tests (`IssuedInvoiceComparison`, `ProductPairing`, `StockWriteBackReconciliation`) — verified against `InvoiceDqtJob.cs:48`, `ProductPairingDqtJob.cs:48`, `StockWriteBackDqtJob.cs:48` respectively.
- `DqtTriggerType.Scheduled` and `DqtRunStatus.Running` — used identically in the production code and in the test assertions.
- All three job constructors take `(IDqtRunRepository, I<runner>, IRecurringJobStatusChecker, ILogger<TJob>)` — confirmed from each job file's lines 24-34. Test files instantiate the SUT with the same shape.

Consistent throughout.
