using Anela.Heblo.Adapters.Plaud;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Azure;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Plaud;

public sealed class PlaudTokenRefreshJobTests : IDisposable
{
    private readonly string _tempHome =
        Path.Combine(Path.GetTempPath(), $"plaud_job_test_{Guid.NewGuid():N}");

    private readonly Mock<IPlaudTokenRefreshClient> _refreshClient = new();
    private readonly Mock<SecretClient> _secretClient = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    // expires_at is a Unix millisecond timestamp; use a far-future value so the past-check passes.
    private static readonly PlaudTokens ValidTokens = new(
        AccessToken: "new-access",
        RefreshToken: "new-refresh",
        ExpiresAt: 99999999999999L,
        TokenType: "bearer");

    private static readonly string CurrentTokensJson = """
        {"access_token":"old-access","refresh_token":"old-refresh","expires_at":99999999999999,"token_type":"bearer"}
        """;

    public PlaudTokenRefreshJobTests()
    {
        Directory.CreateDirectory(Path.Combine(_tempHome, ".plaud"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempHome))
            Directory.Delete(_tempHome, recursive: true);
    }

    private PlaudTokenRefreshJob CreateJob() =>
        new(_refreshClient.Object,
            _secretClient.Object,
            _statusChecker.Object,
            NullLogger<PlaudTokenRefreshJob>.Instance);

    private async Task RunWithTempHome(Func<Task> test)
    {
        var original = Environment.GetEnvironmentVariable("HOME");
        Environment.SetEnvironmentVariable("HOME", _tempHome);
        try { await test(); }
        finally { Environment.SetEnvironmentVariable("HOME", original); }
    }

    [SkippableFact]
    public async Task ExecuteAsync_SkipsWhenJobDisabled()
    {
        Skip.If(OperatingSystem.IsWindows(), "HOME override requires Unix");

        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-token-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await RunWithTempHome(async () => await CreateJob().ExecuteAsync(default));

        _refreshClient.Verify(
            r => r.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _secretClient.Verify(
            s => s.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [SkippableFact]
    public async Task ExecuteAsync_ThrowsWhenDiskTokensMissing()
    {
        Skip.If(OperatingSystem.IsWindows(), "HOME override requires Unix");

        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-token-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // No tokens.json written — the directory exists but the file does not.
        Func<Task> act = () => RunWithTempHome(async () =>
            await CreateJob().ExecuteAsync(default));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [SkippableFact]
    public async Task ExecuteAsync_DoesNotWriteKVWhenResponseHasEmptyAccessToken()
    {
        Skip.If(OperatingSystem.IsWindows(), "HOME override requires Unix");

        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-token-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _refreshClient
            .Setup(r => r.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidTokens with { AccessToken = "" });

        await File.WriteAllTextAsync(
            Path.Combine(_tempHome, ".plaud", "tokens.json"), CurrentTokensJson);

        Func<Task> act = () => RunWithTempHome(async () =>
            await CreateJob().ExecuteAsync(default));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty tokens*");

        _secretClient.Verify(
            s => s.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var diskContent = await File.ReadAllTextAsync(Path.Combine(_tempHome, ".plaud", "tokens.json"));
        diskContent.Should().Contain("old-refresh");
    }

    [SkippableFact]
    public async Task ExecuteAsync_DoesNotWriteKVWhenExpiresAtInPast()
    {
        Skip.If(OperatingSystem.IsWindows(), "HOME override requires Unix");

        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-token-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _refreshClient
            .Setup(r => r.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidTokens with { ExpiresAt = 1L }); // epoch 1 = in the past

        await File.WriteAllTextAsync(
            Path.Combine(_tempHome, ".plaud", "tokens.json"), CurrentTokensJson);

        Func<Task> act = () => RunWithTempHome(async () =>
            await CreateJob().ExecuteAsync(default));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expires_at*past*");

        _secretClient.Verify(
            s => s.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [SkippableFact]
    public async Task ExecuteAsync_WritesToDiskAndKVOnSuccess()
    {
        Skip.If(OperatingSystem.IsWindows(), "HOME override requires Unix");

        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-token-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _refreshClient
            .Setup(r => r.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidTokens);

        var tokensPath = Path.Combine(_tempHome, ".plaud", "tokens.json");
        await File.WriteAllTextAsync(tokensPath, CurrentTokensJson);

        await RunWithTempHome(async () => await CreateJob().ExecuteAsync(default));

        var diskContent = await File.ReadAllTextAsync(tokensPath);
        diskContent.Should().Contain("new-refresh");

        _secretClient.Verify(
            s => s.SetSecretAsync(
                "Plaud--TokensJson",
                It.Is<string>(v => v.Contains("new-refresh")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [SkippableFact]
    public async Task ExecuteAsync_WritesDiskBeforeKV_SoDiskPreservedOnKVFailure()
    {
        Skip.If(OperatingSystem.IsWindows(), "HOME override requires Unix");

        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-token-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _refreshClient
            .Setup(r => r.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidTokens);
        _secretClient
            .Setup(s => s.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException("KV unavailable"));

        var tokensPath = Path.Combine(_tempHome, ".plaud", "tokens.json");
        await File.WriteAllTextAsync(tokensPath, CurrentTokensJson);

        Func<Task> act = () => RunWithTempHome(async () =>
            await CreateJob().ExecuteAsync(default));

        await act.Should().ThrowAsync<RequestFailedException>();

        // Disk was written before KV — new token is on disk even though KV failed.
        var diskContent = await File.ReadAllTextAsync(tokensPath);
        diskContent.Should().Contain("new-refresh");
    }
}
