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

## 4a. Changes shipped in this PR

- New: `Anela.Heblo.Xcc.Http.OutboundCallObservabilityHandler` (DelegatingHandler).
- New: `WithHebloOutboundDefaults()` / `WithHebloOutboundObservability()` extensions on `IHttpClientBuilder`.
- New: `OutboundResilienceOptions` (binds `OutboundResilience` section).
- New: opt-in per-dependency Polly v8 pipelines via `ResiliencePipelineProvider<string>` (`HebloResiliencePipelineExtensions`).
- Modified: every `AddHttpClient(...)` registration listed in section 2 — observability handler attached uniformly.
- Modified: `appsettings.json` gained `OutboundResilience` defaults.
- **PooledConnectionLifetime** is now centrally set to 4 minutes (configurable). Previously, only `FileStorageModule` set it (to 5 minutes — left as-is, intentional).

## 5b. Post-deploy validation checklist

- [ ] Confirm Application Insights queries 3.1–3.4 in section 3 return rows after deploy.
- [ ] Confirm at least one `Outbound call failed` log appears with the new structured properties (`TargetHost`, `TargetPath`, `HttpMethod`, `ElapsedMs`, `Reason`, `OperationId`, `CancellationRequested`).
- [ ] If retries are enabled for any dependency post-FR-2, verify the `OutboundResilience:Dependencies:<Name>:RetryEnabled` flag flips behavior without a redeploy.
- [ ] **24h post-deploy** (FR-6 check): `SocketException` rate per query 3.3.
- [ ] **48h post-deploy** (FR-6 check): same query — rate should be at or below baseline (≤ ~5/day). If not, open a new hypothesis in this doc.
