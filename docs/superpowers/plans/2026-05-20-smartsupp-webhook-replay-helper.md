# Smartsupp Webhook Replay Helper Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone localhost-only helper app that reads recorded Smartsupp webhooks from `SmartsuppWebhookAuditEntries` and reposts them one at a time (button per row + "send next" shortcut) to a configurable target URL, forwarding the original `X-Smartsupp-Hmac` header so the debug target re-runs the full controller (signature verification, parsing, dispatch).

**Architecture:** A new .NET 8 minimal-API project at `backend/tools/SmartsuppWebhookReplay/` references `Anela.Heblo.Persistence` (and transitively `Anela.Heblo.Domain`), boots a Kestrel host bound to `localhost`, exposes three JSON endpoints (`/api/audit`, `/api/audit/{id}`, `/api/audit/{id}/forward`) and serves a single static HTML page from `wwwroot/`. The forward endpoint composes an `HttpRequestMessage` whose body is the recorded `RawBody` and whose `X-Smartsupp-Hmac` header is the recorded signature verbatim — the only header forwarded besides `Content-Type: application/json`. No mutation to the audit row (replay state is client-side `localStorage`). xUnit + FluentAssertions tests cover the forwarder and the forward endpoint.

**Tech Stack:** .NET 8, ASP.NET Core Minimal APIs, EF Core 8 with `Anela.Heblo.Persistence.ApplicationDbContext`, Npgsql, Pgvector, vanilla JS + HTML (no build step), xUnit + FluentAssertions + Microsoft.AspNetCore.Mvc.Testing.

---

## Prerequisite

This plan compiles against types added by the audit work (`feature/smartsupp-audit` branch / external workspace). Specifically:
- `Anela.Heblo.Domain.Features.Smartsupp.SmartsuppWebhookAuditEntry`
- `Anela.Heblo.Domain.Features.Smartsupp.SmartsuppWebhookSignatureStatus`
- `Anela.Heblo.Domain.Features.Smartsupp.SmartsuppWebhookProcessingStatus`
- `Anela.Heblo.Persistence.ApplicationDbContext.SmartsuppWebhookAuditEntries` (`DbSet<SmartsuppWebhookAuditEntry>`)

**Do not start this plan until the audit branch is merged into `main` or rebased onto this branch.** Verify with:

```bash
grep -rn "SmartsuppWebhookAuditEntry" backend/src/Anela.Heblo.Domain/Features/Smartsupp/
grep -n "SmartsuppWebhookAuditEntries" backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
```

Both must return matches before continuing.

---

## File structure

**New project — `backend/tools/SmartsuppWebhookReplay/`:**
- `SmartsuppWebhookReplay.csproj`
- `Program.cs` — host bootstrap, DI, endpoint mapping, static files
- `appsettings.json`
- `appsettings.Development.json`
- `ReplayOptions.cs` — strongly typed `Replay:*` config (`TargetUrl`, `TimeoutSeconds`)
- `Models/AuditSummary.cs` — table row DTO
- `Models/AuditDetail.cs` — single-entry DTO (with `RawBody`, `HeadersJson`)
- `Models/ForwardResult.cs` — return shape of `POST /api/audit/{id}/forward`
- `Services/IWebhookForwarder.cs` + `Services/WebhookForwarder.cs` — owns the outbound `HttpRequestMessage`
- `Endpoints/AuditEndpoints.cs` — `/api/audit`, `/api/audit/{id}` (extension method on `WebApplication`)
- `Endpoints/ForwardEndpoint.cs` — `/api/audit/{id}/forward` (extension method on `WebApplication`)
- `wwwroot/index.html`
- `wwwroot/app.js`
- `wwwroot/app.css`

**New test project — `backend/tools/SmartsuppWebhookReplay.Tests/`:**
- `SmartsuppWebhookReplay.Tests.csproj`
- `WebhookForwarderTests.cs` — unit tests with stubbed `HttpMessageHandler`
- `ForwardEndpointTests.cs` — integration test with `WebApplicationFactory<Program>` + InMemory EF
- `TestHttpMessageHandler.cs` — helper to capture outbound requests

**Modified files:**
- `Anela.Heblo.sln` — register both new csproj files under a new solution folder `tools`. Slugs go at the repo root.

The existing solution lives at the **repo root** (`/Anela.Heblo.sln`), not under `backend/`. Use `dotnet sln Anela.Heblo.sln add ...` from the repo root.

---

## Repo conventions to honor

- Nullable reference types enabled (matches every other csproj).
- C# `record` for immutable data models (DTOs), `class` for entities. DTOs that flow through OpenAPI/Swashbuckle must be `class` per `CLAUDE.md`, but this helper exposes **no** OpenAPI client — frontend is hand-written JS — so `record` is fine here.
- Tests: xUnit + FluentAssertions. Mirror the `Arrange / Act / Assert` block convention used in the rest of the codebase.
- `dotnet format` runs as a post-edit hook. Each commit step assumes formatting has already been applied.
- All DateTime values are UTC.
- Connection-string resolution order matches `DesignTimeDbContextFactory.cs:33-35`: try `ConnectionStrings:<EnvironmentName>` → `ConnectionStrings:DefaultConnection` → `ConnectionStrings:Default`. The user's per-env swap (see existing memory: `project_environments.md`) lives in `Default`.
- The `UserSecretsId` is shared across the API and Persistence projects: `f4e6382a-aefd-47ef-9cd7-7e12daac7e45`. Reuse it so the same `secrets.json` (with connection strings) loads transparently.

---

## Task 1: Create the helper project skeleton

**Files:**
- Create: `backend/tools/SmartsuppWebhookReplay/SmartsuppWebhookReplay.csproj`
- Create: `backend/tools/SmartsuppWebhookReplay/Program.cs` (placeholder)
- Create: `backend/tools/SmartsuppWebhookReplay/appsettings.json`
- Create: `backend/tools/SmartsuppWebhookReplay/appsettings.Development.json`

- [ ] **Step 1: Create the project directory**

Run from the repo root:

```bash
mkdir -p backend/tools/SmartsuppWebhookReplay/wwwroot
mkdir -p backend/tools/SmartsuppWebhookReplay/Endpoints
mkdir -p backend/tools/SmartsuppWebhookReplay/Models
mkdir -p backend/tools/SmartsuppWebhookReplay/Services
```

- [ ] **Step 2: Write the csproj**

Create `backend/tools/SmartsuppWebhookReplay/SmartsuppWebhookReplay.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Anela.Heblo.Tools.SmartsuppWebhookReplay</RootNamespace>
    <AssemblyName>Anela.Heblo.Tools.SmartsuppWebhookReplay</AssemblyName>
    <UserSecretsId>f4e6382a-aefd-47ef-9cd7-7e12daac7e45</UserSecretsId>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration.UserSecrets" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Anela.Heblo.Persistence\Anela.Heblo.Persistence.csproj" />
  </ItemGroup>

</Project>
```

