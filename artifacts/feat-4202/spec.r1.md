# Specification: Move Meta Ads Access Token from URL Query Parameter to Authorization Header

## Summary
`MetaAdsTransactionSource` currently embeds the Meta Graph API OAuth access token as a `?access_token=…` query parameter on outbound HTTP requests, which risks the credential leaking into logs, telemetry, and network traces outside the application's control. This change moves the token to a standard `Authorization: Bearer <token>` header, eliminating the token from the URL entirely and removing the now-unnecessary `RedactToken` helper.

## Background
`MetaAdsTransactionSource.BuildInitialUrl` (backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs:111-113) constructs the Meta Graph API transactions-list URL with the access token appended as a query string parameter. A companion `RedactToken` method (lines 115-122) strips the token before it is written to an explicit debug log call, but this only protects that one log statement.

Several other transport-layer paths do not go through `RedactToken` and would still capture the raw token in the URL:
- ASP.NET Core / `HttpClient` `DiagnosticListener` events, which log outbound request URIs.
- Azure Application Insights HTTP dependency tracking, which records full request URLs by default.
- Proxy and load-balancer access logs on outbound requests.
- Network traces captured during incident investigation.

Since a leaked token would appear in a log-search UI (Application Insights / Log Analytics) without requiring Key Vault access, this is a credential-exposure risk that should be closed at the source. The Meta Graph API supports the OAuth 2.0 standard `Authorization: Bearer` header as a fully equivalent authentication mechanism, so the fix is a transport-layer change with no behavioral change to the data returned by the API.

## Functional Requirements

### FR-1: Send the Meta Graph API access token via the Authorization header
`MetaAdsTransactionSource` must authenticate outbound requests to the Meta Graph API `transactions` endpoint (and any other Meta Graph API endpoint it calls, including pagination follow-up requests) using an `Authorization: Bearer <access_token>` HTTP header instead of an `access_token` URL query parameter.

**Acceptance criteria:**
- `BuildInitialUrl` no longer includes `access_token` (or the token value) anywhere in the constructed URL string.
- The outbound `HttpRequestMessage` sent for the initial page fetch has `Headers.Authorization` set to `new AuthenticationHeaderValue("Bearer", _settings.AccessToken)`.
- Any subsequent/paginated request the source issues (e.g. following a `paging.next` cursor returned by Meta, if applicable in the current implementation) also carries the same `Authorization: Bearer` header rather than a token in its URL.
- All other existing query parameters (`fields`, `time_range`, etc.) remain present and correctly formatted on the URL.
- A request to the Meta Graph API with a valid token and the header set (rather than the query parameter) succeeds and returns transaction data, verified via the existing integration/unit test suite for this adapter (updated as needed) or a manual verification against the live API per the Shoptet-style "no sandbox" caution below.

### FR-2: Remove the `RedactToken` helper and its call sites
Since the token no longer appears in any URL, the `RedactToken` method and the debug log statement that invoked it for redaction purposes become unnecessary and should be removed, with the URL logged as-is.

**Acceptance criteria:**
- `RedactToken` is deleted from `MetaAdsTransactionSource`.
- The debug log call (`_logger.LogDebug(...)`) logs the full URL string directly, with no redaction step, and no token is present in that URL to begin with (guaranteed by FR-1).
- No remaining reference to `RedactToken` exists in the codebase (build passes, no dead references).

### FR-3: Preserve existing pagination and error-handling behavior
The refactor changes only how the request is authenticated (header vs. query param) and must not alter the method signatures' external behavior, response deserialization, retry/resilience pipeline usage (`_pipeline.ExecuteAsync`), or error handling (`EnsureSuccessStatusCode`, null-response exception).

**Acceptance criteria:**
- `FetchPageAsync` (or equivalent) continues to use the existing resilience pipeline (e.g. Polly) wrapping the HTTP call exactly as before.
- A non-success HTTP status still results in an exception via `EnsureSuccessStatusCode()`.
- A null-deserialized response body still throws `InvalidOperationException("MetaAds API returned null response body.")` (or the existing equivalent message).
- Existing unit tests for `MetaAdsTransactionSource` (or new/updated tests covering the header-based auth path) pass after the change.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact is expected or targeted; this is a same-request-shape change (one additional header per HTTP call instead of one additional query parameter). No new network round-trips are introduced.

### NFR-2: Security
This change is itself a security hardening fix. The access token must:
- Never appear in any outbound request URL.
- Continue to be sourced from the existing configuration/secrets mechanism (`_settings.AccessToken`, backed by Azure Key Vault per project convention) with no change to how the token is provisioned or stored.
- Not be logged anywhere in plaintext (the header value itself must not be dumped via a full-headers debug log; only non-sensitive parts of the request, e.g. the URL and method, may be logged).

## Data Model
No data model changes. This is a transport-layer (HTTP request construction) change internal to the `MetaAdsTransactionSource` adapter; no entities, DTOs, or persistence schemas are affected.

## API / Interface Design
- **Internal method signature changes** (within `Anela.Heblo.Adapters.MetaAds`):
  - `BuildInitialUrl(DateTime from, DateTime to)` — return value no longer contains `access_token`; otherwise unchanged signature.
  - `FetchPageAsync(string url, CancellationToken ct)` (or equivalent existing method) — internally constructs an `HttpRequestMessage` explicitly (rather than a bare `GetAsync`/`GetStringAsync` call) so that the `Authorization` header can be attached; return type and calling convention from the rest of the class remain unchanged.
- **External API contract**: No change. This does not touch any MediatR command/query, MVC controller, or public REST/OpenAPI surface of Anela Heblo — it only changes how the `MarketingInvoices` module's outbound call to the third-party Meta Graph API is authenticated.
- **Third-party API used**: `GET https://graph.facebook.com/{ApiVersion}/{AccountId}/transactions`, authenticated via `Authorization: Bearer <token>` per Meta's Graph API / OAuth 2.0 (RFC 6750) support, replacing the `?access_token=` query parameter form.

## Dependencies
- Meta Graph API (`graph.facebook.com`) — confirmed to support Bearer-token header authentication as an alternative to the `access_token` query parameter (standard Graph API behavior, per the brief).
- `System.Net.Http.Headers.AuthenticationHeaderValue` (BCL) for constructing the header.
- Existing resilience pipeline (Polly-based `_pipeline`) already used by the class — no new dependency introduced.
- No change to `MetaAdsSettings`/configuration schema (`_settings.AccessToken`, `_settings.ApiVersion`, `_settings.AccountId` all remain as-is).

## Out of Scope
- Any change to how the access token is obtained, refreshed, or stored (e.g. OAuth token refresh flow, Key Vault secret rotation).
- Changes to other Meta/Facebook API integrations outside `MetaAdsTransactionSource`, if any exist elsewhere in the codebase.
- Broader telemetry/logging redaction policy changes (e.g. Application Insights sampling/scrubbing configuration) — this fix addresses the root cause (token in URL) rather than adding scrubbing rules downstream.
- Any change to the `MarketingInvoices` domain logic, invoice matching, or reporting built on top of the fetched transactions data.
- Retroactive redaction/cleanup of any token values that may already exist in historical logs or Application Insights data from before this fix.

## Open Questions
None.

## Status: COMPLETE
