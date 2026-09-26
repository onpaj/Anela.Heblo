# Architecture Review: Move Meta Ads Access Token from URL Query Parameter to Authorization Header

## Skip Design: true

`MarketingInvoices` and the `Anela.Heblo.Adapters.MetaAds` adapter have no frontend surface at all: no controller, no MediatR-exposed endpoint consumed by React, no `frontend/` references to `MarketingInvoices` or `MetaAds`, and no generated OpenAPI client usage for this module. The import flow is `IRecurringJob` (Hangfire) → `MarketingInvoiceImportService` → `IMarketingTransactionSource` (`MetaAdsTransactionSource`) → persistence, entirely server-side. This is a pure transport-layer change inside one adapter class. No UI/UX work is implicated.

## Architectural Fit Assessment

This is a narrowly-scoped, backend-only hardening fix that changes *how* an existing outbound HTTP call authenticates, not *what* it does. It fits cleanly within the `Anela.Heblo.Adapters.MetaAds` project boundary and touches no other module, contract, or persistence surface.

Verified against the real file (`backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs`, current `main`/worktree state):

- `BuildInitialUrl` (lines 109–113) appends `&access_token={_settings.AccessToken}` to the query string.
- `FetchPageAsync` (lines 94–107) calls `_httpClient.GetAsync(url, innerCt)` — a bare GET with no custom headers.
- `RedactToken` (lines 115–123) is a private static helper used only at line 96, inside the `_logger.LogDebug("MetaAds: GET {Url}", RedactToken(url))` call.
- The retry pipeline (`_pipeline.ExecuteAsync`, `BuildDefaultPipeline`) wraps the `HttpClient` call, not the URL construction — unaffected by this change.
- `GetTransactionsAsync` loops `while (url is not null)`, re-entering `FetchPageAsync(url, ct)` with `url = page.Paging?.Next` — i.e. **the exact same method services both the initial and every paginated request**. There is only one call site that issues HTTP requests to Meta, not two. This simplifies the fix: attaching the `Authorization` header inside `FetchPageAsync` automatically covers pagination, with no separate code path to update.

This matches the existing, well-established convention in this codebase for bearer-token auth against third-party HTTP APIs — the same `request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)` pattern is already used in:
- `Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs:242`
- `Anela.Heblo.Adapters.Microsoft365/Photobank/PhotobankGraphService.cs:248`
- `Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` (4 call sites)
- `Anela.Heblo.Adapters.ShoptetApi/ShoptetPayAdapterServiceCollectionExtensions.cs:24` (as a static default header on the typed `HttpClient`)
- `Anela.Heblo.Application/Common/Graph/GraphApiHelpers.cs:32`
- `Anela.Heblo.Application/Features/Attendance/Overtime/Services/GraphOvertimeReportPublisher.cs:49`

So this is not introducing a new idiom — it is *bringing MetaAds into line* with the idiom every other bearer-token integration in this codebase already follows. That is itself worth stating plainly: the current `MetaAdsTransactionSource` is the outlier, not the target state.

Two ways this could be wired, both valid within existing conventions:

