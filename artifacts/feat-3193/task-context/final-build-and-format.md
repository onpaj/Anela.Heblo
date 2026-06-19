### task: final-build-and-format

Verify the complete solution builds and passes dotnet format before opening the PR.

**Files:** (no new files)

- [ ] **Step 1:** Run the full solution build.

  ```bash
  cd /home/user/worktrees/feature-3193-socket-exception-polly/backend
  dotnet build --no-restore -v q
  ```

  Expected: `Build succeeded.` 0 errors, 0 warnings (or only pre-existing warnings).

- [ ] **Step 2:** Run dotnet format to check for style violations.

  ```bash
  cd /home/user/worktrees/feature-3193-socket-exception-polly/backend
  dotnet format --verify-no-changes --verbosity diagnostic
  ```

  If format reports diffs, run `dotnet format` without `--verify-no-changes` to apply them, then re-run with it to confirm clean.

- [ ] **Step 3:** Run the unit test suite for all touched test projects.

  ```bash
  cd /home/user/worktrees/feature-3193-socket-exception-polly/backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v n
  dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj -v n
  ```

  Expected: all tests pass.

- [ ] **Step 4:** Commit any format-only changes (if any).

  ```bash
  git add -u
  git commit -m "style: apply dotnet format"
  ```

  Skip this step if Step 2 produced no changes.