# Plaud Circuit Breaker and Monitoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop Hangfire from retrying Plaud auth failures 10× per tick, surface a typed exception, and write the deferred auto-refresh design doc.

**Architecture:** Add `[AutomaticRetry(Attempts = 0)]` to `PlaudPollingJob.ExecuteAsync`, introduce `PlaudAuthExpiredException` thrown when the CLI exits with `AUTH_FAILED` in stderr (replacing the generic `InvalidOperationException`), and capture the full stderr in the log and exception message. No new abstractions or DI registrations needed — the exception lives in the adapter; the job change is a one-line attribute.

**Tech Stack:** .NET 8, xUnit 2.9, FluentAssertions 6, Hangfire (attribute-only change), App Insights Kusto for post-deploy verification.

---

## File Map

| File | Action |
|------|--------|
| `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAuthExpiredException.cs` | **Create** — typed exception for auth failures |
| `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs` | **Modify** lines 114-118 — surface stderr and throw typed exception |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs` | **Modify** line 41 — add `[AutomaticRetry(Attempts = 0)]` |
| `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientAuthTests.cs` | **Create** — tests for auth error path |
| `docs/integrations/plaud-token-auto-refresh.md` | **Create** — deferred auto-refresh design doc |

---

## Task 1: Create `PlaudAuthExpiredException`

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAuthExpiredException.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientAuthTests.cs`

- [ ] **Step 1: Write the failing test**

  Create `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientAuthTests.cs`:

  ```csharp
  using FluentAssertions;

  namespace Anela.Heblo.Adapters.Plaud.Tests;

  public sealed class PlaudAuthExceptionTests
  {
      [Fact]
      public void PlaudAuthExpiredException_StoresStderrInMessage()
      {
          const string stderr = "[AUTH_FAILED] Token invalid or expired";

          var ex = new PlaudAuthExpiredException(stderr);

          ex.Message.Should().Contain(stderr);
          ex.Message.Should().Contain("Plaud__TokensJson");
      }
  }
  ```

- [ ] **Step 2: Run the test to confirm it fails**

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Adapters.Plaud.Tests \
    --filter "PlaudAuthExceptionTests" -v normal
  ```

  Expected: compilation error — `PlaudAuthExpiredException` does not exist.

- [ ] **Step 3: Create the exception class**

  Create `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAuthExpiredException.cs`:

  ```csharp
  namespace Anela.Heblo.Adapters.Plaud;

  public sealed class PlaudAuthExpiredException : Exception
  {
      public PlaudAuthExpiredException(string stderr)
          : base($"Plaud authentication expired. Run `plaud login` and update App Service setting `Plaud__TokensJson`. CLI stderr: {stderr}")
      { }
  }
  ```

- [ ] **Step 4: Run the test again to confirm it passes**

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Adapters.Plaud.Tests \
    --filter "PlaudAuthExceptionTests" -v normal
  ```

  Expected: 1 test passed.

