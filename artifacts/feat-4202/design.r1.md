# Design: Move Meta Ads Access Token from URL Query Parameter to Authorization Header

## Component Design

No new components are introduced. This design confines all changes to the existing `MetaAdsTransactionSource` class in `Anela.Heblo.Adapters.MetaAds`, per the architecture review's Decision 1 (explicit `HttpRequestMessage` with an `Authorization` header, not a default header on the typed `HttpClient`) and Decision 2 (no `DelegatingHandler`, no changes to `MetaAdsSettings`, DI registration, or the `IMarketingTransactionSource` contract).

### `MetaAdsTransactionSource : IMarketingTransactionSource`

Responsibility: fetch Meta Graph API marketing transactions for a date range, paginating through `paging.next` cursors, and mapping results into `MarketingTransaction` records. Unchanged except for how outbound requests authenticate.

| Member | Change | Responsibility after change |
|---|---|---|
| `GetTransactionsAsync(DateTime from, DateTime to, CancellationToken ct)` | Unchanged | Public entry point (via `IMarketingTransactionSource`); orchestrates the `BuildInitialUrl` → `FetchPageAsync` → follow-`paging.next` loop; maps `page.Data` into `MarketingTransaction` records within `[from, to]`. |
| `BuildInitialUrl(DateTime from, DateTime to)` | Modified | Builds the transactions-list URL with `fields` and `time_range` query parameters only. No longer appends `access_token`. Signature and return type (`string`) unchanged. |
| `FetchPageAsync(string url, CancellationToken ct)` | Modified | Builds a `new HttpRequestMessage(HttpMethod.Get, url)`, sets `request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken)`, and sends it via `_httpClient.SendAsync(request, innerCt)` (replacing `_httpClient.GetAsync(url, innerCt)`), still wrapped by the existing `_pipeline.ExecuteAsync` resilience pipeline. Deserializes the response body, throws `InvalidOperationException("MetaAds API returned null response body.")` on a null body, and relies on `EnsureSuccessStatusCode()` for non-success responses — all unchanged. Because this is the single call site used for both the initial fetch and every paginated fetch (`GetTransactionsAsync`'s `while (url is not null)` loop re-enters this same method with `page.Paging?.Next`), attaching the header here covers pagination automatically — no separate paginated-fetch code path is introduced. |
| `RedactToken(string url)` | Removed | No longer needed: the token never appears in a URL, so there is nothing to redact before logging. |
| Debug log call | Modified | `_logger.LogDebug("MetaAds: GET {Url}", url)` — logs the URL as-is (no token present by construction). The `Authorization` header value itself must never be logged (no full-headers dump). |
| `BuildDefaultPipeline()` | Unchanged | Continues to wrap the HTTP call exactly as before; retry/resilience behavior is unaffected by the switch from `GetAsync` to `SendAsync`. |

**New using directive:** `System.Net.Http.Headers` (for `AuthenticationHeaderValue`), added to the file's existing `using` block.

**Untouched by this change:**
- `MetaAdsSettings` (`AccessToken`, `ApiVersion`, `AccountId` — all sourced from Key Vault-backed configuration exactly as before).
- `MetaAdsAdapterServiceCollectionExtensions.AddMetaAdsAdapter` (typed `HttpClient` DI registration).
- `IMarketingTransactionSource` contract.
- `MetaAdsInvoiceImportJob` (Hangfire recurring job) and `MarketingInvoiceImportService` — no call-site changes anywhere upstream.

### Test infrastructure (`MetaAdsTransactionSourceTests.cs`)

The existing `CapturingHandler` / `CapturingSequentialHandler` test doubles currently only capture `request.RequestUri`. They need to additionally expose `request.Headers.Authorization` (or the full `HttpRequestMessage`) so tests can assert on the header. This is a test-infrastructure adjustment, not a production-code change.

## Data Schemas

No persistence schema, DTO, or event payload changes. This is a transport-layer change to a single outbound third-party HTTP call; the only shapes affected are the request itself.

### Outbound HTTP request to Meta Graph API

**Before:**
```
GET https://graph.facebook.com/{ApiVersion}/{AccountId}/transactions
    ?fields=...&time_range={...}&access_token={token}
(no Authorization header)
```

**After:**
```
GET https://graph.facebook.com/{ApiVersion}/{AccountId}/transactions
    ?fields=...&time_range={...}
Authorization: Bearer {token}
```

- `access_token` is removed from the query string entirely (initial request and every `paging.next`-derived request).
- All other existing query parameters (`fields`, `time_range`) remain present and unchanged in format.
- `Authorization` header value: `Bearer <access_token>`, where `<access_token>` is `_settings.AccessToken` (unchanged source: Key Vault-backed configuration, `MetaAdsSettings.ConfigurationKey = "MetaAds"`).

### Response body / deserialized page shape

Unchanged. The JSON response schema returned by the Meta Graph API `transactions` endpoint, and the internal type(s) it is deserialized into (including `Paging.Next`), are not affected by this fix — only how the request that fetches that response authenticates.

### Test assertions to add (fixture-level, not a schema change)

- `HttpRequestMessage.Headers.Authorization` equals `new AuthenticationHeaderValue("Bearer", "test-token")` on both the initial and any paginated captured request.
- No captured `RequestUri` (initial or `paging.next`-derived) contains the substring `access_token=`.
