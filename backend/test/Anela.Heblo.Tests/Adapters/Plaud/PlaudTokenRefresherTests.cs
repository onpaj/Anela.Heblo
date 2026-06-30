using Anela.Heblo.Adapters.Plaud;
using Azure;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Plaud;

public sealed class PlaudTokenRefresherTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"plaud_refresher_test_{Guid.NewGuid():N}");
    private readonly string _tokensPath;

    private readonly Mock<IPlaudTokenRefreshClient> _refreshClient = new();
    private readonly Mock<SecretClient> _secretClient = new();

    // expires_at is a Unix millisecond timestamp; use a far-future value so the past-check passes.
    private static readonly PlaudTokens ValidTokens = new(
        AccessToken: "new-access",
        RefreshToken: "new-refresh",
        ExpiresAt: 99999999999999L,
        TokenType: "bearer");

    private const string CurrentTokensJson =
        """{"access_token":"old-access","refresh_token":"old-refresh","expires_at":99999999999999,"token_type":"bearer"}""";

    public PlaudTokenRefresherTests()
    {
        Directory.CreateDirectory(_tempDir);
        _tokensPath = Path.Combine(_tempDir, "tokens.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private PlaudTokenRefresher CreateRefresher(bool withKeyVault = true) =>
        new(_refreshClient.Object,
            NullLogger<PlaudTokenRefresher>.Instance,
            withKeyVault ? _secretClient.Object : null,
            _tokensPath);

    [Fact]
    public async Task RefreshAsync_ThrowsWhenDiskTokensMissing()
    {
        // No tokens.json written.
        Func<Task> act = () => CreateRefresher().RefreshAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");

        _refreshClient.Verify(
            r => r.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_DoesNotPersistWhenResponseHasEmptyAccessToken()
    {
        _refreshClient
            .Setup(r => r.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidTokens with { AccessToken = "" });
        await File.WriteAllTextAsync(_tokensPath, CurrentTokensJson);

        Func<Task> act = () => CreateRefresher().RefreshAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty tokens*");

        _secretClient.Verify(
            s => s.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var diskContent = await File.ReadAllTextAsync(_tokensPath);
        diskContent.Should().Contain("old-refresh");
    }

    [Fact]
    public async Task RefreshAsync_DoesNotPersistWhenExpiresAtInPast()
    {
        _refreshClient
            .Setup(r => r.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidTokens with { ExpiresAt = 1L }); // epoch 1ms = in the past
        await File.WriteAllTextAsync(_tokensPath, CurrentTokensJson);

        Func<Task> act = () => CreateRefresher().RefreshAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expires_at*past*");

        _secretClient.Verify(
            s => s.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_WritesToDiskAndKeyVaultOnSuccess()
    {
        _refreshClient
            .Setup(r => r.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidTokens);
        await File.WriteAllTextAsync(_tokensPath, CurrentTokensJson);

        await CreateRefresher().RefreshAsync();

        var diskContent = await File.ReadAllTextAsync(_tokensPath);
        diskContent.Should().Contain("new-refresh");

        _secretClient.Verify(
            s => s.SetSecretAsync(
                "Plaud--TokensJson",
                It.Is<string>(v => v.Contains("new-refresh")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_WritesDiskOnlyWhenKeyVaultNotConfigured()
    {
        _refreshClient
            .Setup(r => r.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidTokens);
        await File.WriteAllTextAsync(_tokensPath, CurrentTokensJson);

        await CreateRefresher(withKeyVault: false).RefreshAsync();

        var diskContent = await File.ReadAllTextAsync(_tokensPath);
        diskContent.Should().Contain("new-refresh");
    }

    [Fact]
    public async Task RefreshAsync_KeyVaultFailureIsBestEffort_DiskStillWrittenAndNoThrow()
    {
        _refreshClient
            .Setup(r => r.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidTokens);
        _secretClient
            .Setup(s => s.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException("KV unavailable"));
        await File.WriteAllTextAsync(_tokensPath, CurrentTokensJson);

        // Disk write happens before KV and KV failure is swallowed — refresh completes successfully.
        Func<Task> act = () => CreateRefresher().RefreshAsync();
        await act.Should().NotThrowAsync();

        var diskContent = await File.ReadAllTextAsync(_tokensPath);
        diskContent.Should().Contain("new-refresh");
    }
}
