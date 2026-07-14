# Design: Unit tests for RunHydrationTierHandler

## Component Design

**New file:** `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/RunHydrationTierHandlerTests.cs`
**Namespace:** `Anela.Heblo.Tests.Application.BackgroundRefresh`

No production components change. The only "component" introduced is the test class itself and its local test-scoped collaborators.

### `RunHydrationTierHandlerTests`

- **Responsibility:** exercise all four response paths of `RunHydrationTierHandler.Handle(...)` — no enabled tasks in tier, successful hydration, cancellation (both thrown and pre-cancelled-token variants), unexpected exception.
- **Structure:** follows the `MakeSut()` tuple-factory convention from `GetOrderTrackingNumberHandlerTests` — a private static `MakeSut()` builds and returns the handler under test plus its mocks, called fresh at the top of every `[Fact]` (no shared mutable state, per NFR-3).

```csharp
private static (RunHydrationTierHandler Sut, Mock<IBackgroundRefreshTaskRegistry> Registry, Mock<ILogger<RunHydrationTierHandler>> Logger) MakeSut()
{
    var registry = new Mock<IBackgroundRefreshTaskRegistry>();
    var logger = new Mock<ILogger<RunHydrationTierHandler>>();
    var sut = new RunHydrationTierHandler(registry.Object, logger.Object);
    return (sut, registry, logger);
}
```

- **Mocked collaborator:** `IBackgroundRefreshTaskRegistry` (Moq) — `GetRegisteredTasks()` seeded per test with `RefreshTaskConfiguration` lists; `ForceRefreshAsync(taskId, cancellationToken)` configured per test to complete, throw `OperationCanceledException`, or throw a generic `Exception`.
- **Mocked collaborator:** `Mock<ILogger<RunHydrationTierHandler>>` (per arch-review Decision 1, overriding the spec's `NullLogger` default) — paired with a local `VerifyLogged(LogLevel, Times)` helper copied from `GetPackageLabelPdfHandlerTests`:

```csharp
private static void VerifyLogged(Mock<ILogger<RunHydrationTierHandler>> logger, LogLevel level, Times times) =>
    logger.Verify(
        l => l.Log(
            level,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
        times);
```

- **Not mocked:** `RefreshTaskConfiguration` — built directly via object initializer (plain class, `required` init props), never mocked.

### Test cases (one `[Fact]` per path, plus sub-cases)

| Test | Registry setup | `ForceRefreshAsync` behavior | Key assertions |
|---|---|---|---|
| `Handle_ReturnsNotFound_WhenNoEnabledTasksInTier` | empty list, or tier-matching tasks all `Enabled = false` | never called | `NotFound == true`, `ErrorMessage` contains tier number, `TaskCount == 0`, `Cancelled == false`, `ForceRefreshAsync` `Times.Never` |
| `Handle_ReturnsTaskCount_WhenAllTasksCompleteSuccessfully` | 2 enabled tasks in requested tier + 1 enabled task in a different tier | completes normally | `TaskCount == 2`, `NotFound/Cancelled == false`, `Success == true`, `ForceRefreshAsync` called once per in-tier `TaskId` (`Times.Exactly`/per-ID `Verify`), never called for the other-tier task, optionally `VerifyLogged(Information, Once)` |
| `Handle_ReturnsCancelled_WhenOperationCanceledExceptionThrown` | ≥1 enabled task in tier | throws `OperationCanceledException` | `Cancelled == true`, `Success == true`, no exception propagates from `await sut.Handle(...)` |
| `Handle_ReturnsCancelled_WhenTokenAlreadyCancelled` | ≥2 enabled tasks in tier | n/a — real, pre-cancelled `CancellationTokenSource` passed in | `Cancelled == true`, `ForceRefreshAsync` `Times.Never` |
| `Handle_ReturnsFailure_WhenForceRefreshThrowsUnexpectedException` | ≥1 enabled task in tier | throws `InvalidOperationException("boom")` | `Success == false`, `ErrorMessage == "An unexpected error occurred during tier hydration"` (exact match), `Cancelled == false`, `NotFound == false`, `VerifyLogged(Error, Once)` |

All tests are synchronous-equivalent, in-memory, and order-independent (NFR-1/NFR-3) — no `Task.Delay`, no real timers.

## Data Schemas

N/A — no schema, DTO, or contract changes. Test-time object shapes only (existing types, unchanged):

- `RefreshTaskConfiguration` (plain class, `required` init props): `TaskId` (string), `InitialDelay` (TimeSpan, may be `TimeSpan.Zero` in tests), `RefreshInterval` (TimeSpan, may be `TimeSpan.Zero` in tests), `Enabled` (bool), `HydrationTier` (int).
- `RunHydrationTierResponse : BaseResponse` — `Success` (bool, default `true`), `NotFound` (bool), `Cancelled` (bool), `ErrorMessage` (string?), `TaskCount` (int).