1. **Per-request header** (spec's proposed approach): construct `HttpRequestMessage` explicitly in `FetchPageAsync`, set `request.Headers.Authorization` there, `SendAsync` it. This is the pattern used by Microsoft365/GraphApiHelpers.
2. **Static default header on the typed HttpClient**, set once at DI registration time in `MetaAdsAdapterServiceCollectionExtensions.AddMetaAdsAdapter` (`services.AddHttpClient<MetaAdsTransactionSource>().ConfigureHttpClient(...)`), the way `ShoptetPayAdapterServiceCollectionExtensions` does it, keeping `FetchPageAsync`'s `GetAsync(url, ct)` call unchanged.

**Chosen approach: option 1 (per-request header via explicit `HttpRequestMessage`)** — see Decision 1 below.

## Proposed Architecture

### Component Overview

No new components. The change is confined to one class, one project:

```
Anela.Heblo.Adapters.MetaAds (project, unchanged)
└── MetaAdsTransactionSource : IMarketingTransactionSource   (unchanged responsibilities)
    ├── GetTransactionsAsync(from, to, ct)                    — unchanged
    ├── BuildInitialUrl(from, to)                             — MODIFIED: drop access_token param
    ├── FetchPageAsync(url, ct)                               — MODIFIED: build HttpRequestMessage,
    │                                                            set Authorization header, SendAsync
    ├── RedactToken(url)                                      — REMOVED
    └── BuildDefaultPipeline()                                — unchanged (wraps the SendAsync call
                                                                  exactly as it wrapped GetAsync)
```

Call graph and data flow are unchanged: `MetaAdsInvoiceImportJob` (Hangfire `IRecurringJob`) → `MarketingInvoiceImportService` → `IMarketingTransactionSource.GetTransactionsAsync` → `MetaAdsTransactionSource` → `graph.facebook.com`. Only the authentication mechanism of the last hop changes.

### Key Design Decisions

#### Decision 1: Per-request `HttpRequestMessage` with `Authorization` header, not a default header on the typed `HttpClient`

**Options considered:**
- **A. Explicit `HttpRequestMessage` per call**, header set inside `FetchPageAsync`, replacing `_httpClient.GetAsync(url, innerCt)` with `_httpClient.SendAsync(request, innerCt)`.
- **B. Configure a default `Authorization` header once** on the typed `HttpClient` at DI registration (`AddMetaAdsAdapter`), the way `ShoptetPayAdapterServiceCollectionExtensions` does for `ShoptetPay`, keeping `FetchPageAsync` calling `GetAsync` unchanged.

**Chosen approach:** A — explicit `HttpRequestMessage` per call.

**Rationale:**
- The spec (`FR-1` acceptance criteria) is explicit that "the outbound `HttpRequestMessage` sent for the initial page fetch has `Headers.Authorization` set" — this pins down option A as the intended shape; deviating to a client-default header would satisfy the *security* goal but not the letter of the spec's acceptance criteria, and would require justification to change.
- Because `_settings.AccessToken` comes from `IOptions<MetaAdsSettings>` (snapshot, not `IOptionsMonitor`) resolved once when the scoped `MetaAdsTransactionSource`/`HttpClient` is constructed, a default-header-at-registration-time approach (option B) would bind the token even earlier (at DI container build time via `ConfigureHttpClient`), which is a strictly worse staleness story if the token is ever rotated without an app restart. Per-request header construction reads `_settings.AccessToken` fresh on every call (still from the same `IOptions` snapshot per instance, but at least evaluated at usage time, matching current behavior for `BuildInitialUrl`).
- Matches the majority pattern in this codebase (Microsoft365, GraphApiHelpers) over the minority pattern (ShoptetPay), and keeps token handling colocated with the one method that already logs the URL and applies the resilience pipeline — easier to audit in one place.

#### Decision 2: Do not touch `MetaAdsSettings`, DI registration, or `IMarketingTransactionSource`

**Options considered:**
- Leave `MetaAdsSettings`, `MetaAdsAdapterServiceCollectionExtensions`, and the `IMarketingTransactionSource` contract untouched (spec's stance).
- Introduce a dedicated typed `DelegatingHandler` (e.g. `MetaAdsAuthHandler`, mirroring `Anela.Heblo.Adapters.Cups/CupsAuthHandler.cs`) that attaches the header transparently for every request the typed `HttpClient` makes.

**Chosen approach:** Leave settings/DI/contract untouched; attach the header inline in `FetchPageAsync` (Decision 1, option A). Do not introduce a `DelegatingHandler`.

**Rationale:** A `DelegatingHandler` is the more "enterprise" pattern (and it does exist elsewhere in this codebase, e.g. `CupsAuthHandler` for Basic auth) but it is unjustified here: `MetaAdsTransactionSource` has exactly one call site that issues HTTP requests (`FetchPageAsync`), so a handler would add an extra class, an extra DI registration (`.AddHttpMessageHandler<...>()`), and an extra indirection for zero benefit over setting the header directly in the one place that needs it. This keeps the change surgical, matching the "touch only what the task requires" project rule and the spec's explicit scoping (no `MetaAdsSettings`/DI/contract changes).

### Implementation Guidance

### Directory / Module Structure

No new files, no new directories. Single file modified:

- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs`
  - Modify `BuildInitialUrl`: remove the `&access_token={_settings.AccessToken}` fragment.
  - Modify `FetchPageAsync`: build a `new HttpRequestMessage(HttpMethod.Get, url)`, set `request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken)`, call `await _httpClient.SendAsync(request, innerCt)` instead of `GetAsync`.
  - Remove `RedactToken` entirely.
  - Change the debug log call from `_logger.LogDebug("MetaAds: GET {Url}", RedactToken(url))` to `_logger.LogDebug("MetaAds: GET {Url}", url)`.
  - Add `using System.Net.Http.Headers;` (for `AuthenticationHeaderValue`) — confirm it isn't already pulled in transitively; the current `using` block does not include it.

No changes needed to:
- `MetaAdsSettings.cs` — `AccessToken` property stays as-is, still sourced from Key Vault-backed configuration exactly as before.
- `MetaAdsAdapterServiceCollectionExtensions.cs` — `AddHttpClient<MetaAdsTransactionSource>()` registration is unaffected.
- `MetaAdsInvoiceImportJob.cs`, `IMarketingTransactionSource`, `MarketingInvoiceImportService` — no contract or call-site changes; `GetTransactionsAsync`'s public signature and behavior are unchanged.

Test file to update: `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs`. See Specification Amendments below — several existing tests assert on URL content and mock `paging.next` values that embed `access_token=`; these need review, not necessarily rewriting, since the fetch logic follows `paging.next` verbatim regardless of what it contains.

### Interfaces and Contracts

No public interface or contract changes. `IMarketingTransactionSource.GetTransactionsAsync(DateTime from, DateTime to, CancellationToken ct)` is untouched. `BuildInitialUrl` and `FetchPageAsync` are private instance methods with no external callers — their internals can change freely.

The only externally-observable contract is the shape of the outbound HTTP request to `graph.facebook.com`:
- **Before:** `GET https://graph.facebook.com/{ApiVersion}/{AccountId}/transactions?fields=...&access_token={token}&time_range={...}` — no `Authorization` header.
- **After:** `GET https://graph.facebook.com/{ApiVersion}/{AccountId}/transactions?fields=...&time_range={...}` — with `Authorization: Bearer {token}` header, no `access_token` anywhere in the URL.

### Data Flow

Unchanged end-to-end. `MetaAdsInvoiceImportJob` (Hangfire recurring job) invokes `MarketingInvoiceImportService`, which calls `IMarketingTransactionSource.GetTransactionsAsync(from, to, ct)`. Inside `MetaAdsTransactionSource`:

1. `BuildInitialUrl` produces a URL with `fields` and `time_range` only (no token).
2. `FetchPageAsync` builds an `HttpRequestMessage`, attaches `Authorization: Bearer <token>` from `_settings.AccessToken`, sends it via `_httpClient.SendAsync` wrapped by the existing Polly retry pipeline (`_pipeline.ExecuteAsync`), deserializes the JSON body, and returns it.
3. `GetTransactionsAsync` maps `page.Data` items into `MarketingTransaction` records within the `[from, to]` window, and if `page.Paging?.Next` is non-null, loops back into `FetchPageAsync` with that URL — the same header-attaching code path handles pagination automatically, since it's the same method.
4. Errors (`HttpRequestException` from `EnsureSuccessStatusCode`, `InvalidOperationException` for a null body) propagate exactly as before — the pipeline and try/catch around the loop are untouched.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Meta's `paging.next` cursor URL, generated server-side by Facebook, could echo back an `access_token` query parameter appended to the *previous* request even after this fix (some Graph API responses historically included whatever auth param was passed, though the current request will pass none) | Low | Not directly testable without live API access (no sandbox — Shoptet-style caution applies equally here). Since the initial request no longer includes `access_token` as a query param, Meta has nothing to echo back into `next`. If a manual verification against the live API ever shows a token leaking into `paging.next`, add a defensive strip *at that specific point* rather than reintroducing a general-purpose `RedactToken`. Flag in code review; not a blocker for this change. |
| `MetaAdsTransactionSourceTests.GetTransactionsAsync_Pagination_AllPagesCollected` currently mocks a `paging.next` value containing the literal string `&access_token=test-token` | Low | This is test fixture data, not production behavior — the test will still pass unchanged after the fix, because `FetchPageAsync` follows `paging.next` verbatim regardless of its content. No test rewrite strictly required, but see Specification Amendments — consider updating the fixture to avoid a stale/misleading example lingering in the test suite. |
| Existing tests use `HttpClient`-level fakes (`StaticResponseHandler`, `CapturingHandler`, etc.) that only inspect `request.RequestUri`; none currently assert on `request.Headers.Authorization` | Medium | Add at least one new test asserting the `Authorization` header is present and correctly formatted (`Bearer test-token`) on the sent `HttpRequestMessage`, and one asserting `access_token` never appears in any captured URL (initial or paginated). This closes the regression-test gap the spec's FR-1/FR-2 acceptance criteria call for. |
| `using System.Net.Http.Headers;` may be missing from the file's `using` block | Low (build break, immediately caught by `dotnet build`) | Add the `using` directive; verify with `dotnet build` per the project's validation gate before considering the task done. |

## Specification Amendments

The spec (`spec.r1.md`) is accurate and implementation-ready as written; the following are small clarifications grounded in what the real code/tests look like, not changes to scope:

1. **FR-1's acceptance criterion "Any subsequent/paginated request... also carries the same `Authorization: Bearer` header"** is automatically satisfied by construction, not by a separate code path: `GetTransactionsAsync`'s `while` loop calls the *same* `FetchPageAsync` method for both the initial URL and every `page.Paging?.Next` URL. Implementers should not add a second, parallel "paginated fetch" method — doing so would violate the "surgical changes" project rule and risks the two paths drifting (e.g. one attaching the header, one forgetting it).
2. **Test suite impact (not called out explicitly in spec):** `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs` has 7 existing tests, none of which currently assert on request headers. Per FR-1/FR-3 acceptance criteria ("Existing unit tests... pass after the change" + "new/updated tests covering the header-based auth path"), add:
   - An assertion (new test or extension of an existing capturing-handler test) that the sent `HttpRequestMessage.Headers.Authorization` equals `new AuthenticationHeaderValue("Bearer", "test-token")`.
   - An assertion that no captured URL (initial or `paging.next`-derived) contains `access_token=` anywhere.
   The existing `CapturingHandler`/`CapturingSequentialHandler` test doubles only capture `request.RequestUri`; they'll need to additionally expose `request.Headers.Authorization` (or the full `HttpRequestMessage`) to support these new assertions — this is a test-infrastructure tweak, not a production code change.
3. **No amendment needed to Data Model, API/Interface Design, Dependencies, or Out of Scope sections** — all verified consistent with the actual code (`MetaAdsSettings` untouched, no MediatR/controller surface, `System.Net.Http.Headers.AuthenticationHeaderValue` is already a used BCL pattern elsewhere in this repo).

## Prerequisites

None. No configuration, migration, or infrastructure changes are required:
- `_settings.AccessToken` is already sourced from Azure Key Vault-backed configuration (`MetaAdsSettings.ConfigurationKey = "MetaAds"`); the value and its provisioning path are unchanged by this fix.
- No new NuGet package — `AuthenticationHeaderValue` is in `System.Net.Http.Headers` (BCL, already implicitly available via the existing `System.Net.Http` usage in this file).
- No sandbox exists for the Meta Graph API (live-store-only, same caution as Shoptet per project rules) — manual verification of the header-based auth path against the live API should be done cautiously and, per `docs/integrations/shoptet-api.md`'s sibling convention, any new Meta Graph API quirks discovered during that verification should be documented (a `docs/integrations/meta-ads-api.md` does not currently exist; if one is warranted it is out of scope for this fix and should be raised separately, not blocking this change).
