# SocketException Mitigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Investigate the 5x spike in `SocketException: "The operation was canceled."`, add a centralized observability handler that classifies outbound HTTP failures, fix the suspected `PooledConnectionLifetime` misconfiguration, and provide an opt-in per-dependency Polly resilience pipeline.

**Architecture:** A single `DelegatingHandler` (`OutboundCallObservabilityHandler`) is attached to every `AddHttpClient(...)` registration via a new `WithHebloOutboundDefaults()`/`WithHebloOutboundObservability()` extension. The defaults extension also configures a `SocketsHttpHandler` with a 4-minute `PooledConnectionLifetime` (shorter than the 4-minute Azure idle timeout). Classification (`ClientAborted`/`Timeout`/`Network`/`Unknown`) lives in one place; structured properties flow to both `ILogger` and `ITelemetryService`. A separate, opt-in per-dependency Polly v8 pipeline framework is shipped but disabled by default.

**Tech Stack:** .NET 8, `IHttpClientFactory`, `DelegatingHandler`, `SocketsHttpHandler`, Polly v8.4.1 (`ResiliencePipeline`, `IResiliencePipelineProvider`), `ITelemetryService` (Application Insights), `IHttpContextAccessor`, xUnit + FluentAssertions + Moq.

**Spec source:** `spec.r1.md` (FR-1 through FR-6).
**Arch source:** `arch-review.r1.md` (decisions 1-4, risk mitigations).

---

## File Structure

**New files (production):**

- `backend/src/Anela.Heblo.Xcc/Http/OutboundCallReason.cs` — enum (`Unknown`, `ClientAborted`, `Timeout`, `Network`).
- `backend/src/Anela.Heblo.Xcc/Http/OutboundCallLogProperties.cs` — string constants for App Insights property names (PascalCase).
- `backend/src/Anela.Heblo.Xcc/Http/OutboundResilienceOptions.cs` — root options + nested `DependencyResilienceOptions`.
- `backend/src/Anela.Heblo.Xcc/Http/OutboundCallObservabilityHandler.cs` — the `DelegatingHandler` that classifies and logs failures.
- `backend/src/Anela.Heblo.Xcc/Http/HebloHttpClientBuilderExtensions.cs` — `WithHebloOutboundDefaults`, `WithHebloOutboundObservability`.
- `backend/src/Anela.Heblo.Xcc/Http/HebloResiliencePipelineExtensions.cs` — registers per-dependency Polly `ResiliencePipeline` instances via `IResiliencePipelineProvider`, gated by config.

**New files (tests, mirror src under `backend/test/Anela.Heblo.Tests/Xcc/Http/`):**

- `OutboundResilienceOptionsTests.cs`
- `OutboundCallObservabilityHandlerTests.cs`
- `HebloHttpClientBuilderExtensionsTests.cs`
- `HebloResiliencePipelineExtensionsTests.cs`

**Documentation:**

- `docs/investigations/socket-exception-2026-05.md` — FR-1 inventory, FR-2 KQL queries + results, FR-6 post-deploy follow-up.

**Modified files (production):**

- `backend/src/Anela.Heblo.Xcc/Anela.Heblo.Xcc.csproj` — add `Microsoft.AspNetCore.Http.Abstractions` + `Polly`/`Polly.Extensions` references.
- `backend/src/Anela.Heblo.Xcc/XccModule.cs` — register `OutboundResilienceOptions`, `OutboundCallObservabilityHandler`, and the resilience pipelines.
- `backend/src/Anela.Heblo.API/appsettings.json` — add default `OutboundResilience` section.
- Every adapter/module `AddHttpClient(...)` site:
  - **Standard wiring (`.WithHebloOutboundDefaults()`):**
    - `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`
    - `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/HebloShoptetAdapterModule.cs`
    - `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` (4 clients)
    - `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPayAdapterServiceCollectionExtensions.cs`
    - `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs`
    - `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateAdapterServiceCollectionExtensions.cs`
    - `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantAdapterServiceCollectionExtensions.cs`
    - `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs`
    - `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppAdapterServiceCollectionExtensions.cs`
    - `backend/src/Adapters/Anela.Heblo.Adapters.WebSearch/WebSearchAdapterServiceCollectionExtensions.cs`
    - `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs`
    - `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs`
    - `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
    - `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`
  - **Observability-only wiring (`.WithHebloOutboundObservability()` — sites that already configure a custom primary handler):**
    - `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsAdapterServiceCollectionExtensions.cs` (`CupsAuthHandler` is a delegating handler — primary handler is still default; treat as observability-only out of caution because of the auth handler chain)
    - `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` (already sets `SocketsHttpHandler` with `PooledConnectionLifetime = 5 min` — leave it; add observability handler only)
    - `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` (sets a custom `HttpClientHandler` with `AllowAutoRedirect = true`)

---

## Task 1: Investigation document skeleton

**Files:**
- Create: `docs/investigations/socket-exception-2026-05.md`

- [ ] **Step 1: Create the investigation document skeleton**

Create `docs/investigations/socket-exception-2026-05.md` with the following content:

```markdown
# SocketException Spike Investigation — May 2026

**Date:** 2026-05-16
**Branch:** feat-bug-elevated-socketexception-exceptions-
**Trigger:** Application Insights showed 24 `System.Net.Sockets.SocketException` events in the last 24h vs. a 7-day baseline of 4.86/day. Sample message: `"The operation was canceled."`

## 1. Hypotheses

- `CancellationToken` (request abort, timeout, or shutdown) firing mid-socket-operation.
- `HttpClient`/`SocketsHttpHandler` pool issue (e.g., a socket reused after the upstream load balancer closed it).
- Server-side connection resets surfaced as wrapped socket cancellation.
- Application shutdown / IIS recycling cancelling in-flight requests.

## 2. HttpClient inventory (FR-1)

This table captures every `AddHttpClient(...)` registration in the backend and its current resilience posture.

| # | File | Client name / type | Target dependency | Primary handler | PooledConnectionLifetime | HttpClient.Timeout | Retry / circuit-breaker | Notes |
|---|------|--------------------|-------------------|-----------------|--------------------------|---------------------|--------------------------|-------|
| 1 | `Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` | default + `AddFlexiBee` | ABRA Flexi (ERP) | default | default (none) | default (100s) | none | `AddFlexiBee` is from `Rem.FlexiBeeSDK.Client.DI` (external) |
| 2 | `Adapters.Shoptet/HebloShoptetAdapterModule.cs` | default | Shoptet feed scraper | default | default | default | none | |
| 3 | `Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` | `ShoptetOrderClient`, `ShoptetStockClient`, `ShoptetInvoiceClient`, `HeurekaProductFeedClient` | Shoptet REST API | default | default | default | none | All 4 set `Shoptet-Private-API-Token` header |
| 4 | `Adapters.ShoptetApi/ShoptetPayAdapterServiceCollectionExtensions.cs` | `ShoptetPayBankClient` | ShoptetPay | default | default | default | none | |
| 5 | `Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs` | `"Anthropic"` | Anthropic API | default | default | configured per options | none in PR | `HttpTimeoutSeconds` from config |
| 6 | `Adapters.Comgate/ComgateAdapterServiceCollectionExtensions.cs` | `ComgateBankClient` | Comgate | default | default | default | none | |
| 7 | `Adapters.Cups/CupsAdapterServiceCollectionExtensions.cs` | `"Cups"` + `CupsAuthHandler` | CUPS printer (LAN) | default | default | default | none | `CupsAuthHandler` is a `DelegatingHandler` for Basic auth |
| 8 | `Adapters.HomeAssistant/HomeAssistantAdapterServiceCollectionExtensions.cs` | `HomeAssistantConditionsReadingProvider` | Home Assistant | default | default | per options | none | |
| 9 | `Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs` | `MetaAdsTransactionSource` | Meta Ads API | default | default | default | none | |
| 10 | `Adapters.Smartsupp/SmartsuppAdapterServiceCollectionExtensions.cs` | `"Smartsupp"` | Smartsupp API | default | default | per options | none | |
| 11 | `Adapters.WebSearch/WebSearchAdapterServiceCollectionExtensions.cs` | `"SerpApi"` | SerpApi | default | default | per options | none | |
| 12 | `Application/Features/FileStorage/FileStorageModule.cs` | `"ProductExportDownload"` | export URL (Shoptet) | **`SocketsHttpHandler`** | **5 min** | infinite | per-call `DownloadResilienceService` (retry + timeout) | Already pool-correct; treat as observability-only wiring |
| 13 | `Application/Features/Marketing/MarketingModule.cs` | `"MicrosoftGraph"` | Microsoft Graph | default | default | default | none | |
| 14 | `Application/Features/Photobank/PhotobankModule.cs` | `"MicrosoftGraph"` | Microsoft Graph | **`HttpClientHandler`** with `AllowAutoRedirect=true` | default | default | none | Observability-only wiring; do not replace primary handler |
| 15 | `Application/Features/OrgChart/OrgChartModule.cs` | `OrgChartService` | Microsoft Graph | default | default | default | none | |
| 16 | `Application/Features/MeetingTasks/MeetingTasksModule.cs` | `"MicrosoftGraph"` | Microsoft Graph | default | default | default | none | |
| 17 | `Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` | `"MicrosoftGraph"` | Microsoft Graph | default | default | default | none | |

