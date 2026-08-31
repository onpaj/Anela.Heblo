# Design: Unit test coverage for GetBackgroundRefreshTasksHandler.MapToDto

## Component Design

This is a test-only, backend-only change (arch-review Skip Design: true — no user-facing UI component). One new component is added, no existing components change:

- **`GetBackgroundRefreshTasksHandlerTests`** (new test class, `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetBackgroundRefreshTasksHandlerTests.cs`) — responsibility: exercise `GetBackgroundRefreshTasksHandler.Handle` end-to-end against a mocked `IBackgroundRefreshTaskRegistry` to cover the `MapToDto` branch matrix (the `NextScheduledRun` compound condition and the `LastExecution` null check) identified in the coverage-gap issue. It has no production dependents — it is a leaf test component consuming the handler's existing public contract (`Handle(GetBackgroundRefreshTasksRequest, CancellationToken) -> Task<GetBackgroundRefreshTasksResponse>`) and the existing `IBackgroundRefreshTaskRegistry` interface via `Moq`.

No changes to `GetBackgroundRefreshTasksHandler`, its DTOs, or `IBackgroundRefreshTaskRegistry`.

## Data Schemas

No schema changes. The test class constructs and asserts against existing types only, per the interfaces already defined in `arch-review.r1.md` (`Interfaces and Contracts` section):

- **Inputs the test constructs:** `RefreshTaskConfiguration` (`TaskId`, `InitialDelay`, `RefreshInterval`, `Enabled`, `HydrationTier`), `RefreshTaskExecutionLog` (`TaskId`, `StartedAt`, `CompletedAt`, `Status : RefreshTaskExecutionStatus`, `ErrorMessage`, `Metadata`).
- **Output the test asserts against:** `GetBackgroundRefreshTasksResponse.Tasks : IReadOnlyList<RefreshTaskDto>`, each `RefreshTaskDto` carrying `TaskId`, `InitialDelay`, `RefreshInterval`, `Enabled`, `HydrationTier`, `NextScheduledRun : DateTime?`, `LastExecution : RefreshTaskExecutionLogDto?` (itself `TaskId`, `StartedAt`, `CompletedAt`, `Status : string`, `ErrorMessage`, `Duration`, `Metadata`).

No new request/response shapes, no event payloads, no database schema — all types pre-exist and are used exactly as declared in `spec.r1.md`'s Data Model section.