The `Web` SDK gives us Kestrel, minimal APIs, static-files middleware, and `WebApplicationBuilder` out of the box without separate packages. `Persistence` transitively pulls in `Domain`, `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, and `Pgvector` — everything needed.

The shared `UserSecretsId` lets the helper read the same `secrets.json` the API uses; no duplication of connection strings.

- [ ] **Step 3: Write a placeholder `Program.cs`**

Create `backend/tools/SmartsuppWebhookReplay/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { ok = true }));
app.Run();
```

- [ ] **Step 4: Write `appsettings.json`**

Create `backend/tools/SmartsuppWebhookReplay/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://localhost:5050" }
    }
  },
  "Replay": {
    "TargetUrl": "http://localhost:5001/api/webhooks/smartsupp",
    "TimeoutSeconds": 30
  }
}
```

Create `backend/tools/SmartsuppWebhookReplay/appsettings.Development.json` with an empty object so ASP.NET Core does not warn about a missing file:

```json
{}
```

- [ ] **Step 5: Verify the project builds standalone**

Run from the repo root:

```bash
dotnet build backend/tools/SmartsuppWebhookReplay/SmartsuppWebhookReplay.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay/
git commit -m "feat(tools): scaffold SmartsuppWebhookReplay project"
```

---

## Task 2: Add both projects to the solution

**Files:**
- Modify: `Anela.Heblo.sln`

We add **only** the helper project here; the test project gets added in its own task once it exists.

- [ ] **Step 1: Add the helper project to the solution**

Run from the repo root:

```bash
dotnet sln Anela.Heblo.sln add \
  --solution-folder tools \
  backend/tools/SmartsuppWebhookReplay/SmartsuppWebhookReplay.csproj
```

Expected output: `Project ... was added to the solution.`

- [ ] **Step 2: Verify the full solution still builds**

```bash
dotnet build Anela.Heblo.sln
```

Expected: `Build succeeded.` (warnings are fine; errors are not).

- [ ] **Step 3: Commit**

```bash
git add Anela.Heblo.sln
git commit -m "feat(tools): register SmartsuppWebhookReplay in solution"
```

---

## Task 3: Create the `ReplayOptions` config binder

**Files:**
- Create: `backend/tools/SmartsuppWebhookReplay/ReplayOptions.cs`

- [ ] **Step 1: Write the options class**

Create `backend/tools/SmartsuppWebhookReplay/ReplayOptions.cs`:

```csharp
namespace Anela.Heblo.Tools.SmartsuppWebhookReplay;

public sealed class ReplayOptions
{
    public const string SectionKey = "Replay";

    public string TargetUrl { get; set; } = "http://localhost:5001/api/webhooks/smartsupp";
    public int TimeoutSeconds { get; set; } = 30;
}
```

- [ ] **Step 2: Build**

```bash
dotnet build backend/tools/SmartsuppWebhookReplay/SmartsuppWebhookReplay.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay/ReplayOptions.cs
git commit -m "feat(tools): add ReplayOptions binder"
```

---

## Task 4: Create the DTO records

**Files:**
- Create: `backend/tools/SmartsuppWebhookReplay/Models/AuditSummary.cs`
- Create: `backend/tools/SmartsuppWebhookReplay/Models/AuditDetail.cs`
- Create: `backend/tools/SmartsuppWebhookReplay/Models/ForwardResult.cs`

- [ ] **Step 1: Write `AuditSummary.cs`**

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Tools.SmartsuppWebhookReplay.Models;

public sealed record AuditSummary(
    Guid Id,
    DateTime ReceivedAt,
    string? EventName,
    string? AccountId,
    string? AppId,
    SmartsuppWebhookSignatureStatus SignatureStatus,
    SmartsuppWebhookProcessingStatus ProcessingStatus,
    int BodySizeBytes,
    int ProcessingDurationMs,
    int ReplayCount,
    DateTime? LastReplayedAt);
```

- [ ] **Step 2: Write `AuditDetail.cs`**

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Tools.SmartsuppWebhookReplay.Models;

public sealed record AuditDetail(
    Guid Id,
    DateTime ReceivedAt,
    string RemoteIp,
    string? SignatureHeader,
    SmartsuppWebhookSignatureStatus SignatureStatus,
    string HeadersJson,
    string RawBody,
    int BodySizeBytes,
    string? EventName,
    string? AccountId,
    string? AppId,
    DateTime? EventTimestamp,
    SmartsuppWebhookProcessingStatus ProcessingStatus,
    string? ProcessingError,
    int ProcessingDurationMs,
    DateTime? ProcessedAt);
```

- [ ] **Step 3: Write `ForwardResult.cs`**

```csharp
namespace Anela.Heblo.Tools.SmartsuppWebhookReplay.Models;

public sealed record ForwardResult(
    int HttpStatus,
    string ResponseBody,
    int DurationMs,
    DateTime SentAt,
    string TargetUrl);
```

- [ ] **Step 4: Build**

```bash
dotnet build backend/tools/SmartsuppWebhookReplay/SmartsuppWebhookReplay.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay/Models/
git commit -m "feat(tools): add audit DTOs for replay helper"
```

---

## Task 5: Create the helper test project (xUnit)

**Files:**
- Create: `backend/tools/SmartsuppWebhookReplay.Tests/SmartsuppWebhookReplay.Tests.csproj`
- Create: `backend/tools/SmartsuppWebhookReplay.Tests/PlaceholderTest.cs` (sanity-check the harness)

- [ ] **Step 1: Write the test csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>Anela.Heblo.Tools.SmartsuppWebhookReplay.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.8" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.8" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\SmartsuppWebhookReplay\SmartsuppWebhookReplay.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write a placeholder test**

Create `backend/tools/SmartsuppWebhookReplay.Tests/PlaceholderTest.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tools.SmartsuppWebhookReplay.Tests;

public class PlaceholderTest
{
    [Fact]
    public void Sanity()
    {
        true.Should().BeTrue();
    }
}
```

- [ ] **Step 3: Build and run the placeholder test**

```bash
dotnet test backend/tools/SmartsuppWebhookReplay.Tests/SmartsuppWebhookReplay.Tests.csproj
```

Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`.

- [ ] **Step 4: Add the test project to the solution**

```bash
dotnet sln Anela.Heblo.sln add \
  --solution-folder tools \
  backend/tools/SmartsuppWebhookReplay.Tests/SmartsuppWebhookReplay.Tests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay.Tests/ Anela.Heblo.sln
git commit -m "test(tools): add SmartsuppWebhookReplay.Tests project"
```

