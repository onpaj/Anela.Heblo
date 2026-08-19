# Design: Inject TimeProvider into TransportBoxCompletionService

Backend-only refactor (`arch-review.r1.md` → `## Skip Design: true`). No UI, no HTTP contract, no OpenAPI/TypeScript regeneration. This document records component boundaries and data shapes only.

Design incorporates the architect's four Specification Amendments: `TransportBoxStateLog.StateDate` (not `Date`), `TransportBox.StateLog` verified public so the FR-5 escape hatch is struck, `.UtcDateTime` justified by in-memory equivalence (not the spec's Npgsql rationale), and the two conflicting `docs/architecture/` time guides left untouched.

## Component Design

### Component map

```
AddCrossCuttingServices                       LogisticsModule.AddLogisticsModule
(API/Extensions/ServiceCollectionExtensions   (Application/Features/Logistics/
 .cs:130 — AddSingleton(TimeProvider.System))  LogisticsModule.cs:28, :49-52)
        │                                                │
        │  UNCHANGED                                     │  UNCHANGED
        │                                                │
        └──────────────► TransportBoxCompletionService ◄──┘
                         (MODIFIED — ctor + 3 call sites)
                                    │
                                    │ box.ToPick(now, "System") / box.Error(now, "System", msg)
                                    ▼
                         TransportBox (Domain aggregate)   UNCHANGED
                         ChangeState → LastStateChanged = now
                                     → _stateLog.Add(new TransportBoxStateLog(state, now, user, desc))
                                    │
                                    ▼
                         ITransportBoxRepository → EF Core → Postgres   UNCHANGED
```

Exactly one dependency edge is added, from an existing singleton to an existing service. No new type, interface, file, package, or registration.

---

### 1. `TransportBoxCompletionService` — MODIFIED

`backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`

**Responsibility (unchanged):** background refresh task (every 2 min) that loads boxes in `Received` state and transitions each to `Stocked` or `Error` based on its stock-up operation states, writing a timestamped audit entry per transition.

**Responsibility change:** the instant it hands to the aggregate now comes from an injected `TimeProvider` instead of the static wall clock. Nothing else about the component's role moves.

**New field** — added after `_stockOperationQueryService`, matching the declaration order of the constructor parameters:

```csharp
private readonly TimeProvider _timeProvider;
```

**Constructor signature (exact, post-change):**

