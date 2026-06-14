using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public sealed class PlaudTokenStoreTests : IDisposable
{
    private readonly string _tempHome;

    public PlaudTokenStoreTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), $"plaud_home_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempHome, ".plaud"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempHome)) Directory.Delete(_tempHome, recursive: true);
    }

    private PlaudTokenStore CreateSut(SecretClient? secretClient = null) =>
        new PlaudTokenStore(
            secretClient ?? FakeSecretClient.AlwaysSucceeds(),
            Options.Create(new PlaudCredentialsOptions()),
            NullLogger<PlaudTokenStore>.Instance,
            homeDirOverride: _tempHome);

    [Fact]
    public async Task LoadAsync_Throws_WhenDiskFileMissing()
    {
        // Remove the .plaud directory so the file doesn't exist
        Directory.Delete(Path.Combine(_tempHome, ".plaud"), recursive: true);
        var sut = CreateSut();

        Func<Task> act = () => sut.LoadAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*tokens.json*");
    }

    [Fact]
    public async Task LoadAsync_ReturnsParsedTokens_WhenDiskFileExists()
    {
        var tokensPath = Path.Combine(_tempHome, ".plaud", "tokens.json");
        await File.WriteAllTextAsync(tokensPath,
            """{"access_token":"a","refresh_token":"r","expires_at":1234567890}""");
        var sut = CreateSut();

        var result = await sut.LoadAsync(CancellationToken.None);

        result.AccessToken.Should().Be("a");
        result.RefreshToken.Should().Be("r");
        result.ExpiresAt.Should().Be(1234567890L);
    }

    [Fact]
    public async Task SaveAsync_WritesDiskThenKeyVault_WhenBothSucceed()
    {
        var fake = FakeSecretClient.AlwaysSucceeds();
        var sut = CreateSut(fake);
        var tokens = new PlaudTokens("new-access", "new-refresh", 9999999999L);

        var result = await sut.SaveAsync(tokens, CancellationToken.None);

        result.KeyVaultWriteFailed.Should().BeFalse();
        result.KeyVaultError.Should().BeNull();

        var tokensPath = Path.Combine(_tempHome, ".plaud", "tokens.json");
        File.Exists(tokensPath).Should().BeTrue();
        var diskJson = await File.ReadAllTextAsync(tokensPath);
        diskJson.Should().Contain("new-access").And.Contain("new-refresh");

        fake.Writes.Should().ContainSingle();
        fake.Writes[0].Name.Should().Be("Plaud--TokensJson");
        fake.Writes[0].Value.Should().Contain("new-access");
    }

    [Fact]
    public async Task SaveAsync_ReturnsKeyVaultWriteFailed_WhenKvThrows_ButDiskIsUpdated()
    {
        var kvError = new InvalidOperationException("KV down");
        var fake = FakeSecretClient.AlwaysThrows(kvError);
        var sut = CreateSut(fake);
        var tokens = new PlaudTokens("a", "r", 9999999999L);

        var result = await sut.SaveAsync(tokens, CancellationToken.None);

        result.KeyVaultWriteFailed.Should().BeTrue();
        result.KeyVaultError.Should().BeSameAs(kvError);

        var tokensPath = Path.Combine(_tempHome, ".plaud", "tokens.json");
        var diskJson = await File.ReadAllTextAsync(tokensPath);
        diskJson.Should().Contain("\"access_token\":\"a\"");
    }

    [SkippableFact]
    public async Task SaveAsync_Throws_WhenDiskWriteFails()
    {
        Skip.If(OperatingSystem.IsWindows(), "Read-only-dir trick is unix-only");

        // Replace ~/.plaud directory with a regular file so writes fail
        var plaudDir = Path.Combine(_tempHome, ".plaud");
        Directory.Delete(plaudDir, recursive: true);
        await File.WriteAllTextAsync(plaudDir, "blocker");

        var sut = CreateSut();
        var tokens = new PlaudTokens("a", "r", 9999999999L);

        Func<Task> act = () => sut.SaveAsync(tokens, CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
    }
}

// file-scoped fakes (not exported outside this file)
file sealed class FakeSecretClient : SecretClient
{
    private readonly Func<string, string, CancellationToken, Task<Response<KeyVaultSecret>>> _setBehavior;
    public List<(string Name, string Value)> Writes { get; } = new();

    private FakeSecretClient(Func<string, string, CancellationToken, Task<Response<KeyVaultSecret>>> setBehavior)
        : base(new Uri("https://fake.vault.azure.net/"), new FakeTokenCredential())
    {
        _setBehavior = setBehavior;
    }

    public static FakeSecretClient AlwaysSucceeds() => new((name, value, ct) =>
        Task.FromResult(Response.FromValue(new KeyVaultSecret(name, value), new FakeResponse())));

    public static FakeSecretClient AlwaysThrows(Exception ex) => new((_, _, _) => throw ex);

    public override async Task<Response<KeyVaultSecret>> SetSecretAsync(
        string name, string value, CancellationToken cancellationToken = default)
    {
        Writes.Add((name, value));
        return await _setBehavior(name, value, cancellationToken);
    }
}

file sealed class FakeTokenCredential : Azure.Core.TokenCredential
{
    public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext c, CancellationToken ct) => default;
    public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext c, CancellationToken ct) => ValueTask.FromResult<Azure.Core.AccessToken>(default);
}

file sealed class FakeResponse : Response
{
    public override int Status => 200;
    public override string ReasonPhrase => "OK";
    public override System.IO.Stream? ContentStream { get => null; set { } }
    public override string ClientRequestId { get => string.Empty; set { } }
    protected override bool ContainsHeader(string name) => false;
    protected override IEnumerable<HttpHeader> EnumerateHeaders() => Array.Empty<HttpHeader>();
    protected override bool TryGetHeader(string name, out string? value) { value = null; return false; }
    protected override bool TryGetHeaderValues(string name, out IEnumerable<string>? values) { values = null; return false; }
    public override void Dispose() { }
}
