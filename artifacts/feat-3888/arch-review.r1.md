# Architecture Review: Inject TimeProvider into TransportBoxCompletionService

## Skip Design: true

Backend-only refactor. No UI, no HTTP contract, no OpenAPI/TypeScript client regeneration, nothing under `frontend/`. Design phase can be skipped entirely.

## Architectural Fit Assessment

**Verdict: the change is a pure alignment move with an already-established, verified pattern. Approve as specified, with four corrections listed under Specification Amendments.**

Everything the spec asserts about the current state was checked against source:

| Spec claim | Verified | Evidence |
|---|---|---|
| `TimeProvider.System` registered as singleton | ✅ | `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130` — `services.AddSingleton(TimeProvider.System);` |
| Sibling handlers all inject `TimeProvider` as last ctor param | ✅ | `AddItemToBoxHandler.cs:19,27,43`; `RemoveItemFromBoxHandler.cs:19,27,43`; `OpenOrResumeBoxByCodeHandler.cs:18,25,47`; `ChangeTransportBoxStateHandler.cs:20,45,118,243`; `CreateNewTransportBoxHandler.cs:14,21,44` |
| Three `DateTime.UtcNow` call sites in the service | ✅ | `TransportBoxCompletionService.cs:91, 111, 131` |
| Service has no `TimeProvider` today | ✅ | ctor at `TransportBoxCompletionService.cs:14-22` takes 3 deps |
| Constructor-based DI activation, no factory lambda | ✅ | `LogisticsModule.cs:28` — `services.AddTransient<ITransportBoxCompletionService, TransportBoxCompletionService>();` |
| `Microsoft.Extensions.TimeProvider.Testing` 8.1.0 already referenced | ✅ | `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj:26` |
| `FakeTimeProvider` idiom already in use | ✅ | `backend/test/Anela.Heblo.Tests/Common/TimePeriods/TimePeriodResolverTests.cs:3,10` (+ ~10 other test files) |
| Timestamp is persisted, not just logged | ✅ | `TransportBox.cs:241-248` — `ChangeState` writes `LastStateChanged = now` and appends `new TransportBoxStateLog(newState, now, userName, description)` |
| Only one non-DI construction site exists | ✅ | grep for `new TransportBoxCompletionService(` returns exactly one hit: the test file, line 24 |

**Integration points (all of them):**
1. **DI composition** — `LogisticsModule.cs:28` (untouched; the container fills the new parameter).
2. **Background scheduler** — `LogisticsModule.cs:49-52` `RegisterRefreshTask<ITransportBoxCompletionService>` (untouched; it resolves the interface, not the ctor).
3. **Domain aggregate** — `TransportBox.Error(...)` / `TransportBox.ToPick(...)` (untouched; they already take the instant as a parameter, which is precisely why this refactor is a one-line-per-call-site change).
4. **Persistence** — normalized by a global value converter (see Decision 2); no migration, no config change.
5. **Unit tests** — the single behavioural surface that actually changes.

**Module boundaries:** nothing crosses. `TimeProvider` is a BCL type, so the Application layer taking it as a ctor dependency introduces no new project reference and cannot trip `Architecture/ModuleBoundariesTests`.

## Proposed Architecture

### Component Overview

```
 AddCrossCuttingServices                LogisticsModule.AddLogisticsModule
 (API/Extensions/                       (Application/Features/Logistics/
  ServiceCollectionExtensions.cs:130)    LogisticsModule.cs:28, :49)
        |                                        |
        | AddSingleton(TimeProvider.System)      | AddTransient<ITransportBoxCompletionService,
        |                                        |              TransportBoxCompletionService>()
        v                                        | RegisterRefreshTask(...CompleteReceivedBoxesAsync)
  +-------------+                                v
  | TimeProvider|                     +--------------------------------+
  |  (singleton)|-------------------->| TransportBoxCompletionService  |
  +-------------+   ctor param #4     |  _logger                       |
        ^                             |  _transportBoxRepository       |
        |                             |  _stockOperationQueryService   |
        |                             |  _timeProvider   <-- NEW       |
        |                             +--------------------------------+
        |                                        |
        |                                        | ProcessBoxAsync(box)
        |                                        |   :91  box.Error(_timeProvider.GetUtcNow().UtcDateTime, "System", "No stock-up…")
        |                                        |   :111 box.ToPick(_timeProvider.GetUtcNow().UtcDateTime, "System")
        |                                        |   :131 box.Error(_timeProvider.GetUtcNow().UtcDateTime, "System", errorMessage)
        |                                        v
        |                             +--------------------------------+
        |                             | TransportBox (Domain aggregate)|
        |                             |  ChangeState(state, now, user) |
        |                             |    LastStateChanged = now      |
        |                             |    _stateLog.Add(new           |
        |                             |      TransportBoxStateLog(     |
        |                             |        state, now, user, desc))|
        |                             +--------------------------------+
        |                                        |
        |                                        v   ApplicationDbContext:196-205
        |                                   Kind normalized to Unspecified on write,
        |                                   read back as Utc → "timestamp without time zone"
        |
  +----------------------------------+
  | TESTS: new FakeTimeProvider(     |
  |          FrozenNow) as ctor #4   |  (Microsoft.Extensions.Time.Testing, already referenced)
  +----------------------------------+
```

