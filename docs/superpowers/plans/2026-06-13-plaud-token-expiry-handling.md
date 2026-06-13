# Plaud CLI Auth Token Expiry Handling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `PlaudCliClient` self-healing against Plaud refresh-token expiry by adding proactive expiry detection, single-flight in-line refresh against the existing Plaud OAuth endpoint, and structured telemetry — while enabling the existing weekly Hangfire safety-net job by default and shipping an operator runbook for the one failure mode the system cannot recover from automatically (a dead refresh token).

**Architecture:** Introduce two new singleton collaborators inside `Anela.Heblo.Adapters.Plaud` — `IPlaudTokenManager` (owns the in-memory `PlaudTokens` cache, a `SemaphoreSlim` for single-flight refresh, and orchestrates the refresh flow + emits telemetry) and `IPlaudTokenStore` (writes the new tuple disk-first then to Key Vault, returning a non-fatal flag when the KV write fails). `PlaudCliClient.RunCliAsync` calls `EnsureFreshAsync` before each shell-out (no-op on the happy path) and `ForceRefreshAsync` + one retry when the CLI stderr contains `AUTH_FAILED`. The existing `PlaudTokenRefreshJob` is refactored to delegate KV/disk writes to `IPlaudTokenStore`, and its `DefaultIsEnabled` is flipped to `true`.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, Moq/NSubstitute, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging`, `Azure.Security.KeyVault.Secrets`, `Anela.Heblo.Xcc.Telemetry.ITelemetryService`, Hangfire (existing). All tests use fake `HttpMessageHandler` patterns — **no test ever calls `platform.plaud.ai`**.

---

## File Structure

**New source files** (in `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/`):
- `PlaudCredentialsOptions.cs` — typed options (`ExpiryBuffer`, `RefreshTimeout`, `TokensJsonSecretName`).
- `PlaudTelemetryEventNames.cs` — string constants for the 4 custom event names.
- `IPlaudTokenStore.cs` + `PlaudTokenStore.cs` — disk + KV read/write abstraction.
- `PlaudTokenSaveResult.cs` — record returned by `IPlaudTokenStore.SaveAsync`.
- `IPlaudTokenManager.cs` + `PlaudTokenManager.cs` — cache, single-flight, telemetry, orchestration.
- `PlaudTokenRefreshTrigger.cs` — enum (`NearExpiry`, `AuthFailedRetry`) used for telemetry `triggeredBy` property.

**Modified source files**:
- `PlaudCliClient.cs` — inject `IPlaudTokenManager` + `ITelemetryService`; call `EnsureFreshAsync` before each `RunCliAsync`; on `AUTH_FAILED`, call `ForceRefreshAsync` and retry once.
- `PlaudTokenRefreshJob.cs` — delegate disk+KV write to `IPlaudTokenStore`; flip `DefaultIsEnabled` to `true`.
- `PlaudAdapterServiceCollectionExtensions.cs` — register new types as singletons; bind `PlaudCredentialsOptions`.

**New test files** (in `backend/test/Anela.Heblo.Adapters.Plaud.Tests/`):
- `PlaudTokenStoreTests.cs`
- `PlaudTokenManagerTests.cs`
- `PlaudCliClientRefreshRetryTests.cs` (separate file; the existing `PlaudCliClientRunTests.cs` stays for stderr-parsing coverage)

**New operational doc**:
- `docs/operations/plaud-token-rotation.md` — runbook referenced from alerts.

---

## Pre-flight

### Task 0: Verify baseline build passes

- [ ] **Step 1: Build the solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: succeeds with 0 errors.

- [ ] **Step 2: Run the existing Plaud adapter tests**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --no-build`
Expected: all tests pass.

If either fails, fix or report blocker before proceeding. Every later task assumes a clean baseline.

---

