### task: move-dtos-to-new-module

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskStatusDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskExecutionLogDto.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs`

**Goal:** Move the three DTOs from the `BackgroundJobs.Contracts` namespace to `BackgroundRefresh.Contracts`, updating only the namespace declaration.

**Steps:**
- [ ] Step 1: Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskDto.cs`:
  ```csharp
  namespace Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

  public class RefreshTaskDto
  {
      public required string TaskId { get; init; }
      public required TimeSpan InitialDelay { get; init; }
      public required TimeSpan RefreshInterval { get; init; }
      public required bool Enabled { get; init; }
      public int HydrationTier { get; init; }
      public DateTime? NextScheduledRun { get; init; }
      public RefreshTaskExecutionLogDto? LastExecution { get; init; }
  }
  ```

- [ ] Step 2: Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskStatusDto.cs`:
  ```csharp
  namespace Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

  public class RefreshTaskStatusDto
  {
      public required string TaskId { get; init; }
      public required bool Enabled { get; init; }
      public string? Description { get; init; }
      public required TimeSpan RefreshInterval { get; init; }
      public RefreshTaskExecutionLogDto? LastExecution { get; init; }
  }
  ```

- [ ] Step 3: Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskExecutionLogDto.cs`:
  ```csharp
  namespace Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

  public class RefreshTaskExecutionLogDto
  {
      public required string TaskId { get; init; }
      public required DateTime StartedAt { get; init; }
      public DateTime? CompletedAt { get; init; }
      public required string Status { get; init; }
      public string? ErrorMessage { get; init; }
      public TimeSpan? Duration { get; init; }
      public Dictionary<string, object>? Metadata { get; init; }
  }
  ```

- [ ] Step 4: Delete the three source files from the old location:
  ```bash
  rm backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs
  rm backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs
  rm backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs
  ```

**Acceptance criteria:**
- Three new files exist under `BackgroundRefresh/Contracts/` with namespace `Anela.Heblo.Application.Features.BackgroundRefresh.Contracts`.
- Three old files under `BackgroundJobs/Contracts/` are deleted.
- DTO field/property names and types are identical to the originals.

---