### task: add-automatic-retry-attribute

Stop Hangfire from retrying `ProductPairingDqtJob` when it fails — Polly resilience inside the job already handles retry/backoff, so double-retrying amplifies the noise.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs`

- [ ] **Step 1:** Open the file. Note the current `ExecuteAsync` signature at line 42 — it has no attributes above it.

- [ ] **Step 2:** Add `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` to `ExecuteAsync`. Add the using directive for `Hangfire` at the top.

  The top of the file after edit (usings block):
  ```csharp
  using Anela.Heblo.Application.Features.DataQuality.Services;
  using Anela.Heblo.Domain.Features.BackgroundJobs;
  using Anela.Heblo.Domain.Features.DataQuality;
  using Hangfire;
  using Microsoft.Extensions.Logging;
  ```

  The method signature after edit:
  ```csharp
  [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
  public async Task ExecuteAsync(CancellationToken cancellationToken = default)
  ```

- [ ] **Step 3:** Verify the build compiles cleanly.

  ```bash
  cd /home/user/worktrees/feature-3193-socket-exception-polly/backend
  dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore -v q
  ```

  Expected: `Build succeeded.` with 0 errors.

  > **Note:** If you get `The type or namespace name 'Hangfire' could not be found`, check that the Application .csproj already references Hangfire. If not, the attribute must be placed differently — see the note below.

  > **Hangfire reference check:** Run `grep -r "Hangfire" /home/user/worktrees/feature-3193-socket-exception-polly/backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`. If there is no Hangfire reference, the `[AutomaticRetry]` attribute cannot live in the Application project. In that case, keep the attribute on the Hangfire job registration instead: in `HangfireJobRegistrationHelper.cs`, add `GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail })` as a type-specific filter, OR register via `app.UseHangfireDashboard` context. Confirm which approach the codebase uses and apply accordingly. Most likely Hangfire is already a transitive reference — the build will tell you.

- [ ] **Step 4:** Commit.

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs
  git commit -m "fix: disable Hangfire auto-retry on ProductPairingDqtJob (Polly handles retries)"
  ```

---