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

