### task: wire-registration

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs` (`RunCliCoreAsync`)
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientTimeoutTests.cs`

- [ ] **Step 1: Write the timeout regression test**

Add to `PlaudCliClientTimeoutTests` (add usings `Anela.Heblo.Application.Features.MeetingTasks.Services` is not needed; add `using Microsoft.Extensions.Options;`). The test builds the client the same way `PlaudCliClientRunTests.CreateClient` does; the refresher is never invoked because the shim does not emit AUTH_FAILED.

```csharp
    private sealed class NoopRefreshClient : IPlaudTokenRefreshClient
    {
        public Task<PlaudTokens> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
            throw new InvalidOperationException("should not be called");
    }

    [SkippableFact]
    public async Task RunCli_WhenCliHangsPastTimeout_ThrowsTimeoutException_AndHostSurvives()
    {
        Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");

        var dir = Path.Combine(Path.GetTempPath(), $"plaud_timeout_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var shimPath = Path.Combine(dir, "plaud.sh");
            await File.WriteAllTextAsync(shimPath, "#!/bin/sh\nsleep 30\n");
            File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var refresher = new PlaudTokenRefresher(
                new NoopRefreshClient(),
                NullLogger<PlaudTokenRefresher>.Instance,
                secretClient: null,
                tokensFilePath: Path.Combine(dir, "tokens.json"));
            var client = new PlaudCliClient(
                NullLogger<PlaudCliClient>.Instance,
                Options.Create(new PlaudOptions { CliExecutablePath = shimPath, ProcessTimeoutSeconds = 1 }),
                refresher);

            var sw = Stopwatch.StartNew();
            var act = () => client.ListRecentAsync(7);

            await act.Should().ThrowAsync<TimeoutException>();
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));

            // Give any stray timer callback a chance to fire; an unhandled exception would kill the test host.
            await Task.Delay(500);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
```

If `PlaudOptions` has `required`/init-only members that differ from this, mirror `PlaudCliClientRunTests.OptionsFor`.

- [ ] **Step 2: Run the test (baseline)**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.Plaud.Tests --filter "FullyQualifiedName~RunCli_WhenCliHangsPastTimeout"`
Expected: PASS or FAIL depending on race with the old code (the old unguarded callback only crashes when the process is already gone); record the result. The deterministic regression guard is the helper tests from task `kill-helper`.

- [ ] **Step 3: Replace the unguarded registration**

In `RunCliCoreAsync` (`PlaudCliClient.cs`), replace

```csharp
            cts.Token.Register(() => process.Kill(entireProcessTree: true));
```

with

```csharp
            // Declared after `using var process`, so it is disposed first and cannot fire on a disposed Process.
            using var killRegistration = cts.Token.Register(() => TryKillProcessTree(process, _logger));
```

Leave its position immediately after the `ReadToEndAsync` tasks are created (spec FR-2: right after `process.Start()` succeeds).

- [ ] **Step 4: Run all Plaud tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.Plaud.Tests`
Expected: all pass, including every existing `PlaudCliClientRunTests` case (FR-3).

- [ ] **Step 5: Grep for other unguarded callbacks**

Run: `grep -rn "\.Register(\|new Timer\|Task.Run" backend/src/Adapters/Anela.Heblo.Adapters.Plaud/`
Expected: no remaining unguarded callbacks. If any exist, wrap them analogously and add a test.

- [ ] **Step 6: Format and build**

Run: `cd backend && dotnet format --include src/Adapters/Anela.Heblo.Adapters.Plaud test/Anela.Heblo.Adapters.Plaud.Tests && dotnet build`
Expected: build succeeds, no formatting diffs afterwards.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientTimeoutTests.cs
git commit -m "fix(plaud): scope kill callback registration and use non-throwing helper"
```

