# Plaud Token Auto-Refresh — Hangfire Job Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a weekly Hangfire job that rotates the Plaud OAuth refresh token and persists it to Azure Key Vault, eliminating manual token rotation after container restarts.

**Architecture:** `PlaudTokenRefreshClient` (typed `HttpClient` wrapper) calls the Plaud OAuth refresh endpoint. `PlaudTokenRefreshJob` reads the current token from `~/.plaud/tokens.json`, refreshes via the client, validates the response, writes back to disk and Key Vault `Plaud--TokensJson` secret. The job lives in `Anela.Heblo.Adapters.Plaud` (same as `GoogleAdsInvoiceImportJob` pattern) and is manually registered in `PlaudAdapterServiceCollectionExtensions`, then auto-discovered at runtime by `RecurringJobDiscoveryService` via `GetServices<IRecurringJob>()`. A companion bash script grants the Heblo managed identity per-secret write access on `kv-heblo-stg`/`kv-heblo-prod`.

**Tech Stack:** .NET 8, xUnit, Moq, `Azure.Security.KeyVault.Secrets` 4.6.0, `Azure.Identity` 1.13.2, `Microsoft.Extensions.Http` 8.0.0, Hangfire, bash + az CLI

---

## File Map

### Create
| File | Responsibility |
|------|---------------|
| `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokens.cs` | Record matching Plaud refresh response JSON shape |
| `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudTokenRefreshClient.cs` | Testability seam — one method `RefreshAsync` |
| `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshClient.cs` | Typed `HttpClient` wrapper implementing the interface |
| `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshJob.cs` | `IRecurringJob` — weekly refresh flow, validation, disk+KV write |
| `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenRefreshClientTests.cs` | Unit tests using a custom `HttpMessageHandler` stub |
| `backend/test/Anela.Heblo.Tests/Adapters/Plaud/PlaudTokenRefreshJobTests.cs` | Unit tests mocking `IPlaudTokenRefreshClient` + `SecretClient` via Moq |
| `scripts/grant-plaud-token-refresh-permission.sh` | Idempotent az CLI script — seeds KV secret, grants per-secret RBAC |

### Modify
| File | Change |
|------|--------|
| `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj` | Add `Azure.Security.KeyVault.Secrets`, `Azure.Identity`, `Microsoft.Extensions.Http` |
| `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAdapterServiceCollectionExtensions.cs` | Register `SecretClient`, `PlaudTokenRefreshClient`, `PlaudTokenRefreshJob` when KV URI is set |
| `docs/integrations/plaud-token-auto-refresh.md` | Update status from Deferred to Implemented |

---

## Task 1: PlaudTokens record + IPlaudTokenRefreshClient interface

These are pure type definitions with no behavior — no tests needed.

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokens.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudTokenRefreshClient.cs`

- [ ] **Step 1: Create PlaudTokens.cs**

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.Plaud;

public sealed record PlaudTokens(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_at")] long ExpiresAt);
```

- [ ] **Step 2: Create IPlaudTokenRefreshClient.cs**

```csharp
namespace Anela.Heblo.Adapters.Plaud;

public interface IPlaudTokenRefreshClient
{
    Task<PlaudTokens> RefreshAsync(string refreshToken, CancellationToken ct = default);
}
```

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokens.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudTokenRefreshClient.cs
git commit -m "feat(plaud): add PlaudTokens record and IPlaudTokenRefreshClient interface"
```

---

## Task 2: PlaudTokenRefreshClient — TDD

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenRefreshClientTests.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshClient.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Text;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public sealed class PlaudTokenRefreshClientTests
{
    private const string RefreshUrl =
        "https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh";

    private const string ValidResponseJson = """
        {
          "access_token": "new-access",
          "refresh_token": "new-refresh",
          "expires_at": 9999999999
        }
        """;

    private static PlaudTokenRefreshClient CreateClient(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return new PlaudTokenRefreshClient(new HttpClient(handler));
    }

    [Fact]
    public async Task RefreshAsync_ReturnsTokens_WhenResponseIsValid()
    {
        var sut = CreateClient(HttpStatusCode.OK, ValidResponseJson);

        var result = await sut.RefreshAsync("old-refresh");

        result.AccessToken.Should().Be("new-access");
        result.RefreshToken.Should().Be("new-refresh");
        result.ExpiresAt.Should().Be(9999999999L);
    }

    [Fact]
    public async Task RefreshAsync_Throws_WhenResponseIsNonSuccess()
    {
        var sut = CreateClient(HttpStatusCode.Unauthorized, "Unauthorized");

        var act = () => sut.RefreshAsync("old-refresh");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task RefreshAsync_Throws_WhenResponseBodyIsEmpty()
    {
        var sut = CreateClient(HttpStatusCode.OK, "null");

        var act = () => sut.RefreshAsync("old-refresh");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Empty refresh response*");
    }

    [Fact]
    public async Task RefreshAsync_SendsRefreshTokenInBody()
    {
        var capturer = new CapturingHandler(ValidResponseJson);
        var sut = new PlaudTokenRefreshClient(new HttpClient(capturer));

        await sut.RefreshAsync("my-refresh-token");

        capturer.CapturedRequestBody.Should().Contain("my-refresh-token");
        capturer.CapturedRequestUri!.ToString().Should().Be(RefreshUrl);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public string? CapturedRequestBody { get; private set; }
        public Uri? CapturedRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            CapturedRequestUri = request.RequestUri;
            if (request.Content != null)
            {
                await request.Content.LoadIntoBufferAsync();
                CapturedRequestBody = await request.Content.ReadAsStringAsync(ct);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests \
  --filter "PlaudTokenRefreshClientTests" -v minimal
```

