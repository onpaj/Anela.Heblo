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