**Direct `Socket`/`TcpClient`/`NetworkStream` usage:** none found in `backend/`.

## 3. App Insights queries (FR-2)

Run these KQL queries against the production Application Insights resource (Azure portal or `az monitor app-insights query`).

### 3.1 — 24h failing events with full context

```kql
exceptions
| where timestamp > ago(24h)
| where type == "System.Net.Sockets.SocketException"
| extend topFrame = tostring(parse_json(details)[0].parsedStack[0])
| project timestamp, operation_Name, target=tostring(customDimensions["TargetHost"]),
          reason=tostring(customDimensions["Reason"]),
          cloud_RoleInstance, operation_ParentId, topFrame, outerMessage
| order by timestamp desc
```

### 3.2 — Concentration by dependency / time bucket

```kql
exceptions
| where timestamp > ago(24h)
| where type == "System.Net.Sockets.SocketException"
| summarize count() by bin(timestamp, 1h), tostring(customDimensions["TargetHost"])
| render timechart
```

### 3.3 — 7-day baseline comparison

```kql
exceptions
| where timestamp > ago(8d)
| where type == "System.Net.Sockets.SocketException"
| summarize count() by bin(timestamp, 1d)
| render columnchart
```

### 3.4 — Concurrent app recycling / deployment correlation

```kql
union (traces | where message contains "Application started"),
      (traces | where message contains "Application is shutting down"),
      (exceptions | where type == "System.Net.Sockets.SocketException")
| where timestamp > ago(24h)
| project timestamp, itemType, cloud_RoleInstance, message, type
| order by timestamp asc
```

**Results:**

> _To be filled in by the engineer running the queries. Paste the raw rows or screenshots and write a 2-3 sentence summary._

## 4. Decision log

| Date | Decision | Reason |
|------|----------|--------|
| 2026-05-16 | Centralize via `DelegatingHandler` (`OutboundCallObservabilityHandler`) | One place to classify + log; no per-adapter drift |
| 2026-05-16 | Set `PooledConnectionLifetime = 4 min` as default in `WithHebloOutboundDefaults` | Shorter than typical Azure idle timeout — avoids "The operation was canceled." from stale pooled sockets |
| 2026-05-16 | Retries opt-in per-dependency via config, default off | Avoid masking real outages; only enable after FR-2 confirms a dependency is transient |
| 2026-05-16 | Do not introduce `Microsoft.Extensions.Http.Resilience` | Reuse the existing Polly v8 pattern already used by `DownloadResilienceService` |

## 5. Post-deploy follow-up (FR-6)

Run query 3.3 (7-day baseline) at **24h** and **48h** after deploy. Paste results below.

- **24h post-deploy:**
- **48h post-deploy:**

If the rate has not dropped to baseline, reopen with a new hypothesis (e.g., upstream outage, application-recycle storm, deployment churn).
```

- [ ] **Step 2: Commit**

```bash
git add docs/investigations/socket-exception-2026-05.md
git commit -m "docs: add socket exception investigation skeleton"
```

---

## Task 2: Extend `Anela.Heblo.Xcc.csproj` with required packages

**Files:**
- Modify: `backend/src/Anela.Heblo.Xcc/Anela.Heblo.Xcc.csproj`

The handler needs `IHttpContextAccessor` (in `Microsoft.AspNetCore.Http.Abstractions`) and the resilience pipeline registration helper needs `Polly` + `Polly.Extensions`.

- [ ] **Step 1: Add package references**

Edit `backend/src/Anela.Heblo.Xcc/Anela.Heblo.Xcc.csproj`. Inside the `<ItemGroup>` that contains the existing `PackageReference` entries, append:

```xml
        <PackageReference Include="Microsoft.AspNetCore.Http.Abstractions" Version="2.2.0" />
        <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
        <PackageReference Include="Polly" Version="8.4.1" />
        <PackageReference Include="Polly.Extensions" Version="8.4.1" />
```

Result file:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net8.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
    </PropertyGroup>


    <ItemGroup>
        <PackageReference Include="Microsoft.ApplicationInsights" Version="2.22.0" />
        <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="8.0.0" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0" />
        <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="8.0.0" />
        <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
        <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.0" />
        <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
        <PackageReference Include="Microsoft.AspNetCore.Http.Abstractions" Version="2.2.0" />
        <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
        <PackageReference Include="Polly" Version="8.4.1" />
        <PackageReference Include="Polly.Extensions" Version="8.4.1" />
    </ItemGroup>
</Project>
```

- [ ] **Step 2: Restore + build to verify the package added cleanly**

Run from the repo root:

```bash
dotnet build backend/src/Anela.Heblo.Xcc/Anela.Heblo.Xcc.csproj
```

Expected: build succeeds (warnings allowed; zero errors).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/Anela.Heblo.Xcc.csproj
git commit -m "chore(xcc): add HTTP abstractions and Polly references for outbound observability"
```

---

## Task 3: `OutboundCallReason` enum

**Files:**
- Create: `backend/src/Anela.Heblo.Xcc/Http/OutboundCallReason.cs`

- [ ] **Step 1: Create the enum**

Create `backend/src/Anela.Heblo.Xcc/Http/OutboundCallReason.cs`:

```csharp
namespace Anela.Heblo.Xcc.Http;

/// <summary>
/// Classification of an outbound HTTP call failure for observability and alerting.
/// </summary>
public enum OutboundCallReason
{
    Unknown = 0,
    ClientAborted = 1,
    Timeout = 2,
    Network = 3,
}
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build backend/src/Anela.Heblo.Xcc/Anela.Heblo.Xcc.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/Http/OutboundCallReason.cs
git commit -m "feat(xcc): add OutboundCallReason enum"
```

---

## Task 4: `OutboundCallLogProperties` constants

**Files:**
- Create: `backend/src/Anela.Heblo.Xcc/Http/OutboundCallLogProperties.cs`

PascalCase property names that survive Application Insights serialization without nesting (NFR-3).

- [ ] **Step 1: Create the constants file**

Create `backend/src/Anela.Heblo.Xcc/Http/OutboundCallLogProperties.cs`:

```csharp
namespace Anela.Heblo.Xcc.Http;

/// <summary>
/// Property names emitted on structured logs and Application Insights exception telemetry
/// for outbound HTTP call failures. PascalCase to match existing TelemetryService usage.
/// </summary>
public static class OutboundCallLogProperties
{
    public const string TargetHost = "TargetHost";
    public const string TargetPath = "TargetPath";
    public const string HttpMethod = "HttpMethod";
    public const string ElapsedMs = "ElapsedMs";
    public const string CancellationRequested = "CancellationRequested";
    public const string Reason = "Reason";
    public const string OperationId = "OperationId";
}
```

- [ ] **Step 2: Build**

```bash
dotnet build backend/src/Anela.Heblo.Xcc/Anela.Heblo.Xcc.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/Http/OutboundCallLogProperties.cs
git commit -m "feat(xcc): add OutboundCallLogProperties constants"
```

---

## Task 5: `OutboundResilienceOptions` + binding test

**Files:**
- Create: `backend/src/Anela.Heblo.Xcc/Http/OutboundResilienceOptions.cs`
- Test: `backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundResilienceOptionsTests.cs`

- [ ] **Step 1: Write the failing binding test**

Create `backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundResilienceOptionsTests.cs`:

```csharp
using Anela.Heblo.Xcc.Http;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Xcc.Http;

public class OutboundResilienceOptionsTests
{
    [Fact]
    public void Defaults_AreSafe_WhenSectionIsAbsent()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.Configure<OutboundResilienceOptions>(configuration.GetSection(OutboundResilienceOptions.SectionName));

        // Act
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboundResilienceOptions>>().Value;

