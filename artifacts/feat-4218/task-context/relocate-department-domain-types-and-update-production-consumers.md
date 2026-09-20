### task: relocate-department-domain-types-and-update-production-consumers

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/UserManagement/Department.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/UserManagement/IDepartmentClient.cs`
- Delete: `backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs`
- Delete: `backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs` (lines 1, 7)
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs` (line 3)
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` (line 30)

- [ ] **Step 1: Create the new `UserManagement` domain folder with `Department.cs`**

  Create `backend/src/Anela.Heblo.Domain/Features/UserManagement/Department.cs` with this exact content (identical shape to the original, namespace changed):

  ```csharp
  namespace Anela.Heblo.Domain.Features.UserManagement;

  public class Department
  {
      public string Id { get; set; } = string.Empty;
      public string Name { get; set; } = string.Empty;
  }
  ```

- [ ] **Step 2: Create `IDepartmentClient.cs` in the new folder**

  Create `backend/src/Anela.Heblo.Domain/Features/UserManagement/IDepartmentClient.cs` with this exact content:

  ```csharp
  namespace Anela.Heblo.Domain.Features.UserManagement;

  public interface IDepartmentClient
  {
      Task<IEnumerable<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
      Task<Department?> GetDepartmentByIdAsync(string departmentId, CancellationToken cancellationToken = default);
  }
  ```

- [ ] **Step 3: Delete the two old files under `Analytics`**

  ```bash
  git rm backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs
  git rm backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs
  ```

  Expected output: two lines like `rm 'backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs'`.

- [ ] **Step 4: Build the Domain project alone to confirm it's still green**

  ```bash
  dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
  ```

  Expected: `Build succeeded.` — the Domain project has no internal consumers of the old namespace, so it compiles fine on its own.

- [ ] **Step 5: Build the Flexi adapter project to confirm it now fails (red state)**

  ```bash
  dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
  ```

  Expected: `Build FAILED.` with CS0246-style "type or namespace name 'IDepartmentClient'/'Department' could not be found" errors pointing at `FlexiDepartmentClient.cs`, `FlexiDepartmentQueryService.cs`, and `FlexiAdapterServiceCollectionExtensions.cs`. This confirms the move actually took effect and the three production consumers still need updating — proceed to fix them.

- [ ] **Step 6: Update `FlexiDepartmentClient.cs`**

  In `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs`, change line 1 and line 7 only. Do **not** touch line 3 (the FlexiBee SDK alias) — it disambiguates a different, unrelated `IDepartmentClient` and must stay exactly as-is.

  Before:
  ```csharp
  using Anela.Heblo.Domain.Features.Analytics;
  using Microsoft.Extensions.Caching.Memory;
  using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;

  namespace Anela.Heblo.Adapters.Flexi.Accounting.Departments;

  public class FlexiDepartmentClient : Domain.Features.Analytics.IDepartmentClient
  ```

  After:
  ```csharp
  using Anela.Heblo.Domain.Features.UserManagement;
  using Microsoft.Extensions.Caching.Memory;
  using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;

  namespace Anela.Heblo.Adapters.Flexi.Accounting.Departments;

  public class FlexiDepartmentClient : Domain.Features.UserManagement.IDepartmentClient
  ```

  The rest of the file (constructor, `GetDepartmentsAsync`, `GetDepartmentByIdAsync`, caching logic) is unchanged.

- [ ] **Step 7: Update `FlexiDepartmentQueryService.cs`**

  In `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs`, change line 3 only:

  Before:
  ```csharp
  using Anela.Heblo.Application.Features.UserManagement.Contracts;
  using Anela.Heblo.Application.Features.UserManagement.Services;
  using Anela.Heblo.Domain.Features.Analytics;
  ```

  After:
  ```csharp
  using Anela.Heblo.Application.Features.UserManagement.Contracts;
  using Anela.Heblo.Application.Features.UserManagement.Services;
  using Anela.Heblo.Domain.Features.UserManagement;
  ```

  No other line in the file changes — the constructor and the `Department` → `DepartmentDto` mapping LINQ are untouched.

- [ ] **Step 8: Update the DI registration's `using` in `FlexiAdapterServiceCollectionExtensions.cs`**

  In `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`, change line 30 only:

  Before:
  ```csharp
  using Anela.Heblo.Domain.Features.Analytics;
  ```

  After:
  ```csharp
  using Anela.Heblo.Domain.Features.UserManagement;
  ```

  Do not reorder or touch any other `using` line, and do not touch `services.AddScoped<IDepartmentClient, FlexiDepartmentClient>();` (line 91) — it resolves correctly through the updated `using`. `Anela.Heblo.Adapters.Flexi.Analytics` (line 4, for `DepartmentSyncService` etc.) and `Anela.Heblo.Persistence.Analytics` (line 35, for `AddAnalyticsPersistenceServices`) are separate `using` statements for unrelated types and stay exactly as they are.

- [ ] **Step 9: Rebuild the Flexi adapter project and confirm it's green**

  ```bash
  dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
  ```

  Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 10: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Domain/Features/UserManagement/Department.cs \
          backend/src/Anela.Heblo.Domain/Features/UserManagement/IDepartmentClient.cs \
          backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs \
          backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs \
          backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs

  git commit -m "$(cat <<'EOF'
  refactor(4218): move IDepartmentClient/Department to UserManagement domain

  Department and IDepartmentClient had zero real consumers under
  Domain.Features.Analytics; their only consumer (FlexiDepartmentQueryService)
  serves UserManagement's department-lookup flow. Relocate both types and
  update the three production references. DepartmentSyncService and
  Persistence.Analytics.Entities.Department are untouched (distinct types).

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01TrZ96qqxbvqZSyD152WHjE
  EOF
  )"
  ```

  Note: the `git rm` from Step 3 already staged the deletions; `git add` above stages the two new files and the three edited files in the same commit.

---