- [ ] **Step 5: Confirm all existing parser tests still pass**

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Adapters.Plaud.Tests -v normal
  ```

  Expected: all tests passed (green).

- [ ] **Step 6: Commit**

  ```bash
  git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAuthExpiredException.cs \
          backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientAuthTests.cs
  git commit -m "feat(plaud): add PlaudAuthExpiredException for typed auth failures"
  ```

---

## Task 2: Surface stderr and throw typed exception in `PlaudCliClient`

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs:114-118`
- Test: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientAuthTests.cs`

The `RunCliAsync` method runs a real OS process. The cleanest test approach is to point `PlaudCliClient` at a small shell script (or `.cmd` on Windows) that exits non-zero with the auth error on stderr. This is an integration-style unit test — no mocking framework needed.

- [ ] **Step 1: Add the auth-failure CLI invocation test**

  Append to `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientAuthTests.cs`:

  ```csharp
  using Microsoft.Extensions.Logging.Abstractions;
  using Microsoft.Extensions.Options;

  // ---- add inside the namespace but as a separate class ----

  public sealed class PlaudCliClientRunTests
  {
      [SkippableFact]
      public async Task RunCli_WhenCliExitsWithAuthFailed_ThrowsPlaudAuthExpiredException()
      {
          // Skip on platforms that can't run shell scripts
          Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");

          // Arrange — write a tiny shim that mimics Plaud auth failure
          var shimPath = Path.Combine(Path.GetTempPath(), $"plaud_shim_{Guid.NewGuid():N}.sh");
          await File.WriteAllTextAsync(shimPath,
              "#!/bin/sh\necho '[AUTH_FAILED] Token invalid or expired' >&2\nexit 1\n");
          File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

          try
          {
              var options = Options.Create(new PlaudOptions
              {
                  CliExecutablePath = shimPath,
                  ProcessTimeoutSeconds = 10
              });
              var client = new PlaudCliClient(NullLogger<PlaudCliClient>.Instance, options);

              // Act
              Func<Task> act = () => client.ListRecentAsync(7);

              // Assert
              await act.Should()
                  .ThrowAsync<PlaudAuthExpiredException>()
                  .WithMessage("*AUTH_FAILED*");
          }
          finally
          {
              File.Delete(shimPath);
          }
      }

      [SkippableFact]
      public async Task RunCli_WhenCliExitsNonZeroWithoutAuthFailed_ThrowsInvalidOperationException()
      {
          Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");

          var shimPath = Path.Combine(Path.GetTempPath(), $"plaud_shim_{Guid.NewGuid():N}.sh");
          await File.WriteAllTextAsync(shimPath,
              "#!/bin/sh\necho 'some other error' >&2\nexit 1\n");
          File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

          try
          {
              var options = Options.Create(new PlaudOptions
              {
                  CliExecutablePath = shimPath,
                  ProcessTimeoutSeconds = 10
              });
              var client = new PlaudCliClient(NullLogger<PlaudCliClient>.Instance, options);

              Func<Task> act = () => client.ListRecentAsync(7);

              await act.Should()
                  .ThrowAsync<InvalidOperationException>()
                  .WithMessage("*some other error*");
          }
          finally
          {
              File.Delete(shimPath);
          }
      }
  }
  ```

- [ ] **Step 2: Add `SkippableFact` and `Microsoft.Extensions.Logging.Abstractions` NuGet references**

  Check if `Xunit.SkippableFact` is already in the project:

  ```bash
  grep -r "SkippableFact" backend/test/Anela.Heblo.Adapters.Plaud.Tests/
  ```

  If not found, add both packages:

  ```bash
  cd backend
  dotnet add test/Anela.Heblo.Adapters.Plaud.Tests \
    package Xunit.SkippableFact
  dotnet add test/Anela.Heblo.Adapters.Plaud.Tests \
    package Microsoft.Extensions.Logging.Abstractions
  ```

- [ ] **Step 3: Run the new tests to confirm they fail**

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Adapters.Plaud.Tests \
    --filter "PlaudCliClientRunTests" -v normal
  ```

  Expected: `PlaudCliClientRunTests.RunCli_WhenCliExitsWithAuthFailed_ThrowsPlaudAuthExpiredException` fails — the current code throws `InvalidOperationException`, not `PlaudAuthExpiredException`. The second test may pass or fail depending on whether the trimmed error is included in the message.

- [ ] **Step 4: Modify `PlaudCliClient.RunCliAsync` to surface stderr and throw the typed exception**

  In `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs`, replace lines 114-118:

  **Before:**
  ```csharp
  if (process.ExitCode != 0)
  {
      _logger.LogError("Plaud CLI exited with code {ExitCode}: {Error}", process.ExitCode, error);
      throw new InvalidOperationException($"Plaud CLI exited with code {process.ExitCode}");
  }
  ```

  **After:**
  ```csharp
  if (process.ExitCode != 0)
  {
      _logger.LogError("Plaud CLI exited with code {ExitCode}: {Error}", process.ExitCode, error);
      var trimmed = (error ?? string.Empty).Trim();
      if (trimmed.Contains("AUTH_FAILED", StringComparison.Ordinal))
      {
          throw new PlaudAuthExpiredException(trimmed);
      }
      throw new InvalidOperationException(
          $"Plaud CLI exited with code {process.ExitCode}: {trimmed}");
  }
  ```

- [ ] **Step 5: Run all Plaud adapter tests to confirm they all pass**

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Adapters.Plaud.Tests -v normal
  ```

  Expected: all tests pass (including the two new CLI shim tests on non-Windows).

- [ ] **Step 6: Commit**

  ```bash
  git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs \
          backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientAuthTests.cs \
          backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj
  git commit -m "fix(plaud): throw PlaudAuthExpiredException on AUTH_FAILED; include stderr in error message"
  ```

---

## Task 3: Add `[AutomaticRetry(Attempts = 0)]` to `PlaudPollingJob`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs`

`[AutomaticRetry]` is a Hangfire attribute processed by the Hangfire server at runtime — there is no practical way to unit-test it without a full Hangfire server. The verification is post-deploy via App Insights (see verification step at the end of this plan). No new test file is added for this task.

