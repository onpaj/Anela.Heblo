# Architecture Review: Investigate and Mitigate Elevated SocketException Errors

## Skip Design: true

Backend-only observability + resiliency change. No UI, no new endpoints, no DTOs.

## Architectural Fit Assessment

The feature aligns cleanly with what already exists. The codebase already standardizes on:

- **Polly v8 `ResiliencePipeline`** (`Polly` 8.4.1 in `Anela.Heblo.Application.csproj`), with two reference implementations:
  - `Application/Features/Catalog/Infrastructure/CatalogResilienceService.cs` — retry + circuit breaker + timeout, classifies `OperationCanceledException` by inspecting the caller token.
  - `Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs` — retry + timeout, **builds pipeline per-call** specifically so the retry predicate can close over the caller `CancellationToken` (the correct pattern for FR-4).
- **`IHttpClientFactory`** with named/typed clients registered in `*ServiceCollectionExtensions.cs` per adapter. One client (`ProductExportDownloadClientName` in `FileStorageModule.cs:35-48`) already uses `SocketsHttpHandler { PooledConnectionLifetime = 5 min }` — the rest do **not**, leaving them on the default 2-minute `HandlerLifetime` (not `PooledConnectionLifetime`), which is exactly the kind of misconfiguration FR-5 calls out.
- **App Insights stack** with `CostOptimizedTelemetryProcessor` + `CustomSamplingTelemetryProcessor` (exceptions always tracked at 100%), and **`ITelemetryService.TrackException(ex, properties)`** in `Xcc/Telemetry/TelemetryService.cs` as the established channel for enriched exception telemetry. Property names like `Job`, `AttemptNumber`, `IsTerminal` are already in use (`DownloadResilienceService.cs:101-107`).
- **`ChatRetry.RetryOnceAsync`** in `Application/Shared/Http/ChatRetry.cs` as a lightweight one-shot retry for ad-hoc sites that don't need a full Polly pipeline.

Integration points: 15+ HTTP client registrations across 11 adapters + 5 Application-layer modules (Microsoft Graph, Photobank, Flexi, Shoptet, ShoptetApi, Anthropic, Smartsupp, MetaAds, HomeAssistant, WebSearch, Comgate, Cups, FileStorage, OrgChart, MeetingTasks, Marketing, KnowledgeBase). Inventory across these is the FR-1 deliverable.

**The architectural choice is therefore not "what pattern to introduce" but "how to extend the existing patterns uniformly without duplicating logic per adapter."**

## Proposed Architecture

### Component Overview

```
Inbound HTTP request ─► MediatR handler ─► Adapter HttpClient ──► External dependency
                            │                       │
                            │                       └─ OutboundCallObservabilityHandler  (DelegatingHandler, new)
                            │                            │  • measures elapsed ms
                            │                            │  • classifies failure (client_aborted | timeout | network | unknown)
                            │                            │  • emits structured ILogger + ITelemetryService.TrackException
                            │
                            └─ (optionally) IResiliencePipelineProvider for dependencies confirmed transient

                       ┌──────────────────────────────────────────────────┐
                       │ Anela.Heblo.Xcc.Http  (new sub-namespace)        │
                       │   • OutboundCallObservabilityHandler             │
                       │   • OutboundCallLogContext (record)              │
                       │   • OutboundCallExtensions                       │
                       │     - AddHebloOutboundObservability()            │
                       │     - WithHebloOutboundDefaults()                │
                       └──────────────────────────────────────────────────┘
                                  │
                                  ▼
                       Re-used by every AddHttpClient<...>() in adapters
```

### Key Design Decisions

#### Decision 1: Centralize classification + logging in a `DelegatingHandler`, not in each catch site

**Options considered:**
1. Add try/catch + structured logging to every outbound call site (FR-3/FR-4 done per-adapter).
2. Build one `DelegatingHandler` that wraps every `HttpClient` and classifies failures uniformly.
3. Rely solely on App Insights auto-instrumentation (dependency telemetry).