        // Assert
        options.LoggingEnabled.Should().BeTrue();
        options.PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(4));
        options.Dependencies.Should().BeEmpty();
    }

    [Fact]
    public void BindsPerDependencySection_FromConfiguration()
    {
        // Arrange
        var data = new Dictionary<string, string?>
        {
            ["OutboundResilience:LoggingEnabled"] = "true",
            ["OutboundResilience:PooledConnectionLifetime"] = "00:02:30",
            ["OutboundResilience:Dependencies:Shoptet:RetryEnabled"] = "true",
            ["OutboundResilience:Dependencies:Shoptet:MaxRetryAttempts"] = "5",
            ["OutboundResilience:Dependencies:Shoptet:RetryBaseDelay"] = "00:00:00.500",
            ["OutboundResilience:Dependencies:Shoptet:Timeout"] = "00:01:00",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        var services = new ServiceCollection();
        services.Configure<OutboundResilienceOptions>(configuration.GetSection(OutboundResilienceOptions.SectionName));

        // Act
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<OutboundResilienceOptions>>().Value;

        // Assert
        options.PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(2.5));
        options.Dependencies.Should().ContainKey("Shoptet");
        var shoptet = options.Dependencies["Shoptet"];
        shoptet.RetryEnabled.Should().BeTrue();
        shoptet.MaxRetryAttempts.Should().Be(5);
        shoptet.RetryBaseDelay.Should().Be(TimeSpan.FromMilliseconds(500));
        shoptet.Timeout.Should().Be(TimeSpan.FromMinutes(1));
    }
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutboundResilienceOptionsTests"
```

Expected: compilation failure or test failure (the types do not exist yet).

- [ ] **Step 3: Create the options classes**

Create `backend/src/Anela.Heblo.Xcc/Http/OutboundResilienceOptions.cs`:

```csharp
namespace Anela.Heblo.Xcc.Http;

/// <summary>
/// Root options that govern the outbound observability handler and per-dependency
/// resilience pipelines. Bound from the "OutboundResilience" configuration section.
/// Defaults are intentionally safe: logging on, 4-minute connection lifetime, no per-dependency retries.
/// </summary>
public sealed class OutboundResilienceOptions
{
    public const string SectionName = "OutboundResilience";

    /// <summary>
    /// When false, the observability handler short-circuits to pass-through.
    /// Lets an operator disable the handler in production without a redeploy.
    /// </summary>
    public bool LoggingEnabled { get; set; } = true;

    /// <summary>
    /// SocketsHttpHandler.PooledConnectionLifetime applied by WithHebloOutboundDefaults.
    /// Must be shorter than the shortest upstream load-balancer idle timeout (Azure App
    /// Service / Front Door default = 4 minutes) to avoid using a socket the LB has closed.
    /// </summary>
    public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(4);

    /// <summary>
    /// Per-dependency resilience configuration. Key is a logical dependency name
    /// (e.g., "Shoptet", "Flexi"); resolved by the call site via IResiliencePipelineProvider.
    /// </summary>
    public Dictionary<string, DependencyResilienceOptions> Dependencies { get; set; } = new();
}

public sealed class DependencyResilienceOptions
{
    public bool RetryEnabled { get; set; }
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
```

> Note: `set` (not `init`) is required for `Microsoft.Extensions.Configuration.Binder` to bind into the dictionary entries. `Dictionary<string, T>` and nested objects bind correctly with `set`-based setters.

- [ ] **Step 4: Run the test to confirm it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutboundResilienceOptionsTests"
```

Expected: 2 passing tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/Http/OutboundResilienceOptions.cs \
        backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundResilienceOptionsTests.cs
git commit -m "feat(xcc): add OutboundResilienceOptions with per-dependency configuration"
```

---

## Task 6: `OutboundCallObservabilityHandler` — happy path pass-through

**Files:**
- Create: `backend/src/Anela.Heblo.Xcc/Http/OutboundCallObservabilityHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs`

Test infrastructure: we will use a small `TestHttpMessageHandler` that returns a canned response or throws an exception we control. The DelegatingHandler is invoked by `HttpClient` to verify end-to-end behavior.

- [ ] **Step 1: Write the failing happy-path test**

Create `backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs`:

```csharp
using System.Net;
using System.Net.Http;
using Anela.Heblo.Xcc.Http;
using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Xcc.Http;

public class OutboundCallObservabilityHandlerTests
{
    private static OutboundCallObservabilityHandler CreateHandler(
        HttpMessageHandler inner,
        Mock<ITelemetryService>? telemetry = null,
        HttpContext? httpContext = null,
        OutboundResilienceOptions? options = null,
        ILogger<OutboundCallObservabilityHandler>? logger = null)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(httpContext);

        var optionsMonitor = new Mock<IOptionsMonitor<OutboundResilienceOptions>>();
        optionsMonitor.SetupGet(m => m.CurrentValue).Returns(options ?? new OutboundResilienceOptions());

        return new OutboundCallObservabilityHandler(
            logger ?? NullLogger<OutboundCallObservabilityHandler>.Instance,
            (telemetry ?? new Mock<ITelemetryService>()).Object,
            accessor.Object,
            optionsMonitor.Object)
        {
            InnerHandler = inner,
        };
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler);

    [Fact]
    public async Task HappyPath_DoesNotLogOrTrack()
    {
        // Arrange
        var inner = new StubHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var telemetry = new Mock<ITelemetryService>(MockBehavior.Strict);
        var handler = CreateHandler(inner, telemetry);
        var client = CreateClient(handler);

        // Act
        var response = await client.GetAsync("https://api.example.com/items");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        telemetry.VerifyNoOtherCalls();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _send(request, cancellationToken);
    }
}
```

- [ ] **Step 2: Run the test — confirm compile failure**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutboundCallObservabilityHandlerTests"
```

Expected: compilation error (`OutboundCallObservabilityHandler` does not exist).

- [ ] **Step 3: Implement the minimal handler (happy-path only)**

Create `backend/src/Anela.Heblo.Xcc/Http/OutboundCallObservabilityHandler.cs`:

```csharp
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Xcc.Http;

/// <summary>
/// DelegatingHandler attached to every Heblo outbound HttpClient. On the happy path it is a
/// near-zero overhead pass-through. On failure it classifies the exception, emits a structured
/// ILogger entry, and tracks the exception via ITelemetryService with PascalCase properties
/// that align with the rest of the Heblo telemetry surface.
///
/// In a background context (Hangfire job, hosted service) IHttpContextAccessor.HttpContext is
/// null. The handler then falls back to the caller's CancellationToken to decide whether a
/// cancellation came from the caller (ClientAborted) or from a per-call timeout (Timeout).
/// </summary>
public sealed class OutboundCallObservabilityHandler : DelegatingHandler
{
    private readonly ILogger<OutboundCallObservabilityHandler> _logger;
    private readonly ITelemetryService _telemetry;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptionsMonitor<OutboundResilienceOptions> _options;

    public OutboundCallObservabilityHandler(
        ILogger<OutboundCallObservabilityHandler> logger,
        ITelemetryService telemetry,
        IHttpContextAccessor httpContextAccessor,
        IOptionsMonitor<OutboundResilienceOptions> options)
    {
        _logger = logger;
        _telemetry = telemetry;
        _httpContextAccessor = httpContextAccessor;
        _options = options;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 4: Run the test — confirm it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutboundCallObservabilityHandlerTests"
```

Expected: 1 passing test.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/Http/OutboundCallObservabilityHandler.cs \
        backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs
git commit -m "feat(xcc): add OutboundCallObservabilityHandler skeleton (happy path)"
```

---

## Task 7: `OutboundCallObservabilityHandler` — `LoggingEnabled = false` short-circuit

**Files:**
- Modify: `backend/src/Anela.Heblo.Xcc/Http/OutboundCallObservabilityHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `OutboundCallObservabilityHandlerTests`:

```csharp
    [Fact]
    public async Task LoggingDisabled_DoesNotTrackOnException()
    {
        // Arrange
        var inner = new StubHandler((req, ct) => throw new HttpRequestException("boom"));
        var telemetry = new Mock<ITelemetryService>(MockBehavior.Strict);
        var options = new OutboundResilienceOptions { LoggingEnabled = false };
        var handler = CreateHandler(inner, telemetry, options: options);
        var client = CreateClient(handler);

        // Act
        var act = async () => await client.GetAsync("https://api.example.com/items");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        telemetry.VerifyNoOtherCalls();
    }
```

- [ ] **Step 2: Run the test — confirm failure**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutboundCallObservabilityHandlerTests.LoggingDisabled"
```

Expected: the test compiles but fails (no current logging path exists yet — both tests pass because the handler is a pure pass-through; we still want to harden the contract before adding logging).

> If the test passes here, that's fine — it locks in the contract before we add real logging in Task 8+ and prevents a regression where `LoggingEnabled = false` accidentally still tracks.

- [ ] **Step 3: Add the explicit short-circuit branch**

Replace the body of `SendAsync` in `OutboundCallObservabilityHandler.cs`:

```csharp
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.LoggingEnabled)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
```

- [ ] **Step 4: Run all handler tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutboundCallObservabilityHandlerTests"
```

Expected: 2 passing.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/Http/OutboundCallObservabilityHandler.cs \
        backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs
git commit -m "feat(xcc): observability handler honors LoggingEnabled=false"
```

---

## Task 8: `OutboundCallObservabilityHandler` — `ClientAborted` classification

**Files:**
- Modify: `backend/src/Anela.Heblo.Xcc/Http/OutboundCallObservabilityHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `OutboundCallObservabilityHandlerTests`:

```csharp
    [Fact]
    public async Task ClientAborted_WhenHttpContextRequestAbortedFires_LogsWarningWithClientAbortedReason()
    {
        // Arrange
        var inboundCts = new CancellationTokenSource();
        var httpContext = new DefaultHttpContext { RequestAborted = inboundCts.Token };
        var inner = new StubHandler(async (req, ct) =>
        {
            inboundCts.Cancel();
            ct.ThrowIfCancellationRequested();
            return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var telemetry = new Mock<ITelemetryService>();
        Dictionary<string, string>? capturedProperties = null;
        telemetry.Setup(t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()))
                 .Callback<Exception, Dictionary<string, string>?>((_, props) => capturedProperties = props);

        var loggerMock = new Mock<ILogger<OutboundCallObservabilityHandler>>();
        var handler = CreateHandler(inner, telemetry, httpContext: httpContext, logger: loggerMock.Object);
        var client = CreateClient(handler);

        // Act
        var act = async () => await client.GetAsync("https://api.example.com/items", inboundCts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        capturedProperties.Should().NotBeNull();
        capturedProperties![OutboundCallLogProperties.Reason].Should().Be(nameof(OutboundCallReason.ClientAborted));
        capturedProperties[OutboundCallLogProperties.HttpMethod].Should().Be("GET");
        capturedProperties[OutboundCallLogProperties.TargetHost].Should().Be("api.example.com");
        capturedProperties[OutboundCallLogProperties.TargetPath].Should().Be("/items");
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
```

- [ ] **Step 2: Run the test — confirm failure**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutboundCallObservabilityHandlerTests.ClientAborted"
```

Expected: fail (handler does not yet classify or log).

- [ ] **Step 3: Implement classification + logging**

Replace `backend/src/Anela.Heblo.Xcc/Http/OutboundCallObservabilityHandler.cs` with:

```csharp
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Xcc.Http;

public sealed class OutboundCallObservabilityHandler : DelegatingHandler
{
    private readonly ILogger<OutboundCallObservabilityHandler> _logger;
    private readonly ITelemetryService _telemetry;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptionsMonitor<OutboundResilienceOptions> _options;

    public OutboundCallObservabilityHandler(
        ILogger<OutboundCallObservabilityHandler> logger,
        ITelemetryService telemetry,
        IHttpContextAccessor httpContextAccessor,
        IOptionsMonitor<OutboundResilienceOptions> options)
    {
        _logger = logger;
        _telemetry = telemetry;
        _httpContextAccessor = httpContextAccessor;
        _options = options;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.LoggingEnabled)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            var reason = Classify(ex, cancellationToken, out var inboundCancellationRequested);
            LogFailure(ex, request, elapsed, reason, inboundCancellationRequested);
            throw;
        }
    }

    private OutboundCallReason Classify(Exception ex, CancellationToken callerToken, out bool inboundCancellationRequested)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        // In a background context (Hangfire job, hosted service) HttpContext is null;
        // fall back to the caller token as the inbound signal.
        var inboundToken = httpContext is not null ? httpContext.RequestAborted : callerToken;
        inboundCancellationRequested = inboundToken.IsCancellationRequested;

        if (ex is OperationCanceledException)
        {
            return inboundCancellationRequested
                ? OutboundCallReason.ClientAborted
                : OutboundCallReason.Timeout;
        }

        if (ex is SocketException or IOException or HttpRequestException)
        {
            return OutboundCallReason.Network;
        }

        return OutboundCallReason.Unknown;
    }

    private void LogFailure(
        Exception ex,
        HttpRequestMessage request,
        TimeSpan elapsed,
        OutboundCallReason reason,
        bool inboundCancellationRequested)
    {
        var targetHost = request.RequestUri?.Host ?? "unknown";
        var targetPath = request.RequestUri?.AbsolutePath ?? string.Empty;
        var method = request.Method.Method;
        var operationId = Activity.Current?.RootId ?? string.Empty;
        var elapsedMs = (long)elapsed.TotalMilliseconds;

        var level = reason == OutboundCallReason.ClientAborted ? LogLevel.Warning : LogLevel.Error;

        _logger.Log(
            level,
            ex,
            "Outbound call failed: {HttpMethod} {TargetHost}{TargetPath} after {ElapsedMs}ms (Reason: {Reason})",
            method, targetHost, targetPath, elapsedMs, reason);

        _telemetry.TrackException(ex, new Dictionary<string, string>
        {
            [OutboundCallLogProperties.TargetHost] = targetHost,
            [OutboundCallLogProperties.TargetPath] = targetPath,
            [OutboundCallLogProperties.HttpMethod] = method,
            [OutboundCallLogProperties.ElapsedMs] = elapsedMs.ToString(),
            [OutboundCallLogProperties.Reason] = reason.ToString(),
            [OutboundCallLogProperties.CancellationRequested] = inboundCancellationRequested ? "true" : "false",
            [OutboundCallLogProperties.OperationId] = operationId,
        });
    }
}
```

- [ ] **Step 4: Run the handler test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutboundCallObservabilityHandlerTests"
```

Expected: 3 passing tests (HappyPath, LoggingDisabled, ClientAborted).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/Http/OutboundCallObservabilityHandler.cs \
        backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs
git commit -m "feat(xcc): classify client-aborted outbound calls as Warning"
```

---

## Task 9: `OutboundCallObservabilityHandler` — `Timeout` classification

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs`

The handler code from Task 8 already classifies `Timeout`. This task adds the missing test that locks in the contract.

- [ ] **Step 1: Add the failing test**

Append to `OutboundCallObservabilityHandlerTests`:

```csharp
    [Fact]
    public async Task Timeout_WhenInboundTokenNotCancelled_LogsErrorWithTimeoutReason()
    {
        // Arrange
        var inner = new StubHandler((req, ct) => throw new TaskCanceledException("HttpClient.Timeout"));
        var telemetry = new Mock<ITelemetryService>();
        Dictionary<string, string>? captured = null;
        telemetry.Setup(t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()))
                 .Callback<Exception, Dictionary<string, string>?>((_, props) => captured = props);
        // HttpContext exists, but its RequestAborted is not signaled.
        var httpContext = new DefaultHttpContext();
        var loggerMock = new Mock<ILogger<OutboundCallObservabilityHandler>>();
        var handler = CreateHandler(inner, telemetry, httpContext: httpContext, logger: loggerMock.Object);
        var client = CreateClient(handler);

        // Act
        var act = async () => await client.GetAsync("https://api.example.com/v1/resource");

        // Assert
        await act.Should().ThrowAsync<TaskCanceledException>();
        captured.Should().NotBeNull();
        captured![OutboundCallLogProperties.Reason].Should().Be(nameof(OutboundCallReason.Timeout));
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
```

- [ ] **Step 2: Run the new test**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutboundCallObservabilityHandlerTests.Timeout"
```

Expected: passes (handler logic already in place from Task 8).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs
git commit -m "test(xcc): cover Timeout classification for outbound calls"
```

---

## Task 10: `OutboundCallObservabilityHandler` — `Network` classification

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs`

- [ ] **Step 1: Add the failing test**

Append:

```csharp
    [Theory]
    [InlineData(typeof(SocketException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(HttpRequestException))]
    public async Task Network_WhenTransportException_LogsErrorWithNetworkReason(Type exceptionType)
    {
        // Arrange
        Exception thrown = exceptionType.Name switch
        {
            nameof(SocketException) => new SocketException(),
            nameof(IOException) => new IOException("connection reset"),
            nameof(HttpRequestException) => new HttpRequestException("name resolution failed"),
            _ => throw new InvalidOperationException(),
        };
        var inner = new StubHandler((req, ct) => throw thrown);
        var telemetry = new Mock<ITelemetryService>();
        Dictionary<string, string>? captured = null;
        telemetry.Setup(t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()))
                 .Callback<Exception, Dictionary<string, string>?>((_, props) => captured = props);
        var handler = CreateHandler(inner, telemetry);
        var client = CreateClient(handler);

        // Act
        var act = async () => await client.GetAsync("https://api.example.com/x");

        // Assert
        await act.Should().ThrowAsync<Exception>();
        captured.Should().NotBeNull();
        captured![OutboundCallLogProperties.Reason].Should().Be(nameof(OutboundCallReason.Network));
    }
```

> `using System.IO;`, `using System.Net.Sockets;`, and `using System.Net.Http;` are already in the test file from Task 6/8 imports — ensure they are present at the top of the file.

- [ ] **Step 2: Run the new test**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutboundCallObservabilityHandlerTests.Network"
```