```csharp
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

`timeProvider` is the **last** parameter, matching all five sibling handlers in the part (`AddItemToBoxHandler`, `RemoveItemFromBoxHandler`, `OpenOrResumeBoxByCodeHandler`, `ChangeTransportBoxStateHandler`, `CreateNewTransportBoxHandler`). Plain assignment, no `ArgumentNullException` guard — the existing constructor uses plain assignments and so do the siblings.

**Call-site changes** — three lines inside `ProcessBoxAsync`, nothing else in the method body:

| Line | Before | After |
|------|--------|-------|
| 91 | `box.Error(DateTime.UtcNow, "System",`<br>`    "No stock-up operations found for this box");` | `box.Error(_timeProvider.GetUtcNow().UtcDateTime, "System",`<br>`    "No stock-up operations found for this box");` |
| 111 | `box.ToPick(DateTime.UtcNow, "System");` | `box.ToPick(_timeProvider.GetUtcNow().UtcDateTime, "System");` |
| 131 | `box.Error(DateTime.UtcNow, "System", errorMessage);` | `box.Error(_timeProvider.GetUtcNow().UtcDateTime, "System", errorMessage);` |

**Two constraints that a reviewer must enforce on these lines:**

- `.UtcDateTime`, never `.DateTime`. `DateTimeOffset.UtcDateTime` yields `Kind = Utc` — the exact in-memory equivalent of today's `DateTime.UtcNow`; `DateTimeOffset.DateTime` yields `Kind = Unspecified` and would change in-memory behaviour, violating FR-6. This is *not* a persistence concern (see Data Schemas: the column is `timestamp without time zone` behind a Kind-normalizing value converter), and it is *not* caught by the tests — FluentAssertions compares `DateTime` by ticks, not by `Kind`. Catch it in code review by grepping the diff for `GetUtcNow().DateTime`. Note `docs/architecture/Dev_Guidelines_time.md:14` recommends `.DateTime`; the local part convention overrides it here, and that doc is deliberately not edited (Amendment #4).
- No `DateTime.SpecifyKind(...)` wrapper. `.UtcDateTime` is already `Kind = Utc`. The redundant wrapper at `ChangeTransportBoxStateHandler:118` is pre-existing and out of scope.

**Clock read is inline at each call site**, not hoisted to a local at the top of `ProcessBoxAsync`. The three branches (`:85` empty-ops, `:105` all-completed, `:118` any-failed) are mutually exclusive and are followed by two write-free early returns (`:138` pending/submitted skip, `:150` unexpected-state skip). Inline reads the clock exactly once per transition and zero times on the skip paths; hoisting would read it on skip paths and widen the diff for no behavioural gain. `ProcessBoxAsync` handles one box, so there is no cross-box shared-instant requirement.

**Everything else is byte-identical:** the `"System"` actor string, both error message strings, branch conditions, `UpdateAsync`/`SaveChangesAsync` ordering and count, the `BoxProcessingResult` enum and returned values, the try/catch in `CompleteReceivedBoxesAsync`, the `completedCount`/`errorCount`/`skippedCount` counters, and every log template and level.

---

### 2. DI composition — UNCHANGED, verify only

| Component | File | Why no change |
|---|---|---|
| `TimeProvider` singleton | `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130` — `services.AddSingleton(TimeProvider.System);` | Already the single composition root for the clock. |
| Service registration | `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs:28` — `services.AddTransient<ITransportBoxCompletionService, TransportBoxCompletionService>();` | Constructor activation — the container fills the new fourth parameter automatically. No factory lambda to update. |
| Refresh-task registration | `LogisticsModule.cs:49-52` — `RegisterRefreshTask<ITransportBoxCompletionService>(nameof(...CompleteReceivedBoxesAsync), (service, ct) => service.CompleteReceivedBoxesAsync(ct))` | Binds to the interface, not the constructor. |

`ApplicationStartupTests` boots the real graph and is the resolution guard. Grep confirms the only non-DI construction site of `TransportBoxCompletionService` in the repository is the test file — no hidden `new TransportBoxCompletionService(...)` breaks.

---

### 3. `ITransportBoxCompletionService` and the `TransportBox` aggregate — UNCHANGED

- `ITransportBoxCompletionService.CompleteReceivedBoxesAsync(CancellationToken)` (`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxCompletionService.cs`) is byte-identical. The clock is a constructor concern, not part of the contract — putting it on the method would leak infrastructure into a domain-owned interface and would force a change on the refresh-task registration.
- `TransportBox.ToPick(DateTime date, string userName)` (`TransportBox.cs:224`) and `TransportBox.Error(DateTime date, string userName, string exMessage)` (`TransportBox.cs:259`) already take the instant as a parameter. That is precisely why this refactor is one line per call site — the aggregate needs no change at all.
- `TransportBox.ChangeState(newState, now, userName, description, allowedStates)` (private, `TransportBox.cs:241-248`) writes `LastStateChanged = now` and appends `new TransportBoxStateLog(newState, now, userName, description)`. The injected instant is therefore persisted, not merely logged.

**Module boundaries:** `TimeProvider` is a BCL type. The Application layer taking it as a constructor dependency adds no project reference and cannot trip `Architecture/ModuleBoundariesTests`.

---

### 4. `TransportBoxCompletionServiceTests` — MODIFIED

`backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs`

**Responsibility change:** the suite stops depending on the machine clock and becomes able to assert persisted timestamps — the only reason FR-1/FR-2 are worth doing.

**Structure — extend the existing arrangement, do not restructure it.** Add one `using`, one constant, one field; the Moq fields and the constructor-built `_service` stay exactly as they are.

```csharp
using Microsoft.Extensions.Time.Testing;   // new; package already at Anela.Heblo.Tests.csproj:26

private static readonly DateTimeOffset FrozenNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

private readonly FakeTimeProvider _timeProvider;   // new field, alongside the Moq fields

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

The provider is held in a **field**, not constructed inline, because FR-5's advance test needs `Advance(...)` on the same instance the service holds. Use `FakeTimeProvider` from `Microsoft.Extensions.Time.Testing` — the established idiom (`backend/test/Anela.Heblo.Tests/Common/TimePeriods/TimePeriodResolverTests.cs:3,10`, plus ~10 other files). Do not hand-roll a `TimeProvider` subclass. `Microsoft.Extensions.TimeProvider.Testing` 8.1.0 is already referenced; the `.csproj` does not change.