Expected: BUILD FAIL or test errors — `PlaudTokenRefreshClient` does not exist yet.

- [ ] **Step 3: Implement PlaudTokenRefreshClient**

```csharp
using System.Net.Http.Json;

namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudTokenRefreshClient(HttpClient http) : IPlaudTokenRefreshClient
{
    public async Task<PlaudTokens> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            "https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh",
            new { refresh_token = refreshToken },
            ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PlaudTokens>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty refresh response from Plaud API");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests \
  --filter "PlaudTokenRefreshClientTests" -v minimal
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshClient.cs \
        backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenRefreshClientTests.cs
git commit -m "feat(plaud): add PlaudTokenRefreshClient with tests"
```

---

## Task 3: PlaudTokenRefreshJob — TDD

The job reads from `~/.plaud/tokens.json` (same as `PlaudTokenBootstrapper`). Tests temporarily override the `HOME` environment variable to control the file path. Skip on Windows (same convention as `PlaudCliClientRunTests`).

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Adapters/Plaud/PlaudTokenRefreshJobTests.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshJob.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
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

    private static readonly PlaudTokens ValidTokens = new(
        AccessToken: "new-access",
        RefreshToken: "new-refresh",
        ExpiresAt: 9999999999L);

    private static readonly string CurrentTokensJson = """
        {"access_token":"old-access","refresh_token":"old-refresh","expires_at":9999999999}
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

        // Disk was written before KV — the new token is on disk even though KV failed.
        var diskContent = await File.ReadAllTextAsync(tokensPath);
        diskContent.Should().Contain("new-refresh");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests \
  --filter "PlaudTokenRefreshJobTests" -v minimal
```

Expected: BUILD FAIL — `PlaudTokenRefreshJob` does not exist yet.

- [ ] **Step 3: Implement PlaudTokenRefreshJob**

```csharp
using System.Text.Json;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Azure.Security.KeyVault.Secrets;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Plaud;

public class PlaudTokenRefreshJob : IRecurringJob
{
    private readonly IPlaudTokenRefreshClient _refreshClient;
    private readonly SecretClient _secretClient;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<PlaudTokenRefreshJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "plaud-token-refresh",
        DisplayName = "Plaud — refresh auth token",
        Description = "Calls Plaud OAuth refresh endpoint weekly and persists the rotated token back to Key Vault so container restarts pick up the fresh value.",
        CronExpression = "0 4 * * 0",
        DefaultIsEnabled = false
    };

    public PlaudTokenRefreshJob(
        IPlaudTokenRefreshClient refreshClient,
        SecretClient secretClient,
        IRecurringJobStatusChecker statusChecker,
        ILogger<PlaudTokenRefreshJob> logger)
    {
        _refreshClient = refreshClient;
        _secretClient = secretClient;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tokensPath = Path.Combine(homeDir, ".plaud", "tokens.json");

        if (!File.Exists(tokensPath))
            throw new InvalidOperationException(
                $"Plaud tokens file not found at {tokensPath}. Run PlaudTokenBootstrapper first.");

        var diskJson = await File.ReadAllTextAsync(tokensPath, cancellationToken);
        var diskTokens = JsonSerializer.Deserialize<PlaudTokens>(diskJson)
            ?? throw new InvalidOperationException("Failed to deserialize Plaud tokens from disk.");

        var newTokens = await _refreshClient.RefreshAsync(diskTokens.RefreshToken, cancellationToken);

        if (string.IsNullOrEmpty(newTokens.AccessToken) || string.IsNullOrEmpty(newTokens.RefreshToken))
            throw new InvalidOperationException(
                "Plaud refresh response has empty tokens. Refusing to overwrite Key Vault.");

        if (newTokens.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            throw new InvalidOperationException(
                $"Plaud refresh response has expires_at={newTokens.ExpiresAt} in the past. Refusing to overwrite Key Vault.");

        var newJson = JsonSerializer.Serialize(newTokens);

        // Write disk first — if KV fails, the running process still has the fresh token.
        await File.WriteAllTextAsync(tokensPath, newJson, cancellationToken);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tokensPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        await _secretClient.SetSecretAsync("Plaud--TokensJson", newJson, cancellationToken);

        _logger.LogInformation(
            "Plaud token refreshed. expires_at={ExpiresAt}", newTokens.ExpiresAt);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests \
  --filter "PlaudTokenRefreshJobTests" -v minimal
```

Expected: 6 tests pass (on macOS/Linux; Windows will show them as skipped).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshJob.cs \
        backend/test/Anela.Heblo.Tests/Adapters/Plaud/PlaudTokenRefreshJobTests.cs
git commit -m "feat(plaud): add PlaudTokenRefreshJob with tests"
```

---

## Task 4: NuGet packages + DI wiring

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAdapterServiceCollectionExtensions.cs`

- [ ] **Step 1: Update the csproj**

In `Anela.Heblo.Adapters.Plaud.csproj`, add to the first `<ItemGroup>`:

```xml
<PackageReference Include="Azure.Security.KeyVault.Secrets" Version="4.6.0" />
<PackageReference Include="Azure.Identity" Version="1.13.2" />
<PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
```

Full updated file:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net8.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <RootNamespace>Anela.Heblo.Adapters.Plaud</RootNamespace>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Azure.Identity" Version="1.13.2" />
        <PackageReference Include="Azure.Security.KeyVault.Secrets" Version="4.6.0" />
        <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="8.0.0" />
        <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
        <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.3" />
        <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="../../Anela.Heblo.Application/Anela.Heblo.Application.csproj" />
    </ItemGroup>

</Project>
```

- [ ] **Step 2: Update PlaudAdapterServiceCollectionExtensions**

Replace the entire file content:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Plaud;

public static class PlaudAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddPlaudAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaudOptions>(configuration.GetSection(PlaudOptions.SectionKey));
        services.AddSingleton<IPlaudClient, PlaudCliClient>();
        services.AddHostedService<PlaudTokenBootstrapper>();

        // Token refresh job requires Key Vault write access.
        // Skip registration in local dev where KeyVault:Uri is unset.
        var keyVaultUri = configuration["KeyVault:Uri"];
        if (!string.IsNullOrWhiteSpace(keyVaultUri))
        {
            services.AddSingleton(new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential()));
            services.AddHttpClient<PlaudTokenRefreshClient>();
            services.AddScoped<IPlaudTokenRefreshClient>(
                sp => sp.GetRequiredService<PlaudTokenRefreshClient>());
            services.AddScoped<IRecurringJob, PlaudTokenRefreshJob>();
        }

        return services;
    }
}
```

- [ ] **Step 3: Build the full solution**

```bash
cd backend && dotnet build && dotnet format --verify-no-changes
```

Expected: Build succeeds with zero warnings on new files, no formatting errors.

- [ ] **Step 4: Run all Plaud tests**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests -v minimal
dotnet test backend/test/Anela.Heblo.Tests --filter "Plaud" -v minimal
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj \
        backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAdapterServiceCollectionExtensions.cs
git commit -m "feat(plaud): wire PlaudTokenRefreshJob into DI with Key Vault SecretClient"
```

---

## Task 5: Bash script — grant-plaud-token-refresh-permission.sh

**Files:**
- Create: `scripts/grant-plaud-token-refresh-permission.sh`

The script has two phases:
- **setup** (default): seed `Plaud--TokensJson` secret in KV (if missing), resolve MI principal ID, grant `Key Vault Secrets Officer` to that MI scoped to the single secret resource ID.
- **cleanup**: remove `Plaud__TokensJson` from Web App App Settings once KV is confirmed as the source of truth.

- [ ] **Step 1: Create the script**

```bash
#!/usr/bin/env bash
# Grant Heblo's managed identity per-secret write access to Plaud--TokensJson in Key Vault.
#
# Topology:
#   stg  -> Web App "heblo-test" + Key Vault "kv-heblo-stg"
#   prod -> Web App "heblo"      + Key Vault "kv-heblo-prod"
#
# Usage:
#   ./scripts/grant-plaud-token-refresh-permission.sh <stg|prod> [--dry-run] [--phase=setup|cleanup] [--force]
#
# Phases:
#   setup (default): seed Plaud--TokensJson in KV from App Settings, grant per-secret RBAC.
#   cleanup:         remove Plaud__TokensJson from Web App App Settings. Run only after
#                    the new build is deployed and verified.
#
# Idempotent — safe to re-run after partial failure.

set -euo pipefail

ENV_ARG="${1:-}"
if [[ -z "$ENV_ARG" || "$ENV_ARG" == "-h" || "$ENV_ARG" == "--help" ]]; then
    cat <<USAGE
Usage: $0 <stg|prod> [--dry-run] [--phase=setup|cleanup] [--force]
USAGE
    exit 1
fi

DRY_RUN=false
PHASE=setup
FORCE=false
for arg in "${@:2}"; do
    case "$arg" in
        --dry-run) DRY_RUN=true ;;
        --phase=*) PHASE="${arg#--phase=}" ;;
        --force)   FORCE=true ;;
        *) echo "unknown arg: $arg" >&2; exit 1 ;;
    esac
done

RG="rgHeblo"
case "$ENV_ARG" in
    prod) WEBAPP="heblo";      KV="kv-heblo-prod" ;;
    stg)  WEBAPP="heblo-test"; KV="kv-heblo-stg"  ;;
    *) echo "env must be 'stg' or 'prod' (got: $ENV_ARG)" >&2; exit 1 ;;
esac

SECRET_NAME="Plaud--TokensJson"
APP_SETTING_KEY="Plaud__TokensJson"

for cmd in az jq; do
    if ! command -v "$cmd" >/dev/null 2>&1; then
        echo "ERROR: required command '$cmd' not found in PATH" >&2
        exit 1
    fi
done

log() { echo "[$(date +%H:%M:%S)] $*"; }

run() {
    if $DRY_RUN; then
        echo "DRY: $*"
    else
        "$@"
    fi
}

# Prod confirmation gate
if [[ "$ENV_ARG" == "prod" && "$DRY_RUN" == false ]]; then
    echo "*** You are about to mutate PRODUCTION (Web App $WEBAPP, Key Vault $KV) ***"
    read -r -p "Type 'PROD' to continue: " confirm
    if [[ "$confirm" != "PROD" ]]; then
        echo "aborted"; exit 1
    fi
fi

phase_setup() {
    log "Env=$ENV_ARG  WebApp=$WEBAPP  KeyVault=$KV  ResourceGroup=$RG  DryRun=$DRY_RUN"

    # 1. Verify Key Vault exists
    if ! az keyvault show -n "$KV" -g "$RG" -o none 2>/dev/null; then
        echo "ERROR: Key Vault $KV not found in $RG. Run migrate-secrets-to-keyvault.sh first." >&2
        exit 1
    fi
    log "Key Vault $KV exists"

    # 2. Seed Plaud--TokensJson from App Settings (initial seed)
    if $DRY_RUN; then
        log "DRY: would read $APP_SETTING_KEY from $WEBAPP App Settings and seed $SECRET_NAME"
    else
        secret_exists=$(az keyvault secret show --vault-name "$KV" --name "$SECRET_NAME" \
            --query 'id' -o tsv 2>/dev/null || echo "")
        if [[ -n "$secret_exists" && "$FORCE" == false ]]; then
            log "$SECRET_NAME already exists in $KV (use --force to overwrite)"
        else
            value=$(az webapp config appsettings list -g "$RG" -n "$WEBAPP" -o json \
                | jq -r --arg k "$APP_SETTING_KEY" '.[] | select(.name==$k) | .value' 2>/dev/null || echo "")
            if [[ -z "$value" || "$value" == "null" ]]; then
                echo "ERROR: $APP_SETTING_KEY not found in $WEBAPP App Settings." >&2
                echo "       Manually paste the current tokens JSON and run again, or set it first." >&2
                exit 1
            fi
            log "Seeding $SECRET_NAME from $APP_SETTING_KEY"
            az keyvault secret set --vault-name "$KV" --name "$SECRET_NAME" --value "$value" -o none
            log "  Seeded."
        fi
    fi

    # 3. Resolve Heblo MI principal ID
    if $DRY_RUN; then
        PRINCIPAL_ID="<dry-run-principal-id>"
    else
        PRINCIPAL_ID=$(az webapp identity show -g "$RG" -n "$WEBAPP" --query principalId -o tsv 2>/dev/null || echo "")
        if [[ -z "$PRINCIPAL_ID" ]]; then
            echo "ERROR: No managed identity on $WEBAPP. Assign one first:" >&2
            echo "       az webapp identity assign -g $RG -n $WEBAPP" >&2
            exit 1
        fi
    fi
    log "MI principalId=$PRINCIPAL_ID"

    # 4. Resolve KV resource ID (needed for the scope)
    KV_ID=$(az keyvault show -n "$KV" -g "$RG" --query id -o tsv 2>/dev/null || echo "<dry-run-kv-id>")

    # 5. Scope = secret-level resource ID (per-secret RBAC, not vault-wide)
    SECRET_SCOPE="$KV_ID/secrets/$SECRET_NAME"
    log "Granting 'Key Vault Secrets Officer' to MI on scope: $SECRET_SCOPE"

    run az role assignment create \
        --assignee-object-id "$PRINCIPAL_ID" \
        --assignee-principal-type ServicePrincipal \
        --role "Key Vault Secrets Officer" \
        --scope "$SECRET_SCOPE" \
        -o none 2>/dev/null || log "  (assignment already exists or dry-run)"

    cat <<DONE

Setup complete for env=$ENV_ARG.

Next steps:
  1. Deploy the Heblo build that includes PlaudTokenRefreshJob.
  2. Restart: az webapp restart -g $RG -n $WEBAPP
  3. Enable the job via Background Jobs admin UI (DefaultIsEnabled=false).
  4. Trigger manually from Hangfire dashboard to verify end-to-end.
  5. Confirm new secret version:
     az keyvault secret show --vault-name $KV --name $SECRET_NAME \\
         --query 'attributes.{updated:updated,version:id}' -o table
  6. After 1 week stable, run cleanup:
     $0 $ENV_ARG --phase=cleanup
DONE
}

phase_cleanup() {
    log "Env=$ENV_ARG  WebApp=$WEBAPP  KeyVault=$KV  Cleanup App Setting  DryRun=$DRY_RUN"

    if ! $DRY_RUN; then
        if ! az keyvault secret show --vault-name "$KV" --name "$SECRET_NAME" -o none 2>/dev/null; then
            echo "ERROR: $SECRET_NAME not found in $KV. Run --phase=setup first." >&2
            exit 1
        fi
    fi

    if ! $DRY_RUN; then
        echo
        echo "*** This will DELETE App Setting '$APP_SETTING_KEY' from $WEBAPP ***"
        read -r -p "Type 'CLEANUP' to continue: " confirm
        if [[ "$confirm" != "CLEANUP" ]]; then
            echo "aborted"; exit 1
        fi
    fi

    has_setting=$(az webapp config appsettings list -g "$RG" -n "$WEBAPP" -o json \
        | jq -r --arg k "$APP_SETTING_KEY" '[.[] | select(.name==$k)] | length' 2>/dev/null || echo "0")

    if [[ "$has_setting" == "0" ]]; then
        log "$APP_SETTING_KEY not present in App Settings (already cleaned up or never set)"
    else
        log "Deleting $APP_SETTING_KEY from $WEBAPP App Settings"
        run az webapp config appsettings delete -g "$RG" -n "$WEBAPP" \
            --setting-names "$APP_SETTING_KEY" -o none
        log "Done."
    fi
}

case "$PHASE" in
    setup)   phase_setup ;;
    cleanup) phase_cleanup ;;
    *) echo "unknown phase: $PHASE (use setup or cleanup)" >&2; exit 1 ;;
esac
```

- [ ] **Step 2: Make the script executable**

```bash
chmod +x scripts/grant-plaud-token-refresh-permission.sh
```

- [ ] **Step 3: Verify dry-run executes without errors**

```bash
./scripts/grant-plaud-token-refresh-permission.sh stg --dry-run
```

Expected: prints DRY: lines with no bash errors.

- [ ] **Step 4: Verify bash syntax**

```bash
bash -n scripts/grant-plaud-token-refresh-permission.sh
```

Expected: no output (syntax OK).

- [ ] **Step 5: Commit**

```bash
git add scripts/grant-plaud-token-refresh-permission.sh
git commit -m "feat(plaud): add az CLI script to grant per-secret KV write access"
```

---

## Task 6: Update documentation

**Files:**
- Modify: `docs/integrations/plaud-token-auto-refresh.md`

- [ ] **Step 1: Update the doc header and add implementation section**

Replace lines 1–4 (status block) with:

```markdown
# Plaud Token Auto-Refresh (Implemented 2026-05-28)

> **Status:** Implemented — weekly Hangfire job `plaud-token-refresh` rotates the token and
> persists it to Key Vault `Plaud--TokensJson`. Job is disabled by default; enable via
> Background Jobs admin UI after running the RBAC setup script.
```

Add a new section after the existing content (before "## Observed Refresh Endpoint"):

```markdown
## How It Works

1. `PlaudTokenRefreshJob` (`backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshJob.cs`)
   runs weekly (Sunday 04:00 Europe/Prague, `0 4 * * 0`).
2. Reads the current refresh token from `~/.plaud/tokens.json` on disk.
3. Calls `PlaudTokenRefreshClient` (`PlaudTokenRefreshClient.cs`) → Plaud OAuth refresh endpoint.
4. Validates the response: non-empty tokens, `expires_at` in the future. Throws if invalid — KV is
   never overwritten with garbage.
5. Writes new token JSON to disk first (with 0600 permissions), then to Key Vault `Plaud--TokensJson`.
6. `PlaudTokenBootstrapper` picks up the fresh token on the next container restart via
   `IConfiguration` (Key Vault is wired in Program.cs with 30-minute reload).

**RBAC setup (once per env):**
```bash
./scripts/grant-plaud-token-refresh-permission.sh stg
./scripts/grant-plaud-token-refresh-permission.sh stg --phase=cleanup  # after verified
```

**Rollback** — promote the prior KV secret version:
```bash
az keyvault secret list-versions --vault-name kv-heblo-prod --name Plaud--TokensJson -o table
az keyvault secret set --vault-name kv-heblo-prod --name Plaud--TokensJson \
    --value "$(az keyvault secret show --vault-name kv-heblo-prod --name Plaud--TokensJson \
        --version <prev-version-id> --query value -o tsv)"
az webapp restart -g rgHeblo -n heblo
```
```

- [ ] **Step 2: Commit**

```bash
git add docs/integrations/plaud-token-auto-refresh.md
git commit -m "docs(plaud): update token auto-refresh doc to reflect implemented status"
```

---

## Self-Review

### Spec coverage check

| Spec requirement | Covered by |
|-----------------|------------|
| Weekly Hangfire job (Sunday 04:00) | Task 3 — `CronExpression = "0 4 * * 0"` |
| Read refresh token from `~/.plaud/tokens.json` | Task 3 — reads disk first |
| Call Plaud OAuth refresh endpoint | Task 2 — `PlaudTokenRefreshClient` |
| Validate response before writing | Task 3 — empty token + expired `expires_at` guards |
| Write disk first, then KV | Task 3 — explicit ordering, test verifies |
| KV secret name `Plaud--TokensJson` | Task 3 — hardcoded in `SetSecretAsync` call |
| `DefaultIsEnabled = false` | Task 3 — `RecurringJobMetadata` |
| Local dev guard (skip when KV URI unset) | Task 4 — `if (!string.IsNullOrWhiteSpace(keyVaultUri))` |
| Azure RBAC bash script (idempotent, dry-run, prod gate, setup+cleanup phases) | Task 5 |
| Update doc from Deferred → Implemented | Task 6 |
| 4 client unit tests | Task 2 |
| 6 job unit tests | Task 3 |

### Placeholder scan

No TBDs, TODOs, or "similar to Task N" phrases found.

### Type consistency

- `PlaudTokens` record defined in Task 1, used in Task 2 (`PlaudTokenRefreshClient` return type) and Task 3 (disk deserialization + validation).
- `IPlaudTokenRefreshClient` defined in Task 1, implemented by `PlaudTokenRefreshClient` (Task 2), injected into `PlaudTokenRefreshJob` (Task 3), registered in DI (Task 4).
- `SecretClient` used in Task 3 (`PlaudTokenRefreshJob`), registered in Task 4.
- KV secret name `"Plaud--TokensJson"` — consistent in Task 3 job, Task 5 script, Task 6 docs.