Expected: 3 passing data rows.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs
git commit -m "test(xcc): cover Network classification for outbound calls"
```

---

## Task 11: `OutboundCallObservabilityHandler` — URL redaction (no query string)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs`

NFR-2 requires that the query string never leaks to telemetry. `request.RequestUri.AbsolutePath` already excludes the query, so this test locks the contract.

- [ ] **Step 1: Add the failing test**

Append:

```csharp
    [Fact]
    public async Task TelemetryProperties_DoNotIncludeQueryString_NorBearerToken()
    {
        // Arrange
        var inner = new StubHandler((req, ct) => throw new HttpRequestException("boom"));
        var telemetry = new Mock<ITelemetryService>();
        Dictionary<string, string>? captured = null;
        telemetry.Setup(t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()))
                 .Callback<Exception, Dictionary<string, string>?>((_, props) => captured = props);
        var handler = CreateHandler(inner, telemetry);
        var client = CreateClient(handler);

        // Act
        var act = async () => await client.GetAsync("https://api.example.com/v1/secret?token=should-not-leak&apiKey=sk-xyz");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        captured.Should().NotBeNull();
        captured![OutboundCallLogProperties.TargetPath].Should().Be("/v1/secret");
        captured.Values.Should().NotContain(v => v.Contains("token=", StringComparison.OrdinalIgnoreCase));
        captured.Values.Should().NotContain(v => v.Contains("apiKey=", StringComparison.OrdinalIgnoreCase));
        captured.Values.Should().NotContain(v => v.Contains("sk-xyz", StringComparison.Ordinal));
    }
```

- [ ] **Step 2: Run the test**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutboundCallObservabilityHandlerTests.TelemetryProperties_DoNot"
```

Expected: passes.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs
git commit -m "test(xcc): verify outbound observability redacts query string and tokens"
```

---

## Task 12: `OutboundCallObservabilityHandler` — background context fallback

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs`

- [ ] **Step 1: Add the failing tests**

Append:

```csharp
    [Fact]
    public async Task NoHttpContext_WhenCallerTokenCancelled_ClassifiesAsClientAborted()
    {
        // Arrange — Hangfire / hosted service: HttpContext is null.
        using var cts = new CancellationTokenSource();
        var inner = new StubHandler(async (req, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var telemetry = new Mock<ITelemetryService>();
        Dictionary<string, string>? captured = null;
        telemetry.Setup(t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()))
                 .Callback<Exception, Dictionary<string, string>?>((_, props) => captured = props);
        var handler = CreateHandler(inner, telemetry, httpContext: null);
        var client = CreateClient(handler);

        // Act
        var act = async () => await client.GetAsync("https://api.example.com/x", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        captured.Should().NotBeNull();
        captured![OutboundCallLogProperties.Reason].Should().Be(nameof(OutboundCallReason.ClientAborted));
    }

    [Fact]
    public async Task NoHttpContext_WhenCallerTokenNotCancelled_ClassifiesAsTimeout()
    {
        // Arrange — background context, exception is OperationCanceledException with no caller cancellation.
        var inner = new StubHandler((req, ct) => throw new TaskCanceledException("per-call CTS timed out"));
        var telemetry = new Mock<ITelemetryService>();
        Dictionary<string, string>? captured = null;
        telemetry.Setup(t => t.TrackException(It.IsAny<Exception>(), It.IsAny<Dictionary<string, string>>()))
                 .Callback<Exception, Dictionary<string, string>?>((_, props) => captured = props);
        var handler = CreateHandler(inner, telemetry, httpContext: null);
        var client = CreateClient(handler);

        // Act
        var act = async () => await client.GetAsync("https://api.example.com/x");

        // Assert
        await act.Should().ThrowAsync<TaskCanceledException>();
        captured.Should().NotBeNull();
        captured![OutboundCallLogProperties.Reason].Should().Be(nameof(OutboundCallReason.Timeout));
    }
```

- [ ] **Step 2: Run all handler tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutboundCallObservabilityHandlerTests"
```

Expected: all tests pass (9+ rows).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Xcc/Http/OutboundCallObservabilityHandlerTests.cs
git commit -m "test(xcc): cover background-context fallback in observability handler"
```

---

## Task 13: `HebloHttpClientBuilderExtensions` — `WithHebloOutboundObservability`

**Files:**
- Create: `backend/src/Anela.Heblo.Xcc/Http/HebloHttpClientBuilderExtensions.cs`
- Test: `backend/test/Anela.Heblo.Tests/Xcc/Http/HebloHttpClientBuilderExtensionsTests.cs`

`WithHebloOutboundObservability` is the safe additive extension — it only attaches the observability `DelegatingHandler` and does not touch the primary handler. Sites with custom primary handlers (Photobank, FileStorage, Cups) use this variant.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Xcc/Http/HebloHttpClientBuilderExtensionsTests.cs`:

```csharp
using Anela.Heblo.Xcc.Http;
using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Xcc.Http;

public class HebloHttpClientBuilderExtensionsTests
{
    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.Configure<OutboundResilienceOptions>(_ => { });
        services.AddSingleton(new Mock<ITelemetryService>().Object);
        services.AddTransient<OutboundCallObservabilityHandler>();
        return services;
    }

    [Fact]
    public void WithHebloOutboundObservability_AttachesObservabilityHandler()
    {
        // Arrange
        var services = BaseServices();
        services.AddHttpClient("test").WithHebloOutboundObservability();

        // Act
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");

        // Assert — handler is wrapped; the client resolves without throwing
        client.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run the test — confirm compile failure**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HebloHttpClientBuilderExtensionsTests"
```

Expected: compile fail (`WithHebloOutboundObservability` does not exist).

- [ ] **Step 3: Create the extension**

Create `backend/src/Anela.Heblo.Xcc/Http/HebloHttpClientBuilderExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Xcc.Http;

/// <summary>
/// IHttpClientBuilder helpers that wire every Heblo outbound HttpClient with the same
/// observability + connection-pool defaults. Use WithHebloOutboundDefaults() for typical
/// registrations; use WithHebloOutboundObservability() when the caller has already
/// configured a custom primary handler.
/// </summary>
public static class HebloHttpClientBuilderExtensions
{
    /// <summary>
    /// Attaches OutboundCallObservabilityHandler as a delegating handler. Does not change
    /// the primary handler — safe to use on registrations that already call
    /// ConfigurePrimaryHttpMessageHandler with a custom handler.
    /// </summary>
    public static IHttpClientBuilder WithHebloOutboundObservability(this IHttpClientBuilder builder)
    {
        return builder.AddHttpMessageHandler<OutboundCallObservabilityHandler>();
    }
}
```

- [ ] **Step 4: Run the test — confirm pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HebloHttpClientBuilderExtensionsTests"
```

Expected: passes.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/Http/HebloHttpClientBuilderExtensions.cs \
        backend/test/Anela.Heblo.Tests/Xcc/Http/HebloHttpClientBuilderExtensionsTests.cs
git commit -m "feat(xcc): add WithHebloOutboundObservability extension"
```

---

## Task 14: `HebloHttpClientBuilderExtensions` — `WithHebloOutboundDefaults`

**Files:**
- Modify: `backend/src/Anela.Heblo.Xcc/Http/HebloHttpClientBuilderExtensions.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Xcc/Http/HebloHttpClientBuilderExtensionsTests.cs`

Adds a second extension that also configures the primary `SocketsHttpHandler` with `PooledConnectionLifetime` from options.

- [ ] **Step 1: Add the failing test**

Append to `HebloHttpClientBuilderExtensionsTests`:

```csharp
    [Fact]
    public void WithHebloOutboundDefaults_ConfiguresSocketsHttpHandlerWithPooledConnectionLifetime()
    {
        // Arrange
        var services = BaseServices();
        services.Configure<OutboundResilienceOptions>(o => o.PooledConnectionLifetime = TimeSpan.FromMinutes(2));
        services.AddHttpClient("named").WithHebloOutboundDefaults();

        // Act
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        var primaryHandler = factory.CreateHandler("named");

        // Walk the DelegatingHandler chain to the primary.
        var current = primaryHandler;
        while (current is DelegatingHandler dh && dh.InnerHandler is not null)
        {
            current = dh.InnerHandler;
        }

        // Assert
        current.Should().BeOfType<SocketsHttpHandler>();
        var socketsHandler = (SocketsHttpHandler)current!;
        socketsHandler.PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(2));
    }
```

- [ ] **Step 2: Run the test — confirm compile failure**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HebloHttpClientBuilderExtensionsTests.WithHebloOutboundDefaults"
```

Expected: compile fail (`WithHebloOutboundDefaults` does not exist).

- [ ] **Step 3: Add the extension method**

Append to `HebloHttpClientBuilderExtensions.cs` inside the class:

```csharp
    /// <summary>
    /// Attaches the observability handler AND configures the primary handler to a
    /// SocketsHttpHandler with the configured PooledConnectionLifetime. Callers that
    /// need a non-default primary handler (HttpClientHandler, custom redirect rules,
    /// etc.) must use WithHebloOutboundObservability() instead.
    /// </summary>
    public static IHttpClientBuilder WithHebloOutboundDefaults(this IHttpClientBuilder builder)
    {
        builder.ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var options = sp.GetRequiredService<IOptions<OutboundResilienceOptions>>().Value;
            return new SocketsHttpHandler
            {
                PooledConnectionLifetime = options.PooledConnectionLifetime,
            };
        });
        return builder.WithHebloOutboundObservability();
    }