- [ ] **Step 1: Add the `Hangfire` using and the attribute**

  In `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs`:

  Add `using Hangfire;` to the existing using block at the top of the file, and add `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` immediately above the `ExecuteAsync` signature.

  **Before (lines 1-7 and line 41):**
  ```csharp
  using Anela.Heblo.Application.Features.MeetingTasks.Services;
  using Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;
  using Anela.Heblo.Domain.Features.BackgroundJobs;
  using MediatR;
  using Microsoft.Extensions.Logging;
  using Microsoft.Extensions.Options;

  // ...

      public async Task ExecuteAsync(CancellationToken cancellationToken = default)
  ```

  **After:**
  ```csharp
  using Anela.Heblo.Application.Features.MeetingTasks.Services;
  using Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;
  using Anela.Heblo.Domain.Features.BackgroundJobs;
  using Hangfire;
  using MediatR;
  using Microsoft.Extensions.Logging;
  using Microsoft.Extensions.Options;

  // ...

      [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
      public async Task ExecuteAsync(CancellationToken cancellationToken = default)
  ```

- [ ] **Step 2: Verify Hangfire package is already referenced**

  The Hangfire NuGet should already be referenced by the Application layer. Confirm it builds:

  ```bash
  cd backend
  dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```

  Expected: Build succeeded, 0 errors.

  If `Hangfire` types are not found, check which project has Hangfire and find the correct namespace. The attribute is in `Hangfire.Core` (`Hangfire` package).

- [ ] **Step 3: Run the full build to confirm nothing is broken**

  ```bash
  cd backend
  dotnet build
  ```

  Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run `dotnet format` to check style**

  ```bash
  cd backend
  dotnet format --verify-no-changes
  ```

  If it reports changes, apply them:

  ```bash
  cd backend
  dotnet format
  ```