Structurally nothing new is introduced — one edge is added from an already-existing singleton to an already-existing service.

### Key Design Decisions

#### Decision 1: BCL `TimeProvider` injected via constructor — no wrapper abstraction, no interface change

**Options considered:**
- (a) Inject BCL `System.TimeProvider` as the last constructor parameter — the repo-wide pattern.
- (b) Introduce a project-specific `IDateTimeProvider` / `IClock` abstraction.
- (c) Add the clock as a parameter on `ITransportBoxCompletionService.CompleteReceivedBoxesAsync(...)`.

**Chosen approach:** (a), exactly as FR-1 specifies. Signature becomes:

```csharp
public TransportBoxCompletionService(
    ILogger<TransportBoxCompletionService> logger,
    ITransportBoxRepository transportBoxRepository,
    ILogisticsStockOperationQueryService stockOperationQueryService,
    TimeProvider timeProvider)
```

**Rationale:** 108 `GetUtcNow()` call sites across `backend/src` already use the BCL type; there is no `IDateTimeProvider` in the codebase and introducing one would be a repo-wide regression in consistency for zero gain. (c) would change a public interface consumed by the refresh-task registration and leak an infrastructure concern into a domain-owned contract (`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxCompletionService.cs`) — rejected. Last-position placement matches all five sibling handlers.

#### Decision 2: `.UtcDateTime`, never `.DateTime` — and the spec's stated reason for it is wrong

**Options considered:** `_timeProvider.GetUtcNow().UtcDateTime` vs `_timeProvider.GetUtcNow().DateTime` vs `DateTime.SpecifyKind(...UtcDateTime, DateTimeKind.Utc)`.

**Chosen approach:** `_timeProvider.GetUtcNow().UtcDateTime`, unwrapped.

**Rationale — and this is the one place a developer can silently get it wrong.** Repo-wide the split is 57 `.DateTime` vs 23 `.UtcDateTime`, and `docs/architecture/Dev_Guidelines_time.md:14` actively recommends `.DateTime`. That doc guidance is a trap here: `DateTimeOffset.DateTime` on a UTC-offset value returns `Kind = Unspecified`, whereas today's `DateTime.UtcNow` returns `Kind = Utc`. Using `.DateTime` would therefore change the in-memory `DateTimeKind` of `LastStateChanged` and `TransportBoxStateLog.StateDate` relative to current behaviour, violating FR-6. **Every handler in the Transport Boxes part uses `.UtcDateTime`** (`AddItemToBoxHandler:43`, `RemoveItemFromBoxHandler:43`, `OpenOrResumeBoxByCodeHandler:47`, `ChangeTransportBoxStateHandler:118,243`, `CreateNewTransportBoxHandler:44`) — the local part convention governs, not the global doc.

Note the spec's *justification* for this (assumption #2: "Npgsql's `timestamp with time zone` mapping requires `DateTimeKind.Utc`") is factually incorrect for this codebase and should not be relied on by the implementer: `TransportBoxStateLogConfiguration.cs:18` maps `StateDate` as **`timestamp without time zone`**, and `ApplicationDbContext.cs:196-205` installs a global `ValueConverter<DateTime, DateTime>` that forces `Kind = Unspecified` on write and re-stamps `Kind = Utc` on read. Persistence is therefore Kind-agnostic; the reason to use `.UtcDateTime` is *in-memory equivalence and test assertions*, not Npgsql. The conclusion stands, the reasoning does not. Do not add `DateTime.SpecifyKind(...)` — `.UtcDateTime` is already `Kind = Utc`, and the redundant wrapper at `ChangeTransportBoxStateHandler:118` is explicitly out of scope.

