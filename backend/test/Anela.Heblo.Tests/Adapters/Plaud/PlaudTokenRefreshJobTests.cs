using Anela.Heblo.Adapters.Plaud;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Plaud;

public sealed class PlaudTokenRefreshJobTests
{
    private readonly Mock<IPlaudTokenRefreshClient> _refreshClient = new();
    private readonly Mock<IPlaudTokenStore> _tokenStore = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    private static readonly PlaudTokens CurrentTokens = new(
        AccessToken: "old-access",
        RefreshToken: "old-refresh",
        ExpiresAt: 9999999999L);

    private static readonly PlaudTokens ValidNewTokens = new(
        AccessToken: "new-access",
        RefreshToken: "new-refresh",
        ExpiresAt: 99999999999999L,
        TokenType: "bearer");

    public PlaudTokenRefreshJobTests()
    {
        _tokenStore
            .Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentTokens);
        _tokenStore
            .Setup(s => s.SaveAsync(It.IsAny<PlaudTokens>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudTokenSaveResult(KeyVaultWriteFailed: false, KeyVaultError: null));
    }

    private PlaudTokenRefreshJob CreateJob() =>
        new(_refreshClient.Object,
            _tokenStore.Object,
            _statusChecker.Object,
            NullLogger<PlaudTokenRefreshJob>.Instance);

    [Fact]
    public async Task ExecuteAsync_SkipsWhenJobDisabled()
    {
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-token-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await CreateJob().ExecuteAsync(default);

        _refreshClient.Verify(
            r => r.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _tokenStore.Verify(
            s => s.SaveAsync(It.IsAny<PlaudTokens>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenResponseHasEmptyAccessToken()
    {
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-token-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _refreshClient
            .Setup(r => r.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidNewTokens with { AccessToken = "" });

        Func<Task> act = () => CreateJob().ExecuteAsync(default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty tokens*");

        _tokenStore.Verify(
            s => s.SaveAsync(It.IsAny<PlaudTokens>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenExpiresAtInPast()
    {
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-token-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _refreshClient
            .Setup(r => r.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidNewTokens with { ExpiresAt = 1L });

        Func<Task> act = () => CreateJob().ExecuteAsync(default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expires_at*past*");

        _tokenStore.Verify(
            s => s.SaveAsync(It.IsAny<PlaudTokens>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CallsSaveOnSuccess()
    {
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-token-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _refreshClient
            .Setup(r => r.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidNewTokens);

        await CreateJob().ExecuteAsync(default);

        _tokenStore.Verify(
            s => s.SaveAsync(ValidNewTokens, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CompletesWithoutThrowing_WhenKvWriteFails()
    {
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-token-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _refreshClient
            .Setup(r => r.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidNewTokens);
        _tokenStore
            .Setup(s => s.SaveAsync(ValidNewTokens, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudTokenSaveResult(
                KeyVaultWriteFailed: true,
                KeyVaultError: new Exception("KV unavailable")));

        // The new job logs a warning but does NOT throw when KV write fails (disk succeeded).
        Func<Task> act = () => CreateJob().ExecuteAsync(default);

        await act.Should().NotThrowAsync();
    }
}