```

Update the `using` block at the top to add `using System.Net.Http;` so `SocketsHttpHandler` resolves.

- [ ] **Step 4: Run the test — confirm pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HebloHttpClientBuilderExtensionsTests"
```

Expected: both tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/Http/HebloHttpClientBuilderExtensions.cs \
        backend/test/Anela.Heblo.Tests/Xcc/Http/HebloHttpClientBuilderExtensionsTests.cs
git commit -m "feat(xcc): add WithHebloOutboundDefaults extension with PooledConnectionLifetime"
```

---

## Task 15: Register options + handler in `XccModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Xcc/XccModule.cs`

- [ ] **Step 1: Add the registration**

Edit `backend/src/Anela.Heblo.Xcc/XccModule.cs`. Add the using and the two service registrations:

```csharp
using Anela.Heblo.Xcc.Http;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Anela.Heblo.Xcc.Services.Dashboard;
using Anela.Heblo.Xcc.Services.Dashboard.Tiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Xcc;

public static class XccModule
{
    public static IServiceCollection AddXccServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register background refresh services with configuration support
        services.AddBackgroundRefresh(configuration);

        // Register dashboard services
        services.Configure<DashboardOptions>(configuration.GetSection(DashboardOptions.SectionName));
        services.AddSingleton<ITileRegistry, TileRegistry>();
        services.AddScoped<IDashboardService, DashboardService>();

        // Register system tiles
        services.RegisterTile<BackgroundTaskStatusTile>();

        // Outbound HTTP observability + connection-pool defaults.
        // Handler is Transient because IHttpClientFactory creates a new handler chain per
        // CreateClient() call (and recycles the primary handler per HandlerLifetime).
        services.Configure<OutboundResilienceOptions>(configuration.GetSection(OutboundResilienceOptions.SectionName));
        services.AddTransient<OutboundCallObservabilityHandler>();