**Chosen approach:** Option 2 — a single `OutboundCallObservabilityHandler` in `Anela.Heblo.Xcc/Http/`, attached via `AddHttpMessageHandler<OutboundCallObservabilityHandler>()` on every `AddHttpClient(...)` registration (or wired centrally through an `IHttpClientBuilder` extension `WithHebloOutboundDefaults()`).

**Rationale:**
- DRY: classification logic (`client_aborted` / `timeout` / `network`) lives in one place — fits global rule about extracting repeated logic when used in 3+ places. Spec already anticipates this in API/Interface Design.
- Surgical: existing adapter code is **not modified** beyond the registration line.
- The handler has access to `HttpRequestMessage.RequestUri` (host/path stripped of query) and the incoming `CancellationToken` (which `HttpClient` passes through from the caller), satisfying FR-4's need to distinguish the inbound abort from an `HttpClient.Timeout` timeout.
- App Insights dependency telemetry alone (Option 3) does not carry our custom `reason`/`attemptNumber` properties, so it fails NFR-3.

#### Decision 2: Reuse Polly v8 `ResiliencePipeline`, do not introduce a new library or `Microsoft.Extensions.Http.Resilience`

**Options considered:**
1. Add `Microsoft.Extensions.Http.Resilience` (the newer wrapper).
2. Reuse the existing Polly v8 pipeline pattern from `DownloadResilienceService` / `CatalogResilienceService`.
3. Use `AddPolicyHandler` from the older Polly.Extensions.Http surface.

**Chosen approach:** Option 2.

**Rationale:** Polly 8.4.1 and `Polly.Extensions` 8.4.1 are already referenced; both reference implementations use the v8 `ResiliencePipelineBuilder` API. Introducing a new package violates the spec's explicit instruction and adds a parallel pattern. Option 3 is the v7-era API and would regress. For dependencies where retry is confirmed needed (FR-5), publish a per-dependency `ResiliencePipeline` keyed by dependency name via `IResiliencePipelineProvider` (Polly's `AddResiliencePipeline(...)` extension), keeping the wiring close to the existing examples.

#### Decision 3: Configure `SocketsHttpHandler.PooledConnectionLifetime` globally via the `IHttpClientBuilder` extension, not per-adapter

**Options considered:**
1. Touch every `AddHttpClient(...)` site individually to set `PooledConnectionLifetime`.
2. Provide a `WithHebloOutboundDefaults(this IHttpClientBuilder)` extension that all registrations use; centrally sets `PooledConnectionLifetime` (default 4 min) and attaches `OutboundCallObservabilityHandler`.
3. Replace `HttpClient` with a custom factory.

**Chosen approach:** Option 2.

**Rationale:** Spec FR-5 names `PooledConnectionLifetime` as the suspected pool misconfiguration. Default `IHttpClientFactory` rotates the handler every 2 minutes via `HandlerLifetime`, but this is **not** the same as `PooledConnectionLifetime` — the connection pool inside `SocketsHttpHandler` can outlive a handler rotation when reused. Azure Front Door / App Service idle timeouts are typically 4 minutes; setting `PooledConnectionLifetime` to ~4 minutes prevents using a socket the load balancer has already closed (a textbook cause of `SocketException: "The operation was canceled."`). Centralizing the setting prevents per-adapter drift. Per-call overrides (e.g., long downloads in `FileStorageModule`) remain free to opt out.

#### Decision 4: Configuration-driven retry policies (NFR-5 — reversibility)

**Options considered:**
1. Hard-code retry parameters in the resilience service constructor.
2. Bind a single shared `OutboundResilienceOptions` section.
3. Bind per-dependency options sections (e.g., `Resilience:Shoptet`, `Resilience:Flexi`).

**Chosen approach:** Option 3 for sites that get a retry policy in this PR, Option 2 default as fallback.

