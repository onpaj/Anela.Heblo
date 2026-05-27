using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public sealed class PlaudCliClientRunTests
{
    [SkippableFact]
    public async Task RunCli_WhenCliExitsWithAuthFailed_ThrowsPlaudAuthExpiredException()
    {
        // Skip on platforms that can't run shell scripts
        Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");
        if (OperatingSystem.IsWindows()) return; // satisfy CA1416 — unreachable at runtime

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
        if (OperatingSystem.IsWindows()) return; // satisfy CA1416 — unreachable at runtime

        // Arrange — write a tiny shim that mimics a generic non-zero exit
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

            // Act
            Func<Task> act = () => client.ListRecentAsync(7);

            // Assert
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
