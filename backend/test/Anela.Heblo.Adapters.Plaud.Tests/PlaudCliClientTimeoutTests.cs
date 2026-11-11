using System.Diagnostics;
using FluentAssertions;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

            var tokensPath = Path.Combine(dir, "tokens.json");
            var expiresAt = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds();
            await File.WriteAllTextAsync(tokensPath,
                JsonSerializer.Serialize(new PlaudTokens("token", "refresh", expiresAt, "Bearer")));

            var refresher = new PlaudTokenRefresher(
                new NoopRefreshClient(),
                NullLogger<PlaudTokenRefresher>.Instance,
                secretClient: null,
                tokensFilePath: tokensPath);
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
}