#### Decision 3: Read the clock inline at each of the three call sites

**Options considered:** inline read per call site vs one hoisted `var timestamp = _timeProvider.GetUtcNow().UtcDateTime;` at the top of `ProcessBoxAsync` (the shape `AddItemToBoxHandler:43` uses).

**Chosen approach:** inline, as FR-2 decides. I endorse it rather than merely accepting it.

**Rationale:** the three branches in `ProcessBoxAsync` (`:85`, `:105`, `:118`) are mutually exclusive and are followed by two more early-return paths (`:138` skip, `:150` unexpected-state skip) that write nothing. Hoisting would read the clock on both skip paths, produce a three-token-wider diff, and buy nothing — `ProcessBoxAsync` handles exactly one box, so there is no cross-box consistency argument for a shared instant. Inline keeps the diff to three lines plus the constructor.

#### Decision 4: `FakeTimeProvider` stored as a test-class field so `Advance()` is reachable

**Options considered:** pass `new FakeTimeProvider(FrozenNow)` inline in the test constructor; or hold it in a field.

**Chosen approach:** hold it in a `private readonly FakeTimeProvider _timeProvider;` field alongside the existing Moq fields, assigned in the constructor before `_service` is built.

**Rationale:** FR-5 requirement 4 needs `Advance(...)` on the same instance the service holds. An inline construction would force either a second service instance or a restructure of the test class — both worse than one extra field. This preserves the existing "mocks in fields, `_service` built in the constructor" arrangement that FR-4 says to extend rather than restructure. Because the clock is read inline at transition time (Decision 3), advancing the fake before calling `CompleteReceivedBoxesAsync` is sufficient — no per-call plumbing needed.

## Implementation Guidance

### Directory / Module Structure

Exactly two files change. Any third file in the diff is a defect.

```
backend/
├── src/Anela.Heblo.Application/Features/Logistics/Services/
│   └── TransportBoxCompletionService.cs        ← MODIFY (ctor + 3 call sites)
└── test/Anela.Heblo.Tests/Features/Logistics/Services/
    └── TransportBoxCompletionServiceTests.cs   ← MODIFY (fake clock + timestamp assertions)
```

**Explicitly not touched** (each confirmed to require no change):
- `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` — `AddTransient<I,Impl>()` at `:28` is constructor activation; the container resolves the new parameter from the singleton at `ServiceCollectionExtensions.cs:130`. The `RegisterRefreshTask` block at `:49-52` binds to the interface and is unaffected.
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxCompletionService.cs`, `TransportBox.cs`, `TransportBoxStateLog.cs`.
- Any `Anela.Heblo.Persistence` configuration or migration.
- `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — the testing package is already at `:26`.
- `docs/` — see Specification Amendments #4 for the one doc that is arguably stale, and why it should still not be edited here.

### Interfaces and Contracts

**Changed (concrete class constructor only — not a published contract):**

```csharp
// Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs
private readonly TimeProvider _timeProvider;   // new field, after _stockOperationQueryService

public TransportBoxCompletionService(
    ILogger<TransportBoxCompletionService> logger,
    ITransportBoxRepository transportBoxRepository,
    ILogisticsStockOperationQueryService stockOperationQueryService,
    TimeProvider timeProvider)                 // new, last
```

No `ArgumentNullException.ThrowIfNull` guards — the existing constructor does plain assignments and the sibling handlers do the same; adding guards here would be an unrequested style change.

**Unchanged contracts (assert this in review):**
- `ITransportBoxCompletionService.CompleteReceivedBoxesAsync(CancellationToken)` — byte-identical.
- `TransportBox.Error(DateTime date, string userName, string exMessage)` (`TransportBox.cs:259`) and `TransportBox.ToPick(DateTime date, string userName)` (`TransportBox.cs:224`) — already parameterized on the instant; that is why no domain change is needed.