---

## Task 6: `IWebhookForwarder` + `WebhookForwarder` (TDD)

**Files:**
- Test: `backend/tools/SmartsuppWebhookReplay.Tests/TestHttpMessageHandler.cs`
- Test: `backend/tools/SmartsuppWebhookReplay.Tests/WebhookForwarderTests.cs`
- Create: `backend/tools/SmartsuppWebhookReplay/Services/IWebhookForwarder.cs`
- Create: `backend/tools/SmartsuppWebhookReplay/Services/WebhookForwarder.cs`

This service owns the outbound HTTP request. It is the single place that decides which headers to forward, how the body is encoded, and what to return.

- [ ] **Step 1: Write the test helper that captures outbound requests**

Create `backend/tools/SmartsuppWebhookReplay.Tests/TestHttpMessageHandler.cs`:

```csharp
using System.Net;

namespace Anela.Heblo.Tools.SmartsuppWebhookReplay.Tests;

public sealed class TestHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public string ResponseBody { get; set; } = "ok";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(ResponseBody),
        };
    }
}
```

- [ ] **Step 2: Write the failing forwarder tests**

Create `backend/tools/SmartsuppWebhookReplay.Tests/WebhookForwarderTests.cs`:

```csharp
using System.Net;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Tools.SmartsuppWebhookReplay;
using Anela.Heblo.Tools.SmartsuppWebhookReplay.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tools.SmartsuppWebhookReplay.Tests;

public class WebhookForwarderTests
{
    private static SmartsuppWebhookAuditEntry MakeEntry(string body, string? sigHeader) => new()
    {
        Id = Guid.NewGuid(),
        ReceivedAt = DateTime.UtcNow,
        RemoteIp = "1.2.3.4",
        SignatureHeader = sigHeader,
        SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
        HeadersJson = "{}",
        RawBody = body,
        BodySizeBytes = body.Length,
        ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
    };

    private static (WebhookForwarder forwarder, TestHttpMessageHandler handler) Build(
        string targetUrl = "http://localhost:5001/api/webhooks/smartsupp",
        int timeoutSeconds = 30)
    {
        var handler = new TestHttpMessageHandler();
        var factory = new SingleHandlerHttpClientFactory(handler);
        var options = Options.Create(new ReplayOptions
        {
            TargetUrl = targetUrl,
            TimeoutSeconds = timeoutSeconds,
        });
        return (new WebhookForwarder(factory, options), handler);
    }

    [Fact]
    public async Task ForwardAsync_PostsRawBodyAsUtf8Json_ToConfiguredTargetUrl()
    {
        var (forwarder, handler) = Build();
        var entry = MakeEntry("""{"event":"conversation.opened"}""", "sha256=abc");

        await forwarder.ForwardAsync(entry, default);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should().Be("http://localhost:5001/api/webhooks/smartsupp");
        handler.LastRequestBody.Should().Be("""{"event":"conversation.opened"}""");
        handler.LastRequest.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
        handler.LastRequest.Content.Headers.ContentType!.CharSet.Should().Be("utf-8");
    }

    [Fact]
    public async Task ForwardAsync_ForwardsRecordedSignatureHeaderVerbatim()
    {
        var (forwarder, handler) = Build();
        var entry = MakeEntry("{}", "sha256=deadbeef");

        await forwarder.ForwardAsync(entry, default);

        handler.LastRequest!.Headers.TryGetValues("X-Smartsupp-Hmac", out var values).Should().BeTrue();
        values!.Single().Should().Be("sha256=deadbeef");
    }

    [Fact]
    public async Task ForwardAsync_OmitsSignatureHeader_WhenRecordedHeaderIsNull()
    {
        var (forwarder, handler) = Build();
        var entry = MakeEntry("{}", sigHeader: null);

        await forwarder.ForwardAsync(entry, default);

        handler.LastRequest!.Headers.Contains("X-Smartsupp-Hmac").Should().BeFalse();
    }

    [Fact]
    public async Task ForwardAsync_ReturnsTargetResponse()
    {
        var (forwarder, handler) = Build();
        handler.StatusCode = HttpStatusCode.Unauthorized;
        handler.ResponseBody = "bad signature";
        var entry = MakeEntry("{}", "sha256=zzz");

        var result = await forwarder.ForwardAsync(entry, default);

        result.HttpStatus.Should().Be(401);
        result.ResponseBody.Should().Be("bad signature");
        result.TargetUrl.Should().Be("http://localhost:5001/api/webhooks/smartsupp");
        result.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }
}

internal sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public SingleHandlerHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
```

- [ ] **Step 3: Run failing tests**

```bash
dotnet test backend/tools/SmartsuppWebhookReplay.Tests/SmartsuppWebhookReplay.Tests.csproj \
  --filter "FullyQualifiedName~WebhookForwarderTests"
```

Expected: FAIL — types `WebhookForwarder` and `IWebhookForwarder` not defined.

- [ ] **Step 4: Create the interface**

Create `backend/tools/SmartsuppWebhookReplay/Services/IWebhookForwarder.cs`:

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Tools.SmartsuppWebhookReplay.Models;

namespace Anela.Heblo.Tools.SmartsuppWebhookReplay.Services;

