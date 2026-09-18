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
