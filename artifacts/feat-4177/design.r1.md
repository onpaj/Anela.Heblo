# Design: GetTaskStatusHandler coverage-gap tests

## Component Design

No new or changed production components. This is a backend-only, test-only change (per `arch-review.r1.md`: `Skip Design: true` — no user-facing UI component exists for this handler).

The only component introduced is a test class:

- **`GetTaskStatusHandlerTests`** (`backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs`)
  - **Responsibility:** Exercise all three branches of `GetTaskStatusHandler.Handle` (not-found, found-with-null-last-execution, found-with-populated-last-execution) against a mocked `IBackgroundRefreshTaskRegistry`.
  - **Interface:** Standard xUnit `[Fact]` test class; no public interface beyond the test methods themselves. Uses `Mock<IBackgroundRefreshTaskRegistry>` (Moq) and `FluentAssertions` for assertions, matching the sibling `GetBackgroundRefreshTasksHandlerTests` in the same directory.
  - **Dependencies exercised:** `GetTaskStatusHandler` (system under test), `IBackgroundRefreshTaskRegistry` (mocked).

## Data Schemas

No schema changes. The tests construct and assert against existing shapes only:

**Request:**
```csharp
GetTaskStatusRequest { string TaskId }
```

**Response:**
```csharp
GetTaskStatusResponse : BaseResponse
{
    bool Found;
    RefreshTaskStatusDto? Status;
}
```

**Status DTO** (populated when `Found == true`):
```csharp
RefreshTaskStatusDto
{
    string TaskId;
    bool Enabled;
    TimeSpan RefreshInterval;
    RefreshTaskExecutionLogDto? LastExecution;   // null when the task has never executed
}
```

**Execution log DTO** (populated when a last execution exists):
```csharp
RefreshTaskExecutionLogDto
{
    string TaskId;
    DateTime StartedAt;
    DateTime? CompletedAt;
    string Status;        // mapped via RefreshTaskExecutionStatus.ToString()
    string? ErrorMessage;
    TimeSpan? Duration;
    IDictionary<string, object>? Metadata;
}
```

**Registry-side test fixtures** (mocked, not persisted anywhere — inputs to `Mock<IBackgroundRefreshTaskRegistry>` setups):
```csharp
RefreshTaskConfiguration { string TaskId; TimeSpan InitialDelay; TimeSpan RefreshInterval; bool Enabled; int HydrationTier; }
RefreshTaskExecutionLog  { string TaskId; DateTime StartedAt; DateTime? CompletedAt; RefreshTaskExecutionStatus Status; string? ErrorMessage; TimeSpan? Duration; IDictionary<string, object>? Metadata; }
```

No database, event, or external API schemas are involved — `IBackgroundRefreshTaskRegistry` is an in-process registry mocked entirely in-memory for these tests.
