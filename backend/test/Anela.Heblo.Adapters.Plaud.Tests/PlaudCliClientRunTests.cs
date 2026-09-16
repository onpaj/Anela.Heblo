using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public sealed class PlaudCliClientRunTests
{
    // ── Fake refresh client ──────────────────────────────────────────────────

    private sealed class FakeRefreshClient : IPlaudTokenRefreshClient
    {
        private readonly Func<string, Task<PlaudTokens>> _handler;

        public int CallCount { get; private set; }
        public string? CapturedRefreshToken { get; private set; }

        private FakeRefreshClient(Func<string, Task<PlaudTokens>> handler) => _handler = handler;

        public static FakeRefreshClient Succeeds(PlaudTokens tokens) =>
            new(_ => Task.FromResult(tokens));

        public static FakeRefreshClient Throws(Exception ex) =>
            new(_ => Task.FromException<PlaudTokens>(ex));

        public async Task<PlaudTokens> RefreshAsync(string refreshToken, CancellationToken ct = default)
        {
            CapturedRefreshToken = refreshToken;
            CallCount++;
            return await _handler(refreshToken);
        }
    }

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

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Far-future Unix millisecond expiry so the refresher's past-check passes.
    private static readonly long FutureExpiresAt =
        DateTimeOffset.UtcNow.AddDays(14).ToUnixTimeMilliseconds();

    private static PlaudOptions OptionsFor(string shimPath) => new()
    {
        CliExecutablePath = shimPath,
        ProcessTimeoutSeconds = 10
    };

    private static (string dir, string shimPath, string tokensPath) CreateTestDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"plaud_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return (dir, Path.Combine(dir, "plaud.sh"), Path.Combine(dir, "tokens.json"));
    }

    private static PlaudCliClient CreateClient(string shimPath, IPlaudTokenRefreshClient refreshClient, string tokensPath)
    {
        var refresher = new PlaudTokenRefresher(
            refreshClient,
            NullLogger<PlaudTokenRefresher>.Instance,
            secretClient: null,
            tokensFilePath: tokensPath);

        return new PlaudCliClient(
            NullLogger<PlaudCliClient>.Instance,
            Options.Create(OptionsFor(shimPath)),
            refresher);
    }

    private static async Task WriteTokensAsync(string path, string accessToken = "old-token",
        string refreshToken = "refresh-token")
    {
        var tokens = new PlaudTokens(accessToken, refreshToken, FutureExpiresAt, "Bearer");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(tokens));
    }

    // ── Auth-failed: retry succeeds after refresh ─────────────────────────

    [SkippableFact]
    public async Task RunCli_WhenAuthFails_RefreshesTokenAndRetries_ReturnsOutput()
    {
        Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");
        if (OperatingSystem.IsWindows()) return;

        var (dir, shimPath, tokensPath) = CreateTestDir();
        try
        {
            // Shim succeeds only after the tokens file contains the refreshed access token.
            await File.WriteAllTextAsync(shimPath,
                $"""
                #!/bin/sh
                TFILE="{tokensPath}"
                if grep -q '"refreshed-token"' "$TFILE" 2>/dev/null; then
                    printf "Recordings in the last 7 days: 0\n"
                    exit 0
                else
                    printf '[AUTH_FAILED] Token invalid or expired\n' >&2
                    exit 1
                fi
                """);
            File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            await WriteTokensAsync(tokensPath);

            var refreshed = new PlaudTokens("refreshed-token", "new-refresh", FutureExpiresAt, "Bearer");
            var refreshClient = FakeRefreshClient.Succeeds(refreshed);
            var client = CreateClient(shimPath, refreshClient, tokensPath);

            var result = await client.ListRecentAsync(7);

            result.Should().BeEmpty();
            refreshClient.CallCount.Should().Be(1);
            refreshClient.CapturedRefreshToken.Should().Be("refresh-token");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Auth-failed: refresh HTTP call throws ─────────────────────────────

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

            var expectedInner = new HttpRequestException("Plaud token refresh failed: 401 Unauthorized");
            var refreshClient = FakeRefreshClient.Throws(expectedInner);
            var client = CreateClient(shimPath, refreshClient, tokensPath);

            Func<Task> act = () => client.ListRecentAsync(7);

            var thrown = await act.Should().ThrowAsync<PlaudAuthExpiredException>();
            thrown.Which.InnerException.Should().BeSameAs(expectedInner);
            thrown.Which.Message.Should().Contain("token refresh failed");
            refreshClient.CallCount.Should().Be(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Auth-failed: retry also fails ─────────────────────────────────────

    [SkippableFact]
    public async Task RunCli_WhenAuthFailsAndRetryAlsoFails_ThrowsPlaudAuthExpiredException()
    {
        Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");
        if (OperatingSystem.IsWindows()) return;

        var (dir, shimPath, tokensPath) = CreateTestDir();
        try
        {
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
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Auth-failed: tokens file missing ─────────────────────────────────

    [SkippableFact]
    public async Task RunCli_WhenAuthFailsAndTokensFileMissing_ThrowsPlaudAuthExpiredException()
    {
        Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");
        if (OperatingSystem.IsWindows()) return;

        var (dir, shimPath, tokensPath) = CreateTestDir();
        try
        {
            await File.WriteAllTextAsync(shimPath,
                "#!/bin/sh\nprintf '[AUTH_FAILED] Token invalid or expired\\n' >&2\nexit 1\n");
            File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            // Intentionally do NOT write tokens file.
            var refreshClient = FakeRefreshClient.Succeeds(
                new PlaudTokens("t", "r", FutureExpiresAt, "Bearer"));
            var client = CreateClient(shimPath, refreshClient, tokensPath);

            Func<Task> act = () => client.ListRecentAsync(7);

            await act.Should().ThrowAsync<PlaudAuthExpiredException>();
            refreshClient.CallCount.Should().Be(0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Non-zero exit without AUTH_FAILED ────────────────────────────────

    [SkippableFact]
    public async Task RunCli_WhenCliExitsNonZeroWithoutAuthFailed_ThrowsInvalidOperationException()
    {
        Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");
        if (OperatingSystem.IsWindows()) return;

        var (dir, shimPath, tokensPath) = CreateTestDir();
        try
        {
            await File.WriteAllTextAsync(shimPath,
                "#!/bin/sh\nprintf 'some other error\\n' >&2\nexit 1\n");
            File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var refreshClient = FakeRefreshClient.Succeeds(
                new PlaudTokens("t", "r", FutureExpiresAt, "Bearer"));
            var client = CreateClient(shimPath, refreshClient, tokensPath);

            Func<Task> act = () => client.ListRecentAsync(7);

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*some other error*");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

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

    // ── Recording-id prefixing: lookups must reach the CLI in "of_" form ─────

    private const string BareRecordingId = "61d13e01b52688976a6e0b6f6d952888";
    private const string PrefixedRecordingId = "of_" + BareRecordingId;

    /// <summary>
    /// Runs <paramref name="invoke"/> against a shim that records every argument it was handed
    /// (one per line) and satisfies the "-o &lt;file&gt;" contract the transcript/summary calls use.
    /// </summary>
    private static async Task<string[]> CaptureCliArgsAsync(Func<PlaudCliClient, Task> invoke)
    {
        // Mirrors the guard in each caller; also satisfies CA1416 for SetUnixFileMode below.
        if (OperatingSystem.IsWindows()) return Array.Empty<string>();

        var (dir, shimPath, _) = CreateTestDir();
        try
        {
            var argsFile = Path.Combine(dir, "args.log");
            await File.WriteAllTextAsync(shimPath,
                $$"""
                #!/bin/sh
                for a in "$@"; do
                    echo "$a" >> "{{argsFile}}"
                done
                prev=""
                for a in "$@"; do
                    if [ "$prev" = "-o" ]; then printf '{}' > "$a"; fi
                    prev="$a"
                done
                exit 0
                """);
            File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var client = new PlaudCliClient(
                NullLogger<PlaudCliClient>.Instance,
                Options.Create(OptionsFor(shimPath)),
                new FakeTokenRefresher());

            await invoke(client);

            return await File.ReadAllLinesAsync(argsFile);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [SkippableFact]
    public async Task GetTranscriptAsync_PassesPrefixedRecordingIdToCli()
    {
        Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");
        if (OperatingSystem.IsWindows()) return;

        var args = await CaptureCliArgsAsync(client => client.GetTranscriptAsync(BareRecordingId));

        args.Should().ContainInOrder("transcript", PrefixedRecordingId);
        args.Should().NotContain(BareRecordingId);
    }

    [SkippableFact]
    public async Task GetSummaryAsync_PassesPrefixedRecordingIdToCli()
    {
        Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");
        if (OperatingSystem.IsWindows()) return;

        var args = await CaptureCliArgsAsync(client => client.GetSummaryAsync(BareRecordingId));

        args.Should().ContainInOrder("summary", PrefixedRecordingId);
        args.Should().NotContain(BareRecordingId);
    }

    [SkippableFact]
    public async Task GetFileDetailAsync_PassesPrefixedRecordingIdToCli()
    {
        Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");
        if (OperatingSystem.IsWindows()) return;

        var args = await CaptureCliArgsAsync(client => client.GetFileDetailAsync(BareRecordingId));

        args.Should().ContainInOrder("file", PrefixedRecordingId);
        args.Should().NotContain(BareRecordingId);
    }
}