        return services;
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build backend/src/Anela.Heblo.Xcc/Anela.Heblo.Xcc.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/XccModule.cs
git commit -m "feat(xcc): register outbound observability handler in XccModule"
```

---

## Task 16: Default `OutboundResilience` config in `appsettings.json`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Add the section**

Insert the following section in `backend/src/Anela.Heblo.API/appsettings.json` immediately after the `"ApplicationInsights": { ... }` block (placement is illustrative — the JSON is valid wherever you insert it, but keep the file alphabetized within reason and place near other infrastructure settings):

```json
  "OutboundResilience": {
    "LoggingEnabled": true,
    "PooledConnectionLifetime": "00:04:00",
    "Dependencies": {}
  },
```

Be careful with the trailing comma — if you place the section before `"Application": { ... }`, the trailing comma is needed; if you place it as the last property in the object, drop the trailing comma. Validate JSON before commit.

- [ ] **Step 2: Validate JSON parses cleanly**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

If a `Microsoft.Extensions.Configuration.Json` parse failure appears at startup, you have a JSON syntax error — fix and retry.

Run a quick parse sanity check (no network access required):

```bash
node -e "JSON.parse(require('fs').readFileSync('backend/src/Anela.Heblo.API/appsettings.json', 'utf8')); console.log('OK')"
```

Expected: `OK`.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(api): add OutboundResilience defaults to appsettings.json"
```

---

## Task 17: Wire `WithHebloOutboundDefaults` — adapter group A (Flexi, Shoptet, ShoptetApi, Anthropic, Comgate)

**Files (modify each):**
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/HebloShoptetAdapterModule.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPayAdapterServiceCollectionExtensions.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateAdapterServiceCollectionExtensions.cs`

Each adapter `*.csproj` already transitively references `Anela.Heblo.Xcc` (verify via `cat backend/src/Adapters/.../*.csproj` if uncertain).

- [ ] **Step 1: Flexi — add `.WithHebloOutboundDefaults()` to the `AddHttpClient()` call**

In `FlexiAdapterServiceCollectionExtensions.cs`, replace:

```csharp
        services.AddHttpClient();
```

with:

```csharp
        services.AddHttpClient(string.Empty).WithHebloOutboundDefaults();
```

Add at the top of the file: `using Anela.Heblo.Xcc.Http;`.

> Note: `AddHttpClient(string.Empty)` is the explicit way to get an `IHttpClientBuilder` for the default client. The bare `AddHttpClient()` returns `IServiceCollection`.

- [ ] **Step 2: Shoptet — same change in `HebloShoptetAdapterModule.cs`**

Replace:

```csharp
        services.AddHttpClient();
```

with:

```csharp
        services.AddHttpClient(string.Empty).WithHebloOutboundDefaults();
```

Add `using Anela.Heblo.Xcc.Http;`.

- [ ] **Step 3: ShoptetApi — wire 4 clients**

In `ShoptetApiAdapterServiceCollectionExtensions.cs`, append `.WithHebloOutboundDefaults()` to each of the 4 `AddHttpClient<...>` calls (`IEshopOrderClient`, `IEshopStockClient`, `IShoptetInvoiceClient`, `IProductEshopUrlClient`):

```csharp
        services.AddHttpClient<IEshopOrderClient, ShoptetOrderClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
        }).WithHebloOutboundDefaults();
```

Apply the same `.WithHebloOutboundDefaults()` suffix to `IEshopStockClient`, `IShoptetInvoiceClient`, and `IProductEshopUrlClient` registrations.

Add `using Anela.Heblo.Xcc.Http;` to the file header.

- [ ] **Step 4: ShoptetPay**

In `ShoptetPayAdapterServiceCollectionExtensions.cs`, append `.WithHebloOutboundDefaults()` to the `ShoptetPayBankClient` `AddHttpClient` call. Add the using.

- [ ] **Step 5: Anthropic**

In `AnthropicAdapterServiceCollectionExtensions.cs`, append `.WithHebloOutboundDefaults()` to:

```csharp
        services.AddHttpClient("Anthropic", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        }).WithHebloOutboundDefaults();
```

Add the using.

- [ ] **Step 6: Comgate**

In `ComgateAdapterServiceCollectionExtensions.cs`, replace:

```csharp
        services.AddHttpClient<ComgateBankClient>();
```

with:

```csharp
        services.AddHttpClient<ComgateBankClient>().WithHebloOutboundDefaults();
```

Add the using.

- [ ] **Step 7: Build all touched projects**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: compiles with no errors.

- [ ] **Step 8: Run the existing test suite for these adapters (smoke)**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --filter "Category!=Integration" --no-build
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "Category!=Integration" --no-build
```

Expected: existing tests still pass (handler is additive; behavior unchanged unless a request fails).

- [ ] **Step 9: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi \
        backend/src/Adapters/Anela.Heblo.Adapters.Shoptet \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi \
        backend/src/Adapters/Anela.Heblo.Adapters.Anthropic \
        backend/src/Adapters/Anela.Heblo.Adapters.Comgate
git commit -m "feat: wire outbound observability defaults in Flexi/Shoptet/ShoptetApi/Anthropic/Comgate"
```

---

## Task 18: Wire `WithHebloOutboundDefaults` — adapter group B (HomeAssistant, MetaAds, Smartsupp, WebSearch)

**Files (modify each):**
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantAdapterServiceCollectionExtensions.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppAdapterServiceCollectionExtensions.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.WebSearch/WebSearchAdapterServiceCollectionExtensions.cs`

- [ ] **Step 1: HomeAssistant**

Append `.WithHebloOutboundDefaults()` to:

```csharp
        services.AddHttpClient<HomeAssistantConditionsReadingProvider>((sp, client) =>
        {
            // ... existing body ...
        }).WithHebloOutboundDefaults();
```

Add `using Anela.Heblo.Xcc.Http;`.

- [ ] **Step 2: MetaAds**

Replace:

```csharp
        services.AddHttpClient<MetaAdsTransactionSource>();
```

with:

```csharp
        services.AddHttpClient<MetaAdsTransactionSource>().WithHebloOutboundDefaults();
```

Add the using.

- [ ] **Step 3: Smartsupp**

Append `.WithHebloOutboundDefaults()` to the `AddHttpClient("Smartsupp", ...)` call. Add the using.

- [ ] **Step 4: WebSearch**

Append `.WithHebloOutboundDefaults()` to the `AddHttpClient("SerpApi", ...)` call. Add the using.

- [ ] **Step 5: Build the solution**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: compiles cleanly.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant \
        backend/src/Adapters/Anela.Heblo.Adapters.MetaAds \
        backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp \
        backend/src/Adapters/Anela.Heblo.Adapters.WebSearch
git commit -m "feat: wire outbound observability defaults in HomeAssistant/MetaAds/Smartsupp/WebSearch"
```

---

## Task 19: Wire `WithHebloOutboundDefaults` — Application modules (Marketing, OrgChart, MeetingTasks, KnowledgeBase)

**Files (modify each):**
- `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs`
- `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`

These three modules call `services.AddHttpClient("MicrosoftGraph")` (idempotent — the LAST `WithHebloOutboundDefaults()` call wins). To avoid drift, all four should append `.WithHebloOutboundDefaults()`.

- [ ] **Step 1: Marketing**

Replace:

```csharp
                services.AddHttpClient("MicrosoftGraph");
```

with:

```csharp
                services.AddHttpClient("MicrosoftGraph").WithHebloOutboundDefaults();
```

Add `using Anela.Heblo.Xcc.Http;`.

- [ ] **Step 2: OrgChart**

Replace:

```csharp
            services.AddHttpClient<IOrgChartService, OrgChartService>();
```

with:

```csharp
            services.AddHttpClient<IOrgChartService, OrgChartService>().WithHebloOutboundDefaults();
```

Add the using. Additionally, if this module also re-registers `"MicrosoftGraph"` via `AddHttpClient("MicrosoftGraph")`, append `.WithHebloOutboundDefaults()` there too.

- [ ] **Step 3: MeetingTasks**

Replace:

```csharp
            services.AddHttpClient("MicrosoftGraph");
```

with:

```csharp
            services.AddHttpClient("MicrosoftGraph").WithHebloOutboundDefaults();
```

Add the using.

- [ ] **Step 4: KnowledgeBase**

Replace:

```csharp
            services.AddHttpClient("MicrosoftGraph");
```

with:

```csharp
            services.AddHttpClient("MicrosoftGraph").WithHebloOutboundDefaults();
```

Add the using.

- [ ] **Step 5: Build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: compiles cleanly.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing \
        backend/src/Anela.Heblo.Application/Features/OrgChart \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase
git commit -m "feat: wire outbound observability defaults in Marketing/OrgChart/MeetingTasks/KnowledgeBase modules"
```

---

## Task 20: Wire `WithHebloOutboundObservability` — sites with custom primary handlers (Cups, FileStorage, Photobank)

**Files (modify each):**
- `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsAdapterServiceCollectionExtensions.cs`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs`

These sites already set a primary handler (`SocketsHttpHandler` in FileStorage, `HttpClientHandler` in Photobank) or a delegating handler that depends on the default primary (Cups). Per arch-review risk mitigation: attach the observability handler only — do NOT touch the primary handler.

- [ ] **Step 1: Cups**

Replace:

```csharp
        services.AddHttpClient("Cups")
            .AddHttpMessageHandler<CupsAuthHandler>();
```

with:

```csharp
        services.AddHttpClient("Cups")
            .AddHttpMessageHandler<CupsAuthHandler>()
            .WithHebloOutboundObservability();
```

Add `using Anela.Heblo.Xcc.Http;`.

- [ ] **Step 2: FileStorage**

In `FileStorageModule.cs`, append `.WithHebloOutboundObservability()` to the existing `ProductExportDownloadClientName` builder chain (after `.ConfigureHttpClient(c => { ... })`):

```csharp
        services.AddHttpClient(ProductExportDownloadClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.All,
            })
            .ConfigureHttpClient(c =>
            {
                // Intentional: per-call timeout is enforced by linked CancellationTokenSource ...
                c.Timeout = Timeout.InfiniteTimeSpan;
            })
            .WithHebloOutboundObservability();
```

Add `using Anela.Heblo.Xcc.Http;`.

- [ ] **Step 3: Photobank**

In `PhotobankModule.cs`, append `.WithHebloOutboundObservability()` to the `MicrosoftGraph` registration:

```csharp
            services.AddHttpClient("MicrosoftGraph", _ => { })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = true,
            })
            .WithHebloOutboundObservability();
```

Add `using Anela.Heblo.Xcc.Http;`.

- [ ] **Step 4: Build the solution**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: clean build.

- [ ] **Step 5: Run FileStorage tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileStorage" --no-build
```

Expected: existing tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Cups \
        backend/src/Anela.Heblo.Application/Features/FileStorage \
        backend/src/Anela.Heblo.Application/Features/Photobank
git commit -m "feat: attach observability handler to Cups, FileStorage, Photobank HttpClients"
```

---

## Task 21: `HebloResiliencePipelineExtensions` — opt-in per-dependency Polly pipelines

**Files:**
- Create: `backend/src/Anela.Heblo.Xcc/Http/HebloResiliencePipelineExtensions.cs`
- Test: `backend/test/Anela.Heblo.Tests/Xcc/Http/HebloResiliencePipelineExtensionsTests.cs`

Provides `AddHebloOutboundResiliencePipelines(this IServiceCollection)` which iterates `OutboundResilienceOptions.Dependencies` and registers a `ResiliencePipeline` per entry where `RetryEnabled = true`. Call sites resolve via `IResiliencePipelineProvider` keyed by the dependency name (`Shoptet`, `Flexi`, etc.). By default no pipelines are registered — opt-in only.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Xcc/Http/HebloResiliencePipelineExtensionsTests.cs`:

```csharp
using Anela.Heblo.Xcc.Http;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Registry;
using Xunit;

namespace Anela.Heblo.Tests.Xcc.Http;

public class HebloResiliencePipelineExtensionsTests
{
    private static IServiceProvider BuildProvider(Dictionary<string, string?> configData)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<OutboundResilienceOptions>(config.GetSection(OutboundResilienceOptions.SectionName));
        services.AddHebloOutboundResiliencePipelines();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void NoDependenciesConfigured_RegistersNoPipelines()
    {
        // Arrange + Act
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["OutboundResilience:LoggingEnabled"] = "true",
        });
        var pipelineProvider = provider.GetRequiredService<ResiliencePipelineProvider<string>>();

        // Assert — asking for an unknown key throws KeyNotFoundException
        Action act = () => pipelineProvider.GetPipeline("Shoptet");
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void RetryDisabledForDependency_DoesNotRegisterPipeline()
    {
        // Arrange + Act
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["OutboundResilience:Dependencies:Shoptet:RetryEnabled"] = "false",
            ["OutboundResilience:Dependencies:Shoptet:MaxRetryAttempts"] = "3",
        });
        var pipelineProvider = provider.GetRequiredService<ResiliencePipelineProvider<string>>();

        // Assert
        Action act = () => pipelineProvider.GetPipeline("Shoptet");
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void RetryEnabledForDependency_RegistersPipelineKeyedByName()
    {
        // Arrange + Act
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["OutboundResilience:Dependencies:Shoptet:RetryEnabled"] = "true",
            ["OutboundResilience:Dependencies:Shoptet:MaxRetryAttempts"] = "2",
            ["OutboundResilience:Dependencies:Shoptet:RetryBaseDelay"] = "00:00:00.100",
            ["OutboundResilience:Dependencies:Shoptet:Timeout"] = "00:00:05",
        });
        var pipelineProvider = provider.GetRequiredService<ResiliencePipelineProvider<string>>();

        // Assert
        var pipeline = pipelineProvider.GetPipeline("Shoptet");
        pipeline.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run the test — confirm compile failure**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HebloResiliencePipelineExtensionsTests"
```

Expected: compile fail (extension method does not exist).

- [ ] **Step 3: Implement the extension**

Create `backend/src/Anela.Heblo.Xcc/Http/HebloResiliencePipelineExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Anela.Heblo.Xcc.Http;

/// <summary>
/// Registers per-dependency Polly v8 ResiliencePipeline instances based on
/// OutboundResilienceOptions.Dependencies. Each dependency entry where
/// RetryEnabled = true gets a pipeline keyed by the dependency name; call sites
/// resolve them via IResiliencePipelineProvider&lt;string&gt;.
///
/// Retries fire on HttpRequestException, TimeoutRejectedException, and on
/// OperationCanceledException only when the caller token has not requested cancellation.
/// This is the same predicate logic used in DownloadResilienceService.
/// </summary>
public static class HebloResiliencePipelineExtensions
{
    public static IServiceCollection AddHebloOutboundResiliencePipelines(this IServiceCollection services)
    {
        services.AddResiliencePipeline<string, object>("__placeholder__", (_, _) => { });

        services.AddSingleton<IConfigureOptions<ResiliencePipelineRegistry<string>>>(sp =>
            new ConfigureNamedOptions<ResiliencePipelineRegistry<string>>(
                Options.DefaultName,
                registry =>
                {
                    var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<OutboundResilienceOptions>>();
                    var logger = sp.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Anela.Heblo.Xcc.Http.OutboundResilience");
                    var options = optionsMonitor.CurrentValue;

                    foreach (var (name, dependencyOptions) in options.Dependencies)
                    {
                        if (!dependencyOptions.RetryEnabled)
                        {
                            continue;
                        }

                        var localName = name;
                        var localOptions = dependencyOptions;
                        registry.TryAddBuilder(name, (builder, _) =>
                            BuildPipeline(builder, localName, localOptions, logger));
                    }
                }));

        return services;
    }

    private static void BuildPipeline(
        ResiliencePipelineBuilder builder,
        string dependencyName,
        DependencyResilienceOptions options,
        ILogger logger)
    {
        builder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.RetryBaseDelay,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is HttpRequestException)
                    {
                        return PredicateResult.True();
                    }
                    if (args.Outcome.Exception is TimeoutRejectedException)
                    {
                        return PredicateResult.True();
                    }
                    // Retry on OperationCanceledException only when the caller has NOT cancelled.
                    // The caller's CancellationToken is available via args.Context.CancellationToken.
                    if (args.Outcome.Exception is OperationCanceledException
                        && !args.Context.CancellationToken.IsCancellationRequested)
                    {
                        return PredicateResult.True();
                    }
                    return PredicateResult.False();
                },
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "Retry {AttemptNumber} for {Dependency} after {Delay} due to {ExceptionType}",
                        args.AttemptNumber + 1,
                        dependencyName,
                        args.RetryDelay,
                        args.Outcome.Exception?.GetType().Name);
                    return ValueTask.CompletedTask;
                },
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = options.Timeout,
            });
    }
}
```

> **Why the `__placeholder__` registration:** calling `AddResiliencePipeline` once is required to register the underlying `ResiliencePipelineRegistry<string>` with DI. After that, our `IConfigureOptions<ResiliencePipelineRegistry<string>>` runs against the same registry. The placeholder pipeline is never looked up.

- [ ] **Step 4: Register the helper in `XccModule`**

Edit `backend/src/Anela.Heblo.Xcc/XccModule.cs` and add at the end of `AddXccServices`:

```csharp
        services.AddHebloOutboundResiliencePipelines();
```

- [ ] **Step 5: Run the resilience tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HebloResiliencePipelineExtensionsTests"
```

Expected: 3 passing tests.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/Http/HebloResiliencePipelineExtensions.cs \
        backend/src/Anela.Heblo.Xcc/XccModule.cs \
        backend/test/Anela.Heblo.Tests/Xcc/Http/HebloResiliencePipelineExtensionsTests.cs
git commit -m "feat(xcc): add opt-in per-dependency Polly resilience pipelines"
```

---

## Task 22: Update investigation doc with finalized inventory and post-deploy plan

**Files:**
- Modify: `docs/investigations/socket-exception-2026-05.md`

- [ ] **Step 1: Add a "Changes shipped in this PR" section after section 4 (Decision log)**

Append to the file:

```markdown
## 4a. Changes shipped in this PR

- New: `Anela.Heblo.Xcc.Http.OutboundCallObservabilityHandler` (DelegatingHandler).
- New: `WithHebloOutboundDefaults()` / `WithHebloOutboundObservability()` extensions on `IHttpClientBuilder`.
- New: `OutboundResilienceOptions` (binds `OutboundResilience` section).
- New: opt-in per-dependency Polly v8 pipelines via `IResiliencePipelineProvider<string>` (`HebloResiliencePipelineExtensions`).
- Modified: every `AddHttpClient(...)` registration listed in section 2 — observability handler attached uniformly.
- Modified: `appsettings.json` gained `OutboundResilience` defaults.
- **PooledConnectionLifetime** is now centrally set to 4 minutes (configurable). Previously, only `FileStorageModule` set it (to 5 minutes — left as-is, intentional).

## 5b. Post-deploy validation checklist

- [ ] Confirm Application Insights queries 3.1–3.4 in section 3 return rows after deploy.
- [ ] Confirm at least one `Outbound call failed` log appears with the new structured properties (`TargetHost`, `TargetPath`, `HttpMethod`, `ElapsedMs`, `Reason`, `OperationId`, `CancellationRequested`).
- [ ] If retries are enabled for any dependency post-FR-2, verify the `OutboundResilience:Dependencies:<Name>:RetryEnabled` flag flips behavior without a redeploy.
- [ ] **24h post-deploy** (FR-6 check): `SocketException` rate per query 3.3.
- [ ] **48h post-deploy** (FR-6 check): same query — rate should be at or below baseline (≤ ~5/day). If not, open a new hypothesis in this doc.
```

- [ ] **Step 2: Commit**

```bash
git add docs/investigations/socket-exception-2026-05.md
git commit -m "docs: record shipped changes and post-deploy validation checklist"
```

---

## Task 23: Final validation — build, format, full test suite

**Files:** none modified in this task; verification only.

- [ ] **Step 1: Run `dotnet format`**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

If this reports formatting drift, drop `--verify-no-changes`, re-run, then commit the formatting fixes.

- [ ] **Step 2: Full build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: zero errors.

- [ ] **Step 3: Run all touched test projects**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build
dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --no-build --filter "Category!=Integration"
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --no-build --filter "Category!=Integration"
dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj --no-build
dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --no-build
```

Expected: all pass. Any failure must be diagnosed before merging — new tests added in this plan should be green, and no existing test should regress (the handler is additive).

- [ ] **Step 4: If formatting changes were committed in Step 1, push everything**

```bash
git status
# If clean, you're done; otherwise commit the format fixes:
# git add -A
# git commit -m "style: dotnet format"
```

---

## Self-Review (run before declaring the plan complete)

**Spec coverage:**

| Requirement | Task(s) |
|---|---|
| FR-1 inventory | Task 1 (skeleton) + Task 22 (final) |
| FR-2 App Insights correlation | Task 1 (KQL queries 3.1–3.4) |
| FR-3 structured logging | Tasks 6–12 (handler) |
| FR-4 cancellation vs timeout vs network | Tasks 8 (ClientAborted), 9 (Timeout), 10 (Network), 12 (background fallback) |
| FR-5 resiliency for transient failures | Task 21 (opt-in pipelines) + Tasks 17–20 (pool lifetime defaults) |
| FR-6 post-deploy validation | Task 22 (checklist) |
| NFR-1 perf cap | Handler design uses `Stopwatch.GetTimestamp`/`GetElapsedTime` only — no allocations on the happy path; logging only on failure |
| NFR-2 no secret leakage | Task 11 (query string + token redaction test) |
| NFR-3 queryable property names | Task 4 (PascalCase constants) + Task 8 (TrackException property dictionary) |
| NFR-4 backwards compat | No public API contract changes; handler is additive |
| NFR-5 reversibility | `LoggingEnabled = false` short-circuit (Task 7), per-dependency `RetryEnabled` flag (Task 21) — both honor config without redeploy |

**Placeholder scan:** none — every code step shows actual code; every command step shows the exact command.

**Type consistency:**
- `OutboundCallReason` (Task 3) — values: `Unknown`, `ClientAborted`, `Timeout`, `Network`. Referenced by name in Tasks 8–12.
- `OutboundCallLogProperties` (Task 4) — constants: `TargetHost`, `TargetPath`, `HttpMethod`, `ElapsedMs`, `CancellationRequested`, `Reason`, `OperationId`. Used in Tasks 8, 11.
- `OutboundResilienceOptions.SectionName = "OutboundResilience"` (Task 5). Used in Tasks 5 (tests), 15 (`XccModule`), 16 (`appsettings.json`).
- `DependencyResilienceOptions.RetryEnabled` (Task 5). Used in Task 21 to skip non-opted-in dependencies.
- `WithHebloOutboundDefaults` (Task 14) and `WithHebloOutboundObservability` (Task 13) — names consistent across Tasks 17–20 wiring.
- `AddHebloOutboundResiliencePipelines` (Task 21) — registered once in `XccModule` (Task 21 Step 4).

**Risks acknowledged from arch-review (cross-check):**
- Photobank/Cups/FileStorage primary handlers preserved (Task 20).
- Double-logging avoided: no adapter currently catch-and-logs outbound HTTP at the registration sites we touched — verified via Grep against `*ServiceCollectionExtensions.cs`. If a future task adds catch-and-log, it must use `LogLevel.Debug` per arch decision.
- Background-context fallback explicitly tested (Task 12).
- `TimeoutRejectedException` not handled in observability handler but is handled in the Polly pipeline predicate (Task 21).