## Task 1: Typed credential options

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCredentialsOptions.cs`

- [ ] **Step 1: Create the options class**

Write `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCredentialsOptions.cs`:

```csharp
namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudCredentialsOptions
{
    public const string SectionKey = "Plaud:Credentials";

    public string TokensJsonSecretName { get; init; } = "Plaud--TokensJson";
    public TimeSpan ExpiryBuffer { get; init; } = TimeSpan.FromHours(72);
    public TimeSpan RefreshTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCredentialsOptions.cs
git commit -m "feat(plaud): add typed PlaudCredentialsOptions for refresh tuning"
```

---

## Task 2: Telemetry event-name constants

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTelemetryEventNames.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshTrigger.cs`

- [ ] **Step 1: Create the event-name constants**

Write `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTelemetryEventNames.cs`:

```csharp
namespace Anela.Heblo.Adapters.Plaud;

internal static class PlaudTelemetryEventNames
{
    public const string NearExpiry = "PlaudTokenNearExpiry";
    public const string Expired = "PlaudTokenExpired";
    public const string Refreshed = "PlaudTokenRefreshed";
    public const string RefreshFailed = "PlaudTokenRefreshFailed";
    public const string KeyVaultWriteFailed = "PlaudTokenRefreshKeyVaultWriteFailed";
}
```

- [ ] **Step 2: Create the trigger enum**

Write `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshTrigger.cs`:

```csharp
namespace Anela.Heblo.Adapters.Plaud;

internal enum PlaudTokenRefreshTrigger
{
    NearExpiry,
    AuthFailedRetry
}
```

- [ ] **Step 3: Build**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTelemetryEventNames.cs backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshTrigger.cs
git commit -m "feat(plaud): add telemetry event-name constants and refresh-trigger enum"
```

---

## Task 3: `PlaudTokenSaveResult` record

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenSaveResult.cs`

- [ ] **Step 1: Create the record**

Write `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenSaveResult.cs`:

```csharp
namespace Anela.Heblo.Adapters.Plaud;

internal sealed record PlaudTokenSaveResult(bool KeyVaultWriteFailed, Exception? KeyVaultError);
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenSaveResult.cs
git commit -m "feat(plaud): add PlaudTokenSaveResult for split disk/KV write outcomes"
```

---

## Task 4: `IPlaudTokenStore` abstraction

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudTokenStore.cs`

- [ ] **Step 1: Create the interface**

Write `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudTokenStore.cs`:

```csharp
namespace Anela.Heblo.Adapters.Plaud;

internal interface IPlaudTokenStore
{
    /// <summary>
    /// Loads the current tokens from disk (~/.plaud/tokens.json).
    /// Throws if the disk file is missing or unreadable — PlaudTokenBootstrapper must run first.
    /// </summary>
    Task<PlaudTokens> LoadAsync(CancellationToken ct);

    /// <summary>
    /// Persists the tokens disk-first, then to Key Vault.
    /// Throws if the disk write fails (fatal — CLI would still see the old token).
    /// If the KV write fails, returns KeyVaultWriteFailed=true with the captured exception.
    /// </summary>
    Task<PlaudTokenSaveResult> SaveAsync(PlaudTokens tokens, CancellationToken ct);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudTokenStore.cs
git commit -m "feat(plaud): add IPlaudTokenStore abstraction (disk + KV)"
```

---

## Task 5: `PlaudTokenStore` — failing test for `LoadAsync` (missing file throws)

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenStoreTests.cs`

- [ ] **Step 1: Write the failing test**

Write `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenStoreTests.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
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
        Environment.SetEnvironmentVariable("PLAUD_TEST_HOME", _tempHome);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PLAUD_TEST_HOME", null);
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
        var sut = CreateSut();

        Func<Task> act = () => sut.LoadAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*tokens.json*");
    }
}
```

Add the test-only fake secret client at the bottom of the same file:

```csharp
file sealed class FakeSecretClient : SecretClient
{
    private readonly Func<string, string, CancellationToken, Response<KeyVaultSecret>> _setBehavior;
    public List<(string Name, string Value)> Writes { get; } = new();

    private FakeSecretClient(Func<string, string, CancellationToken, Response<KeyVaultSecret>> setBehavior)
        : base(new Uri("https://fake.vault.azure.net/"), new FakeCredential())
    {
        _setBehavior = setBehavior;
    }

    public static FakeSecretClient AlwaysSucceeds() => new((name, value, ct) =>
        Response.FromValue(new KeyVaultSecret(name, value), new FakeResponse()));

    public static FakeSecretClient AlwaysThrows(Exception ex) => new((_, _, _) => throw ex);

    public override Task<Response<KeyVaultSecret>> SetSecretAsync(
        string name, string value, CancellationToken cancellationToken = default)
    {
        Writes.Add((name, value));
        return Task.FromResult(_setBehavior(name, value, cancellationToken));
    }
}

file sealed class FakeCredential : Azure.Core.TokenCredential
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
```

- [ ] **Step 2: Run the test — expect FAIL (PlaudTokenStore does not exist yet)**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --filter "FullyQualifiedName~PlaudTokenStoreTests"`
Expected: Compilation error (type `PlaudTokenStore` not found).

- [ ] **Step 3: Commit (red)**

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenStoreTests.cs
git commit -m "test(plaud): add PlaudTokenStore failing test for missing disk file"
```

---

## Task 6: `PlaudTokenStore` — minimal implementation for `LoadAsync`

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenStore.cs`

- [ ] **Step 1: Create the implementation**

Write `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenStore.cs`:

```csharp
using System.Text.Json;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud;

internal sealed class PlaudTokenStore : IPlaudTokenStore
{
    private readonly SecretClient _secretClient;
    private readonly IOptions<PlaudCredentialsOptions> _options;
    private readonly ILogger<PlaudTokenStore> _logger;
    private readonly string _homeDir;

    public PlaudTokenStore(
        SecretClient secretClient,
        IOptions<PlaudCredentialsOptions> options,
        ILogger<PlaudTokenStore> logger,
        string? homeDirOverride = null)
    {
        _secretClient = secretClient;
        _options = options;
        _logger = logger;
        _homeDir = homeDirOverride
            ?? Environment.GetEnvironmentVariable("PLAUD_TEST_HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private string TokensPath => Path.Combine(_homeDir, ".plaud", "tokens.json");

    public async Task<PlaudTokens> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(TokensPath))
            throw new InvalidOperationException(
                $"Plaud tokens.json not found at {TokensPath}. PlaudTokenBootstrapper must run first.");

        var json = await File.ReadAllTextAsync(TokensPath, ct);
        return JsonSerializer.Deserialize<PlaudTokens>(json)
            ?? throw new InvalidOperationException("Failed to deserialize Plaud tokens from disk.");
    }

    public Task<PlaudTokenSaveResult> SaveAsync(PlaudTokens tokens, CancellationToken ct) =>
        throw new NotImplementedException();
}
```

- [ ] **Step 2: Run the test — expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --filter "FullyQualifiedName~PlaudTokenStoreTests"`
Expected: 1 PASS (`LoadAsync_Throws_WhenDiskFileMissing`).

- [ ] **Step 3: Commit (green)**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenStore.cs
git commit -m "feat(plaud): minimal PlaudTokenStore.LoadAsync (disk-only)"
```

---

## Task 7: `PlaudTokenStore.LoadAsync` happy-path test

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenStoreTests.cs`

- [ ] **Step 1: Add the happy-path test**

Append to `PlaudTokenStoreTests` (before `Dispose`):

```csharp
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
```

- [ ] **Step 2: Run the test — expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --filter "FullyQualifiedName~PlaudTokenStoreTests"`
Expected: 2 PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenStoreTests.cs
git commit -m "test(plaud): PlaudTokenStore.LoadAsync happy-path"
```

---

## Task 8: `PlaudTokenStore.SaveAsync` — disk-success + KV-success test

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenStoreTests.cs`

- [ ] **Step 1: Add the failing test**

Append:

```csharp
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
```

- [ ] **Step 2: Run test — expect FAIL (NotImplementedException)**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --filter "FullyQualifiedName~SaveAsync_WritesDiskThenKeyVault"`
Expected: FAIL.

- [ ] **Step 3: Implement `SaveAsync`**

Replace the `SaveAsync` method body in `PlaudTokenStore.cs`:

```csharp
    public async Task<PlaudTokenSaveResult> SaveAsync(PlaudTokens tokens, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(tokens);

        // Disk first — CLI reads this file on every invocation.
        var tokensPath = TokensPath;
        var dir = Path.GetDirectoryName(tokensPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(tokensPath, json, ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tokensPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        // KV second — non-fatal on failure (in-process and disk are already fresh).
        try
        {
            await _secretClient.SetSecretAsync(_options.Value.TokensJsonSecretName, json, ct);
            return new PlaudTokenSaveResult(KeyVaultWriteFailed: false, KeyVaultError: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Plaud token KV write failed (disk update succeeded)");
            return new PlaudTokenSaveResult(KeyVaultWriteFailed: true, KeyVaultError: ex);
        }
    }
```

- [ ] **Step 4: Run test — expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --filter "FullyQualifiedName~PlaudTokenStoreTests"`
Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenStore.cs backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenStoreTests.cs
git commit -m "feat(plaud): PlaudTokenStore.SaveAsync writes disk-first then KV"
```

---

## Task 9: `PlaudTokenStore.SaveAsync` — KV-failure non-fatal test

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenStoreTests.cs`

- [ ] **Step 1: Add the test**

Append:

```csharp
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
```

- [ ] **Step 2: Run test — expect PASS** (already handled by Task 8 implementation)

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --filter "FullyQualifiedName~PlaudTokenStoreTests"`
Expected: 4 PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenStoreTests.cs
git commit -m "test(plaud): PlaudTokenStore.SaveAsync KV failure is non-fatal"
```

---

## Task 10: `PlaudTokenStore.SaveAsync` — disk-failure is fatal test

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenStoreTests.cs`

- [ ] **Step 1: Add the test**

Append:

```csharp
    [Fact]
    public async Task SaveAsync_Throws_WhenDiskWriteFails()
    {
        Skip.If(OperatingSystem.IsWindows(), "Read-only-dir trick is unix-only");

        // Make ~/.plaud unwritable by replacing it with a read-only file
        var plaudDir = Path.Combine(_tempHome, ".plaud");
        Directory.Delete(plaudDir, recursive: true);
        await File.WriteAllTextAsync(plaudDir, "blocker");

        var sut = CreateSut();
        var tokens = new PlaudTokens("a", "r", 9999999999L);

        Func<Task> act = () => sut.SaveAsync(tokens, CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
    }
```

Mark the test class with `[SkippableFact]` (it already uses `Skip.If`). If the project doesn't have `Xunit.SkippableFact` referenced from the existing `PlaudCliClientRunTests`, it does — keep the existing using directives.

- [ ] **Step 2: Change `[Fact]` to `[SkippableFact]` on this test only**

Replace `[Fact]` with `[SkippableFact]` on the test added in Step 1.

- [ ] **Step 3: Run test — expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --filter "FullyQualifiedName~PlaudTokenStoreTests"`
Expected: 5 PASS (or 4 PASS + 1 SKIP on Windows).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenStoreTests.cs
git commit -m "test(plaud): PlaudTokenStore.SaveAsync disk failure surfaces as IOException"
```

---

## Task 11: `IPlaudTokenManager` interface

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudTokenManager.cs`

- [ ] **Step 1: Create the interface**

Write `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudTokenManager.cs`:

```csharp
namespace Anela.Heblo.Adapters.Plaud;

internal interface IPlaudTokenManager
{
    /// <summary>
    /// No-op on the happy path. If the cached token is inside ExpiryBuffer of expiry,
    /// triggers a single-flight refresh, persists via IPlaudTokenStore, and updates the cache.
    /// </summary>
    Task EnsureFreshAsync(CancellationToken ct);

    /// <summary>
    /// Forces a refresh now (called after the CLI returns AUTH_FAILED).
    /// Returns true on success (caller may retry the CLI). Returns false when the refresh itself
    /// fails — caller surfaces PlaudAuthExpiredException so the runbook fires.
    /// Single-flight: concurrent callers await the same refresh task.
    /// </summary>
    Task<bool> ForceRefreshAsync(CancellationToken ct);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudTokenManager.cs
git commit -m "feat(plaud): add IPlaudTokenManager abstraction"
```

---

## Task 12: `PlaudTokenManager` — failing test for happy path (no refresh)

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs`

- [ ] **Step 1: Write the test scaffold + first test**

Write `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs`:

```csharp
using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public sealed class PlaudTokenManagerTests
{
    private static long UnixSecondsFromNow(TimeSpan offset) =>
        DateTimeOffset.UtcNow.Add(offset).ToUnixTimeSeconds();

    private static (PlaudTokenManager Sut,
                    Mock<IPlaudTokenStore> Store,
                    Mock<IPlaudTokenRefreshClient> Refresh,
                    Mock<ITelemetryService> Telemetry)
        CreateSut(PlaudTokens initial, PlaudCredentialsOptions? opts = null)
    {
        var store = new Mock<IPlaudTokenStore>();
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(initial);
        store.Setup(s => s.SaveAsync(It.IsAny<PlaudTokens>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new PlaudTokenSaveResult(false, null));

        var refresh = new Mock<IPlaudTokenRefreshClient>();
        var telemetry = new Mock<ITelemetryService>();

        var sut = new PlaudTokenManager(
            store.Object,
            refresh.Object,
            telemetry.Object,
            Options.Create(opts ?? new PlaudCredentialsOptions()),
            NullLogger<PlaudTokenManager>.Instance);

        return (sut, store, refresh, telemetry);
    }

    [Fact]
    public async Task EnsureFreshAsync_DoesNothing_WhenTokenIsWellOutsideBuffer()
    {
        var initial = new PlaudTokens("a", "r", UnixSecondsFromNow(TimeSpan.FromDays(20)));
        var (sut, _, refresh, telemetry) = CreateSut(initial);

        await sut.EnsureFreshAsync(CancellationToken.None);

        refresh.Verify(r => r.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        telemetry.Verify(t => t.TrackBusinessEvent(
            It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, double>>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run — expect FAIL (PlaudTokenManager does not exist)**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --filter "FullyQualifiedName~PlaudTokenManagerTests"`
Expected: Compilation error (type not found).

- [ ] **Step 3: Commit (red)**

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs
git commit -m "test(plaud): PlaudTokenManager happy-path no-refresh failing test"
```

---

## Task 13: `PlaudTokenManager` — minimal implementation (happy path only)

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenManager.cs`

- [ ] **Step 1: Implement the bare minimum**

Write `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenManager.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud;

internal sealed class PlaudTokenManager : IPlaudTokenManager
{
    private readonly IPlaudTokenStore _store;
    private readonly IPlaudTokenRefreshClient _refreshClient;
    private readonly ITelemetryService _telemetry;
    private readonly IOptions<PlaudCredentialsOptions> _options;
    private readonly ILogger<PlaudTokenManager> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly byte[] _hmacKey = RandomNumberGenerator.GetBytes(32);

    private PlaudTokens? _cached;
    private bool _loaded;

    public PlaudTokenManager(
        IPlaudTokenStore store,
        IPlaudTokenRefreshClient refreshClient,
        ITelemetryService telemetry,
        IOptions<PlaudCredentialsOptions> options,
        ILogger<PlaudTokenManager> logger)
    {
        _store = store;
        _refreshClient = refreshClient;
        _telemetry = telemetry;
        _options = options;
        _logger = logger;
    }

    public async Task EnsureFreshAsync(CancellationToken ct)
    {
        var tokens = await GetCachedAsync(ct);
        if (!IsInsideExpiryBuffer(tokens)) return;

        await RefreshUnderSemaphoreAsync(PlaudTokenRefreshTrigger.NearExpiry, ct);
    }

    public Task<bool> ForceRefreshAsync(CancellationToken ct) =>
        throw new NotImplementedException();

    private async Task<PlaudTokens> GetCachedAsync(CancellationToken ct)
    {
        if (_loaded && _cached is not null) return _cached;

        await _semaphore.WaitAsync(ct);
        try
        {
            if (_loaded && _cached is not null) return _cached;
            _cached = await _store.LoadAsync(ct);
            _loaded = true;
            return _cached;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private bool IsInsideExpiryBuffer(PlaudTokens tokens)
    {
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(tokens.ExpiresAt);
        return expiresAt - DateTimeOffset.UtcNow <= _options.Value.ExpiryBuffer;
    }

    private async Task RefreshUnderSemaphoreAsync(PlaudTokenRefreshTrigger trigger, CancellationToken ct)
    {
        // Placeholder; filled out in later tasks.
        await Task.CompletedTask;
    }

    internal string ComputeTokenIdShort(string refreshToken)
    {
        using var hmac = new HMACSHA256(_hmacKey);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes).Substring(0, 4).ToLowerInvariant();
    }
}
```

- [ ] **Step 2: Run test — expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --filter "FullyQualifiedName~EnsureFreshAsync_DoesNothing"`
Expected: PASS.

- [ ] **Step 3: Commit (green)**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenManager.cs
git commit -m "feat(plaud): minimal PlaudTokenManager (happy path, no-op)"
```

---

## Task 14: `PlaudTokenManager.EnsureFreshAsync` — near-expiry triggers refresh

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenManager.cs`

- [ ] **Step 1: Add the failing test**

Append to `PlaudTokenManagerTests`:

```csharp
    [Fact]
    public async Task EnsureFreshAsync_RefreshesAndEmitsNearExpiry_WhenInsideBuffer()
    {
        var initial = new PlaudTokens("old-a", "old-r", UnixSecondsFromNow(TimeSpan.FromHours(24)));
        var rotated = new PlaudTokens("new-a", "new-r", UnixSecondsFromNow(TimeSpan.FromDays(30)));
        var (sut, store, refresh, telemetry) = CreateSut(initial);

        refresh.Setup(r => r.RefreshAsync("old-r", It.IsAny<CancellationToken>())).ReturnsAsync(rotated);

        await sut.EnsureFreshAsync(CancellationToken.None);

        refresh.Verify(r => r.RefreshAsync("old-r", It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.SaveAsync(rotated, It.IsAny<CancellationToken>()), Times.Once);
        telemetry.Verify(t => t.TrackBusinessEvent(
            PlaudTelemetryEventNames.NearExpiry,
            It.Is<Dictionary<string, string>>(d => d.ContainsKey("expiresAt") && d.ContainsKey("bufferHours") && d.ContainsKey("tokenIdShort")),
            It.IsAny<Dictionary<string, double>>()),
            Times.Once);
        telemetry.Verify(t => t.TrackBusinessEvent(
            PlaudTelemetryEventNames.Refreshed,
            It.Is<Dictionary<string, string>>(d => d["triggeredBy"] == "near-expiry"),
            It.IsAny<Dictionary<string, double>>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet test ... --filter "FullyQualifiedName~EnsureFreshAsync_RefreshesAndEmitsNearExpiry"`
Expected: FAIL (refresh count is 0).

- [ ] **Step 3: Implement `RefreshUnderSemaphoreAsync`**

Replace `RefreshUnderSemaphoreAsync` in `PlaudTokenManager.cs`:

```csharp
    private async Task<bool> RefreshUnderSemaphoreAsync(PlaudTokenRefreshTrigger trigger, CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            // Re-check after acquiring: a concurrent caller may have already refreshed.
            var current = _cached ?? await _store.LoadAsync(ct);
            if (!IsInsideExpiryBuffer(current) && trigger == PlaudTokenRefreshTrigger.NearExpiry)
            {
                return true;
            }

            var tokenIdShort = ComputeTokenIdShort(current.RefreshToken);

            if (trigger == PlaudTokenRefreshTrigger.NearExpiry)
            {
                _telemetry.TrackBusinessEvent(PlaudTelemetryEventNames.NearExpiry,
                    new Dictionary<string, string>
                    {
                        ["expiresAt"] = current.ExpiresAt.ToString(),
                        ["bufferHours"] = _options.Value.ExpiryBuffer.TotalHours.ToString("0"),
                        ["tokenIdShort"] = tokenIdShort
                    });
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.Value.RefreshTimeout);

            PlaudTokens rotated;
            try
            {
                rotated = await _refreshClient.RefreshAsync(current.RefreshToken, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                _telemetry.TrackBusinessEvent(PlaudTelemetryEventNames.RefreshFailed,
                    new Dictionary<string, string>
                    {
                        ["reason"] = "Timeout",
                        ["tokenIdShort"] = tokenIdShort
                    });
                _logger.LogError("Plaud token refresh timed out after {Timeout}", _options.Value.RefreshTimeout);
                return false;
            }
            catch (Exception ex)
            {
                _telemetry.TrackBusinessEvent(PlaudTelemetryEventNames.RefreshFailed,
                    new Dictionary<string, string>
                    {
                        ["reason"] = "HttpError",
                        ["tokenIdShort"] = tokenIdShort
                    });
                _logger.LogError(ex, "Plaud token refresh failed");
                return false;
            }

            if (string.IsNullOrEmpty(rotated.AccessToken) || string.IsNullOrEmpty(rotated.RefreshToken))
            {
                _telemetry.TrackBusinessEvent(PlaudTelemetryEventNames.RefreshFailed,
                    new Dictionary<string, string>
                    {
                        ["reason"] = "EmptyResponse",
                        ["tokenIdShort"] = tokenIdShort
                    });
                return false;
            }

            if (rotated.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                _telemetry.TrackBusinessEvent(PlaudTelemetryEventNames.RefreshFailed,
                    new Dictionary<string, string>
                    {
                        ["reason"] = "ExpiredInResponse",
                        ["tokenIdShort"] = tokenIdShort
                    });
                return false;
            }

            PlaudTokenSaveResult saveResult;
            try
            {
                saveResult = await _store.SaveAsync(rotated, ct);
            }
            catch (Exception ex)
            {
                _telemetry.TrackBusinessEvent(PlaudTelemetryEventNames.RefreshFailed,
                    new Dictionary<string, string>
                    {
                        ["reason"] = "DiskWriteFailed",
                        ["tokenIdShort"] = tokenIdShort
                    });
                _logger.LogError(ex, "Plaud token disk write failed; in-memory cache NOT updated");
                return false;
            }

            // Disk + in-memory updated. KV update may have failed — non-fatal.
            _cached = rotated;
            _loaded = true;

            if (saveResult.KeyVaultWriteFailed)
            {
                _telemetry.TrackBusinessEvent(PlaudTelemetryEventNames.KeyVaultWriteFailed,
                    new Dictionary<string, string>
                    {
                        ["tokenIdShort"] = ComputeTokenIdShort(rotated.RefreshToken)
                    });
            }

            _telemetry.TrackBusinessEvent(PlaudTelemetryEventNames.Refreshed,
                new Dictionary<string, string>
                {
                    ["expiresAt"] = rotated.ExpiresAt.ToString(),
                    ["tokenIdShort"] = ComputeTokenIdShort(rotated.RefreshToken),
                    ["triggeredBy"] = trigger == PlaudTokenRefreshTrigger.NearExpiry ? "near-expiry" : "auth-failed-retry"
                });

            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }
```

Also change the signature of `EnsureFreshAsync` so it discards the return value:

```csharp
    public async Task EnsureFreshAsync(CancellationToken ct)
    {
        var tokens = await GetCachedAsync(ct);
        if (!IsInsideExpiryBuffer(tokens)) return;
        await RefreshUnderSemaphoreAsync(PlaudTokenRefreshTrigger.NearExpiry, ct);
    }
```

And implement `ForceRefreshAsync`:

```csharp
    public Task<bool> ForceRefreshAsync(CancellationToken ct) =>
        RefreshUnderSemaphoreAsync(PlaudTokenRefreshTrigger.AuthFailedRetry, ct);
```

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --filter "FullyQualifiedName~PlaudTokenManagerTests"`
Expected: 2 PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenManager.cs backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs
git commit -m "feat(plaud): PlaudTokenManager refresh + telemetry on near-expiry"
```

---

## Task 15: `PlaudTokenManager.ForceRefreshAsync` — success → returns true and emits `auth-failed-retry`

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs`

- [ ] **Step 1: Add the test**

Append:

```csharp
    [Fact]
    public async Task ForceRefreshAsync_ReturnsTrueAndEmitsAuthFailedRetry_WhenRefreshSucceeds()
    {
        var initial = new PlaudTokens("old-a", "old-r", UnixSecondsFromNow(TimeSpan.FromMinutes(-10))); // already expired
        var rotated = new PlaudTokens("new-a", "new-r", UnixSecondsFromNow(TimeSpan.FromDays(30)));
        var (sut, _, refresh, telemetry) = CreateSut(initial);

        refresh.Setup(r => r.RefreshAsync("old-r", It.IsAny<CancellationToken>())).ReturnsAsync(rotated);

        var result = await sut.ForceRefreshAsync(CancellationToken.None);

        result.Should().BeTrue();
        telemetry.Verify(t => t.TrackBusinessEvent(
            PlaudTelemetryEventNames.Refreshed,
            It.Is<Dictionary<string, string>>(d => d["triggeredBy"] == "auth-failed-retry"),
            It.IsAny<Dictionary<string, double>>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run — expect PASS** (already handled by Task 14)

Run: `dotnet test ... --filter "FullyQualifiedName~ForceRefreshAsync_ReturnsTrue"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs
git commit -m "test(plaud): ForceRefreshAsync returns true and emits auth-failed-retry"
```

---

## Task 16: `PlaudTokenManager.ForceRefreshAsync` — refresh fails → returns false, emits `RefreshFailed`

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs`

- [ ] **Step 1: Add the test**

Append:

```csharp
    [Fact]
    public async Task ForceRefreshAsync_ReturnsFalseAndEmitsRefreshFailed_WhenRefreshThrows()
    {
        var initial = new PlaudTokens("old-a", "old-r", UnixSecondsFromNow(TimeSpan.FromMinutes(-1)));
        var (sut, _, refresh, telemetry) = CreateSut(initial);

        refresh.Setup(r => r.RefreshAsync("old-r", It.IsAny<CancellationToken>()))
               .ThrowsAsync(new HttpRequestException("boom"));

        var result = await sut.ForceRefreshAsync(CancellationToken.None);

        result.Should().BeFalse();
        telemetry.Verify(t => t.TrackBusinessEvent(
            PlaudTelemetryEventNames.RefreshFailed,
            It.Is<Dictionary<string, string>>(d => d["reason"] == "HttpError"),
            It.IsAny<Dictionary<string, double>>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run — expect PASS**

Run: `dotnet test ... --filter "FullyQualifiedName~ForceRefreshAsync_ReturnsFalse"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs
git commit -m "test(plaud): ForceRefreshAsync returns false on refresh failure"
```

---

## Task 17: `PlaudTokenManager` — KV write failure is non-fatal, cache still updated, warning event emitted

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs`

- [ ] **Step 1: Add the test**

Append:

```csharp
    [Fact]
    public async Task ForceRefreshAsync_ReturnsTrueAndEmitsKvWarning_WhenKvWriteFails()
    {
        var initial = new PlaudTokens("old-a", "old-r", UnixSecondsFromNow(TimeSpan.FromMinutes(-1)));
        var rotated = new PlaudTokens("new-a", "new-r", UnixSecondsFromNow(TimeSpan.FromDays(30)));
        var (sut, store, refresh, telemetry) = CreateSut(initial);

        refresh.Setup(r => r.RefreshAsync("old-r", It.IsAny<CancellationToken>())).ReturnsAsync(rotated);
        store.Setup(s => s.SaveAsync(rotated, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new PlaudTokenSaveResult(KeyVaultWriteFailed: true, KeyVaultError: new Exception("kv")));

        var result = await sut.ForceRefreshAsync(CancellationToken.None);

        result.Should().BeTrue();
        telemetry.Verify(t => t.TrackBusinessEvent(
            PlaudTelemetryEventNames.KeyVaultWriteFailed,
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, double>>()),
            Times.Once);
        telemetry.Verify(t => t.TrackBusinessEvent(
            PlaudTelemetryEventNames.Refreshed,
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, double>>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run — expect PASS**

Run: `dotnet test ... --filter "FullyQualifiedName~ReturnsTrueAndEmitsKvWarning"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs
git commit -m "test(plaud): KV write failure is non-fatal, emits warning event"
```

---

## Task 18: `PlaudTokenManager` — disk write failure surfaces as refresh failure, cache NOT updated

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs`

- [ ] **Step 1: Add the test**

Append:

```csharp
    [Fact]
    public async Task ForceRefreshAsync_ReturnsFalseAndEmitsDiskWriteFailed_WhenStoreThrows()
    {
        var initial = new PlaudTokens("old-a", "old-r", UnixSecondsFromNow(TimeSpan.FromMinutes(-1)));
        var rotated = new PlaudTokens("new-a", "new-r", UnixSecondsFromNow(TimeSpan.FromDays(30)));
        var (sut, store, refresh, telemetry) = CreateSut(initial);

        refresh.Setup(r => r.RefreshAsync("old-r", It.IsAny<CancellationToken>())).ReturnsAsync(rotated);
        store.Setup(s => s.SaveAsync(rotated, It.IsAny<CancellationToken>()))
             .ThrowsAsync(new IOException("disk full"));

        var result = await sut.ForceRefreshAsync(CancellationToken.None);

        result.Should().BeFalse();
        telemetry.Verify(t => t.TrackBusinessEvent(
            PlaudTelemetryEventNames.RefreshFailed,
            It.Is<Dictionary<string, string>>(d => d["reason"] == "DiskWriteFailed"),
            It.IsAny<Dictionary<string, double>>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run — expect PASS**

Run: `dotnet test ... --filter "FullyQualifiedName~ReturnsFalseAndEmitsDiskWriteFailed"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs
git commit -m "test(plaud): disk failure during refresh surfaces and does not update cache"
```

---

## Task 19: `PlaudTokenManager` — concurrent callers single-flight to one refresh call

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs`

- [ ] **Step 1: Add the test**

Append:

```csharp
    [Fact]
    public async Task ForceRefreshAsync_SingleFlight_WhenCalledConcurrently()
    {
        var initial = new PlaudTokens("old-a", "old-r", UnixSecondsFromNow(TimeSpan.FromMinutes(-1)));
        var rotated = new PlaudTokens("new-a", "new-r", UnixSecondsFromNow(TimeSpan.FromDays(30)));
        var (sut, _, refresh, _) = CreateSut(initial);

        var gate = new TaskCompletionSource<PlaudTokens>(TaskCreationOptions.RunContinuationsAsynchronously);
        refresh.Setup(r => r.RefreshAsync("old-r", It.IsAny<CancellationToken>())).Returns(gate.Task);

        var t1 = sut.ForceRefreshAsync(CancellationToken.None);
        var t2 = sut.ForceRefreshAsync(CancellationToken.None);

        // Give both tasks a chance to enter the semaphore queue
        await Task.Delay(50);
        gate.SetResult(rotated);

        await Task.WhenAll(t1, t2);

        refresh.Verify(r => r.RefreshAsync("old-r", It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run — expect PASS**

Run: `dotnet test ... --filter "FullyQualifiedName~SingleFlight"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs
git commit -m "test(plaud): concurrent ForceRefreshAsync collapses to single refresh call"
```

---

## Task 20: Wire `IPlaudTokenManager` + `ITelemetryService` into `PlaudCliClient` (no behavior change yet)

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs`

- [ ] **Step 1: Update the constructor and field set**

Replace the field/ctor block in `PlaudCliClient.cs` (lines 9-18):

```csharp
public sealed class PlaudCliClient : IPlaudClient
{
    private readonly ILogger<PlaudCliClient> _logger;
    private readonly IOptions<PlaudOptions> _options;
    private readonly IPlaudTokenManager? _tokenManager;
    private readonly ITelemetryService? _telemetry;

    public PlaudCliClient(ILogger<PlaudCliClient> logger, IOptions<PlaudOptions> options)
        : this(logger, options, tokenManager: null, telemetry: null)
    {
    }

    internal PlaudCliClient(
        ILogger<PlaudCliClient> logger,
        IOptions<PlaudOptions> options,
        IPlaudTokenManager? tokenManager,
        ITelemetryService? telemetry)
    {
        _logger = logger;
        _options = options;
        _tokenManager = tokenManager;
        _telemetry = telemetry;
    }
```

Add `using Anela.Heblo.Xcc.Telemetry;` at the top of the file.

> **Why the legacy ctor:** the existing `PlaudCliClientRunTests` construct the client without DI. Keeping the parameterless overload preserves those tests.

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj`
Expected: PASS.

- [ ] **Step 3: Run all Plaud tests — expect no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj`
Expected: all existing tests PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs
git commit -m "refactor(plaud): inject optional token manager + telemetry into PlaudCliClient"
```

---

## Task 21: Failing test for `PlaudCliClient` AUTH_FAILED → refresh → retry → success

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRefreshRetryTests.cs`

- [ ] **Step 1: Write the failing test**

Write `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRefreshRetryTests.cs`:

```csharp
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
        if (OperatingSystem.IsWindows()) return;

        // Arrange — shim that fails on first call, succeeds on second.
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

            // Act
            var result = await client.ListRecentAsync(7);

            // Assert
            result.Should().BeEmpty();
            manager.Verify(m => m.EnsureFreshAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            manager.Verify(m => m.ForceRefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
            var counter = int.Parse(await File.ReadAllTextAsync(counterPath));
            counter.Should().Be(2); // CLI invoked exactly twice
        }
        finally
        {
            if (File.Exists(shimPath)) File.Delete(shimPath);
            if (File.Exists(counterPath)) File.Delete(counterPath);
        }
    }
}
```

- [ ] **Step 2: Run — expect FAIL (ForceRefreshAsync never called; CLI invoked once and throws)**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --filter "FullyQualifiedName~PlaudCliClientRefreshRetryTests"`
Expected: FAIL (current code throws `PlaudAuthExpiredException`).

- [ ] **Step 3: Commit (red)**

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRefreshRetryTests.cs
git commit -m "test(plaud): failing test for AUTH_FAILED → refresh → retry"
```

---

## Task 22: Implement `PlaudCliClient` AUTH_FAILED → refresh → retry

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs`

- [ ] **Step 1: Refactor `RunCliAsync` to call `EnsureFreshAsync` + retry once on AUTH_FAILED**

Replace `RunCliAsync` (starting at line 80) with:

```csharp
    private async Task<string> RunCliAsync(string[] args, CancellationToken ct)
    {
        if (_tokenManager is not null)
            await _tokenManager.EnsureFreshAsync(ct);

        try
        {
            return await ExecuteCliAsync(args, ct);
        }
        catch (PlaudAuthExpiredException) when (_tokenManager is not null)
        {
            var refreshed = await _tokenManager.ForceRefreshAsync(ct);
            if (!refreshed)
            {
                _telemetry?.TrackException(new PlaudAuthExpiredException("Refresh failed"));
                throw;
            }

            try
            {
                return await ExecuteCliAsync(args, ct);
            }
            catch (PlaudAuthExpiredException)
            {
                _telemetry?.TrackException(new PlaudAuthExpiredException("Retry after refresh still AUTH_FAILED"));
                throw;
            }
        }
    }

    private async Task<string> ExecuteCliAsync(string[] args, CancellationToken ct)
    {
        var options = _options.Value;
        var psi = new ProcessStartInfo
        {
            FileName = options.CliExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.ProcessTimeoutSeconds));

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            cts.Token.Register(() => process.Kill(entireProcessTree: true));

            await process.WaitForExitAsync(cts.Token);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogError("Plaud CLI exited with code {ExitCode}: {Error}", process.ExitCode, error);
                var trimmed = (error ?? string.Empty).Trim();
                if (trimmed.Contains("AUTH_FAILED", StringComparison.Ordinal))
                {
                    throw new PlaudAuthExpiredException(trimmed);
                }
                var suffix = trimmed.Length > 0 ? $": {trimmed}" : string.Empty;
                throw new InvalidOperationException(
                    $"Plaud CLI exited with code {process.ExitCode}{suffix}");
            }

            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("Plaud CLI stderr output: {Error}", error);
            }

            return output;
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Plaud CLI process exceeded {options.ProcessTimeoutSeconds} seconds timeout");
        }
    }
```

- [ ] **Step 2: Run the new test — expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --filter "FullyQualifiedName~PlaudCliClientRefreshRetryTests"`
Expected: PASS.

- [ ] **Step 3: Run all Plaud tests — expect no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj`
Expected: all PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs
git commit -m "feat(plaud): PlaudCliClient detects AUTH_FAILED, refreshes, retries once"
```

---

## Task 23: Second-AUTH_FAILED-after-refresh → throws

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRefreshRetryTests.cs`

- [ ] **Step 1: Add the test**

Append:

```csharp
    [SkippableFact]
    public async Task ListRecentAsync_ThrowsAuthExpired_WhenRetriedCliStillAuthFails()
    {
        Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");
        if (OperatingSystem.IsWindows()) return;

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
```

- [ ] **Step 2: Run — expect PASS**

Run: `dotnet test ... --filter "FullyQualifiedName~ThrowsAuthExpired_WhenRetriedCliStillAuthFails"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRefreshRetryTests.cs
git commit -m "test(plaud): second AUTH_FAILED after refresh surfaces PlaudAuthExpiredException"
```

---

## Task 24: ForceRefresh returns false → AUTH_FAILED bubbles unchanged

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRefreshRetryTests.cs`

- [ ] **Step 1: Add the test**

Append:

```csharp
    [SkippableFact]
    public async Task ListRecentAsync_ThrowsAuthExpired_WhenForceRefreshReturnsFalse()
    {
        Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash");
        if (OperatingSystem.IsWindows()) return;

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
```

- [ ] **Step 2: Run — expect PASS**

Run: `dotnet test ... --filter "FullyQualifiedName~ForceRefreshReturnsFalse"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRefreshRetryTests.cs
git commit -m "test(plaud): when ForceRefreshAsync returns false, auth exception is surfaced"
```

---

## Task 25: Refactor `PlaudTokenRefreshJob` to use `IPlaudTokenStore`

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshJob.cs`

- [ ] **Step 1: Update job to depend on `IPlaudTokenStore`**

Replace the class body with:

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Plaud;

public class PlaudTokenRefreshJob : IRecurringJob
{
    private readonly IPlaudTokenRefreshClient _refreshClient;
    private readonly IPlaudTokenStore _tokenStore;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<PlaudTokenRefreshJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "plaud-token-refresh",
        DisplayName = "Plaud — refresh auth token",
        Description = "Calls Plaud OAuth refresh endpoint weekly and persists the rotated token back to Key Vault so container restarts pick up the fresh value.",
        CronExpression = "0 4 * * 0",
        DefaultIsEnabled = true
    };

    public PlaudTokenRefreshJob(
        IPlaudTokenRefreshClient refreshClient,
        IPlaudTokenStore tokenStore,
        IRecurringJobStatusChecker statusChecker,
        ILogger<PlaudTokenRefreshJob> logger)
    {
        _refreshClient = refreshClient;
        _tokenStore = tokenStore;
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

        var current = await _tokenStore.LoadAsync(cancellationToken);
        var newTokens = await _refreshClient.RefreshAsync(current.RefreshToken, cancellationToken);

        if (string.IsNullOrEmpty(newTokens.AccessToken) || string.IsNullOrEmpty(newTokens.RefreshToken))
            throw new InvalidOperationException(
                "Plaud refresh response has empty tokens. Refusing to persist.");

        if (newTokens.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            throw new InvalidOperationException(
                $"Plaud refresh response has expires_at={newTokens.ExpiresAt} in the past. Refusing to persist.");

        var saveResult = await _tokenStore.SaveAsync(newTokens, cancellationToken);

        if (saveResult.KeyVaultWriteFailed)
            _logger.LogWarning(saveResult.KeyVaultError,
                "Plaud token KV write failed in weekly job (disk OK)");

        _logger.LogInformation(
            "Plaud token refreshed by weekly job. expires_at={ExpiresAt}", newTokens.ExpiresAt);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj`
Expected: PASS (the existing `PlaudAdapterServiceCollectionExtensions` will be updated in the next task — for now it still injects `SecretClient` directly, which compiles because `PlaudTokenRefreshJob` is constructor-resolved per-scope and DI rejects an unresolvable ctor at runtime, not build time).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshJob.cs
git commit -m "refactor(plaud): job delegates KV/disk writes to IPlaudTokenStore; enable by default"
```

---

## Task 26: DI registration — bind options, register store/manager/refresh as singletons

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAdapterServiceCollectionExtensions.cs`

- [ ] **Step 1: Replace the registration extension**

Replace the file with:

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
        services.Configure<PlaudCredentialsOptions>(configuration.GetSection(PlaudCredentialsOptions.SectionKey));
        services.AddHostedService<PlaudTokenBootstrapper>();

        var keyVaultUri = configuration["KeyVault:Uri"];
        if (string.IsNullOrWhiteSpace(keyVaultUri))
        {
            // Local dev: register the CLI client without refresh capability.
            services.AddSingleton<IPlaudClient, PlaudCliClient>();
            return services;
        }

        services.AddSingleton(new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential()));
        services.AddHttpClient<PlaudTokenRefreshClient>();
        services.AddSingleton<IPlaudTokenRefreshClient>(
            sp => sp.GetRequiredService<PlaudTokenRefreshClient>());
        services.AddSingleton<IPlaudTokenStore, PlaudTokenStore>();
        services.AddSingleton<IPlaudTokenManager, PlaudTokenManager>();

        services.AddSingleton<IPlaudClient>(sp => new PlaudCliClient(
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PlaudCliClient>>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlaudOptions>>(),
            sp.GetRequiredService<IPlaudTokenManager>(),
            sp.GetRequiredService<Anela.Heblo.Xcc.Telemetry.ITelemetryService>()));

        services.AddScoped<IRecurringJob, PlaudTokenRefreshJob>();

        return services;
    }
}
```

- [ ] **Step 2: Build whole solution — expect PASS**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: 0 errors.

- [ ] **Step 3: Run full Plaud test suite**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj`
Expected: all PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAdapterServiceCollectionExtensions.cs
git commit -m "feat(plaud): register store/manager/refresh as singletons, wire token manager into client"
```

---

## Task 27: Token-leakage guard test (NFR-2)

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs`

- [ ] **Step 1: Add the test**

Append:

```csharp
    [Fact]
    public async Task TelemetryEvents_NeverContain_RefreshTokenOrAccessTokenContents()
    {
        var initial = new PlaudTokens("super-secret-access", "super-secret-refresh",
            UnixSecondsFromNow(TimeSpan.FromMinutes(-1)));
        var rotated = new PlaudTokens("rotated-access-xyz", "rotated-refresh-xyz",
            UnixSecondsFromNow(TimeSpan.FromDays(30)));
        var capturedProps = new List<Dictionary<string, string>>();

        var (sut, _, refresh, telemetry) = CreateSut(initial);
        refresh.Setup(r => r.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(rotated);
        telemetry.Setup(t => t.TrackBusinessEvent(
            It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, double>>()))
            .Callback<string, Dictionary<string, string>?, Dictionary<string, double>?>((_, p, _) =>
            {
                if (p is not null) capturedProps.Add(p);
            });

        await sut.ForceRefreshAsync(CancellationToken.None);

        foreach (var props in capturedProps)
        {
            foreach (var (_, value) in props)
            {
                value.Should().NotContain("super-secret-access");
                value.Should().NotContain("super-secret-refresh");
                value.Should().NotContain("rotated-access-xyz");
                value.Should().NotContain("rotated-refresh-xyz");
            }
        }
    }
```

- [ ] **Step 2: Run — expect PASS**

Run: `dotnet test ... --filter "FullyQualifiedName~TelemetryEvents_NeverContain"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenManagerTests.cs
git commit -m "test(plaud): assert no token contents leak into telemetry properties"
```

---

## Task 28: Update `PlaudAuthExpiredException` message to reference Key Vault, not App Service

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAuthExpiredException.cs`

- [ ] **Step 1: Update message text**

Replace both message strings:

```csharp
namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudAuthExpiredException : Exception
{
    private const string Guidance =
        "Plaud authentication expired. Refresh failed or refresh token is dead. " +
        "Run `plaud login` locally and rotate the Plaud--TokensJson secret in Key Vault. " +
        "See docs/operations/plaud-token-rotation.md.";

    public PlaudAuthExpiredException(string stderr)
        : base($"{Guidance} CLI stderr: {stderr ?? "(empty)"}")
    { }

    public PlaudAuthExpiredException(string stderr, Exception innerException)
        : base($"{Guidance} CLI stderr: {stderr ?? "(empty)"}", innerException)
    { }
}
```

- [ ] **Step 2: Update the existing `PlaudAuthExceptionTests` if it asserts the old text**

Run: `grep -n 'App Service setting' backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudAuthExceptionTests.cs`

If a match is found, open the file and replace the substring matching the old message with `Key Vault` or `plaud-token-rotation`. If no match, skip.

- [ ] **Step 3: Build + run all Plaud tests**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj`
Expected: all PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAuthExpiredException.cs backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudAuthExceptionTests.cs
git commit -m "refactor(plaud): point auth-expired guidance at Key Vault + runbook"
```

---

## Task 29: Operator runbook

**Files:**
- Create: `docs/operations/plaud-token-rotation.md`

- [ ] **Step 1: Write the runbook**

Write `docs/operations/plaud-token-rotation.md`:

```markdown
# Plaud Token Rotation Runbook

> Triggered by alert: `Heblo-Plaud-AuthExpired`, `PlaudTokenNearExpiry`, or `PlaudTokenRefreshFailed`.

## When to run

Run this runbook when **any** of the following fires:
- `Heblo-Plaud-AuthExpired` (Sev 2) — `PlaudAuthExpiredException` count > 0 in 15 min.
- `PlaudTokenRefreshFailed` (Sev 2) — in-line refresh failed, runbook is now required.
- `PlaudTokenNearExpiry` (Sev 3) — proactive warning, schedule rotation before hard expiry.

The system attempts in-line + weekly refresh automatically. This runbook only fires
when the refresh token itself is dead (rotation lapsed, refresh-token revoked, etc.).

## Steps

1. **Run `plaud login` locally** to obtain a fresh tuple:
   ```bash
   plaud login
   cat ~/.plaud/tokens.json
   ```
   You'll get back JSON of the shape:
   ```json
   {"access_token":"...","refresh_token":"...","expires_at":1234567890}
   ```

2. **Rotate the Key Vault secret** — the whole JSON blob is one secret:
   ```bash
   az keyvault secret set \
     --vault-name kv-heblo-prod \
     --name "Plaud--TokensJson" \
     --value "$(cat ~/.plaud/tokens.json)"
   ```
   > Do **not** put this secret in App Service environment variables. Per `CLAUDE.md`,
   > all secrets live in Key Vault. The KV separator is `--`.

3. **Restart the Heblo Azure Web App** so the new secret is loaded into config:
   - Azure Portal → `Heblo` Web App → Restart, **or**
   - `az webapp restart --name Heblo --resource-group <rg>`

4. **Verify the `plaud-token-refresh` Hangfire job is enabled** in production:
   - Web app → `/admin/background-jobs` UI.
   - Confirm `plaud-token-refresh` shows **Enabled**. If not, enable it.
   - This is defence-in-depth — `PlaudCliClient` also self-refreshes, but the weekly
     job catches process-lifetime drift.

5. **Confirm the surge has stopped**: open Application Insights and run:
   ```kusto
   exceptions
   | where timestamp > ago(15m)
   | where problemId == "Anela.Heblo.Adapters.Plaud.PlaudAuthExpiredException at Anela.Heblo.Adapters.Plaud.PlaudCliClient+<RunCliAsync>d__7.MoveNext"
   | count
   ```
   Expected: 0 within 15 minutes of restart.

## Why this happens

- Plaud refresh tokens expire ~30 days after issue.
- `PlaudCliClient` refreshes proactively inside `ExpiryBuffer` (default 72h) and reactively on `AUTH_FAILED`.
- The weekly `plaud-token-refresh` Hangfire job is a safety net.
- This runbook only fires when **all three** paths have failed — usually because the refresh token itself is dead.

## Alert configuration (reference)

All three alerts route to action group `ag-heblo-ops` (email `ondra@anela.cz`).

| Alert | Source | Severity | Window | Eval |
|---|---|---|---|---|
| `Heblo-Plaud-AuthExpired` | `exceptions` where problemId matches PlaudAuthExpiredException, count > 0 | 2 | 15 min | 5 min |
| `PlaudTokenNearExpiry` | `customEvents` where name == "PlaudTokenNearExpiry", count > 0 | 3 | 60 min | 15 min |
| `PlaudTokenRefreshFailed` | `customEvents` where name == "PlaudTokenRefreshFailed", count > 0 | 2 | 15 min | 5 min |
```

- [ ] **Step 2: Commit**

```bash
git add docs/operations/plaud-token-rotation.md
git commit -m "docs(ops): add Plaud token rotation runbook"
```

---

## Task 30: Final validation pass

- [ ] **Step 1: Format**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: no changes (or only whitespace tidy).

- [ ] **Step 2: Build whole solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: 0 errors, 0 warnings introduced by this PR.

- [ ] **Step 3: Run Plaud adapter tests + full backend tests**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj`
Expected: all PASS.

Run: `dotnet test backend/Anela.Heblo.sln --no-build`
Expected: all PASS (no unrelated regressions).

- [ ] **Step 4: Commit any format / fixup**

```bash
git add -A
git diff --staged --quiet || git commit -m "chore(plaud): dotnet format pass"
```

---

## Self-Review Notes

**Spec coverage (each requirement → task):**
- FR-1 (runbook + rotation + enabling the job) → Task 29 (runbook) + Task 25 (`DefaultIsEnabled = true`).
- FR-2 (proactive expiry detection + `PlaudTokenNearExpiry` event + configurable buffer) → Task 1 (options), Task 14 (near-expiry event).
- FR-3 (in-line refresh, single-flight, retry-once, structured failure event, KV-failure non-fatal) → Tasks 12-19 (manager behaviors), Tasks 21-24 (CLI retry-once), Task 17 (KV non-fatal).
- FR-4 (alerts) → Task 29 (runbook documents alert config; alerts themselves are operator-side per arch review).
- FR-5 (unit tests for each scenario) → Tasks 12-19, 21-24, 27.
- NFR-1 (≤5 ms happy path) → Task 13 in-memory cache, no IO on happy path.
- NFR-2 (no token contents in logs/telemetry; HMAC tokenIdShort) → Task 13 (HMAC), Task 27 (test).
- NFR-3 (idempotent refresh, disk-first ordering, weekly job is independent safety net) → Tasks 8/25 (disk-first), Task 25 (job still works).
- NFR-4 (structured events with consistent properties) → Task 14 (event property set).

**Type / signature consistency check:**
- `IPlaudTokenManager.EnsureFreshAsync` and `ForceRefreshAsync` signatures consistent across Tasks 11, 13, 14, 21-24.
- `IPlaudTokenStore.LoadAsync` / `SaveAsync(PlaudTokens, CancellationToken)` returning `PlaudTokenSaveResult` — same in Tasks 4, 6, 8, 14, 25.
- `PlaudTelemetryEventNames` constants used consistently in Tasks 2, 14-18, 22, 27.
- `PlaudTokenRefreshTrigger` enum (Task 2) values match the `triggeredBy` strings emitted in Task 14 (`"near-expiry"` / `"auth-failed-retry"`).
- `PlaudCliClient` ctor — two overloads in Task 20, both used in Tasks 21-24, 26.

No `TBD` / `TODO` placeholders remain. Each step contains exact paths, code, and commands.