**Assertion surface (all verified public — the spec's "if not publicly readable, drop it" escape hatch is struck per Amendment #2):**

```csharp
box.LastStateChanged                 // DateTime?      TransportBox.cs:21
box.StateLog                         // IReadOnlyList<TransportBoxStateLog>   TransportBox.cs:37
entry.State, entry.StateDate, entry.User, entry.Description
```

`TransportBoxStateLog`'s constructor is `internal`, so tests never construct one — they read the entry the transition appended. `CreateBox` builds a box with an empty `_stateLog` and `StateLog` is append-only, so after a single transition the collection holds exactly one entry. Assert `box.StateLog.Should().ContainSingle()` and read that entry rather than relying on `Last()` ordering. Keep the existing FluentAssertions `.Should().Be(...)` style and the existing `CreateBox` / `CreateStatus` / `SetupQueryReturns` helpers.

**Coverage the file must carry after the change:**

| # | Scenario | Extends / new | Timestamp assertions |
|---|---|---|---|
| 1 | `AllOperationsCompleted` → `Stocked` | extend existing | `box.LastStateChanged == FrozenNow.UtcDateTime`; single state-log entry with `State == Stocked`, `StateDate == FrozenNow.UtcDateTime`, `User == "System"` |
| 2 | `AnyOperationFailed` → `Error` | extend existing | `box.LastStateChanged == FrozenNow.UtcDateTime`; entry `State == Error`, `StateDate == FrozenNow.UtcDateTime`, `User == "System"` |
| 3 | `NoOperationsForBox` → `Error` | extend existing | same as #2 |
| 4 | Clock advance is honoured | new test | `_timeProvider.Advance(TimeSpan.FromHours(1))` before `CompleteReceivedBoxesAsync`; written instant is `FrozenNow.UtcDateTime.AddHours(1)` on both `LastStateChanged` and `StateDate` |

Because the clock is read inline at transition time, advancing the fake before invoking the service is sufficient — no per-call plumbing. All seven existing tests (`NoReceivedBoxes_DoesNothing`, `AllOperationsCompleted_TransitionsBoxToStocked`, `AnyOperationFailed_TransitionsBoxToError`, `OperationsPending_LeavesBoxInReceived`, `NoOperationsForBox_TransitionsToError`, `MultipleBoxes_ProcessesAll`, `OperationsSubmitted_LeavesBoxInReceived`) keep their intent; only the fourth constructor argument and the added assertions change. No test may reference `DateTime.UtcNow` or otherwise touch the real clock.

**Regression guard this buys:** reintroducing `DateTime.UtcNow` at any of the three call sites fails tests 1-4. Note again that switching `.UtcDateTime` → `.DateTime` does *not* fail them (tick-equal, Kind differs) — that one is a review-gate check, not a test-gate check.

---

### 5. Components explicitly outside the boundary

`git diff --name-only` must return exactly two paths — the service and its test file. Any third file is a defect. Specifically untouched: `LogisticsModule.cs`, `ITransportBoxCompletionService.cs`, `TransportBox.cs`, `TransportBoxStateLog.cs`, every `Anela.Heblo.Persistence` configuration and migration, `Anela.Heblo.Tests.csproj`, `docs/features/complete-received-boxes-job.md`, `docs/architecture/DateTime_StandardizationGuide.md`, `docs/architecture/Dev_Guidelines_time.md`, `TransportBoxBaseTile.cs:47`, `ChangeTransportBoxStateHandler:118`, and the `ModuleBoundariesTests` allowlists. The two contradictory time-guidance docs are reconciled in a separate follow-up issue referenced from the PR description, not here.

## Data Schemas

### Database — unchanged. No migration.

No entity, configuration, column, type, index, or migration changes. For reference, the two columns this service writes:

| Entity | Property | CLR type | Column | Column type | Config |
|---|---|---|---|---|---|
| `TransportBox` | `LastStateChanged` | `DateTime?` | `LastStateChanged` | timestamp | `TransportBox.cs:21` |
| `TransportBoxStateLog` | `StateDate` | `DateTime` | `StateDate` | `timestamp without time zone` | `TransportBoxStateLogConfiguration.cs:16-19` |

**Kind handling on the wire (why persistence is indifferent to this change):** `ApplicationDbContext.cs:190-208` installs a global `ValueConverter<DateTime, DateTime>` / `ValueConverter<DateTime?, DateTime?>` over every `DateTime` property — `DateTime.SpecifyKind(v, DateTimeKind.Unspecified)` on write, `DateTime.SpecifyKind(v, DateTimeKind.Utc)` on read. Persistence is therefore Kind-agnostic, and the spec's assumption #2 (Npgsql `timestamp with time zone` requires `Kind = Utc`) is superseded per Amendment #3. The reason to use `.UtcDateTime` is in-memory equivalence with `DateTime.UtcNow` and assertability in tests — the conclusion holds, the original rationale does not.

Under `TimeProvider.System` the persisted values and their `Kind` are indistinguishable from before the change.

### `TransportBoxStateLog` — read-side shape consumed by the tests (unchanged)

`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateLog.cs`

```csharp
public class TransportBoxStateLog : Entity<int>
{
    public TransportBoxState State { get; private set; }
    public DateTime StateDate { get; private set; }   // NOT "Date" — the spec's Data Model section names it wrongly (Amendment #1)
    public string? User { get; private set; }
    public string? Description { get; set; }

    internal TransportBoxStateLog(TransportBoxState state, DateTime stateDate, string? user, string? description = null);
}
```

Values written by this service per transition: `State` ∈ {`Stocked`, `Error`}, `StateDate` = the injected instant, `User` = `"System"`, `Description` = `null` for `ToPick`, the error message string for `Error`.

### API request/response shapes — none

The service is not exposed over HTTP. It is invoked in-process by `BackgroundRefreshSchedulerService` through `RegisterRefreshTask`, whose signature `CompleteReceivedBoxesAsync(CancellationToken)` is unchanged. Consequently: no controller, no DTO, no OpenAPI document change, no C#/TypeScript client regeneration, no frontend build or E2E run.

### Event payloads — none

No domain events, integration events, or message-bus payloads are published or consumed by this service. The only cross-boundary read is `ILogisticsStockOperationQueryService.GetOperationsBySourceAsync(LogisticsStockOperationSource.TransportBox, box.Id, ct)` returning `IReadOnlyList<LogisticsStockOperationStatus>` — unchanged in shape and usage.

### Configuration and secrets — none

No `appsettings.json` key, refresh-task schedule, initial delay, hydration tier, feature flag, environment variable, or Key Vault secret is added or modified.
