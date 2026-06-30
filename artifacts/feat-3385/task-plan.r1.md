# Move BackgroundRefresh DTOs out of BackgroundJobs Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move three response DTOs (`RefreshTaskDto`, `RefreshTaskStatusDto`, `RefreshTaskExecutionLogDto`) from the `BackgroundJobs` module into a new `BackgroundRefresh` Application module with updated namespaces and a module registration class.

**Architecture:** A new `Application/Features/BackgroundRefresh/` module is created with a `BackgroundRefreshModule.cs` registration class and a `Contracts/` subfolder. The three DTOs are moved verbatim with only their namespace declaration changed. `ApplicationModule.cs` gains a call to `AddBackgroundRefreshModule()`, and `BackgroundRefreshController.cs` updates its using directive to the new namespace.

**Tech Stack:** .NET 8, C#, MediatR (no handlers added here), OpenAPI TypeScript client generation via `npm run build`.

---

### task: create-background-refresh-module

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/BackgroundRefreshModule.cs`

**Goal:** Create the new Application module folder and registration class so `ApplicationModule.cs` can call `AddBackgroundRefreshModule()`.

**Steps:**
- [ ] Step 1: Create `BackgroundRefreshModule.cs` at `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/BackgroundRefreshModule.cs` with the following content:
  ```csharp
  using Microsoft.Extensions.DependencyInjection;

  namespace Anela.Heblo.Application.Features.BackgroundRefresh;

  public static class BackgroundRefreshModule
  {
      public static IServiceCollection AddBackgroundRefreshModule(this IServiceCollection services)
      {
          // No Application-layer services to register yet.
          // BackgroundRefreshController wires directly to IBackgroundRefreshTaskRegistry (Xcc).
          // MediatR handlers will be added here when the HTTP surface is migrated to CQRS.
          return services;
      }
  }
  ```

**Acceptance criteria:**
- File exists at the path above.
- Namespace is `Anela.Heblo.Application.Features.BackgroundRefresh`.
- Class is `public static` with a single `AddBackgroundRefreshModule` extension method returning `IServiceCollection`.

---

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

### task: wire-module-into-application-root

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

**Goal:** Register the new `BackgroundRefreshModule` in the application composition root alongside the other module calls.

**Steps:**
- [ ] Step 1: Add the using directive for the new module. In `ApplicationModule.cs`, after line 10 (`using Anela.Heblo.Application.Features.BackgroundJobs;`), insert:
  ```csharp
  using Anela.Heblo.Application.Features.BackgroundRefresh;
  ```

- [ ] Step 2: Add the module registration call. In `ApplicationModule.cs`, after line 79 (`services.AddBackgroundJobsModule();`), insert:
  ```csharp
  services.AddBackgroundRefreshModule();
  ```

  The relevant section of `ApplicationModule.cs` after the change should read:
  ```csharp
  using Anela.Heblo.Application.Features.BackgroundJobs;
  using Anela.Heblo.Application.Features.BackgroundRefresh;
  using Anela.Heblo.Application.Features.Bank;
  // ... (remaining usings unchanged)
  ```

  And in the method body:
  ```csharp
  services.AddBackgroundJobsModule();
  services.AddBackgroundRefreshModule();
  services.AddBankModule(configuration);
  ```

**Acceptance criteria:**
- `ApplicationModule.cs` has `using Anela.Heblo.Application.Features.BackgroundRefresh;` in the using block.
- `ApplicationModule.cs` calls `services.AddBackgroundRefreshModule();` in `AddApplicationServices`.

---

### task: update-controller-namespace

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs`

**Goal:** Replace the old `BackgroundJobs.Contracts` using directive with the new `BackgroundRefresh.Contracts` one so the controller resolves the DTOs from the new location.

**Steps:**
- [ ] Step 1: In `BackgroundRefreshController.cs`, replace line 1:
  ```csharp
  using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
  ```
  with:
  ```csharp
  using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
  ```

  The top of the file after the change:
  ```csharp
  using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
  using Anela.Heblo.Domain.Features.Authorization;
  using Anela.Heblo.Xcc.Services.BackgroundRefresh;
  using Microsoft.AspNetCore.Mvc;
  ```

**Acceptance criteria:**
- `BackgroundRefreshController.cs` line 1 references `BackgroundRefresh.Contracts`, not `BackgroundJobs.Contracts`.
- No other lines in the controller are changed.

---

### task: verify-build-and-regenerate-client

**Files:**
- No files modified — verification only.

**Goal:** Confirm the backend compiles cleanly, no stray references to the old namespace remain, and the TypeScript OpenAPI client regenerates without errors.

**Steps:**
- [ ] Step 1: Build the backend from the repo root:
  ```bash
  dotnet build backend/Anela.Heblo.sln
  ```
  Expected: `Build succeeded` with 0 errors and 0 warnings related to these changes.

- [ ] Step 2: Verify no file in `backend/` references the old namespace for these DTOs:
  ```bash
  grep -r "BackgroundJobs\.Contracts\.RefreshTask" backend/
  ```
  Expected: no output (zero matches).

- [ ] Step 3: Run `dotnet format` to confirm formatting is clean:
  ```bash
  dotnet format backend/Anela.Heblo.sln --verify-no-changes
  ```
  Expected: exits with code 0.

- [ ] Step 4: Build the frontend to regenerate the TypeScript OpenAPI client and confirm no TypeScript errors:
  ```bash
  cd frontend && npm run build
  ```
  Expected: build completes without TypeScript errors.

- [ ] Step 5: Run the frontend linter:
  ```bash
  cd frontend && npm run lint
  ```
  Expected: exits with code 0.

**Acceptance criteria:**
- `dotnet build` exits with 0 errors.
- `grep` for old namespace returns no matches.
- `npm run build` exits with 0 errors.
- `npm run lint` exits with 0 errors.
