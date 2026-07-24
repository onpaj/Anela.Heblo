### task: run-cli-async-success-and-retry-tests


## Goal
Close the remaining coverage gap in `PlaudCliClient.RunCliAsync`'s success path (`backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs` lines 82-110), which today has no test exercising the plain "CLI succeeds on first try" branch — meaning line 89 (`await _tokenRefresher.SyncToKeyVaultAsync(ct);`) and line 90 (`return output;`) are entirely uncovered. Also strengthen two existing AUTH_FAILED-path tests so they assert the *exact* wrapping/no-retry-loop behavior (inner-exception identity on refresh failure; exactly-twice CLI invocation on retry failure) instead of only the thrown exception's type. All changes are additive/in-place edits to `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRunTests.cs`; no production code changes.

## Context
`RunCliAsync` (private, exercised via `ListRecentAsync`) at `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs` lines 82-110:

```csharp
private async Task<string> RunCliAsync(string[] args, CancellationToken ct)
{
    try
    {
        var output = await RunCliCoreAsync(args, ct);
        // The CLI rotates the on-disk refresh token during a normal (non-AUTH_FAILED) call.
        // Mirror it to Key Vault so a container restart never re-seeds a stale token. Best-effort.
        await _tokenRefresher.SyncToKeyVaultAsync(ct);
        return output;
    }
    catch (PlaudAuthExpiredException)
    {
        _logger.LogWarning("Plaud auth expired; attempting token refresh and retry.");
        try
        {
            await _tokenRefresher.RefreshAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plaud token refresh failed.");
            throw new PlaudAuthExpiredException("token refresh failed", ex);
        }

        _logger.LogInformation("Plaud token refreshed; retrying CLI call.");
        // A second AUTH_FAILED here means the refreshed token is still rejected — surface it.
        // RefreshAsync already persisted the fresh token to Key Vault, so no extra sync needed.
        return await RunCliCoreAsync(args, ct);
    }
}
```

Constructor: `public PlaudCliClient(ILogger<PlaudCliClient> logger, IOptions<PlaudOptions> options, IPlaudTokenRefresher tokenRefresher)` (lines 15-20).

`IPlaudTokenRefresher` (`backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudTokenRefresher.cs`):
```csharp
public interface IPlaudTokenRefresher
{
    Task RefreshAsync(CancellationToken ct = default);
    Task SyncToKeyVaultAsync(CancellationToken ct = default); // "Best-effort — never throws." per XML doc
}
```

`PlaudAuthExpiredException` (`backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAuthExpiredException.cs`) has two constructors:
```csharp
public PlaudAuthExpiredException(string stderr)
    : base($"{RecoveryHint} CLI stderr: {stderr ?? "(empty)"}")
{ }

public PlaudAuthExpiredException(string stderr, Exception innerException)
    : base($"{RecoveryHint} CLI stderr: {stderr ?? "(empty)"}", innerException)
{ }
```
The two-arg ctor is the one hit by the wrapping `throw new PlaudAuthExpiredException("token refresh failed", ex);` at line 102 — its rendered `Message` therefore contains the literal substring `"CLI stderr: token refresh failed"`, so asserting `.Message` contains `"token refresh failed"` is valid.

The target test file is `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRunTests.cs` (246 lines), `public sealed class PlaudCliClientRunTests` in namespace `Anela.Heblo.Adapters.Plaud.Tests`. Current structure:

- Lines 1-4: `using System.Text.Json; using FluentAssertions; using Microsoft.Extensions.Logging.Abstractions; using Microsoft.Extensions.Options;`
- Lines 10-33: `// ── Fake refresh client ──` banner, then `private sealed class FakeRefreshClient : IPlaudTokenRefreshClient` with `CallCount`, `CapturedRefreshToken`, factory methods `Succeeds(PlaudTokens)` / `Throws(Exception)`, and `RefreshAsync(string refreshToken, CancellationToken ct = default)`.
- Lines 35-66: `// ── Helpers ──` banner: `FutureExpiresAt` (far-future Unix ms expiry), `OptionsFor(string shimPath)` returning `PlaudOptions { CliExecutablePath = shimPath, ProcessTimeoutSeconds = 10 }`, `CreateTestDir()` returning `(string dir, string shimPath, string tokensPath)` under a fresh temp dir, `CreateClient(string shimPath, IPlaudTokenRefreshClient refreshClient, string tokensPath)` which builds a real `PlaudTokenRefresher` wired to the fake HTTP client and wraps it in `PlaudCliClient`.
- Lines 68-73: `WriteTokensAsync(string path, string accessToken = "old-token", string refreshToken = "refresh-token")` — writes a serialized `PlaudTokens` to disk.
- Lines 77-117: `RunCli_WhenAuthFails_RefreshesTokenAndRetries_ReturnsOutput` — the retry-succeeds test (reference pattern; do not modify).
- Lines 121-149: `RunCli_WhenAuthFailsAndRefreshThrows_ThrowsPlaudAuthExpiredException` — **to be strengthened (FR-5)**.
- Lines 153-182: `RunCli_WhenAuthFailsAndRetryAlsoFails_ThrowsPlaudAuthExpiredException` — **to be strengthened (FR-6)**.
- Lines 186-213: `RunCli_WhenAuthFailsAndTokensFileMissing_ThrowsPlaudAuthExpiredException` — unrelated, do not touch.
- Lines 217-244: `RunCli_WhenCliExitsNonZeroWithoutAuthFailed_ThrowsInvalidOperationException` — unrelated, do not touch.

Every `[SkippableFact]` test in this file follows the same shape: `Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash"); if (OperatingSystem.IsWindows()) return;`, then `var (dir, shimPath, tokensPath) = CreateTestDir();` inside a `try { ... } finally { Directory.Delete(dir, recursive: true); }` block. The shim script is written with `File.WriteAllTextAsync(shimPath, "#!/bin/sh\n...")` and made executable via `File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);`. `Xunit.SkippableFact` package is already referenced; `[SkippableFact]` and `Skip` come from that package's namespace, already resolvable in this file today (no new using needed — verify by checking the file has no explicit `using Xunit.Sdk;`/`using Xunit;` beyond the global `<Using Include="Xunit" />` in the csproj, which is sufficient since `Xunit.SkippableFact` types live in the `Xunit` namespace).

## Files to create/modify
- `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRunTests.cs`:
  1. Add a new private nested class `FakeTokenRefresher : IPlaudTokenRefresher`.
  2. Add a new `[SkippableFact]` test `RunCli_WhenCliSucceeds_CallsSyncToKeyVaultAndReturnsOutput`.
  3. Modify `RunCli_WhenAuthFailsAndRefreshThrows_ThrowsPlaudAuthExpiredException` (lines 121-149) in place to add inner-exception and message assertions.
  4. Modify `RunCli_WhenAuthFailsAndRetryAlsoFails_ThrowsPlaudAuthExpiredException` (lines 153-182) in place to add an invocation-count assertion via a modified shim script.

## Implementation steps

