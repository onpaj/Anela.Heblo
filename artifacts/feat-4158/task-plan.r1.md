# Bank Staleness Warning Deduplication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the duplicate watermark-staleness warning log from `ImportBankStatementHandler.Handle` so it is emitted exactly once per job-triggered import run (from `BankImportJobBase` only), instead of twice.

**Architecture:** `BankImportJobBase.ResolveDateFromAsync` already computes and logs the staleness warning for every scheduled run before calling the handler. `ImportBankStatementHandler` independently re-evaluates the identical condition against the same `BankImportState` and logs a second, differently-formatted warning. Per `arch-review.r1.md` (Decision 1 and Decision 2), the fix removes the handler's check entirely — no replacement check is added anywhere, including the manual-trigger (`BankStatementsController`) path, which accepts the loss of this specific log line as a deliberate, documented trade-off.

**Tech Stack:** .NET 8, MediatR, xUnit, Moq, FluentAssertions.

---

### task: remove-duplicate-staleness-check

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`

This task is a targeted removal with a supporting regression test change, not new-feature TDD — there is no new production behavior to drive with a new failing test. Instead: first adjust the existing test suite to assert the **post-fix** behavior (which will fail against today's code), confirm the failure, then make the production change, then confirm the suite passes.

- [ ] **Step 1: Update the existing tests to assert the post-fix behavior (this makes one test fail against current code)**

Open `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`.

Delete the `Handle_LogsStaleWarning_WhenWatermarkIsStale` test entirely (current lines 369–395):

```csharp
    [Fact]
    public async Task Handle_LogsStaleWarning_WhenWatermarkIsStale()
    {
        var from = new DateTime(2026, 6, 10);
        var to = new DateTime(2026, 6, 10);
        var request = new ImportBankStatementRequest("ComgateCZK", from, to);
        var existingState = new BankImportState("ComgateCZK");
        // 10 days ago; default StaleWarningDays = 3, so 10 > 3 triggers the warning.
        existingState.RecordSuccess(DateTime.UtcNow.AddDays(-10), DateTime.UtcNow, DateTime.UtcNow);

        _mockStateRepository
            .Setup(r => r.GetByAccountAsync("ComgateCZK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingState);
        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", from, to))
            .ReturnsAsync(new List<BankStatementHeader>());

        await _handler.Handle(request, CancellationToken.None);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("stale")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

Replace the immediately-following `Handle_DoesNotLogWarning_WhenWatermarkIsFresh` test (current lines 397–423) with a single renamed test that proves the specific regression from issue #4158 is fixed — the handler logs no staleness warning **even when the watermark is stale** (10 days ago), because that responsibility now belongs solely to `BankImportJobBase`:

```csharp
    [Fact]
    public async Task Handle_DoesNotLogStaleWarning_EvenWhenWatermarkIsStale()
    {
        var from = new DateTime(2026, 6, 10);
        var to = new DateTime(2026, 6, 10);
        var request = new ImportBankStatementRequest("ComgateCZK", from, to);
        var existingState = new BankImportState("ComgateCZK");
        // 10 days ago; default StaleWarningDays = 3, so this was previously stale-by-threshold.
        // The handler must NOT log about it - that is BankImportJobBase's responsibility now
        // (regression test for the duplicate-warning fix, issue #4158).
        existingState.RecordSuccess(DateTime.UtcNow.AddDays(-10), DateTime.UtcNow, DateTime.UtcNow);

        _mockStateRepository
            .Setup(r => r.GetByAccountAsync("ComgateCZK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingState);
        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", from, to))
            .ReturnsAsync(new List<BankStatementHeader>());

        await _handler.Handle(request, CancellationToken.None);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("stale")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
```

Then update the two `ImportBankStatementHandler` constructor call sites in the same file to drop the `Options.Create(new BankImportWatermarkOptions())` argument (its position, between `_mockStateRepository.Object` and `_mockMapper.Object`), matching the constructor signature change made in Step 3.

Constructor site 1 — in the test class constructor (current lines 65–73):

```csharp
        _handler = new ImportBankStatementHandler(
            _mockFactory.Object,
            _mockImportService.Object,
            _mockRepository.Object,
            Options.Create(_bankSettings),
            _mockStateRepository.Object,
            _mockMapper.Object,
            _mockLogger.Object);
```

Constructor site 2 — in `Constructor_WithNullFactory_ThrowsArgumentNullException` (current lines 79–87):

```csharp
    [Fact]
    public void Constructor_WithNullFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ImportBankStatementHandler(
            null!,
            _mockImportService.Object,
            _mockRepository.Object,
            Options.Create(_bankSettings),
            _mockStateRepository.Object,
            _mockMapper.Object,
            _mockLogger.Object));
    }
```

- [ ] **Step 2: Run the Bank tests to confirm the expected failures**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportBankStatementHandlerTests"`

Expected: **build failure** (not just a test failure) — the test file now calls `new ImportBankStatementHandler(...)` with 7 arguments while the production constructor still requires 8 (`IOptions<BankImportWatermarkOptions>` still present), so this will not compile. This confirms Step 1's test changes are wired to the still-unmodified production code. Do not proceed to Step 3 until you've seen this exact compile error (a `CS1501`/`CS7036`-style "no overload takes 7 arguments" error) — if it fails for a different reason, stop and investigate before continuing.

- [ ] **Step 3: Remove the duplicate staleness check and the now-unused dependency from the handler**

Open `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`.

Remove the now-unused using directive (line 4) — `BankImportWatermarkOptions` (the only type this handler used from that namespace) will no longer be referenced anywhere in the file after this task:

```csharp
using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;
```

Remove the `_watermarkOptions` field (current line 21):

```csharp
    private readonly BankImportWatermarkOptions _watermarkOptions;
```

Update the constructor signature and body (current lines 25–43) — remove the `IOptions<BankImportWatermarkOptions> watermarkOptions` parameter and its assignment:

```csharp
    public ImportBankStatementHandler(
        IBankClientFactory factory,
        IBankStatementImportService bankStatementImportService,
        IBankStatementImportRepository repository,
        IOptions<BankAccountSettings> bankSettings,
        IBankImportStateRepository stateRepository,
        IMapper mapper,
        ILogger<ImportBankStatementHandler> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _bankStatementImportService = bankStatementImportService ?? throw new ArgumentNullException(nameof(bankStatementImportService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _bankSettings = bankSettings.Value ?? throw new ArgumentNullException(nameof(bankSettings));
        _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
```

Remove the duplicate staleness-check block from `Handle` (current lines 70–77), while keeping the `state` load above it (current lines 67–68) unchanged — `state` is still needed later in the method:

```csharp
        var state = await _stateRepository.GetByAccountAsync(accountSetting.Name, cancellationToken)
                    ?? new BankImportState(accountSetting.Name);

        try
```

(i.e. the `if (state.LastValidImportDate.HasValue) { ... daysBehind ... LogWarning(...) }` block that previously sat between the `state = ...` line and the `try` block is deleted outright — nothing replaces it.)

- [ ] **Step 4: Run the Bank tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportBankStatementHandlerTests|FullyQualifiedName~BankImportJobBaseTests"`

Expected: **PASS**, all tests including `Handle_DoesNotLogStaleWarning_EvenWhenWatermarkIsStale` (new) and the full `BankImportJobBaseTests` suite (unchanged, still verifying the job-side warning fires as before).

- [ ] **Step 5: Full backend build and format check**

Run: `cd backend && dotnet build`
Expected: Build succeeds with no new warnings (specifically, no unused-field or unused-using warnings from the removed `_watermarkOptions`/using directive).

Run: `cd backend && dotnet format --verify-no-changes`
Expected: No formatting diffs. If it reports diffs, run `dotnet format` (without `--verify-no-changes`) to apply them, then re-stage the affected files before committing.

- [ ] **Step 6: Run the full Bank feature test suite (guard against unrelated breakage)**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Bank"`
Expected: PASS — this includes `ImportBankStatementHandlerTests`, `BankImportJobBaseTests`, and `BankStatementImportIntegrationTests` (which also construct `ImportBankStatementRequest`/exercise the handler indirectly through DI in some cases — confirm no other file constructs `ImportBankStatementHandler` directly with the old 8-argument signature; `Grep` in the architecting phase found only the two call sites already covered in Step 1).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs
git commit -m "fix(bank): remove duplicate staleness warning from ImportBankStatementHandler

The staleness check in ImportBankStatementHandler.Handle duplicated the
check BankImportJobBase already performs and logs before invoking the
handler, causing every scheduled job run with a stale watermark to log
the warning twice. BankImportJobBase remains the sole source of this
warning for job-triggered runs; the manual-API trigger path
(BankStatementsController) no longer logs a staleness warning, which is
an accepted, documented trade-off (see artifacts/feat-4158/arch-review.r1.md,
Decision 2) rather than a regression to fix here.

Fixes #4158"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (remove duplicate check from handler) → covered by Step 3.
- FR-2 (remove unused `_watermarkOptions` dependency) → covered by Step 3 (field, constructor param, using directive all removed; confirmed in the architecting phase that `_watermarkOptions` has no other use in the class).
- FR-3 / Open Question → resolved by `arch-review.r1.md` Decision 2 to option (b) (accept the gap on the manual-trigger path); no task adds a replacement check, and the commit message documents this explicitly so it is a visible, intentional trade-off.
- NFR-1 (no behavior change to import execution/watermark state/return values) → Step 3 preserves the `state` load and all downstream `state.RecordSuccess`/`RecordFailure`/`UpsertAsync` calls untouched; only the log-and-branch block is removed.
- NFR-2 (log message consistency) → no new message introduced; the surviving job-side message is untouched.

**Placeholder scan:** No "TBD"/"handle appropriately"/unshown code — every step shows exact before/after code, exact file paths and line ranges, and exact commands with expected output.

**Type consistency:** `ImportBankStatementHandler`'s constructor parameter list in Step 3 (7 params: `factory, bankStatementImportService, repository, bankSettings, stateRepository, mapper, logger`) matches exactly what both test call sites in Step 1 are updated to pass (same 7 arguments, same order, `Options.Create(new BankImportWatermarkOptions())` removed from both). No other file constructs `ImportBankStatementHandler` directly (confirmed via `arch-review.r1.md`'s grep of `new ImportBankStatementHandler(`).
