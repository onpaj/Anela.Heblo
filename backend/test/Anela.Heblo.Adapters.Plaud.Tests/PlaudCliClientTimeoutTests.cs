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