1. **Add `FakeTokenRefresher`.** Immediately after the closing `}` of `FakeRefreshClient` (line 33) and before the `// ── Helpers ──` banner (line 35), insert a new banner and nested class:
   ```csharp
   // ── Fake token refresher ────────────────────────────────────────────────

   private sealed class FakeTokenRefresher : IPlaudTokenRefresher
   {
       public int RefreshCallCount { get; private set; }
       public int SyncCallCount { get; private set; }

       public Task RefreshAsync(CancellationToken ct = default)
       {
           RefreshCallCount++;
           throw new InvalidOperationException("should not be called");
       }

       public Task SyncToKeyVaultAsync(CancellationToken ct = default)
       {
           SyncCallCount++;
           return Task.CompletedTask;
       }
   }
   ```
   This fakes `IPlaudTokenRefresher` directly (the interface `PlaudCliClient`'s constructor takes), bypassing the real `PlaudTokenRefresher` and the existing `CreateClient` helper entirely. Do not modify `CreateClient` — it is used by all four other tests in the file and must keep constructing the real `PlaudTokenRefresher` + `FakeRefreshClient` combination.

2. **Add the new success-path test.** After the last existing test method (`RunCli_WhenCliExitsNonZeroWithoutAuthFailed_ThrowsInvalidOperationException`, ending at line 244) and before the class's closing `}` (line 245), insert:
   ```csharp
   // ── Plain success path ─────────────────────────────────────────────────

   [SkippableFact]
   public async Task RunCli_WhenCliSucceeds_CallsSyncToKeyVaultAndReturnsOutput()
   {
       Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");
       if (OperatingSystem.IsWindows()) return;

       var (dir, shimPath, _) = CreateTestDir();
       try
       {
           await File.WriteAllTextAsync(shimPath,
               "#!/bin/sh\nprintf \"Recordings in the last 7 days: 0\\n\"\nexit 0\n");
           File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

           var fake = new FakeTokenRefresher();
           var client = new PlaudCliClient(
               NullLogger<PlaudCliClient>.Instance,
               Options.Create(OptionsFor(shimPath)),
               fake);

           var result = await client.ListRecentAsync(7);

           result.Should().BeEmpty();
           fake.SyncCallCount.Should().Be(1);
           fake.RefreshCallCount.Should().Be(0);
       }
       finally
       {
           Directory.Delete(dir, recursive: true);
       }
   }
   ```
   Notes:
   - `CreateTestDir()` returns `(dir, shimPath, tokensPath)`; this test discards `tokensPath` (via `_`) since the fake refresher never touches disk.
   - The shim's stdout `"Recordings in the last 7 days: 0\n"` matches `ParseFilesOutput`'s expectation that the first line is a header line to skip and there are no data rows — `ListRecentAsync(7)` therefore returns an empty list, matching `PlaudCliClientParserTests.ParseFilesOutput_WithHeaderOnly_ReturnsEmptyList`'s already-confirmed behavior for this exact input string.
   - The client is constructed directly with `new PlaudCliClient(...)`, not via the shared `CreateClient` helper, since this test needs `FakeTokenRefresher` wired in directly rather than a real `PlaudTokenRefresher`.
   - Asserting `fake.RefreshCallCount.Should().Be(0)` confirms `RefreshAsync` (which throws if called) was never invoked — proving the success path never touches the retry branch.

3. **Strengthen `RunCli_WhenAuthFailsAndRefreshThrows_ThrowsPlaudAuthExpiredException` (FR-5).** Current body (lines 122-149):
   ```csharp
   [SkippableFact]
   public async Task RunCli_WhenAuthFailsAndRefreshThrows_ThrowsPlaudAuthExpiredException()
   {
       Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");
       if (OperatingSystem.IsWindows()) return;

       var (dir, shimPath, tokensPath) = CreateTestDir();
       try
       {
           await File.WriteAllTextAsync(shimPath,
               "#!/bin/sh\nprintf '[AUTH_FAILED] Token invalid or expired\\n' >&2\nexit 1\n");
           File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

           await WriteTokensAsync(tokensPath);

           var refreshClient = FakeRefreshClient.Throws(
               new HttpRequestException("Plaud token refresh failed: 401 Unauthorized"));
           var client = CreateClient(shimPath, refreshClient, tokensPath);

           Func<Task> act = () => client.ListRecentAsync(7);

           await act.Should().ThrowAsync<PlaudAuthExpiredException>();
           refreshClient.CallCount.Should().Be(1);
       }
       finally
       {
           Directory.Delete(dir, recursive: true);
       }
   }
   ```
   Change it to capture the thrown exception in a named local *before* passing it to `FakeRefreshClient.Throws(...)`, and add assertions on `InnerException` reference-equality and the outer message. Replace the `var refreshClient = ...` line through the `refreshClient.CallCount.Should().Be(1);` line with:
   ```csharp
           var expectedInner = new HttpRequestException("Plaud token refresh failed: 401 Unauthorized");
           var refreshClient = FakeRefreshClient.Throws(expectedInner);
           var client = CreateClient(shimPath, refreshClient, tokensPath);

           Func<Task> act = () => client.ListRecentAsync(7);

           var thrown = await act.Should().ThrowAsync<PlaudAuthExpiredException>();
           thrown.Which.InnerException.Should().BeSameAs(expectedInner);
           thrown.Which.Message.Should().Contain("token refresh failed");
           refreshClient.CallCount.Should().Be(1);
   ```
   `ThrowAsync<T>()` returns a `Task<ExceptionAssertions<T>>`; awaiting it yields `ExceptionAssertions<PlaudAuthExpiredException>`, whose `.Which` property (FluentAssertions) exposes the single caught exception instance for further assertions. `BeSameAs` is FluentAssertions' reference-equality assertion, equivalent to the spec's suggested `ReferenceEquals` check. Do not change the shim script or any other part of the test.

4. **Strengthen `RunCli_WhenAuthFailsAndRetryAlsoFails_ThrowsPlaudAuthExpiredException` (FR-6).** Current body (lines 154-182):
   ```csharp
   [SkippableFact]
   public async Task RunCli_WhenAuthFailsAndRetryAlsoFails_ThrowsPlaudAuthExpiredException()
   {
       Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");
       if (OperatingSystem.IsWindows()) return;

       var (dir, shimPath, tokensPath) = CreateTestDir();
       try
       {
           // Shim always fails with AUTH_FAILED, regardless of tokens.
           await File.WriteAllTextAsync(shimPath,
               "#!/bin/sh\nprintf '[AUTH_FAILED] Token invalid or expired\\n' >&2\nexit 1\n");
           File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

           await WriteTokensAsync(tokensPath);

           var refreshed = new PlaudTokens("refreshed-token", "new-refresh", FutureExpiresAt, "Bearer");
           var refreshClient = FakeRefreshClient.Succeeds(refreshed);
           var client = CreateClient(shimPath, refreshClient, tokensPath);

           Func<Task> act = () => client.ListRecentAsync(7);

           await act.Should().ThrowAsync<PlaudAuthExpiredException>();
           refreshClient.CallCount.Should().Be(1);
       }
       finally
       {
           Directory.Delete(dir, recursive: true);
       }
   }
   ```
   Modify the shim script to append an invocation marker line to a counter file inside the existing per-test `dir` on every invocation (still always failing with `AUTH_FAILED`), then assert the counter file has exactly 2 lines after the exception is thrown. Replace the shim-writing block and add a counter-file path and post-assertion:
   ```csharp
           // Shim always fails with AUTH_FAILED, regardless of tokens. Also records one line per
           // invocation to countFile so the test can assert the CLI is invoked exactly twice
           // (initial call + one retry) with no runaway retry loop.
           var countFile = Path.Combine(dir, "invocations.log");
           await File.WriteAllTextAsync(shimPath,
               $"#!/bin/sh\necho invoked >> \"{countFile}\"\nprintf '[AUTH_FAILED] Token invalid or expired\\n' >&2\nexit 1\n");
           File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

           await WriteTokensAsync(tokensPath);

           var refreshed = new PlaudTokens("refreshed-token", "new-refresh", FutureExpiresAt, "Bearer");
           var refreshClient = FakeRefreshClient.Succeeds(refreshed);
           var client = CreateClient(shimPath, refreshClient, tokensPath);

           Func<Task> act = () => client.ListRecentAsync(7);

           await act.Should().ThrowAsync<PlaudAuthExpiredException>();
           refreshClient.CallCount.Should().Be(1);

           var invocationLines = await File.ReadAllLinesAsync(countFile);
           invocationLines.Should().HaveCount(2);
   ```
   The `countFile` lives inside `dir` (from `CreateTestDir()`), so the existing `finally { Directory.Delete(dir, recursive: true); }` block cleans it up automatically — no new cleanup logic needed. Keep the existing `refreshClient.CallCount.Should().Be(1);` assertion unchanged (it already confirms `RefreshAsync` itself is not retried); the new `invocationLines.Should().HaveCount(2)` assertion additionally confirms the CLI process itself is invoked exactly twice.

5. Save the file. Double check: `FakeTokenRefresher` is placed between `FakeRefreshClient` and the `// ── Helpers ──` banner; the new test is appended at the end of the class; the two strengthened tests keep their original method signatures, `[SkippableFact]` attribute, and Windows-skip guard unchanged; no other test in the file was touched.

6. From the repository root, build and run the full test project to confirm all tests (5 original + 1 new, with 2 strengthened) pass:
   ```
   dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj
   ```
   Confirm 0 failed, and that `RunCli_WhenCliSucceeds_CallsSyncToKeyVaultAndReturnsOutput` appears and passes and that `RunCli_WhenAuthFailsAndRefreshThrows_ThrowsPlaudAuthExpiredException` and `RunCli_WhenAuthFailsAndRetryAlsoFails_ThrowsPlaudAuthExpiredException` still pass with their strengthened assertions. If running on a Windows CI/dev box, note the `[SkippableFact]` tests will report as skipped rather than passed — re-run on Linux/macOS (or WSL with bash available) to actually exercise the new assertions before considering this task done. Also run `dotnet format backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --verify-no-changes` (or apply formatting and check `git diff`) to confirm the edits match repository formatting conventions.
