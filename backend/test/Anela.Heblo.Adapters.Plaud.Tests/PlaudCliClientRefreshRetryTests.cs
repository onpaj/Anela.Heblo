using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public sealed class PlaudCliClientRefreshRetryTests
{
    [SkippableFact]
    public async Task ListRecentAsync_OnAuthFailed_ForcesRefreshAndRetriesOnce()
    {
        Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");

        var counterPath = Path.Combine(Path.GetTempPath(), $"plaud_counter_{Guid.NewGuid():N}");
        var shimPath = Path.Combine(Path.GetTempPath(), $"plaud_shim_{Guid.NewGuid():N}.sh");
        var script = $@"#!/bin/sh
COUNTER_FILE='{counterPath}'
COUNT=0
[ -f ""$COUNTER_FILE"" ] && COUNT=$(cat ""$COUNTER_FILE"")
NEW=$((COUNT+1))
echo $NEW > ""$COUNTER_FILE""
if [ ""$COUNT"" = ""0"" ]; then
  echo '[AUTH_FAILED] Token invalid or expired' >&2
  exit 1
fi
echo 'Recordings in the last 7 days: 0'
exit 0
";
        await File.WriteAllTextAsync(shimPath, script);
        File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var manager = new Mock<IPlaudTokenManager>();
            manager.Setup(m => m.EnsureFreshAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            manager.Setup(m => m.ForceRefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var telemetry = new Mock<ITelemetryService>();

            var options = Options.Create(new PlaudOptions
            {
                CliExecutablePath = shimPath,
                ProcessTimeoutSeconds = 10
            });

            var client = new PlaudCliClient(
                NullLogger<PlaudCliClient>.Instance, options, manager.Object, telemetry.Object);

            var result = await client.ListRecentAsync(7);

            result.Should().BeEmpty();
            manager.Verify(m => m.EnsureFreshAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            manager.Verify(m => m.ForceRefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
            var counter = int.Parse(await File.ReadAllTextAsync(counterPath));
            counter.Should().Be(2);
        }
        finally
        {
            if (File.Exists(shimPath)) File.Delete(shimPath);
            if (File.Exists(counterPath)) File.Delete(counterPath);
        }
    }

    [SkippableFact]
    public async Task ListRecentAsync_ThrowsAuthExpired_WhenRetriedCliStillAuthFails()
    {
        Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");

        var shimPath = Path.Combine(Path.GetTempPath(), $"plaud_shim_{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(shimPath,
            "#!/bin/sh\necho '[AUTH_FAILED] Token invalid or expired' >&2\nexit 1\n");
        File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var manager = new Mock<IPlaudTokenManager>();
            manager.Setup(m => m.EnsureFreshAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            manager.Setup(m => m.ForceRefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var telemetry = new Mock<ITelemetryService>();

            var options = Options.Create(new PlaudOptions
            {
                CliExecutablePath = shimPath,
                ProcessTimeoutSeconds = 10
            });

            var client = new PlaudCliClient(
                NullLogger<PlaudCliClient>.Instance, options, manager.Object, telemetry.Object);

            Func<Task> act = () => client.ListRecentAsync(7);

            await act.Should().ThrowAsync<PlaudAuthExpiredException>();
            manager.Verify(m => m.ForceRefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            if (File.Exists(shimPath)) File.Delete(shimPath);
        }
    }

    [SkippableFact]
    public async Task ListRecentAsync_ThrowsAuthExpired_WhenForceRefreshReturnsFalse()
    {
        Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");

        var shimPath = Path.Combine(Path.GetTempPath(), $"plaud_shim_{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(shimPath,
            "#!/bin/sh\necho '[AUTH_FAILED] Token invalid or expired' >&2\nexit 1\n");
        File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var manager = new Mock<IPlaudTokenManager>();
            manager.Setup(m => m.EnsureFreshAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            manager.Setup(m => m.ForceRefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var telemetry = new Mock<ITelemetryService>();

            var options = Options.Create(new PlaudOptions
            {
                CliExecutablePath = shimPath,
                ProcessTimeoutSeconds = 10
            });

            var client = new PlaudCliClient(
                NullLogger<PlaudCliClient>.Instance, options, manager.Object, telemetry.Object);

            Func<Task> act = () => client.ListRecentAsync(7);

            await act.Should().ThrowAsync<PlaudAuthExpiredException>();
            telemetry.Verify(t => t.TrackException(
                It.IsAny<PlaudAuthExpiredException>(), It.IsAny<Dictionary<string, string>>()),
                Times.Once);
        }
        finally
        {
            if (File.Exists(shimPath)) File.Delete(shimPath);
        }
    }
}
