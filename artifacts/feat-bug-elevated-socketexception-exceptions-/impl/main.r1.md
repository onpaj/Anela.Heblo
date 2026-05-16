All tasks complete. Here is the implementation summary:

---

# Implementation: SocketException Spike Mitigation

## What was implemented

A centralized outbound HTTP observability and resiliency layer that classifies `SocketException` failures, fixes the `PooledConnectionLifetime` misconfiguration suspected as the root cause, and provides opt-in per-dependency Polly retry pipelines.

## Files created/modified

### New production files (`backend/src/Anela.Heblo.Xcc/Http/`)
- `OutboundCallReason.cs` — enum: `Unknown`, `ClientAborted`, `Timeout`, `Network`
- `OutboundCallLogProperties.cs` — PascalCase constants for App Insights properties
- `OutboundResilienceOptions.cs` — options class with `LoggingEnabled`, `PooledConnectionLifetime` (default 4 min), per-dependency `DependencyResilienceOptions`
- `OutboundCallObservabilityHandler.cs` — `DelegatingHandler` that classifies failures and logs/tracks with structured properties; pass-through on happy path and when `LoggingEnabled=false`
- `HebloHttpClientBuilderExtensions.cs` — `WithHebloOutboundDefaults()` (sets `SocketsHttpHandler.PooledConnectionLifetime` + attaches handler) and `WithHebloOutboundObservability()` (handler only, for custom-primary-handler sites)
- `HebloResiliencePipelineExtensions.cs` — registers per-dependency Polly v8 pipelines (opt-in via config, default off)

### Modified production files
- `Anela.Heblo.Xcc.csproj` — added `Microsoft.AspNetCore.Http.Abstractions`, `Microsoft.Extensions.Http`, `Polly`, `Polly.Extensions`
- `XccModule.cs` — registers handler, options, resilience pipelines
- `appsettings.json` — added `OutboundResilience` defaults section
- **17 adapter/module files** — wired `WithHebloOutboundDefaults()` or `WithHebloOutboundObservability()` on every `AddHttpClient(...)` call
- `Anela.Heblo.Adapters.HomeAssistant.csproj` + `Anela.Heblo.Adapters.Smartsupp.csproj` — added Xcc project reference

### New test files (`backend/test/Anela.Heblo.Tests/Xcc/Http/`)
- `OutboundResilienceOptionsTests.cs` — 2 tests (defaults, config binding)
- `OutboundCallObservabilityHandlerTests.cs` — 10 tests (happy path, logging disabled, ClientAborted, Timeout, Network×3, URL redaction, background context×2)
- `HebloHttpClientBuilderExtensionsTests.cs` — 2 tests (both extension methods)
- `HebloResiliencePipelineExtensionsTests.cs` — 3 tests (no deps, retry disabled, retry enabled)

### Documentation
- `docs/investigations/socket-exception-2026-05.md` — FR-1 inventory (17 clients), KQL queries for FR-2, decision log, shipped changes, post-deploy checklist

## Tests
- **60 new Xcc tests** — all passing
- **3,321 total tests passing**, 3 skipped, 0 failures
- One test fixture fix: `FileStorageModuleTests` needed `IHttpContextAccessor` registration

## How to verify
```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Xcc"
```

## PR Summary

The core fix for the 5× `SocketException: "The operation was canceled."` spike is setting `SocketsHttpHandler.PooledConnectionLifetime = 4 minutes` on every outbound `HttpClient`. Previously only `FileStorageModule` set this — all other 16 clients used the default `IHttpClientFactory` handler rotation which does NOT prevent the OS-level socket pool from holding connections longer than Azure's 4-minute load-balancer idle timeout, causing stale-socket errors.

The `OutboundCallObservabilityHandler` DelegatingHandler provides structured classification (`ClientAborted` / `Timeout` / `Network` / `Unknown`) with PascalCase App Insights properties so future spikes can be pinpointed to a specific dependency within seconds instead of requiring manual log triage. The opt-in Polly retry framework ships disabled by default and can be enabled per-dependency via configuration (`OutboundResilience:Dependencies:Shoptet:RetryEnabled: true`) without a redeploy once FR-2 App Insights analysis confirms which dependency is transient.

## Status
DONE