- [ ] **Step 5: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs
  git commit -m "fix(plaud): disable Hangfire retries on PlaudPollingJob — fail immediately on first error"
  ```

---

## Task 4: Write deferred auto-refresh design doc

**Files:**
- Create: `docs/integrations/plaud-token-auto-refresh.md`

- [ ] **Step 1: Create the doc**

  Create `docs/integrations/plaud-token-auto-refresh.md` with the following content:

  ````markdown
  # Plaud Token Auto-Refresh (Deferred)

  > **Status:** Deferred — depends on Azure Key Vault infra for Heblo.
  > Pick up when `rgHeblo` has a Key Vault provisioned and the App Service Managed Identity is configured.

  ## Root Cause of the Bootstrapper-Overwrite Problem

  `PlaudTokenBootstrapper` (`backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenBootstrapper.cs`)
  writes `~/.plaud/tokens.json` from the App Service setting `Plaud__TokensJson` on every container start.

  The Plaud CLI auto-refreshes its tokens on every call, so continuous 5-minute polling normally keeps the
  refresh token alive indefinitely. However, a container restart re-seeds a potentially stale token from
  the App Service setting. If the stored `refresh_token` has aged past Plaud's hard TTL, every subsequent
  CLI call fails with `[AUTH_FAILED] Token invalid or expired`.

  **Short-term mitigation (implemented):** `PlaudPollingJob` now has
  `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]`, which prevents the
  10× retry flood and throws `PlaudAuthExpiredException` with actionable message. An Azure Monitor alert
  fires within 5 minutes of the first failure (see monitoring alert `Heblo-Plaud-AuthExpired`).

  ## Observed Refresh Endpoint

  From `@plaud-ai/cli` source inspection:

  ```
  POST https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh
  Content-Type: application/json

  {
    "refresh_token": "<current_refresh_token>"
  }
  ```

  Response shape (observed):

  ```json
  {
    "access_token": "...",
    "refresh_token": "...",
    "expires_at": 1234567890
  }
  ```

  > **Open question:** Confirm Plaud's refresh-token hard TTL by inspecting `expires_at` and observing
  > rotation over several days after implementing this. The hard TTL appears to be ~30 days but is not
  > officially documented.

  ## Proposed Design

  ### `PlaudTokenRefreshClient`

  New HttpClient wrapper in `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/`:

  ```csharp
  public sealed class PlaudTokenRefreshClient
  {
      private readonly HttpClient _http;

      public PlaudTokenRefreshClient(HttpClient http) => _http = http;

      public async Task<PlaudTokens> RefreshAsync(string refreshToken, CancellationToken ct = default)
      {
          var response = await _http.PostAsJsonAsync(
              "https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh",
              new { refresh_token = refreshToken },
              ct);

          response.EnsureSuccessStatusCode();
          return await response.Content.ReadFromJsonAsync<PlaudTokens>(cancellationToken: ct)
              ?? throw new InvalidOperationException("Empty refresh response from Plaud API");
      }
  }

  public sealed record PlaudTokens(string AccessToken, string RefreshToken, long ExpiresAt);
  ```

  ### `PlaudTokenRefreshJob`

  New recurring job in `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/`:

  ```csharp
  [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
  public async Task ExecuteAsync(CancellationToken ct = default)
  {
      // 1. Read current tokens JSON from Key Vault secret "plaud-tokens-json"
      // 2. Deserialize to extract refresh_token
      // 3. Call PlaudTokenRefreshClient.RefreshAsync
      // 4. Serialize new tokens to JSON
      // 5. Write back to Key Vault secret "plaud-tokens-json"
      // 6. Overwrite ~/.plaud/tokens.json (same as PlaudTokenBootstrapper does today)
  }

  public RecurringJobMetadata Metadata { get; } = new()
  {
      JobName = "plaud-token-refresh",
      DisplayName = "Plaud — refresh auth token",
      CronExpression = "0 4 * * 0",  // weekly, Sunday 04:00
      DefaultIsEnabled = false
  };
  ```

  ### Storage: Key Vault Secret

  - Secret name: `plaud-tokens-json`
  - Value: full content of `~/.plaud/tokens.json` (the JSON blob the CLI expects)
  - **Change `PlaudTokenBootstrapper`** to read from KV on startup instead of from the App Service setting
    `Plaud__TokensJson`. This removes the restart-stale-token problem entirely.
  - Remove `Plaud__TokensJson` App Service setting once KV is in place.

  ## Infra Prerequisites

  1. Key Vault provisioned in `rgHeblo` (e.g. `kv-heblo`).
  2. App Service Managed Identity (`Heblo`) granted `Key Vault Secrets Officer` on the single secret
     `plaud-tokens-json` (least privilege — not on the entire vault).
  3. Add `Azure.Security.KeyVault.Secrets` NuGet to the infrastructure layer.

  ## Verification Queries (for after implementation)

  ```bash
  # Confirm token refresh job ran successfully
  az monitor app-insights query --app aiHeblo -g rgHeblo \
    --analytics-query "traces | where message contains 'plaud-token-refresh' | order by timestamp desc | take 10"

  # Confirm no auth failures in the 7 days after implementation
  az monitor app-insights query --app aiHeblo -g rgHeblo \
    --analytics-query "exceptions | where type endswith 'PlaudAuthExpiredException' | where timestamp > ago(7d) | count"
  ```
  ````

- [ ] **Step 2: Commit**

  ```bash
  git add docs/integrations/plaud-token-auto-refresh.md
  git commit -m "docs(plaud): add deferred auto-refresh design doc (depends on Key Vault infra)"
  ```

---

## Task 5: Final build + format verification

- [ ] **Step 1: Full build**

  ```bash
  cd backend
  dotnet build
  ```

  Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Format check**

  ```bash
  cd backend
  dotnet format --verify-no-changes
  ```

  If changes are reported, apply them:

  ```bash
  cd backend
  dotnet format
  git add -u
  git commit -m "chore: dotnet format"
  ```

- [ ] **Step 3: Run all Plaud tests one final time**

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Adapters.Plaud.Tests -v normal
  ```

  Expected: all tests pass.

---

## Post-Deploy Verification

After deploying to production, run this App Insights query to confirm the circuit breaker is working:

```bash
az monitor app-insights query --app aiHeblo -g rgHeblo \
  --analytics-query "exceptions | where timestamp > ago(30m) and type endswith 'PlaudAuthExpiredException' | summarize Count=count() by bin(timestamp, 5m) | order by timestamp desc"
```

Each 5-minute bin should show ≤ 1 occurrence (down from ≤ 10 before this fix).

---

## Manual Step (Out of Band): Azure Monitor Alert

This is not automated — set it up manually in the Azure Portal after deploying:

1. Portal → App Insights `aiHeblo` (RG `rgHeblo`) → **Alerts** → **Create alert rule**
2. **Scope**: `aiHeblo`
3. **Condition**: Custom log search:
   ```kusto
   exceptions
   | where cloud_RoleName == "Heblo-API-Production"
   | where type endswith "PlaudAuthExpiredException"
   ```
   Threshold: count > 0 over the last 15 minutes, evaluated every 5 minutes.
4. **Action group**: email `ondra@anela.cz` (create `ag-heblo-ops` if none exists)
5. **Severity**: 2 (warning)
6. **Alert name**: `Heblo-Plaud-AuthExpired`

**Recovery procedure when alert fires:**
1. `plaud login` locally
2. `cat ~/.plaud/tokens.json | pbcopy`
3. Azure Portal → App Service `Heblo` → Configuration → `Plaud__TokensJson` → paste → Save

This alert stays in place even after auto-refresh is implemented (as a safety net).
