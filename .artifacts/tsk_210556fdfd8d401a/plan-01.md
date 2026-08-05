# Plan: Fix dead SerpApi 429/503 retry (status check outside Polly pipeline)

## Summary

`SerpApiWebSearchClient.SearchAsync` runs the HTTP call inside a Polly retry pipeline but calls `response.EnsureSuccessStatusCode()` *after* `Pipeline.ExecuteAsync` returns. Because `HttpClient.GetAsync` doesn't throw on 4xx/5xx, the pipeline sees every non-transport failure as a success and never retries. The two statuses the retry predicate explicitly enumerates (429, 503) are therefore unreachable dead code. This plan fixes the defect by moving the status check inside the pipeline delegate, mirroring the working pattern already used by `AnthropicChatClient` in the same module, and adds tests that prove 429/503 are retried and other statuses are not.

## Context

This is an arch-review finding (`tsk_5b7c1977386843f5:16c750d6`), not a user-facing feature request. `IWebSearchClient` (SerpApi-backed in production; `Mock` in committed `appsettings.json`, real key from Key Vault) backs the article-generation fact-gathering steps and the RAG query expander. A transient 429 (routine on quota-tiered SerpApi plans) or 503 currently fails the whole call on the first attempt instead of being absorbed by the intended 3-attempt exponential backoff — defeating the resilience the pipeline was written to provide. The fix is scoped to correctness of existing intended behavior; no new retry policy is being designed, just made reachable.

## Functional requirements

**FR-1 — Success/failure check must run inside the Polly pipeline.**
Move the equivalent of `response.EnsureSuccessStatusCode()` into the `Pipeline.ExecuteAsync` delegate in `SerpApiWebSearchClient.SearchAsync`, so a non-success response is converted to a thrown `HttpRequestException` (carrying `StatusCode`) *before* the delegate returns — mirroring `AnthropicChatClient.GetResponseAsync` (`backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicChatClient.cs:97-108`), which checks `!response.IsSuccessStatusCode` inside `_pipeline.ExecuteAsync` and throws `new HttpRequestException(msg, null, response.StatusCode)`.
- Acceptance: for a 429 or 503 response, the pipeline observes an `HttpRequestException` with a matching `StatusCode` and retries per `ShouldHandle`, up to `MaxRetryAttempts = 3`, with exponential backoff starting at the configured delay.
- Acceptance: a response that eventually succeeds (e.g. 429 then 429 then 200) returns the successful `WebSearchResult` from `SearchAsync` without the caller ever observing an exception.
- Acceptance: a response that exhausts all retries still failing (e.g. always 429) causes `SearchAsync` to throw `HttpRequestException` with `StatusCode == HttpStatusCode.TooManyRequests`.

**FR-2 — Non-retriable statuses still fail fast, without retry delay.**
- Acceptance: a 400 (or any status not matched by `ShouldHandle`, e.g. 401/404) causes `SearchAsync` to throw `HttpRequestException` on the first attempt, with no retry attempts made (verify via call-count assertion on the mocked handler).

**FR-3 — Transport-level failures keep working exactly as before.**
The existing `ex.StatusCode is null` branch (network-level errors, e.g. `HttpRequestException` thrown by `SendAsync` itself before any response exists) must remain reachable and unaffected by this change.
- Acceptance: existing behavior for a thrown transport exception (simulated via the mocked handler throwing `HttpRequestException` with no status code) still triggers a retry.

**FR-4 — No behavior change to the success path.**
- Acceptance: the two existing tests (`SearchAsync_ReturnsHits_WhenSerpApiReturnsValidOrganicResults`, `SearchAsync_ReturnsEmptyHits_WhenOrganicResultsIsMissing`) continue to pass unmodified.
- Acceptance: `SearchAsync_Throws_WhenApiKeyIsEmpty` continues to pass unmodified (this check happens before the HTTP call and pipeline, so it is untouched by the fix).

## Non-functional requirements

- **Testability of retry timing:** the current `Pipeline` is a `private static readonly` field with a real 2-second base delay — a retry test would take ~14s wall-clock (2s + 4s + 8s) under the current design. `AnthropicChatClient` solves this with a `public static ResiliencePipeline BuildPipeline(TimeSpan baseDelay)` factory plus an optional `ResiliencePipeline? pipeline` constructor parameter (defaulting to a `DefaultPipeline` built with the real 2s delay), letting tests inject a zero-delay pipeline. Apply the same shape to `SerpApiWebSearchClient` so retry tests run fast and deterministically. This is a testability seam, not a behavior change — production callers are unaffected since DI will keep resolving the default 2-second-delay pipeline.
- **No change to production defaults:** `MaxRetryAttempts = 3`, `Delay = 2s` (exponential), and the retriable status set (429, 503, null) stay exactly as configured today — only *where* the check runs changes.
- **Logging:** follow `AnthropicChatClient`'s pattern of logging the error status/body at the point the exception is constructed (`_logger.LogError` or `LogWarning`, matching this module's existing log level choice for adapter failures) so retried failures are still observable in production logs. Do not add a URL-logging handler (existing code comment at `SerpApiWebSearchClient.cs:50` explicitly forbids this because the URL contains the API key) — keep any new logging to status/response body, never the request URL.

