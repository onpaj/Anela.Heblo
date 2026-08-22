# Inject TimeProvider into TransportBoxCompletionService Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the three direct `DateTime.UtcNow` reads in `TransportBoxCompletionService` with an injected `TimeProvider`, and prove it with a frozen `FakeTimeProvider` in the existing unit test suite.

**Architecture:** `TransportBoxCompletionService` gains a fourth constructor parameter, `TimeProvider timeProvider`, stored in `private readonly TimeProvider _timeProvider`, matching every sibling handler in the Transport Boxes part. The three mutually-exclusive state-transition branches in `ProcessBoxAsync` each read the clock inline as `_timeProvider.GetUtcNow().UtcDateTime` (never `.DateTime`, never wrapped in `DateTime.SpecifyKind`). No DI registration changes — `TimeProvider.System` is already a singleton (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130`) and `LogisticsModule.cs:28` uses constructor activation. The unit test class holds a `FakeTimeProvider` field frozen at a fixed instant so the persisted `LastStateChanged` and `TransportBoxStateLog.StateDate` become assertable.

**Tech Stack:** .NET 8, C#, xUnit, Moq, FluentAssertions, `Microsoft.Extensions.Time.Testing.FakeTimeProvider` (package `Microsoft.Extensions.TimeProvider.Testing` 8.1.0, already referenced at `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj:26` — no package change).

**Working directory for every command in this plan:** `/home/user/worktrees/feature-3888-Arch-Review-Transportboxes-Transportboxcompletions` (git worktree, branch `feature/3888-Arch-Review-Transportboxes-Transportboxcompletions`).

---

## File Structure

Exactly two files change. A third file in `git diff --name-only` is a defect.

| File | Role after the change |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs` | MODIFY — one new field, one new constructor parameter, three call sites switched from `DateTime.UtcNow` to `_timeProvider.GetUtcNow().UtcDateTime`. Everything else byte-identical. |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` | MODIFY — one new `using`, one frozen-instant constant, one `FakeTimeProvider` field, the fourth constructor argument, timestamp assertions on three existing tests, one new clock-advance test. |

**Explicitly NOT touched** (verified to need no change): `LogisticsModule.cs`, `ITransportBoxCompletionService.cs`, `TransportBox.cs`, `TransportBoxStateLog.cs`, any `Anela.Heblo.Persistence` configuration or migration, `Anela.Heblo.Tests.csproj`, `TransportBoxBaseTile.cs`, `ChangeTransportBoxStateHandler.cs` (its redundant `DateTime.SpecifyKind` at `:118` stays), `docs/architecture/DateTime_StandardizationGuide.md`, `docs/architecture/Dev_Guidelines_time.md`, `docs/features/complete-received-boxes-job.md`, and anything under `frontend/`.

## Reference facts the implementer needs (all verified against source)

- `TransportBox.LastStateChanged` is `public DateTime? { get; set; }` (`TransportBox.cs:21`).
- `TransportBox.StateLog` is `public IReadOnlyList<TransportBoxStateLog> => _stateLog` (`TransportBox.cs:37`), append-only.
- `TransportBoxStateLog` exposes `State`, **`StateDate`** (not `Date`), `User`, `Description`. Its constructor is `internal`, so tests never build one — they read the entry the transition appended.
- `TransportBox.ChangeState(newState, now, userName, description, allowedStates)` (`TransportBox.cs:241-248`) sets `LastStateChanged = now` and appends `new TransportBoxStateLog(newState, now, userName, description)`. The injected instant is persisted, not merely logged.
- `TransportBox.ToPick(DateTime date, string userName)` transitions to `Stocked`; `TransportBox.Error(DateTime date, string userName, string exMessage)` transitions to `Error` with the message as the log `Description`.
- The test helper `CreateBox` builds a box with an **empty** state log, so after exactly one transition `box.StateLog` contains exactly one entry — assert `ContainSingle()` and read `Single()` rather than relying on ordering.
- Use `.UtcDateTime`, **never** `.DateTime`. `DateTimeOffset.UtcDateTime` yields `Kind = Utc` (the exact in-memory equivalent of `DateTime.UtcNow`); `DateTimeOffset.DateTime` yields `Kind = Unspecified` and would change in-memory behaviour. FluentAssertions compares `DateTime` by ticks, not `Kind`, so the tests will **not** catch this mistake — it is a review-gate check. Note `docs/architecture/Dev_Guidelines_time.md:14` recommends `.DateTime`; the local Transport Boxes convention overrides it here and that doc is deliberately left unedited.

---

### task: inject-timeprovider

Add the `TimeProvider` dependency. TDD in C# means the "failing test" is a compile failure: the test class asks for a four-argument constructor that does not exist yet.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs:1-28`
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs:10-22`

- [ ] **Step 1: Write the failing test — add the frozen clock to the test class**

In `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs`, add the `using` (keep the existing alphabetical-ish grouping — insert after the `Microsoft.Extensions.Logging` line) and replace the field block plus constructor.

Add this `using` line after `using Microsoft.Extensions.Logging;`:

```csharp
using Microsoft.Extensions.Time.Testing;
```

Replace lines 14-28 (the field declarations and the constructor) with:

```csharp
    private static readonly DateTimeOffset FrozenNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ILogger<TransportBoxCompletionService>> _loggerMock;
    private readonly Mock<ITransportBoxRepository> _transportBoxRepositoryMock;
    private readonly Mock<ILogisticsStockOperationQueryService> _stockOperationQueryServiceMock;
    private readonly FakeTimeProvider _timeProvider;
    private readonly TransportBoxCompletionService _service;

    public TransportBoxCompletionServiceTests()
    {
        _loggerMock = new Mock<ILogger<TransportBoxCompletionService>>();
        _transportBoxRepositoryMock = new Mock<ITransportBoxRepository>();
        _stockOperationQueryServiceMock = new Mock<ILogisticsStockOperationQueryService>();
        _timeProvider = new FakeTimeProvider(FrozenNow);
        _service = new TransportBoxCompletionService(
            _loggerMock.Object,
            _transportBoxRepositoryMock.Object,
            _stockOperationQueryServiceMock.Object,
            _timeProvider);
    }
```

The provider is held in a **field**, not passed inline, because the clock-advance test later in this plan needs `Advance(...)` on the same instance the service holds.

- [ ] **Step 2: Run the build to verify it fails**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`

Expected: FAIL with `error CS1729: 'TransportBoxCompletionService' does not contain a constructor that takes 4 arguments` pointing at `TransportBoxCompletionServiceTests.cs`.

- [ ] **Step 3: Write the minimal implementation — add the field and constructor parameter**

In `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`, replace lines 10-22 with:

```csharp
    private readonly ILogger<TransportBoxCompletionService> _logger;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly ILogisticsStockOperationQueryService _stockOperationQueryService;
    private readonly TimeProvider _timeProvider;

    public TransportBoxCompletionService(
        ILogger<TransportBoxCompletionService> logger,
        ITransportBoxRepository transportBoxRepository,
        ILogisticsStockOperationQueryService stockOperationQueryService,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _transportBoxRepository = transportBoxRepository;
        _stockOperationQueryService = stockOperationQueryService;
        _timeProvider = timeProvider;
    }
```

`timeProvider` is the **last** parameter, matching all five sibling handlers in the part. Plain assignment — do **not** add `ArgumentNullException.ThrowIfNull`; the existing constructor and the siblings use plain assignments. Do not add a `using System;` — `TimeProvider` resolves through the project's implicit usings, exactly as `DateTime` already does in this file.

- [ ] **Step 4: Run the build and the test suite to verify they pass**

Run: `dotnet build Anela.Heblo.sln`

Expected: `Build succeeded.` with 0 errors.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"`

Expected: PASS — `Passed! - Failed: 0, Passed: 7`. All seven pre-existing tests still pass; the service still reads the wall clock, which nothing asserts yet.

- [ ] **Step 5: Verify DI still resolves the service graph**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ApplicationStartupTests"`

Expected: PASS, `Failed: 0`. This is the guard that `TimeProvider` is resolvable in the real host (registered as a singleton at `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130`). A failure here would surface as `InvalidOperationException: Unable to resolve service for type 'System.TimeProvider'`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs
git commit -m "refactor(logistics): inject TimeProvider into TransportBoxCompletionService"
```

---

### task: use-injected-clock-for-transitions

Assert the frozen instant on all three transition kinds first (they fail while the service still reads the wall clock), then switch the three call sites.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` (assertion blocks of `AllOperationsCompleted_TransitionsBoxToStocked`, `AnyOperationFailed_TransitionsBoxToError`, `NoOperationsForBox_TransitionsToError`)
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs:91,111,131`

- [ ] **Step 1: Write the failing assertions — Stocked transition**

In `CompleteReceivedBoxesAsync_AllOperationsCompleted_TransitionsBoxToStocked`, replace the assertion block that currently starts with `box.State.Should().Be(TransportBoxState.Stocked);` and ends with the `SaveChangesAsync` verification, with:

```csharp
        box.State.Should().Be(TransportBoxState.Stocked);
        box.LastStateChanged.Should().Be(FrozenNow.UtcDateTime);

        box.StateLog.Should().ContainSingle();
        var stateLogEntry = box.StateLog.Single();
        stateLogEntry.State.Should().Be(TransportBoxState.Stocked);
        stateLogEntry.StateDate.Should().Be(FrozenNow.UtcDateTime);
        stateLogEntry.User.Should().Be("System");

        _transportBoxRepositoryMock.Verify(
            x => x.UpdateAsync(box, It.IsAny<CancellationToken>()),
            Times.Once);
        _transportBoxRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
```

- [ ] **Step 2: Write the failing assertions — Error from failed operations**

In `CompleteReceivedBoxesAsync_AnyOperationFailed_TransitionsBoxToError`, replace the single assertion line `box.State.Should().Be(TransportBoxState.Error);` with:

```csharp
        box.State.Should().Be(TransportBoxState.Error);
        box.LastStateChanged.Should().Be(FrozenNow.UtcDateTime);

        box.StateLog.Should().ContainSingle();
        var stateLogEntry = box.StateLog.Single();
        stateLogEntry.State.Should().Be(TransportBoxState.Error);
        stateLogEntry.StateDate.Should().Be(FrozenNow.UtcDateTime);
        stateLogEntry.User.Should().Be("System");
```

- [ ] **Step 3: Write the failing assertions — Error from no operations**

In `CompleteReceivedBoxesAsync_NoOperationsForBox_TransitionsToError`, replace the single assertion line `box.State.Should().Be(TransportBoxState.Error);` with:

```csharp
        box.State.Should().Be(TransportBoxState.Error);
        box.LastStateChanged.Should().Be(FrozenNow.UtcDateTime);

        box.StateLog.Should().ContainSingle();
        var stateLogEntry = box.StateLog.Single();
        stateLogEntry.State.Should().Be(TransportBoxState.Error);
        stateLogEntry.StateDate.Should().Be(FrozenNow.UtcDateTime);
        stateLogEntry.User.Should().Be("System");
        stateLogEntry.Description.Should().Be("No stock-up operations found for this box");
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"`

Expected: FAIL — `Failed: 3, Passed: 4`. Each of the three failures reads roughly `Expected box.LastStateChanged to be <2026-01-15 12:00:00>, but found <…current wall-clock instant…>`, because the service still calls `DateTime.UtcNow`.

- [ ] **Step 5: Replace the three `DateTime.UtcNow` call sites**

In `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`, make exactly these three edits inside `ProcessBoxAsync` and nothing else.

Line 91 — replace:

```csharp
            box.Error(DateTime.UtcNow, "System",
                "No stock-up operations found for this box");
```

with:

```csharp
            box.Error(_timeProvider.GetUtcNow().UtcDateTime, "System",
                "No stock-up operations found for this box");
```

Line 111 — replace:

```csharp
            box.ToPick(DateTime.UtcNow, "System");
```

with:

```csharp
            box.ToPick(_timeProvider.GetUtcNow().UtcDateTime, "System");
```

Line 131 — replace:

```csharp
            box.Error(DateTime.UtcNow, "System", errorMessage);
```

with:

```csharp
            box.Error(_timeProvider.GetUtcNow().UtcDateTime, "System", errorMessage);
```

Constraints on these three lines: use `.UtcDateTime`, never `.DateTime`; do not wrap in `DateTime.SpecifyKind(...)`; do not hoist the read into a local at the top of `ProcessBoxAsync` (the branches are mutually exclusive, and the two skip paths at `:138` and `:150` must keep reading the clock zero times). Leave the `"System"` string, both message strings, branch conditions, `UpdateAsync`/`SaveChangesAsync` ordering, returned `BoxProcessingResult` values, and every log statement byte-identical.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"`

Expected: PASS — `Passed! - Failed: 0, Passed: 7`.

- [ ] **Step 7: Verify no wall-clock read remains in the service**

Run: `grep -n "DateTime\.UtcNow\|DateTime\.Now" backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`

Expected: no output, exit status 1.

Run: `grep -n "GetUtcNow()\.DateTime\|SpecifyKind" backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`

Expected: no output, exit status 1. (Any hit here is the `Kind = Unspecified` trap described in the Reference facts — fix it before continuing.)

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs
git commit -m "refactor(logistics): read transition timestamps from injected TimeProvider"
```

---

### task: clock-advance-regression-test

Prove the service reads the injected clock on every call rather than capturing an instant once — the assertion that would still hold if someone reintroduced a static clock read is not enough on its own.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` (new test, inserted after `CompleteReceivedBoxesAsync_OperationsSubmitted_LeavesBoxInReceived` and before the `SetupQueryReturns` helper)

- [ ] **Step 1: Write the failing test**

Insert this test method after `CompleteReceivedBoxesAsync_OperationsSubmitted_LeavesBoxInReceived` and before the `private void SetupQueryReturns(...)` helper:

```csharp
    [Fact]
    public async Task CompleteReceivedBoxesAsync_ClockAdvanced_WritesAdvancedTimestamp()
    {
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepositoryMock
            .Setup(x => x.GetReceivedBoxesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box });

        SetupQueryReturns(box.Id, new List<LogisticsStockOperationStatus>
        {
            CreateStatus("BOX-000001-PROD1", LogisticsStockOperationState.Completed),
        });

        _transportBoxRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _transportBoxRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _timeProvider.Advance(TimeSpan.FromHours(1));

        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        var expectedTimestamp = FrozenNow.UtcDateTime.AddHours(1);

        box.State.Should().Be(TransportBoxState.Stocked);
        box.LastStateChanged.Should().Be(expectedTimestamp);

        box.StateLog.Should().ContainSingle();
        var stateLogEntry = box.StateLog.Single();
        stateLogEntry.State.Should().Be(TransportBoxState.Stocked);
        stateLogEntry.StateDate.Should().Be(expectedTimestamp);
        stateLogEntry.User.Should().Be("System");
    }
```

- [ ] **Step 2: Run the new test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"`

Expected: PASS — `Passed! - Failed: 0, Passed: 8`.

Note: this test passes immediately, because the previous task already made the service read the injected clock. That is expected — its purpose is to lock the behaviour in. Step 3 is what proves it is a real guard.

- [ ] **Step 3: Prove the guard bites — temporary sabotage check**

Temporarily change line 111 of `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs` back to:

```csharp
            box.ToPick(DateTime.UtcNow, "System");
```

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"`

Expected: FAIL — `Failed: 2` (`AllOperationsCompleted_TransitionsBoxToStocked` and `ClockAdvanced_WritesAdvancedTimestamp`), confirming the regression guard the whole change exists for.

Now revert the sabotage — line 111 must read exactly:

```csharp
            box.ToPick(_timeProvider.GetUtcNow().UtcDateTime, "System");
```

Run: `git diff --stat backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`

Expected: no output (the file is identical to the last commit, so the sabotage is fully reverted).

- [ ] **Step 4: Confirm no test touches the real clock**

Run: `grep -n "DateTime\.UtcNow\|DateTime\.Now" backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs`

Expected: no output, exit status 1.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs
git commit -m "test(logistics): assert TransportBoxCompletionService honours an advanced fake clock"
```

---

### task: validate-and-verify-scope

Run the full validation gate from `CLAUDE.md` plus the two-file diff rule from the architecture review.

**Files:** none modified except by `dotnet format`, which should produce no diff.

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build Anela.Heblo.sln`

Expected: `Build succeeded.` — 0 errors, and no new warnings attributable to the two changed files.

- [ ] **Step 2: Check formatting**

Run: `dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --include backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs --verify-no-changes`

Expected: exit status 0, no `error WHITESPACE`/`error IDE…` output.

Run: `dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --include backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs --verify-no-changes`

Expected: exit status 0, no output. If either command reports changes, run it again without `--verify-no-changes`, re-run the tests, and amend the last commit.

- [ ] **Step 3: Run the full backend test project**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`

Expected: `Passed!` with `Failed: 0`. This covers `TransportBoxCompletionServiceTests` (8 tests), `ApplicationStartupTests`, and `Architecture/ModuleBoundariesTests`.

- [ ] **Step 4: Verify the diff is exactly two files**

Run: `git diff --name-only origin/main...HEAD`

Expected: exactly these two lines and nothing else:

```
backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs
backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs
```

(`artifacts/feat-3888/*` files may appear if the pipeline committed them; any other **source** or **docs** path is a defect — revert it.)

- [ ] **Step 5: Confirm no unintended behavioural change**

Run: `git diff origin/main...HEAD -- backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`

Expected: exactly four hunks — the field + constructor block, and the three call sites. Read the diff and confirm no log template, log level, error message, branch condition, `UpdateAsync`/`SaveChangesAsync` call, counter, or `BoxProcessingResult` value moved.

- [ ] **Step 6: Note the follow-up for the conflicting time-guidance docs**

No file change. When the PR description is written, record that `docs/architecture/DateTime_StandardizationGuide.md` §3 ("ALWAYS use `DateTime.UtcNow`") and `docs/architecture/Dev_Guidelines_time.md:14` (recommends `GetUtcNow().DateTime`) both contradict the convention this change follows, that both are repo-wide guidance deliberately left untouched here (Amendment #4), and that reconciling them belongs in a separate follow-up issue.

- [ ] **Step 7: Final commit if anything moved**

Only if Steps 2 or 5 required a fix:

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs
git commit -m "style(logistics): apply dotnet format to TransportBoxCompletion changes"
```

Otherwise no commit is needed — the working tree is already clean.

---

## Self-Review

**1. Spec coverage** (spec.r1.md FR-1 … FR-6, NFR-1 … NFR-4, as amended by arch-review.r1.md):

| Requirement | Covered by |
|---|---|
| FR-1 inject `TimeProvider`, last ctor param, `_timeProvider` field, no null guard, interface unchanged | `inject-timeprovider` Step 3 |
| FR-2 replace all three `DateTime.UtcNow` with `_timeProvider.GetUtcNow().UtcDateTime`, inline, no `SpecifyKind` | `use-injected-clock-for-transitions` Steps 5, 7 |
| FR-3 DI resolves with no registration change | `inject-timeprovider` Step 5 (`ApplicationStartupTests`); `validate-and-verify-scope` Step 4 (no `LogisticsModule.cs` in the diff) |
| FR-4 test class uses `FakeTimeProvider` frozen at `FrozenNow`, existing arrangement extended not restructured, all seven tests keep passing | `inject-timeprovider` Steps 1, 4; `clock-advance-regression-test` Step 4 (no real-clock reference) |
| FR-5 timestamp assertions on all three transition kinds + a clock-advance test + reintroduction fails a test | `use-injected-clock-for-transitions` Steps 1-3; `clock-advance-regression-test` Steps 1, 3 |
| FR-6 no behavioural change | `validate-and-verify-scope` Steps 4, 5 |
| NFR-1 performance / NFR-2 security | No action required; nothing in the plan alters call frequency, the `"System"` actor, or any secret surface |
| NFR-3 consistency (no `DateTime.UtcNow` under `.../Logistics/Services/`) | `use-injected-clock-for-transitions` Step 7 |
| NFR-4 determinism | Frozen `FakeTimeProvider` in `inject-timeprovider` Step 1; grep guard in `clock-advance-regression-test` Step 4 |
| Amendment #1 `StateDate` not `Date` | Every state-log assertion uses `stateLogEntry.StateDate` |
| Amendment #2 state-log assertions mandatory, no escape hatch | All four timestamp assertion blocks assert `StateLog` as well as `LastStateChanged` |
| Amendment #3 `.UtcDateTime` justified by in-memory equivalence | Reference facts section; grep guard in Step 7 |
| Amendment #4 conflicting docs untouched, follow-up noted | `validate-and-verify-scope` Steps 4, 6 |

No gaps.

**2. Placeholder scan:** every code step contains complete, compilable C#. No "TBD", no "add error handling", no "similar to task N" — the assertion block is repeated in full for each of the three transition tests rather than cross-referenced. Every command has an explicit expected result.

**3. Type consistency:** `_timeProvider` names the field in both the service (`TimeProvider`) and the test class (`FakeTimeProvider`) — distinct classes, distinct files, no collision. `FrozenNow` is `DateTimeOffset`; every assertion compares against `FrozenNow.UtcDateTime` (a `DateTime`), matching `LastStateChanged` (`DateTime?`) and `StateDate` (`DateTime`). `box.StateLog.Single()` returns `TransportBoxStateLog`, whose `State`, `StateDate`, `User`, `Description` members are all used exactly as declared. `_timeProvider.Advance(TimeSpan)` exists on `FakeTimeProvider`, which is why the field is typed `FakeTimeProvider` and not `TimeProvider`. The test constructor's fourth argument (`FakeTimeProvider`) binds to the service's fourth parameter (`TimeProvider`) by inheritance.