**Rationale:** Some dependencies (Shoptet, Flexi) need different timeouts and retry counts than others. Per-dependency sections under `Resilience:*` follow the existing Options pattern (`ProductExportOptions`, `HangfireOptions`). Lets operators tune without redeploy (NFR-5). Disable-via-config is required so a misbehaving retry policy can be turned off.

## Implementation Guidance

### Directory / Module Structure

Add to **`backend/src/Anela.Heblo.Xcc/Http/`** (new folder — `Xcc` is the cross-cutting concerns project, the natural home):

```
backend/src/Anela.Heblo.Xcc/Http/
├── OutboundCallObservabilityHandler.cs   # DelegatingHandler
├── OutboundCallReason.cs                 # enum: ClientAborted | Timeout | Network | Unknown
├── OutboundCallLogProperties.cs          # const strings for property names
├── OutboundResilienceOptions.cs          # options record (per-dependency)
└── HebloHttpClientBuilderExtensions.cs   # IHttpClientBuilder.WithHebloOutboundDefaults(...)
```

Per-dependency wiring stays where it is:

- Update each `AddHttpClient(...)` in adapter `*ServiceCollectionExtensions.cs` to call `.WithHebloOutboundDefaults()`.
- For dependencies where FR-2 confirms transient socket failures, add a `IResiliencePipelineProvider`-registered pipeline next to the existing pattern (do **not** invent a new structure).

Tests in **`backend/test/Anela.Heblo.Tests/Xcc/Http/`** mirroring `src/`. xUnit + FluentAssertions + Moq per `csharp-testing.md`.

**Investigation artifact:** `docs/investigations/socket-exception-2026-05.md` (spec FR-1, FR-2, FR-6 reference this). The `docs/investigations/` folder already exists.

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Xcc.Http;

public enum OutboundCallReason { Unknown = 0, ClientAborted, Timeout, Network }

public sealed class OutboundCallObservabilityHandler : DelegatingHandler
{
    private readonly ILogger<OutboundCallObservabilityHandler> _logger;
    private readonly ITelemetryService _telemetry;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // ctor injection; classify on exception, log with structured properties:
    //   TargetHost, TargetPath, HttpMethod, ElapsedMs, TimeoutMs,
    //   CancellationRequested, AttemptNumber (from HttpRequestMessage.Options if Polly populates it),
    //   Reason, OperationId (from Activity.Current?.RootId)
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken);
}

public static class HebloHttpClientBuilderExtensions
{
    // Attach OutboundCallObservabilityHandler + configure SocketsHttpHandler
    // with PooledConnectionLifetime taken from OutboundResilienceOptions
    // (default 4 minutes). Caller may further customize after this call.
    public static IHttpClientBuilder WithHebloOutboundDefaults(this IHttpClientBuilder builder);
}

public sealed class OutboundResilienceOptions
{
    public const string SectionName = "OutboundResilience";
    public TimeSpan PooledConnectionLifetime { get; init; } = TimeSpan.FromMinutes(4);
    public bool LoggingEnabled { get; init; } = true;
    public Dictionary<string, DependencyResilienceOptions> Dependencies { get; init; } = new();
}