public interface IWebhookForwarder
{
    Task<ForwardResult> ForwardAsync(SmartsuppWebhookAuditEntry entry, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Implement the forwarder**

Create `backend/tools/SmartsuppWebhookReplay/Services/WebhookForwarder.cs`:

```csharp
using System.Diagnostics;
using System.Text;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Tools.SmartsuppWebhookReplay.Models;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tools.SmartsuppWebhookReplay.Services;

public sealed class WebhookForwarder : IWebhookForwarder
{
    private const string SignatureHeader = "X-Smartsupp-Hmac";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ReplayOptions _options;

    public WebhookForwarder(IHttpClientFactory httpFactory, IOptions<ReplayOptions> options)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
    }

    public async Task<ForwardResult> ForwardAsync(
        SmartsuppWebhookAuditEntry entry,
        CancellationToken cancellationToken)
    {
        using var client = _httpFactory.CreateClient(nameof(WebhookForwarder));
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TargetUrl)
        {
            Content = new StringContent(entry.RawBody ?? string.Empty, Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrEmpty(entry.SignatureHeader))
        {
            request.Headers.TryAddWithoutValidation(SignatureHeader, entry.SignatureHeader);
        }

        var sw = Stopwatch.StartNew();
        using var response = await client.SendAsync(request, cancellationToken);
        sw.Stop();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new ForwardResult(
            HttpStatus: (int)response.StatusCode,
            ResponseBody: body,
            DurationMs: (int)sw.ElapsedMilliseconds,
            SentAt: DateTime.UtcNow,
            TargetUrl: _options.TargetUrl);
    }
}
```

- [ ] **Step 6: Run tests until green**

```bash
dotnet test backend/tools/SmartsuppWebhookReplay.Tests/SmartsuppWebhookReplay.Tests.csproj \
  --filter "FullyQualifiedName~WebhookForwarderTests"
```

Expected: PASS — 4 tests.

- [ ] **Step 7: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay/Services/ \
        backend/tools/SmartsuppWebhookReplay.Tests/WebhookForwarderTests.cs \
        backend/tools/SmartsuppWebhookReplay.Tests/TestHttpMessageHandler.cs
git commit -m "feat(tools): add WebhookForwarder service with tests"
```

---

## Task 7: Wire DI + DbContext + options into `Program.cs`

**Files:**
- Modify: `backend/tools/SmartsuppWebhookReplay/Program.cs`

This task replaces the placeholder `Program.cs` with the real bootstrap. The DB registration mirrors `DesignTimeDbContextFactory.cs:33-35`: try the environment-name connection string first, then `DefaultConnection`, then `Default`. Pgvector is registered on the data source so the shared `ApplicationDbContext` model loads cleanly.

- [ ] **Step 1: Replace `Program.cs` content**

```csharp
using Anela.Heblo.Persistence;
using Anela.Heblo.Tools.SmartsuppWebhookReplay;
using Anela.Heblo.Tools.SmartsuppWebhookReplay.Endpoints;
using Anela.Heblo.Tools.SmartsuppWebhookReplay.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ReplayOptions>(builder.Configuration.GetSection(ReplayOptions.SectionKey));

var environmentName = builder.Environment.EnvironmentName;
var connectionString =
    builder.Configuration.GetConnectionString(environmentName)
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        $"No connection string configured. Tried 'ConnectionStrings:{environmentName}', " +
        "'ConnectionStrings:DefaultConnection', 'ConnectionStrings:Default'.");

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(dataSource, npgsql => npgsql.CommandTimeout(60)));

builder.Services.AddHttpClient();
builder.Services.AddScoped<IWebhookForwarder, WebhookForwarder>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { ok = true }));
app.MapGet("/api/config", (IOptions<ReplayOptions> options) =>
    Results.Ok(new { targetUrl = options.Value.TargetUrl, timeoutSeconds = options.Value.TimeoutSeconds }));
app.MapAuditEndpoints();
app.MapForwardEndpoint();

app.Run();

public partial class Program;
```

Notes on the imports above:
- `using Pgvector;` (not `Pgvector.Npgsql`) — that's where the `UseVector()` extension on `NpgsqlDataSourceBuilder` lives in this codebase (see `PersistenceModule.cs:32`).
- `UseVector()` is **only** called on the data source builder, not inside the `UseNpgsql(...)` lambda. The vector type mappings travel with the data source.

The trailing `public partial class Program;` is what `WebApplicationFactory<Program>` needs in integration tests.

`MapAuditEndpoints` and `MapForwardEndpoint` do not exist yet — the next two tasks create them. The build will fail until then; that is expected.

- [ ] **Step 2: Build — expected failure**

```bash
dotnet build backend/tools/SmartsuppWebhookReplay/SmartsuppWebhookReplay.csproj
```

Expected: FAIL — `'WebApplication' does not contain a definition for 'MapAuditEndpoints'`. This proves the wiring is in place. We fix the build in Task 8.

(No commit yet — keep the breaking change with its fix.)

---

## Task 8: `/api/audit` list endpoint + `/api/audit/{id}` detail endpoint

**Files:**
- Test: `backend/tools/SmartsuppWebhookReplay.Tests/AuditEndpointsTests.cs`
- Create: `backend/tools/SmartsuppWebhookReplay/Endpoints/AuditEndpoints.cs`

These endpoints read the audit table directly via `ApplicationDbContext`. The list endpoint returns oldest-first (natural replay order), supports filters, and caps `take` at 500.

- [ ] **Step 1: Add a test fixture that boots `Program` against InMemory EF**

Create `backend/tools/SmartsuppWebhookReplay.Tests/ReplayWebApplicationFactory.cs`:

```csharp
using Anela.Heblo.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Tools.SmartsuppWebhookReplay.Tests;

public sealed class ReplayWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"replay_{Guid.NewGuid()}";

    public Action<IServiceCollection>? AdditionalServices { get; set; }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(cfg =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Anything non-empty — the real Npgsql wiring is going to be removed below.
                ["ConnectionStrings:Default"] = "Host=ignored;Database=ignored;Username=ignored;Password=ignored",
                ["Replay:TargetUrl"] = "http://test.invalid/api/webhooks/smartsupp",
                ["Replay:TimeoutSeconds"] = "5",
            });
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Strip the real Npgsql DbContext registration and replace with InMemory.
            var dbContextDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                         || d.ServiceType == typeof(ApplicationDbContext))
                .ToList();
            foreach (var d in dbContextDescriptors) services.Remove(d);

            var dataSourceDescriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("NpgsqlDataSource", StringComparison.Ordinal) == true)
                .ToList();
            foreach (var d in dataSourceDescriptors) services.Remove(d);

            services.AddDbContext<ApplicationDbContext>(opt => opt.UseInMemoryDatabase(_dbName));

            AdditionalServices?.Invoke(services);
        });
    }

    public ApplicationDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }
}
```

- [ ] **Step 2: Write the failing endpoint tests**

Create `backend/tools/SmartsuppWebhookReplay.Tests/AuditEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Anela.Heblo.Tools.SmartsuppWebhookReplay.Models;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tools.SmartsuppWebhookReplay.Tests;

public class AuditEndpointsTests
{
    private static SmartsuppWebhookAuditEntry MakeEntry(
        DateTime receivedAt,
        string eventName,
        SmartsuppWebhookProcessingStatus status = SmartsuppWebhookProcessingStatus.Success) => new()
    {
        Id = Guid.NewGuid(),
        ReceivedAt = receivedAt,
        EventName = eventName,
        SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
        ProcessingStatus = status,
        RawBody = $$"""{"event":"{{eventName}}"}""",
        BodySizeBytes = 20,
        HeadersJson = "{}",
        RemoteIp = "1.2.3.4",
    };

