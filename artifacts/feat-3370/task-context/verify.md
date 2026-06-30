### task: verify

**Files:** (none — verification only)

- [ ] **Step 1: Full build.**

  ```bash
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```

  Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 2: Authorization tests.**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Authorization"
  ```

  Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`.

- [ ] **Step 3: Confirm module boundary is clean.**

  ```bash
  grep -r "UserManagement.Services" backend/src/Anela.Heblo.Application/Features/Authorization/ || echo "CLEAN"
  ```

  Expected output: `CLEAN` (no matches — the violation is gone).
