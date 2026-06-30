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