    [Fact]
    public async Task List_ReturnsRowsOrderedByReceivedAtAscending()
    {
        using var factory = new ReplayWebApplicationFactory();
        using (var ctx = factory.CreateDbContext())
        {
            ctx.SmartsuppWebhookAuditEntries.Add(MakeEntry(DateTime.UtcNow.AddMinutes(-1), "b"));
            ctx.SmartsuppWebhookAuditEntries.Add(MakeEntry(DateTime.UtcNow.AddMinutes(-2), "a"));
            await ctx.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var rows = await client.GetFromJsonAsync<AuditSummary[]>("/api/audit");

        rows.Should().NotBeNull();
        rows!.Should().HaveCount(2);
        rows[0].EventName.Should().Be("a");
        rows[1].EventName.Should().Be("b");
    }

    [Fact]
    public async Task List_FiltersByEventName()
    {
        using var factory = new ReplayWebApplicationFactory();
        using (var ctx = factory.CreateDbContext())
        {
            ctx.SmartsuppWebhookAuditEntries.Add(MakeEntry(DateTime.UtcNow, "conv.opened"));
            ctx.SmartsuppWebhookAuditEntries.Add(MakeEntry(DateTime.UtcNow, "conv.closed"));
            await ctx.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var rows = await client.GetFromJsonAsync<AuditSummary[]>("/api/audit?eventName=conv.closed");

        rows!.Should().ContainSingle().Which.EventName.Should().Be("conv.closed");
    }

    [Fact]
    public async Task List_CapsTakeAt500()
    {
        using var factory = new ReplayWebApplicationFactory();
        using (var ctx = factory.CreateDbContext())
        {
            for (var i = 0; i < 3; i++)
                ctx.SmartsuppWebhookAuditEntries.Add(MakeEntry(DateTime.UtcNow.AddSeconds(-i), $"e{i}"));
            await ctx.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/audit?take=99999");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_ReturnsDetail_WithRawBodyAndHeaders()
    {
        using var factory = new ReplayWebApplicationFactory();
        Guid id;
        using (var ctx = factory.CreateDbContext())
        {
            var entry = MakeEntry(DateTime.UtcNow, "conv.opened");
            entry.HeadersJson = """{"x-smartsupp-hmac":"sha256=abc"}""";
            ctx.SmartsuppWebhookAuditEntries.Add(entry);
            await ctx.SaveChangesAsync();
            id = entry.Id;
        }

        var client = factory.CreateClient();
        var detail = await client.GetFromJsonAsync<AuditDetail>($"/api/audit/{id}");

        detail.Should().NotBeNull();
        detail!.Id.Should().Be(id);
        detail.RawBody.Should().Contain("conv.opened");
        detail.HeadersJson.Should().Contain("sha256=abc");
    }

    [Fact]
    public async Task GetById_Returns404_WhenIdMissing()
    {
        using var factory = new ReplayWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/audit/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 3: Run the failing tests**

```bash
dotnet test backend/tools/SmartsuppWebhookReplay.Tests/SmartsuppWebhookReplay.Tests.csproj \
  --filter "FullyQualifiedName~AuditEndpointsTests"
```

Expected: FAIL — either compilation failure (`MapAuditEndpoints` does not exist) or 404s.

- [ ] **Step 4: Create the endpoints module**

Create `backend/tools/SmartsuppWebhookReplay/Endpoints/AuditEndpoints.cs`:

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Anela.Heblo.Tools.SmartsuppWebhookReplay.Models;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tools.SmartsuppWebhookReplay.Endpoints;

public static class AuditEndpoints
{
    private const int MaxTake = 500;
    private const int DefaultTake = 100;

    public static void MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/api/audit", ListAsync);
        app.MapGet("/api/audit/{id:guid}", GetByIdAsync);
    }

    private static async Task<IResult> ListAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken,
        DateTime? from = null,
        DateTime? to = null,
        string? eventName = null,
        SmartsuppWebhookSignatureStatus? signatureStatus = null,
        SmartsuppWebhookProcessingStatus? processingStatus = null,
        int skip = 0,
        int take = DefaultTake)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, MaxTake);

        var query = db.SmartsuppWebhookAuditEntries.AsNoTracking();
        if (from.HasValue) query = query.Where(e => e.ReceivedAt >= from.Value);
        if (to.HasValue) query = query.Where(e => e.ReceivedAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(eventName)) query = query.Where(e => e.EventName == eventName);
        if (signatureStatus.HasValue) query = query.Where(e => e.SignatureStatus == signatureStatus.Value);
        if (processingStatus.HasValue) query = query.Where(e => e.ProcessingStatus == processingStatus.Value);

        var rows = await query
            .OrderBy(e => e.ReceivedAt)
            .Skip(skip)
            .Take(take)
            .Select(e => new AuditSummary(
                e.Id,
                e.ReceivedAt,
                e.EventName,
                e.AccountId,
                e.AppId,
                e.SignatureStatus,
                e.ProcessingStatus,
                e.BodySizeBytes,
                e.ProcessingDurationMs,
                e.ReplayCount,
                e.LastReplayedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(rows);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var entry = await db.SmartsuppWebhookAuditEntries
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new AuditDetail(
                e.Id,
                e.ReceivedAt,
                e.RemoteIp,
                e.SignatureHeader,
                e.SignatureStatus,
                e.HeadersJson,
                e.RawBody,
                e.BodySizeBytes,
                e.EventName,
                e.AccountId,
                e.AppId,
                e.EventTimestamp,
                e.ProcessingStatus,
                e.ProcessingError,
                e.ProcessingDurationMs,
                e.ProcessedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return entry is null ? Results.NotFound() : Results.Ok(entry);
    }
}
```

- [ ] **Step 5: Add a placeholder `MapForwardEndpoint` so the build passes**

The forward endpoint is implemented in Task 9. Add a stub here so `Program.cs` compiles between tasks. Create `backend/tools/SmartsuppWebhookReplay/Endpoints/ForwardEndpoint.cs`:

```csharp
namespace Anela.Heblo.Tools.SmartsuppWebhookReplay.Endpoints;

public static class ForwardEndpoint
{
    public static void MapForwardEndpoint(this WebApplication app)
    {
        // Implementation lives in Task 9.
        app.MapPost("/api/audit/{id:guid}/forward",
            () => Results.StatusCode(StatusCodes.Status501NotImplemented));
    }
}
```

- [ ] **Step 6: Run the tests until green**

```bash
dotnet test backend/tools/SmartsuppWebhookReplay.Tests/SmartsuppWebhookReplay.Tests.csproj \
  --filter "FullyQualifiedName~AuditEndpointsTests"
```

Expected: PASS — 5 tests.

- [ ] **Step 7: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay/Program.cs \
        backend/tools/SmartsuppWebhookReplay/Endpoints/ \
        backend/tools/SmartsuppWebhookReplay.Tests/ReplayWebApplicationFactory.cs \
        backend/tools/SmartsuppWebhookReplay.Tests/AuditEndpointsTests.cs
git commit -m "feat(tools): add audit list + detail endpoints"
```

---

## Task 9: `/api/audit/{id}/forward` endpoint (TDD)

**Files:**
- Test: `backend/tools/SmartsuppWebhookReplay.Tests/ForwardEndpointTests.cs`
- Modify: `backend/tools/SmartsuppWebhookReplay/Endpoints/ForwardEndpoint.cs`

The endpoint loads the audit row, delegates to `IWebhookForwarder`, and returns `ForwardResult`. We stub `IWebhookForwarder` in the test so we don't open a real socket.

- [ ] **Step 1: Write the failing test**

Create `backend/tools/SmartsuppWebhookReplay.Tests/ForwardEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Tools.SmartsuppWebhookReplay.Models;
using Anela.Heblo.Tools.SmartsuppWebhookReplay.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tools.SmartsuppWebhookReplay.Tests;

public class ForwardEndpointTests
{
    private sealed class StubForwarder : IWebhookForwarder
    {
        public SmartsuppWebhookAuditEntry? LastEntry { get; private set; }
        public ForwardResult Result { get; set; } =
            new(200, "ok", 12, DateTime.UtcNow, "http://test.invalid/api/webhooks/smartsupp");

        public Task<ForwardResult> ForwardAsync(SmartsuppWebhookAuditEntry entry, CancellationToken ct)
        {
            LastEntry = entry;
            return Task.FromResult(Result);
        }
    }

    [Fact]
    public async Task Forward_PostsStoredEntryThroughForwarder_AndReturnsResult()
    {
        var stub = new StubForwarder();
        using var factory = new ReplayWebApplicationFactory
        {
            AdditionalServices = services =>
            {
                var d = services.Single(s => s.ServiceType == typeof(IWebhookForwarder));
                services.Remove(d);
                services.AddSingleton<IWebhookForwarder>(stub);
            },
        };

        Guid id;
        using (var ctx = factory.CreateDbContext())
        {
            var entry = new SmartsuppWebhookAuditEntry
            {
                Id = Guid.NewGuid(),
                ReceivedAt = DateTime.UtcNow,
                RawBody = """{"event":"conv.opened"}""",
                SignatureHeader = "sha256=abc",
                SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
                ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
                BodySizeBytes = 24,
                EventName = "conv.opened",
                HeadersJson = "{}",
                RemoteIp = "1.2.3.4",
            };
            ctx.SmartsuppWebhookAuditEntries.Add(entry);
            await ctx.SaveChangesAsync();
            id = entry.Id;
        }

        var client = factory.CreateClient();
        var response = await client.PostAsync($"/api/audit/{id}/forward", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ForwardResult>();
        result.Should().NotBeNull();
        result!.HttpStatus.Should().Be(200);
        result.ResponseBody.Should().Be("ok");
        stub.LastEntry.Should().NotBeNull();
        stub.LastEntry!.Id.Should().Be(id);
        stub.LastEntry.SignatureHeader.Should().Be("sha256=abc");
    }

    [Fact]
    public async Task Forward_Returns404_WhenAuditIdMissing()
    {
        using var factory = new ReplayWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/audit/{Guid.NewGuid()}/forward", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 2: Run the failing test**

```bash
dotnet test backend/tools/SmartsuppWebhookReplay.Tests/SmartsuppWebhookReplay.Tests.csproj \
  --filter "FullyQualifiedName~ForwardEndpointTests"
```

Expected: FAIL — the stub endpoint returns `501 NotImplemented`.

- [ ] **Step 3: Replace the stub `ForwardEndpoint.cs`**

```csharp
using Anela.Heblo.Persistence;
using Anela.Heblo.Tools.SmartsuppWebhookReplay.Services;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tools.SmartsuppWebhookReplay.Endpoints;

public static class ForwardEndpoint
{
    public static void MapForwardEndpoint(this WebApplication app)
    {
        app.MapPost("/api/audit/{id:guid}/forward", ForwardAsync);
    }

    private static async Task<IResult> ForwardAsync(
        Guid id,
        ApplicationDbContext db,
        IWebhookForwarder forwarder,
        CancellationToken cancellationToken)
    {
        var entry = await db.SmartsuppWebhookAuditEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (entry is null) return Results.NotFound();

        var result = await forwarder.ForwardAsync(entry, cancellationToken);
        return Results.Ok(result);
    }
}
```

- [ ] **Step 4: Run tests until green**

```bash
dotnet test backend/tools/SmartsuppWebhookReplay.Tests/SmartsuppWebhookReplay.Tests.csproj \
  --filter "FullyQualifiedName~ForwardEndpointTests"
```

Expected: PASS — 2 tests.

- [ ] **Step 5: Run the full helper test suite**

```bash
dotnet test backend/tools/SmartsuppWebhookReplay.Tests/SmartsuppWebhookReplay.Tests.csproj
```

Expected: PASS — all tests in the helper test project (4 forwarder + 5 audit endpoints + 2 forward endpoint + 1 placeholder = 12 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay/Endpoints/ForwardEndpoint.cs \
        backend/tools/SmartsuppWebhookReplay.Tests/ForwardEndpointTests.cs
git commit -m "feat(tools): add /api/audit/{id}/forward endpoint"
```

---

## Task 10: Static UI — `wwwroot/index.html`, `app.js`, `app.css`

**Files:**
- Create: `backend/tools/SmartsuppWebhookReplay/wwwroot/index.html`
- Create: `backend/tools/SmartsuppWebhookReplay/wwwroot/app.js`
- Create: `backend/tools/SmartsuppWebhookReplay/wwwroot/app.css`

Single page, no build step. JS is plain ES modules; the browser loads it directly.

- [ ] **Step 1: Write `index.html`**

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <title>Smartsupp Webhook Replay</title>
  <link rel="stylesheet" href="/app.css" />
</head>
<body>
  <header>
    <h1>Smartsupp Webhook Replay</h1>
    <div class="target">Target: <code id="target-url">…</code></div>
    <div class="actions">
      <button id="send-next" type="button">Send next ↵</button>
      <button id="refresh" type="button">Refresh (r)</button>
      <span class="hint">↵/n = send next · s = skip · click row = set cursor</span>
    </div>
  </header>

  <section class="filters">
    <label>Event: <input id="filter-event" type="text" placeholder="conversation.opened" /></label>
    <label>Processing:
      <select id="filter-processing">
        <option value="">(any)</option>
        <option value="0">NotProcessed</option>
        <option value="1">MalformedJson</option>
        <option value="2">Success</option>
        <option value="3">HandlerException</option>
      </select>
    </label>
    <label>Signature:
      <select id="filter-signature">
        <option value="">(any)</option>
        <option value="0">Valid</option>
        <option value="1">Missing</option>
        <option value="2">Mismatch</option>
        <option value="3">AppIdMismatch</option>
      </select>
    </label>
    <label>From: <input id="filter-from" type="datetime-local" /></label>
    <button id="apply-filters" type="button">Apply</button>
  </section>

  <table id="audit-table">
    <thead>
      <tr>
        <th>#</th><th>Time</th><th>Event</th><th>Account</th>
        <th>Sig</th><th>Proc</th><th>Size</th><th>Dur</th><th>Action</th>
      </tr>
    </thead>
    <tbody id="audit-rows"></tbody>
  </table>

  <script type="module" src="/app.js"></script>
</body>
</html>
```

- [ ] **Step 2: Write `app.css`**

```css
* { box-sizing: border-box; }
body { font-family: -apple-system, system-ui, sans-serif; margin: 0; padding: 16px; background: #fafafa; color: #222; }
header { display: flex; gap: 16px; align-items: center; flex-wrap: wrap; padding-bottom: 12px; border-bottom: 1px solid #ddd; }
header h1 { font-size: 18px; margin: 0; }
.target code { background: #eee; padding: 2px 6px; border-radius: 4px; }
.actions { display: flex; gap: 8px; align-items: center; }
.actions .hint { font-size: 12px; color: #777; }
.filters { display: flex; gap: 12px; flex-wrap: wrap; padding: 12px 0; align-items: end; }
.filters label { font-size: 12px; display: flex; flex-direction: column; gap: 4px; }
.filters input, .filters select { padding: 4px 6px; font: inherit; }
button { padding: 6px 12px; font: inherit; cursor: pointer; }
table { width: 100%; border-collapse: collapse; font-size: 13px; }
th, td { padding: 6px 8px; text-align: left; border-bottom: 1px solid #eee; }
th { background: #f0f0f0; position: sticky; top: 0; }
tr.cursor { background: #fff7d6; }
tr.sent-ok { background: #ecf9ec; }
tr.sent-err { background: #fdecec; }
td .pill { display: inline-block; padding: 2px 6px; border-radius: 10px; font-size: 11px; font-weight: 600; }
.pill.ok { background: #d6f3d6; color: #1a5a1a; }
.pill.err { background: #f9d6d6; color: #6a1818; }
.sig-0 { color: #1a5a1a; } .sig-1, .sig-2, .sig-3 { color: #6a1818; }
.proc-2 { color: #1a5a1a; } .proc-1, .proc-3 { color: #6a1818; }
```

- [ ] **Step 3: Write `app.js`**

```javascript
const SIG_NAMES = ["Valid", "Missing", "Mismatch", "AppIdMismatch"];
const PROC_NAMES = ["NotProcessed", "MalformedJson", "Success", "HandlerException"];

const state = {
  rows: [],
  cursor: 0,
  results: new Map(), // id -> { status, durationMs }
};

const targetEl = document.getElementById("target-url");
const tbody = document.getElementById("audit-rows");

const cursorKey = () => `replay:cursor:${targetEl.textContent || "default"}`;

async function loadTarget() {
  const response = await fetch("/api/config");
  const config = await response.json();
  targetEl.textContent = config.targetUrl;
}

function buildQuery() {
  const params = new URLSearchParams();
  const evt = document.getElementById("filter-event").value.trim();
  if (evt) params.set("eventName", evt);
  const proc = document.getElementById("filter-processing").value;
  if (proc !== "") params.set("processingStatus", proc);
  const sig = document.getElementById("filter-signature").value;
  if (sig !== "") params.set("signatureStatus", sig);
  const from = document.getElementById("filter-from").value;
  if (from) params.set("from", new Date(from).toISOString());
  params.set("take", "500");
  return params.toString();
}

async function loadRows() {
  const response = await fetch(`/api/audit?${buildQuery()}`);
  state.rows = await response.json();
  state.cursor = Math.min(
    Number(localStorage.getItem(cursorKey()) ?? 0),
    Math.max(0, state.rows.length - 1),
  );
  render();
}

function render() {
  tbody.innerHTML = "";
  state.rows.forEach((row, idx) => {
    const tr = document.createElement("tr");
    if (idx === state.cursor) tr.classList.add("cursor");
    const result = state.results.get(row.id);
    if (result) tr.classList.add(result.status < 400 ? "sent-ok" : "sent-err");

    tr.innerHTML = `
      <td>${idx + 1}</td>
      <td>${new Date(row.receivedAt).toLocaleString()}</td>
      <td>${escape(row.eventName ?? "")}</td>
      <td>${escape(row.accountId ?? "")}</td>
      <td class="sig-${row.signatureStatus}">${SIG_NAMES[row.signatureStatus] ?? row.signatureStatus}</td>
      <td class="proc-${row.processingStatus}">${PROC_NAMES[row.processingStatus] ?? row.processingStatus}</td>
      <td>${row.bodySizeBytes}</td>
      <td>${row.processingDurationMs}ms</td>
      <td>
        <button data-id="${row.id}" class="send-btn">Send</button>
        ${result
          ? `<span class="pill ${result.status < 400 ? "ok" : "err"}">${result.status} · ${result.durationMs}ms</span>`
          : ""}
      </td>`;
    tr.addEventListener("click", (e) => {
      if (e.target.matches(".send-btn")) return;
      state.cursor = idx;
      localStorage.setItem(cursorKey(), String(idx));
      render();
    });
    tbody.appendChild(tr);
  });
  tbody.querySelectorAll(".send-btn").forEach((btn) =>
    btn.addEventListener("click", (e) => {
      e.stopPropagation();
      sendById(btn.dataset.id);
    }));
}

async function sendById(id) {
  const response = await fetch(`/api/audit/${id}/forward`, { method: "POST" });
  if (!response.ok) {
    state.results.set(id, { status: response.status, durationMs: 0 });
  } else {
    const body = await response.json();
    state.results.set(id, { status: body.httpStatus, durationMs: body.durationMs });
  }
  render();
}

async function sendNext() {
  if (state.rows.length === 0) return;
  const row = state.rows[state.cursor];
  if (!row) return;
  await sendById(row.id);
  state.cursor = Math.min(state.cursor + 1, state.rows.length - 1);
  localStorage.setItem(cursorKey(), String(state.cursor));
  render();
}

function escape(text) {
  const div = document.createElement("div");
  div.textContent = text;
  return div.innerHTML;
}

document.getElementById("send-next").addEventListener("click", sendNext);
document.getElementById("refresh").addEventListener("click", loadRows);
document.getElementById("apply-filters").addEventListener("click", loadRows);
document.addEventListener("keydown", (e) => {
  if (e.target.matches("input, select, textarea")) return;
  if (e.key === "n" || e.key === "Enter") { e.preventDefault(); sendNext(); }
  else if (e.key === "s") {
    state.cursor = Math.min(state.cursor + 1, state.rows.length - 1);
    localStorage.setItem(cursorKey(), String(state.cursor));
    render();
  }
  else if (e.key === "r") { e.preventDefault(); loadRows(); }
});

loadTarget().then(loadRows);
```

- [ ] **Step 4: Make sure static files are served**

`Program.cs` already calls `app.UseDefaultFiles()` + `app.UseStaticFiles()` (added in Task 7). No code change needed here. But confirm by running the helper and opening the root URL:

```bash
dotnet run --project backend/tools/SmartsuppWebhookReplay
```

Open `http://localhost:5050/` in a browser. Expected: the page renders. The audit table will be empty until the audit branch is live in the DB you are pointing at, but no JS errors should appear in the console.

Stop the host with Ctrl+C.

- [ ] **Step 5: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay/wwwroot/
git commit -m "feat(tools): add replay helper web UI (table + send-next)"
```

---

## Task 11: README + run instructions

**Files:**
- Create: `backend/tools/SmartsuppWebhookReplay/README.md`

- [ ] **Step 1: Write the README**

```markdown
# Smartsupp Webhook Replay

Debug-only helper that reposts recorded Smartsupp webhooks (from the `SmartsuppWebhookAuditEntries` table) one by one to a configurable target URL. Forwards the original `X-Smartsupp-Hmac` header verbatim so the target server runs the full controller path (signature check, JSON parsing, MediatR dispatch).

## Run

```
dotnet run --project backend/tools/SmartsuppWebhookReplay
```

Then open <http://localhost:5050/>.

## Configure

`appsettings.json`:

- `Replay:TargetUrl` — default `http://localhost:5001/api/webhooks/smartsupp`
- `Replay:TimeoutSeconds` — default `30`
- `Kestrel:Endpoints:Http:Url` — default `http://localhost:5050`

Connection string: reads from the **same** User Secrets store as the API project (`UserSecretsId f4e6382a-aefd-47ef-9cd7-7e12daac7e45`). Resolution order:

1. `ConnectionStrings:<ASPNETCORE_ENVIRONMENT>`
2. `ConnectionStrings:DefaultConnection`
3. `ConnectionStrings:Default`

To target staging vs production, swap `ConnectionStrings:Default` in your `secrets.json` (existing dev workflow).

## HMAC caveat

The helper forwards the recorded `X-Smartsupp-Hmac` header **verbatim**. The target server must run the same `Smartsupp:WebhookSecret` as the environment that originally received the webhook, otherwise the controller returns `401 Unauthorized`. This is the desired behavior for end-to-end debug — the helper is intentionally not a re-signer.

## Replay state

The target controller writes a **new** audit row on every successful replay (it has no idea the request is a replay). Replay counts in the helper UI are tracked client-side in `localStorage` only — the audit row's `ReplayCount` column (set by the in-process `/api/admin/smartsupp/webhooks/{id}/replay` endpoint) is **not** touched by this tool.
```

- [ ] **Step 2: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay/README.md
git commit -m "docs(tools): add README for SmartsuppWebhookReplay"
```

---

## Task 12: Full-suite verification + format check

- [ ] **Step 1: Format the new code**

```bash
dotnet format Anela.Heblo.sln \
  --include backend/tools/SmartsuppWebhookReplay backend/tools/SmartsuppWebhookReplay.Tests
```

Expected: exits cleanly with no remaining diff. If there is a diff, run a `chore: dotnet format` commit.

- [ ] **Step 2: Build the full solution**

```bash
dotnet build Anela.Heblo.sln
```

Expected: `Build succeeded.`

- [ ] **Step 3: Run the helper test suite**

```bash
dotnet test backend/tools/SmartsuppWebhookReplay.Tests/SmartsuppWebhookReplay.Tests.csproj
```

Expected: PASS — all 12 tests.

- [ ] **Step 4: End-to-end smoke test (manual)**

1. Confirm the audit branch is live on this branch and the audit DB table has at least one row (recorded by hitting `/api/webhooks/smartsupp` with a real Smartsupp payload, or by inserting a manual test row).
2. Start the API on `localhost:5001` (`dotnet run --project backend/src/Anela.Heblo.API`) using the same Smartsupp secret as the recording env.
3. Start the helper: `dotnet run --project backend/tools/SmartsuppWebhookReplay`.
4. Open `http://localhost:5050/`. Confirm the audit table is populated.
5. Click **Send** on one row. Confirm:
   - UI pill shows `200 · …ms` (green).
   - API logs show `smartsupp webhook event=... bodySize=...` (the controller received it).
   - A new audit row appears in the DB for the replayed request — refresh the helper UI to verify.
6. Repeat with the **Send next ↵** button and the `n` keyboard shortcut. Cursor advances.
7. Refresh the page; cursor position is preserved via `localStorage`.

If any step fails, debug before declaring complete.

- [ ] **Step 5: No commit**

Verification only — nothing to commit unless `dotnet format` produced a diff in Step 1.

---

## Verification summary

| Aspect | How to verify |
| --- | --- |
| Builds in isolation | `dotnet build backend/tools/SmartsuppWebhookReplay/SmartsuppWebhookReplay.csproj` |
| Builds in solution | `dotnet build Anela.Heblo.sln` |
| Tests pass | `dotnet test backend/tools/SmartsuppWebhookReplay.Tests/SmartsuppWebhookReplay.Tests.csproj` |
| Forwarder builds correct request | `WebhookForwarderTests` (4 tests) |
| List/Get endpoints behave | `AuditEndpointsTests` (5 tests) |
| Forward endpoint dispatches + 404s correctly | `ForwardEndpointTests` (2 tests) |
| UI renders + sends | Manual smoke test in Task 12 Step 4 |
| HMAC forwarded verbatim | `ForwardAsync_ForwardsRecordedSignatureHeaderVerbatim` + smoke test |

---

## Out of scope (explicit YAGNI)

- Re-signing the body with a different `WebhookSecret`. The user explicitly chose "forward verbatim" — same secret on both ends.
- Mutating `ReplayCount`/`LastReplayedAt`/`LastReplayedBy` on the source audit row. The in-process replay endpoint (added by the audit plan) already handles that case; this helper deliberately stays read-only against the source table.
- Authentication. The helper binds to `localhost` only and is dev-only.
- CORS / HTTPS — single-origin, plain HTTP.
- Server-side cursor — `localStorage` is enough for a single user.
- Docker packaging — this is a local debug tool, not deployed.
- Filtering by `from`/`to` UI controls beyond the basic `From` datepicker — add later if needed.
- A "Send all" bulk button — explicit one-at-a-time is the point of the tool.
