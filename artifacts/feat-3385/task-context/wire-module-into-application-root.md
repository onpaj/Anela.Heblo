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