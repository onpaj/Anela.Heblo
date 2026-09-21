# Task Plan: Move `IDepartmentClient`/`Department` from Analytics to UserManagement Domain

Source specs: `artifacts/feat-4218/spec.r1.md`, `artifacts/feat-4218/arch-review.r1.md`, `artifacts/feat-4218/design.r1.md`.

This is a same-behavior namespace relocation. Two domain files move from `Anela.Heblo.Domain.Features.Analytics` to a new `Anela.Heblo.Domain.Features.UserManagement` namespace/folder; five consumer files (3 production, 1 test, plus a DI registration `using`) are updated to match. No logic, DTO, or API shape changes.

All commands below assume the current working directory is the worktree root: `/home/user/worktrees/feature-4218-Arch-Review-Usermanagement-Domain-Features-Analyti`.

---

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

### task: update-test-references-for-department-move

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs` (line 2)
- Verify only (no change): `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/DepartmentSyncServiceTests.cs`

- [ ] **Step 1: Build the Flexi test project to confirm it's currently broken (red state)**

  ```bash
  dotnet build backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj
  ```

  Expected: `Build FAILED.` — `FlexiDepartmentQueryServiceTests.cs` still references `Anela.Heblo.Domain.Features.Analytics`, which no longer contains `IDepartmentClient`/`Department` (they moved in the previous task). The error should be CS0246 on `IDepartmentClient` and `Department` in that file only.

- [ ] **Step 2: Update the `using` in `FlexiDepartmentQueryServiceTests.cs`**

  In `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs`, change line 2 only:

  Before:
  ```csharp
  using Anela.Heblo.Adapters.Flexi.Accounting.Departments;
  using Anela.Heblo.Domain.Features.Analytics;
  using FluentAssertions;
  using Moq;
  using Xunit;
  ```

  After:
  ```csharp
  using Anela.Heblo.Adapters.Flexi.Accounting.Departments;
  using Anela.Heblo.Domain.Features.UserManagement;
  using FluentAssertions;
  using Moq;
  using Xunit;
  ```

  No other line changes — `Mock<IDepartmentClient>`, `new List<Department> { ... }`, and all assertions resolve against the relocated types unchanged.

- [ ] **Step 3: Confirm `DepartmentSyncServiceTests.cs` needs no change**

  ```bash
  grep -n "Domain.Features.Analytics\|IDepartmentClient\|^using.*Department" backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/DepartmentSyncServiceTests.cs
  ```

  Expected output shows only:
  ```
  8:using Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments;
  ```
  (plus `Persistence.Analytics`/`Persistence.Analytics.Entities` usings already in the file). This confirms the file uses the FlexiBee SDK's own `IDepartmentClient` and `Persistence.Analytics.Entities.Department` — a distinct type from the one moved — and is correctly left untouched, per spec FR-6.

- [ ] **Step 4: Build the Flexi test project and confirm it's green**

  ```bash
  dotnet build backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj
  ```

  Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 5: Run the Flexi adapter test project and confirm all tests pass**

  ```bash
  dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj
  ```

  Expected: all tests pass, including `FlexiDepartmentQueryServiceTests.GetDepartmentsAsync_MapsDomainDepartmentsToDtos`, `FlexiDepartmentQueryServiceTests.GetDepartmentsAsync_WhenClientReturnsNoDepartments_ReturnsEmptyList`, and every test in `DepartmentSyncServiceTests` (unaffected, still green). Look for a summary line like `Passed!  - Failed: 0, Passed: N, Skipped: 0`.

- [ ] **Step 6: Commit**

  ```bash
  git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs

  git commit -m "$(cat <<'EOF'
  test(4218): update FlexiDepartmentQueryServiceTests for UserManagement namespace

  Follows the IDepartmentClient/Department relocation to
  Domain.Features.UserManagement. DepartmentSyncServiceTests is unaffected —
  it exercises the unrelated FlexiBee-SDK IDepartmentClient and
  Persistence.Analytics.Entities.Department.

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01TrZ96qqxbvqZSyD152WHjE
  EOF
  )"
  ```

---

### task: verify-full-solution-and-module-boundaries

**Files:** none modified — verification only.

- [ ] **Step 1: Repo-wide sweep for stray old-namespace references (spec FR-7)**

  ```bash
  grep -rn "Domain.Features.Analytics.IDepartmentClient\|Domain\.Features\.Analytics\.Department\b" backend/
  ```

  Expected output: no matches (empty output, exit code 1 from `grep`). If anything matches, it is a missed reference — go back and update it before continuing (this would indicate a gap versus spec FR-7's exhaustive-sweep claim).

- [ ] **Step 2: Confirm the old `Analytics` domain files are gone and the new `UserManagement` domain files exist**

  ```bash
  ls backend/src/Anela.Heblo.Domain/Features/Analytics/ | grep -i department
  ls backend/src/Anela.Heblo.Domain/Features/UserManagement/
  ```

  Expected: the first command prints nothing (no `Department.cs`/`IDepartmentClient.cs` left under `Analytics`); the second prints exactly:
  ```
  Department.cs
  IDepartmentClient.cs
  ```

- [ ] **Step 3: Full solution build**

  ```bash
  dotnet build Anela.Heblo.sln
  ```

  Expected: `Build succeeded.` with 0 errors across all projects (Domain, Adapters.Flexi, API, and all test projects).

- [ ] **Step 4: Format check**

  ```bash
  dotnet format --verify-no-changes
  ```

  Expected: exits 0 with no reported formatting violations. If it reports violations in any of the 7 touched files, run `dotnet format` (no `--verify-no-changes`) to apply fixes, review the diff to confirm it only touches formatting (whitespace/using order), then `git add` and amend the relevant commit from an earlier task only if that task's commit hasn't been pushed yet — otherwise add a small follow-up commit.

- [ ] **Step 5: Run the architecture module-boundary fitness tests (NFR-3)**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"
  ```

  Expected: all `ModuleBoundariesTests` cases pass, including `Consumer_types_should_not_reference_provider_owned_namespaces` for the `"Authorization -> UserManagement"` rule and `Domain_must_not_reference_Application_and_relocated_invoice_types_must_be_gone`. This confirms populating `Domain.Features.UserManagement` did not trip any existing boundary rule (that rule only forbids `Application.Features.Authorization` from referencing the namespace, which nothing in this change does).

- [ ] **Step 6: Run the full backend test suite**

  ```bash
  dotnet test Anela.Heblo.sln
  ```

  Expected: all tests pass, summary line shows `Failed: 0`. This is the final confirmation that the relocation introduced no regressions anywhere in the solution.

  This task makes no code changes if all checks pass — nothing to commit. If Step 4 required a formatting fix, commit that fix now:

  ```bash
  git add -A
  git commit -m "$(cat <<'EOF'
  chore(4218): apply dotnet format after department namespace move

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01TrZ96qqxbvqZSyD152WHjE
  EOF
  )"
  ```