public sealed class DependencyResilienceOptions
{
    public bool RetryEnabled { get; init; }
    public int MaxRetryAttempts { get; init; } = 3;
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
```

**Logging property names (NFR-3):** PascalCase (matches existing usage in `DownloadResilienceService` and `TelemetryService`): `TargetHost`, `TargetPath`, `HttpMethod`, `ElapsedMs`, `TimeoutMs`, `CancellationRequested`, `AttemptNumber`, `Reason`, `OperationId`. Property values are scalars only — no nested objects (NFR-3).

**Log levels (FR-3, FR-4):**
- `Reason = ClientAborted` → `LogLevel.Warning`
- `Reason = Timeout` → `LogLevel.Error`
- `Reason = Network` → `LogLevel.Error`
- `Reason = Unknown` → `LogLevel.Error`

### Data Flow

For a failing outbound call:

```
Caller (MediatR handler, has CancellationToken from HttpContext.RequestAborted)
  │
  ├─ Polly pipeline (if configured for this dependency)
  │     • per-attempt timeout via AddTimeout
  │     • OnRetry → ITelemetryService.TrackException with AttemptNumber
  │
  └─ HttpClient.SendAsync(request, callerToken)
        │
        ├─ OutboundCallObservabilityHandler.SendAsync
        │     • Stopwatch start
        │     • try base.SendAsync; on exception →
        │         classify:
        │            inboundToken?.IsCancellationRequested == true  → ClientAborted
        │            ex is TaskCanceledException
        │              && innerCancellation is from per-call CTS / handler timeout → Timeout
        │            ex is SocketException / IOException / HttpRequestException w/o cancellation → Network
        │         build property dictionary
        │         _logger.Log(level, ex, "Outbound call failed: {HttpMethod} {TargetHost}{TargetPath}", ...)
        │         _telemetry.TrackException(ex, properties)
        │         rethrow
        │
        └─ SocketsHttpHandler (PooledConnectionLifetime = 4 min)
```

**Classification rule (concrete, FR-4):**

| Condition | Reason | Level |
|---|---|---|
| `IHttpContextAccessor.HttpContext?.RequestAborted.IsCancellationRequested == true` AND exception is `OperationCanceledException` | `ClientAborted` | Warning |
| `OperationCanceledException` AND inbound token not cancelled (i.e., cancelled by `HttpClient.Timeout` or per-call CTS) | `Timeout` | Error |
| `SocketException`, `IOException`, `HttpRequestException` with no cancellation in chain | `Network` | Error |
| Otherwise | `Unknown` | Error |

**URL redaction (NFR-2):** Log `request.RequestUri.Host` and `request.RequestUri.AbsolutePath` only. Never log query string or headers. Already aligned with `csharp-security.md`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Replacing default `IHttpClientFactory` handler with `SocketsHttpHandler` per registration could break existing per-adapter handler chains (e.g., Photobank's `HttpClientHandler` with custom redirects, Cups' `CupsAuthHandler`). | HIGH | `WithHebloOutboundDefaults()` must use `ConfigurePrimaryHttpMessageHandler` only when not previously configured, or be additive via `AddHttpMessageHandler<...>`. The handler-rotation primary handler is set per adapter; the observability handler is a `DelegatingHandler` on top. For Photobank and Cups, add only the observability handler — do not touch the primary handler. |
| Double-logging if individual adapters already log on failure. | MEDIUM | FR-1 inventory must enumerate existing catch-and-log sites; replace them with the centralized handler, or downgrade them to `Debug`. Do not add the handler and leave per-adapter logs writing the same event at the same level. |
| `IHttpContextAccessor` returns `null` in background contexts (Hangfire jobs, hosted services). FR-4 client-abort detection won't work there. | MEDIUM | When `HttpContext` is null, treat the *caller* `CancellationToken` (the one passed to `SendAsync`) as the inbound token. If that token is the one that fired, classify as `ClientAborted`; otherwise `Timeout`/`Network`. Document in the handler's class-level summary. |
| Adding the handler increases per-call overhead. NFR-1 cap is < 1 ms p99 per logged failure. | LOW | The handler is a no-op on the happy path apart from `Stopwatch.GetTimestamp()`. Logging only on failure. Verify with a microbenchmark in tests. |
| 24-hour spike could be a single-cause incident already resolved by deploy time → FR-6 may report "no spike" but baseline drift is masked. | MEDIUM | FR-2 must compare the spike against a 7-day baseline before any code change. If the spike has already subsided pre-deploy, document and downscope FR-5 to just pool-lifetime + logging. |
| Polly v8 `AddTimeout` raises `TimeoutRejectedException`, not `OperationCanceledException` — classification needs to handle both. | LOW | Add `TimeoutRejectedException` → `Reason.Timeout`. Already the pattern in `DownloadResilienceService.cs:67-71`. |
| Behavior change under feature flag is required (NFR-5) but the handler is wired at DI registration time. | MEDIUM | `OutboundResilienceOptions.LoggingEnabled = false` short-circuits the handler to pass-through. Retry policies are opt-in per dependency via configuration, defaulting to disabled — adding the handler does not enable retries. |
| Inventory in `docs/integrations/` does **not** currently contain an App Insights query-tooling guide, despite FR-2 referencing it. | LOW | Treat as a spec amendment (see below). Use Application Insights Logs (KQL) directly via Azure portal; document the queries used in the investigation doc. |

## Specification Amendments

1. **FR-2 wording** — "Use the App Insights query tooling already available in this project (see `docs/integrations/` and existing App Insights helpers)" — there is no such queryable helper in `docs/integrations/`. Amend FR-2 to: *"Run KQL queries against the production Application Insights resource directly (Azure portal or `az monitor app-insights query`). Save the queries used and their results into `docs/investigations/socket-exception-2026-05.md`."* No code dependency on a query helper.

2. **FR-3 / NFR-3 property naming** — Spec is silent on case convention. Lock to **PascalCase** (matches `DownloadResilienceService` and `TelemetryService` usage in this repo: `Job`, `AttemptNumber`, `IsTerminal`). Add concrete property list to the spec: `TargetHost`, `TargetPath`, `HttpMethod`, `ElapsedMs`, `TimeoutMs`, `CancellationRequested`, `AttemptNumber`, `Reason`, `OperationId`.

3. **FR-5 implementation surface** — Spec says retry "applied to identified transient call sites." Clarify *how*: a per-dependency configuration section `OutboundResilience:Dependencies:<Name>` driving an `IResiliencePipelineProvider`-registered pipeline reused at the call site (do **not** modify the `HttpClient` registration with `AddPolicyHandler` — the project does not use that API). Retry must be opt-in (default disabled) so the PR can ship the observability + pool-lifetime change without enabling retries until FR-2 proves they're needed.

4. **FR-1 acceptance criteria** — Add: *"Inventory is captured in `docs/investigations/socket-exception-2026-05.md`, not the PR description."* The investigation doc is a durable artifact; PR descriptions are not.

5. **Out of Scope clarification** — Add: *"No changes to App Insights sampling rules or `CostOptimizedTelemetryProcessor`."* Exceptions are already always-tracked; touching the processors is unrelated.

6. **NFR-1** — Tighten to: *"On the happy path, the observability handler must add < 50 μs per call (only a `Stopwatch.GetTimestamp` pair). On the failure path, < 1 ms p99 from logging."* The current spec wording about "< 1 ms p99 per logged failure" is fine; this just makes the happy-path bound explicit.

## Prerequisites

- **No infrastructure changes required.** App Insights, Polly v8, `IHttpClientFactory`, and `IHttpContextAccessor` are already wired.
- **No new NuGet packages.** Reuse `Polly` 8.4.1 (already in `Anela.Heblo.Application.csproj`); add a `Polly` and `Polly.Extensions` reference to `Anela.Heblo.Xcc.csproj` only if the handler or extension methods directly reference Polly types. If the handler stays Polly-free (recommended — Polly stays in Application/adapter layers, `Xcc.Http` only does observability), no csproj changes in `Xcc`.
- **Config section `OutboundResilience` must be added to `appsettings.json`** with safe defaults: `LoggingEnabled = true`, `PooledConnectionLifetime = 00:04:00`, empty `Dependencies` dictionary. Verify behavior is identical to today when section is absent (defaults bind correctly).
- **Access to production Application Insights** for FR-2 / FR-6 KQL queries. Solo developer has this access.
- **Inventory artifact created first:** `docs/investigations/socket-exception-2026-05.md` must be drafted in the same PR as the code changes, before the centralized handler is wired, so the audit trail of "which sites changed and why" is complete.