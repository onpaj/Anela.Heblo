# Architecture Review: RunHydrationTierHandler unit test coverage

## Skip Design: true

## Architectural Fit Assessment

This is a pure test-coverage addition to an existing, already-correct MediatR handler. No new component, no new interface, no production behavior change is proposed. `RunHydrationTierHandler` is a small, self-contained handler (39 LOC) with a single dependency (`IBackgroundRefreshTaskRegistry`) that is already interface-based and trivially mockable — there is no seam to design, only tests to write. The spec's own framing ("test-coverage-gap fix, not a behavior change") is correct and matches what I verified in code.

Skip Design is warranted: no UI, no API/contract change, no data model change, no new module boundary. The only architectural question worth resolving is *how* the test should be structured to match this codebase's established conventions — which I verified directly rather than taking the spec's word for it.

## Proposed Architecture

### Component Overview

No new components. One new test file:

```
backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/RunHydrationTierHandlerTests.cs
```

This is the first test file under `Application/BackgroundRefresh/` — the directory does not exist yet (confirmed). It should be created fresh, mirroring the flat-per-feature-folder convention already used under `Application/Packaging/`, `Application/Marketing/`, `Application/ShoptetOrders/`, etc.

### Key Design Decisions

#### Decision 1: Logger test double — `NullLogger<T>.Instance` vs. `Mock<ILogger<T>>`

**Options considered:**
- (a) `NullLogger<RunHydrationTierHandler>.Instance` as the spec's FR-6 defaults to, with logger verification in FR-5 marked optional ("may be omitted if the codebase has no established logger-verification pattern to follow").
- (b) `Mock<ILogger<RunHydrationTierHandler>>` with an explicit `VerifyLogged(LogLevel, Times)` helper.

**Chosen approach:** (b) — use `Mock<ILogger<RunHydrationTierHandler>>`, not `NullLogger`.

**Rationale:** I checked the premise behind FR-5's escape hatch and it's false. `backend/test/Anela.Heblo.Tests/Application/Packaging/GetPackageLabelPdfHandlerTests.cs` already has an established, working pattern:

```csharp
private readonly Mock<ILogger<GetPackageLabelPdfHandler>> _logger = new();

private void VerifyLogged(LogLevel level, Times times) =>
    _logger.Verify(
        l => l.Log(
            level,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
        times);
```

Since the brief explicitly calls out the `LogError` call and its fixed error message as a regression risk ("callers branch on `Success`... a missing `await`... would ship silently" — and separately, the log call itself is one of the four uncovered branches per the coverage report), and a working verification pattern already exists one folder over, there is no reason to fall back to `NullLogger` and skip the assertion. Use the mock, assert `VerifyLogged(LogLevel.Error, Times.Once())` on the unexpected-exception path (FR-5), and optionally `VerifyLogged(LogLevel.Information, Times.Once())` on the success path (FR-3) to lock in the `LogInformation` call at line 28 of the handler — that line is also currently unexercised.

This does not change FR-6's "no new test infrastructure" constraint — the `VerifyLogged` helper is copy-paste-sized (7 lines) and lives locally in the new test file, exactly as it does in `GetPackageLabelPdfHandlerTests`.

#### Decision 2: Test class shape — `MakeSut()` factory

**Options considered:** ad-hoc mock construction per test vs. a shared private static factory.

**Chosen approach:** Follow `GetOrderTrackingNumberHandlerTests`'s exact shape — a private static `MakeSut()` returning a tuple of `(Handler Sut, Mock<...> ...)` for each dependency, called at the top of every `[Fact]`. Confirmed this is a live, repeated convention (also used by `TierBasedHydrationOrchestratorTests`, `GetPackageLabelPdfHandlerTests` uses constructor-field style — both patterns coexist in the codebase, but the spec explicitly asked to mirror `GetOrderTrackingNumberHandlerTests`, which uses the tuple-factory style; keep that choice since it's the spec's stated model file).

**Rationale:** Consistency with the file the spec nominates as the template avoids introducing a third style into a two-style codebase.

## Implementation Guidance

### Directory / Module Structure

```
backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/
└── RunHydrationTierHandlerTests.cs   (new, only file in new folder)
```

Namespace: `Anela.Heblo.Tests.Application.BackgroundRefresh` — matches the `Application.Packaging` / `Application.Marketing` / `Application.ShoptetOrders` sibling namespacing convention (folder path mirrors namespace path under `Application/`).

No `.csproj` changes needed — verified `Anela.Heblo.Tests.csproj` already has `ProjectReference`s to both `Anela.Heblo.Application.csproj` and `Anela.Heblo.Xcc.csproj`, and already pulls in `Moq` 4.20.72 and `FluentAssertions` 6.12.0 as package references. `xunit` is referenced globally via `<Using Include="Xunit" />`.

### Interfaces and Contracts

Verified directly against source (no assumptions):