## Data model

No data model changes. No new entities — this is a control-flow fix within a single adapter method. `WebSearchResult`, `WebSearchHit`, `WebSearchAdapterOptions` are unchanged.

## Interfaces

No interface changes. `IWebSearchClient.SearchAsync` signature, `WebSearchOptions`, and `WebSearchResult` are unchanged. No new endpoints, events, or UI. This is entirely internal to `Anela.Heblo.Adapters.WebSearch`.

## Dependencies and scope

**In scope:**
- `backend/src/Adapters/Anela.Heblo.Adapters.WebSearch/SerpApiWebSearchClient.cs` — move the status check inside the pipeline delegate; optionally restructure the static pipeline into a `BuildPipeline`/injectable-instance shape for testability.
- `backend/test/Anela.Heblo.Tests/Adapters/WebSearch/SerpApiWebSearchClientTests.cs` — add retry-path tests (429 retried then succeeds, 503 retried then exhausts, 400 fails fast without retry, transport exception still retries).

**Out of scope:**
- Changing `MaxRetryAttempts`, delay/backoff values, or which statuses are retriable — the existing policy is not being redesigned, only made reachable.
- `AnthropicChatClient` itself — it already does this correctly; used only as the reference pattern.
- Any other `IWebSearchClient` implementation (`Mock` provider) or the `WebSearch:Provider` configuration switch.
- Callers of `IWebSearchClient` (article generation, RAG query expander) — they already handle exceptions from this interface; no change needed on their side since the exception type (`HttpRequestException`) is unchanged, only *when* it's thrown relative to retries.
- Rate-limit-aware backoff (e.g. honoring a `Retry-After` header on 429, as `AnthropicChatClient` does via `DelayGenerator` for its 529 case) — not requested by the finding; note as an open question below.

## Rough plan

1. Read `docs/architecture/development_guidelines.md` for any module-specific conventions on adapters/resilience before touching code (per CLAUDE.md doc-map rule).
2. Restructure `SerpApiWebSearchClient`'s pipeline construction to mirror `AnthropicChatClient`: extract a `public static ResiliencePipeline BuildPipeline(TimeSpan baseDelay)`, keep a `DefaultPipeline` built with `TimeSpan.FromSeconds(2)`, and add an optional `ResiliencePipeline? pipeline = null` constructor parameter defaulting to `DefaultPipeline`.
3. In `SearchAsync`, move the response inside the `Pipeline.ExecuteAsync` delegate: call `client.GetAsync`, check `!response.IsSuccessStatusCode`, and if so read the body (for logging) and throw `new HttpRequestException(message, null, response.StatusCode)`; return the successful response otherwise. Remove the now-redundant `response.EnsureSuccessStatusCode()` call after `ExecuteAsync`.
4. Add tests in `SerpApiWebSearchClientTests.cs` using an injected zero-delay pipeline (`SerpApiWebSearchClient.BuildPipeline(TimeSpan.Zero)`), following the existing `Moq.Protected` handler-mocking pattern already used in the file (and the sequential-response-queue pattern used in `AnthropicChatClientTests.cs` for retry-then-succeed tests):
   - 429 → 429 → 200 succeeds and returns hits, with the handler invoked exactly 3 times.
   - 503 on every attempt → `SearchAsync` throws `HttpRequestException` with `StatusCode == HttpStatusCode.ServiceUnavailable`, handler invoked exactly `MaxRetryAttempts + 1` times.
   - 400 → `SearchAsync` throws immediately, handler invoked exactly once (no retry).
   - Existing transport-exception-retries-on-null-status-code behavior stays covered (add if not already present — currently it is not covered in this file).
5. Run `dotnet build` and the full test project (`dotnet test` targeting `Anela.Heblo.Tests`, at minimum the `WebSearch` and `Anthropic` adapter test namespaces) to confirm no regressions and the new tests pass.
6. Run `dotnet format` per repo validation rules.

## Open questions

- **Should 429 honor a `Retry-After` header** the way `AnthropicChatClient` does for 529 via `DelayGenerator`? The finding doesn't ask for this and SerpApi's actual 429 response headers aren't documented in `docs/integrations/shoptet-api.md`-equivalent for SerpApi (there is no `docs/integrations/serpapi.md`). Default: out of scope for this fix; flag as a possible follow-up if SerpApi is confirmed to send `Retry-After` on 429.
- **Should the pipeline restructuring (static `BuildPipeline` + injectable instance) be done at all**, versus leaving the field as-is and only accepting the ~14s real-delay cost in one slow retry test? Default: do the restructuring — it's the same shape already proven in `AnthropicChatClient` in this module, keeps the new tests fast/deterministic, and is a small, low-risk, easily-reviewable change consistent with "surgical changes" guidance (it's directly required to test the fix, not a gratuitous improvement).
- **Where should the error body be logged from (`LogError` vs `LogWarning`)?** `AnthropicChatClient` uses `LogError`. Default: match that for consistency within the module.