**Contracts the tests must consume (verified public, so FR-5's "if not publicly readable" fallback is dead text):**

```csharp
// TransportBox.cs:21, :37
public DateTime? LastStateChanged { get; set; }
public IReadOnlyList<TransportBoxStateLog> StateLog => _stateLog;

// TransportBoxStateLog.cs — note the property name
public TransportBoxState State { get; private set; }
public DateTime StateDate { get; private set; }   // NOT "Date"
public string? User { get; private set; }
public string? Description { get; set; }
```

State-log assertions are therefore **mandatory**, not conditional. `StateLog` is append-only (`_stateLog.Add` in `ChangeState`), so `box.StateLog.Last()` is the entry written by the transition under test; the test helper `CreateBox` builds a box with an empty log, so `box.StateLog` will contain exactly one entry after a single transition — assert `.Should().ContainSingle()` and read that entry rather than relying on ordering.

### Data Flow

**Production (`TimeProvider.System`) — unchanged in observable effect:**
```
BackgroundRefreshSchedulerService (every 2 min)
  → ITransportBoxCompletionService.CompleteReceivedBoxesAsync(ct)
    → ITransportBoxRepository.GetReceivedBoxesAsync(ct)
    → foreach box: ProcessBoxAsync(box, ct)
        → ILogisticsStockOperationQueryService.GetOperationsBySourceAsync(TransportBox, box.Id, ct)
        → branch:
           ops.Count == 0        → box.Error(_timeProvider.GetUtcNow().UtcDateTime, "System", "No stock-up…")  → Failed
           allCompleted          → box.ToPick(_timeProvider.GetUtcNow().UtcDateTime, "System")                 → Completed
           anyFailed             → box.Error(_timeProvider.GetUtcNow().UtcDateTime, "System", errorMessage)    → Failed
           pendingOrSubmitted    → (no clock read)                                                             → Skipped
           otherwise             → (no clock read)                                                             → Skipped
        → TransportBox.ChangeState: LastStateChanged = now; _stateLog.Add(TransportBoxStateLog(state, now, "System", desc))
        → UpdateAsync + SaveChangesAsync
        → ApplicationDbContext value converter: Kind=Utc → Kind=Unspecified → "timestamp without time zone"
```
The only edge that moves is the source of `now`: `DateTime.UtcNow` (static) becomes `_timeProvider.GetUtcNow().UtcDateTime` (injected). Under `TimeProvider.System` the value and `Kind` are identical.

**Test (`FakeTimeProvider(FrozenNow)`):** the same path, with `now == FrozenNow.UtcDateTime` deterministically — which is what makes `LastStateChanged` and `StateLog.Last().StateDate` assertable. The advance test calls `_timeProvider.Advance(TimeSpan.FromHours(1))` before `CompleteReceivedBoxesAsync` and asserts the written instant is `FrozenNow.UtcDateTime.AddHours(1)`; because the clock is read inline at the transition, that value propagates without any further wiring.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Implementer follows `docs/architecture/Dev_Guidelines_time.md:14` and writes `.DateTime` instead of `.UtcDateTime`, silently flipping `DateTimeKind` from `Utc` to `Unspecified` | **Medium** | Decision 2 is explicit. Code review must grep the diff for `GetUtcNow().DateTime` and reject it. The FR-5 assertions (comparing against `FrozenNow.UtcDateTime`, which is `Kind = Utc`) will fail under FluentAssertions' `DateTime` comparison only if Kind is compared — do **not** rely on the tests to catch this; catch it in review. |
| Test asserts a non-existent `TransportBoxStateLog.Date` property (the spec's Data Model section names it that) | Low | Corrected in Amendment #1: the property is `StateDate`. Compile failure catches it immediately. |
| FR-5's "if the collection is not publicly readable, drop the state-log assertion" escape hatch gets used, weakening the regression guard | Low | `TransportBox.StateLog` is verified public (`TransportBox.cs:37`). Amendment #2 removes the escape hatch — state-log assertions are required. |
| Scope creep into the ~30 other `DateTime.UtcNow` sites, or into `TransportBoxBaseTile.cs:47` / `ChangeTransportBoxStateHandler:118` | Low | Spec's Out of Scope list is correct and sufficient. Enforce the two-file diff rule mechanically: `git diff --name-only` must return exactly the two paths. |
| DI resolution fails at startup because `TimeProvider` isn't registered in some host variant (tests, MCP host, migrations host) | Low | `AddCrossCuttingServices` is the single composition root registering it (`ServiceCollectionExtensions.cs:130`), and `ApplicationStartupTests` boots the real graph. Run `ApplicationStartupTests` as part of validation — it is the guard. The only non-DI construction site in the whole repo is the one test file (verified by grep), so no hidden `new TransportBoxCompletionService(...)` breaks. |
| A future developer reintroduces `DateTime.UtcNow` here | Low | FR-5's acceptance criterion ("a deliberate reintroduction causes at least one test to fail") is the guard and is satisfied by the frozen-clock assertions plus the advance test. No architecture test needs to be added — that would be scope creep. |
| `dotnet format` churn beyond the intended lines | Low | Run `dotnet format` and inspect; the change is local enough that no reflow should occur. |

## Specification Amendments

Four corrections. None change the shape of the work; all three of the first three are needed for the tests to compile or to hold their intended strength.

1. **Data Model section — wrong property name.** The spec refers to `TransportBoxStateLog.Date`. The actual property is **`StateDate`** (`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateLog.cs`), mapped to column `"StateDate"` of type `timestamp without time zone` (`TransportBoxStateLogConfiguration.cs:16-19`). FR-5 assertions must read `box.StateLog.Last().StateDate`.

2. **FR-5, implementer note — delete the conditional escape hatch.** `TransportBox.StateLog` is a public `IReadOnlyList<TransportBoxStateLog>` (`TransportBox.cs:37`), and `TransportBoxStateLog.State` / `.StateDate` / `.User` are public getters. The clause "If the collection is not publicly readable … the state-log assertion may be dropped" is moot and should be struck — state-log assertions are **required** alongside the `LastStateChanged` assertions, for all three transition kinds.

3. **Assumptions #2 — replace the persistence rationale.** The spec justifies `.UtcDateTime` by claiming Npgsql's `timestamp with time zone` mapping requires `Kind = Utc`. In this codebase the relevant columns are `timestamp without time zone` and `ApplicationDbContext.cs:196-205` applies a global `ValueConverter<DateTime, DateTime>` (`Kind = Unspecified` on write, `Kind = Utc` on read), so persistence is Kind-agnostic. Restate the assumption as: *`DateTimeOffset.UtcDateTime` (`Kind = Utc`) is the exact in-memory equivalent of `DateTime.UtcNow`, whereas `DateTimeOffset.DateTime` (`Kind = Unspecified`) is not; `.UtcDateTime` is therefore required for FR-6 (no behavioural change) and matches every sibling handler in the part.* The conclusion is unchanged.

4. **Documentation conflict — acknowledge, do not fix here.** `docs/architecture/DateTime_StandardizationGuide.md` §3 ("Application Code Standard") states *"ALWAYS use `DateTime.UtcNow` for storing timestamps"*, which this change deliberately contradicts, and `docs/architecture/Dev_Guidelines_time.md:14` recommends `GetUtcNow().DateTime`, which Decision 2 rejects. Both are repo-wide guidance documents, not Transport-Box-specific, and editing them is well outside this refactor. **Recommendation: leave both untouched in this PR** (consistent with the spec's Out of Scope) **and file a separate follow-up issue** to reconcile them with the injected-`TimeProvider` + `.UtcDateTime` convention. Mention the follow-up issue number in the PR description so the contradiction is not rediscovered later.

Everything else in `spec.r1.md` — FR-1 through FR-6, NFR-1 through NFR-4, the Out of Scope list, and the resolution of the sole Open Question in favour of inline clock reads — is accepted as written.

## Prerequisites

None blocking. All dependencies already exist and were verified:

- ✅ `TimeProvider.System` singleton registration — `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130`.
- ✅ `Microsoft.Extensions.TimeProvider.Testing` 8.1.0 — `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj:26`; `FakeTimeProvider` already used in ~10 test files (reference idiom: `backend/test/Anela.Heblo.Tests/Common/TimePeriods/TimePeriodResolverTests.cs:3,10`).
- ✅ Constructor-based DI activation for the service — `LogisticsModule.cs:28`.
- ✅ Public `TransportBox.StateLog` / `LastStateChanged` accessors for assertions — `TransportBox.cs:21,37`.
- ✅ No other construction site of `TransportBoxCompletionService` anywhere in the repo besides the test file.

**Validation gate before completion** (as specified, plus one addition):
- `dotnet build` — clean; `dotnet format` — no diff.
- `dotnet test backend/test/Anela.Heblo.Tests` — the Logistics service tests, `ApplicationStartupTests`, and `Architecture/ModuleBoundariesTests` pass.
- `git diff --name-only` returns exactly the two files listed under Directory / Module Structure.
- No frontend build/lint, no OpenAPI regeneration, no E2E run — nothing under `frontend/` changes and no HTTP contract moves.