- `IBackgroundRefreshTaskRegistry` (`backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/IBackgroundRefreshTaskRegistry.cs`): the two methods the handler calls are `IReadOnlyList<RefreshTaskConfiguration> GetRegisteredTasks()` and `Task ForceRefreshAsync(string taskId, CancellationToken cancellationToken = default)` — both are plain interface methods, trivially mockable with Moq, no `virtual`/sealed concerns.
- `RefreshTaskConfiguration` (same folder): a plain class (not a record — consistent with this repo's "DTOs are classes" convention, though this one is a domain config type, not a DTO) with `required` init props `TaskId` (string), `InitialDelay`/`RefreshInterval` (TimeSpan), `Enabled` (bool), `HydrationTier` (int, defaults to 1). Object-initializer construction is correct and is exactly how `TierBasedHydrationOrchestratorTests.cs` already builds these in the test suite.
- `RunHydrationTierResponse : BaseResponse` — confirmed `Success` defaults to `true` via `BaseResponse`'s parameterless constructor (`Success = true`), so FR-3's "Success true (inherited default)" and FR-4's "Success remaining true... per current implementation" are both accurate.
- Handler control flow (re-read line by line): empty/all-disabled filter → early return before any logging; non-empty → `LogInformation` → loop with `ThrowIfCancellationRequested()` **before** each `ForceRefreshAsync` call → catch `OperationCanceledException` → catch `Exception` with `LogError`. This confirms FR-4's note that an already-cancelled token seeded before the loop starts will hit `ThrowIfCancellationRequested()` on the *first* iteration before `ForceRefreshAsync` is ever called — `Times.Never` is the correct assertion for that sub-case.

### Data Flow

Test → mocked `IBackgroundRefreshTaskRegistry.GetRegisteredTasks()` returns a seeded `List<RefreshTaskConfiguration>` → handler filters/sorts → (mocked) `ForceRefreshAsync` per task, configured per test to succeed / throw `OperationCanceledException` / throw generic `Exception` → handler maps outcome to `RunHydrationTierResponse` fields → test asserts on response + mock invocation counts + (per Decision 1) logger invocations. Everything is synchronous-equivalent in-memory; no real `Task.Delay`, no real cancellation timers, consistent with NFR-1/NFR-3.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec's FR-5 logger-verification "opt-out" clause leads implementer to skip a meaningful assertion because they don't check for an existing pattern | Low | Resolved in this review (Decision 1) — mandate `Mock<ILogger<T>>` + local `VerifyLogged` helper, not `NullLogger`. Remove the opt-out language when translating spec to task. |
| `OrderBy(t => t.TaskId)` in the handler means FR-3's "two distinct TaskId" test must pick TaskIds whose string-sort order is asserted-against explicitly, or the "not double-invoked / not skipped" check could pass by accident regardless of order | Low | When writing FR-3's test, assert both `ForceRefreshAsync` calls happened (`Times.Exactly` per TaskId) rather than relying on call order; order-sensitivity is not part of the coverage gap, don't over-specify it. |
| None of the four branches share setup, so risk of one test's mock leaking into another is near-zero given `MakeSut()`-per-test isolation | Low | Already mitigated by the chosen `MakeSut()` pattern (fresh mocks every `[Fact]`, no shared mutable state) — no action needed beyond following the template. |
| Coverage tool (17.9% → target 60%+) may still flag the file if the `RunHydrationTierRequestValidator` is bundled into the same coverage unit and remains untested | Low | Out of scope per spec — flag if the coverage-gap routine re-triggers on the validator separately; not this task's problem. |

## Specification Amendments

1. **FR-5 / FR-6, logger verification is not optional — remove the escape hatch.** The spec's language "if the codebase has no established logger-verification pattern to follow" is factually incorrect; `GetPackageLabelPdfHandlerTests.cs` establishes exactly this pattern (`Mock<ILogger<T>>` + local `Log(...)` verification helper). Amend FR-5 to require `VerifyLogged(LogLevel.Error, Times.Once())` (or an equivalently named local helper) as a hard acceptance criterion, and amend FR-6 to specify `Mock<ILogger<RunHydrationTierHandler>>` instead of `NullLogger<RunHydrationTierHandler>.Instance` as the standard test double for all five tests (not just the exception-path one), for consistency within the file.
2. **Optional addition, not required:** consider also asserting `VerifyLogged(LogLevel.Information, Times.Once())` on the success-path test (FR-3), since `LogInformation` at handler line 28 is likewise currently unexercised and is cheap to cover once the logger mock exists for other reasons. Not adding this is not a defect — call it out as a nice-to-have only if it doesn't complicate the "successful hydration" test's focus.

No other amendments — FR-1 through FR-4, FR-6 (constructor/interface shapes), and all NFRs were verified accurate against the actual handler, request/response, interface, and config-class source.

## Prerequisites

None. No other in-flight work touches `RunHydrationTierHandler.cs`, its request/response types, or `IBackgroundRefreshTaskRegistry`. The task can start immediately.
