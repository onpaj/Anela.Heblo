# Plaud CLI Timeout Callback Crash Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Plaud CLI timeout/cancellation callback never throw, so a hung CLI can no longer terminate the whole .NET host.

**Architecture:** Add an `internal static TryKillProcessTree(Process, ILogger)` helper to `PlaudCliClient` that absorbs all expected kill failures. `RunCliCoreAsync` registers it via a `using var` `CancellationTokenRegistration` (disposed before the `Process`). `InternalsVisibleTo("Anela.Heblo.Adapters.Plaud.Tests")` already exists in `AssemblyAttributes.cs`.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, Xunit.SkippableFact, `System.Diagnostics.Process`.

Files touched:
- Modify `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs` (`RunCliCoreAsync`, ~line 150, plus new helper)
- Create `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientTimeoutTests.cs`
- Modify `docs/processes/sync-plaud-recordings.md` (Known quirks, `verified_at`)

---

### task: kill-helper

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs`
- Create: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientTimeoutTests.cs`

- [ ] **Step 1: Write the failing helper tests**

Create `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientTimeoutTests.cs`:

```csharp
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public sealed class PlaudCliClientTimeoutTests
{
    [Fact]
    public void TryKillProcessTree_UnstartedProcess_DoesNotThrow()
    {
        // HasExited throws InvalidOperationException when no process is associated.
        using var process = new Process();

        var act = () => PlaudCliClient.TryKillProcessTree(process, NullLogger.Instance);

        act.Should().NotThrow();
    }

    [SkippableFact]
    public async Task TryKillProcessTree_AlreadyExitedProcess_DoesNotThrow()
    {
        Skip.If(OperatingSystem.IsWindows(), "Uses /bin/sh");

        using var process = Process.Start(new ProcessStartInfo("/bin/sh", new[] { "-c", "exit 0" })
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        await process.WaitForExitAsync();

        var act = () => PlaudCliClient.TryKillProcessTree(process, NullLogger.Instance);

        act.Should().NotThrow();
    }

    [SkippableFact]
    public async Task TryKillProcessTree_RunningProcess_KillsIt()
    {
        Skip.If(OperatingSystem.IsWindows(), "Uses /bin/sh");

        using var process = Process.Start(new ProcessStartInfo("/bin/sh", new[] { "-c", "sleep 30" })
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        PlaudCliClient.TryKillProcessTree(process, NullLogger.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(cts.Token);
        process.HasExited.Should().BeTrue();
    }
}
```

Check that the test project already references `Xunit.SkippableFact` (it is used in `PlaudCliClientRunTests.cs`, so it does).

- [ ] **Step 2: Run tests to verify they fail to compile**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.Plaud.Tests --filter "FullyQualifiedName~PlaudCliClientTimeoutTests"`
Expected: build FAIL, `'PlaudCliClient' does not contain a definition for 'TryKillProcessTree'`.

- [ ] **Step 3: Add the helper**

In `PlaudCliClient.cs`, add `using System.ComponentModel;` at the top (alongside `using System.Diagnostics;`), and add this method directly above `public static List<PlaudRecordingSummary> ParseFilesOutput`:

```csharp
    /// <summary>
    /// Kills the CLI process tree. Runs from a <see cref="CancellationToken"/> callback on a timer
    /// thread, where any escaping exception terminates the whole host — so it must never throw.
    /// </summary>
    internal static void TryKillProcessTree(Process process, ILogger logger)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogDebug(ex, "Plaud CLI process already exited; nothing to kill.");
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning(ex, "Failed to kill Plaud CLI process tree.");
        }
        catch (NotSupportedException ex)
        {
            logger.LogWarning(ex, "Killing Plaud CLI process tree is not supported.");
        }
        catch (Exception ex)
        {
            // Last-resort guard: this runs on a timer thread where any escape is fatal.
            logger.LogWarning(ex, "Unexpected error killing Plaud CLI process tree.");
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.Plaud.Tests --filter "FullyQualifiedName~PlaudCliClientTimeoutTests"`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientTimeoutTests.cs
git commit -m "fix(plaud): add non-throwing TryKillProcessTree helper"
```

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

### task: update-process-doc

**Files:**
- Modify: `docs/processes/sync-plaud-recordings.md`

- [ ] **Step 1: Add the Known quirk**

Under `## Known quirks` in `docs/processes/sync-plaud-recordings.md`, add:

```markdown
- **The CLI timeout kill must never throw.** `PlaudCliClient` kills the CLI process tree from a
  `CancellationToken` callback (after `ProcessTimeoutSeconds`, default 60 s). That callback runs on a
  timer thread, so an exception escaping it terminates the whole host. Until #4462 an unguarded
  `Process.Kill` on an already-exited process threw `InvalidOperationException` and caused the
  2026-10-09 production outage (container exit 139, ~3 min restarts, 502/503). The kill now goes
  through `TryKillProcessTree`, which swallows and logs kill failures, and the registration is
  disposed before the `Process`.
```

- [ ] **Step 2: Bump `verified_at`**

Set the `verified_at:` front-matter value to the current `git rev-parse --short HEAD`, quoted as in the existing line.

- [ ] **Step 3: Check the index**

Run: `python3 scripts/process-docs/check.py index`
Expected: exits 0 with no staleness or index errors.

- [ ] **Step 4: Commit**

```bash
git add docs/processes/sync-plaud-recordings.md
git commit -m "docs(plaud): document timeout kill-callback quirk (#4462)"
```

---

## Self-review

- FR-1 → task `kill-helper`; FR-2, FR-3 → task `wire-registration`; FR-4 → both test tasks; FR-5 → task `update-process-doc`.
- Out of scope per spec: root-causing the CLI hang, the prod DB mitigation, workflow changes.
- Names consistent: `TryKillProcessTree(Process, ILogger)`, `killRegistration`, `PlaudCliClientTimeoutTests`.
