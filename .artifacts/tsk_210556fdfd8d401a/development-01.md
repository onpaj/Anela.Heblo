# Development: SerpApi retry pipeline fix

## Summary

Fixed the arch-review finding: `SerpApiWebSearchClient.SearchAsync` checked `response.EnsureSuccessStatusCode()` *after* `Pipeline.ExecuteAsync` returned, so Polly's retry pipeline never observed non-success statuses (`HttpClient.GetAsync` doesn't throw on 4xx/5xx). The 429/503 branches of the retry predicate were unreachable dead code — only the `ex.StatusCode is null` (transport-failure) branch could ever fire.

The fix mirrors the existing, working pattern already used by `AnthropicChatClient` in the same module: the success check now runs *inside* the pipeline delegate, throwing `HttpRequestException` (carrying `StatusCode`) before the delegate returns, so the pipeline can retry it.

## Files changed

- `backend/src/Adapters/Anela.Heblo.Adapters.WebSearch/SerpApiWebSearchClient.cs`
  - Replaced the `private static readonly ResiliencePipeline Pipeline` field with:
    - `public static ResiliencePipeline BuildPipeline(TimeSpan baseDelay)` — factory with the same retry config as before (`MaxRetryAttempts = 3`, exponential backoff, same `ShouldHandle` predicate for 429/503/null).
    - `private static readonly ResiliencePipeline DefaultPipeline = BuildPipeline(TimeSpan.FromSeconds(2))` — production default, same 2s base delay as before.
  - Added an optional constructor parameter `ResiliencePipeline? pipeline = null`, stored in a new `_pipeline` instance field defaulting to `DefaultPipeline`. This is additive/source-compatible — existing 3-arg call sites (including the three original tests and DI registration) are unaffected.
  - `SearchAsync`: moved the response validation inside the `_pipeline.ExecuteAsync` delegate. On `!httpResponse.IsSuccessStatusCode`, reads the response body, logs `_logger.LogError("SerpApi error {Status}: {Body}", ...)` (never logs the URL, which carries the API key — preserved the existing constraint), and throws `HttpRequestException` with `StatusCode` set. Removed the now-dead `response.EnsureSuccessStatusCode()` call after `ExecuteAsync`.
  - No DI, interface, or data-model changes — `WebSearchAdapterServiceCollectionExtensions` needed no edits since it uses plain `AddScoped<IWebSearchClient, SerpApiWebSearchClient>()` and the container resolves the new optional parameter to `null` → `DefaultPipeline`.

- `backend/test/Anela.Heblo.Tests/Adapters/WebSearch/SerpApiWebSearchClientTests.cs`
  - Added `InstantRetryPipeline = SerpApiWebSearchClient.BuildPipeline(TimeSpan.Zero)` for deterministic, fast retry tests.
  - Added a `CreateHttpClientFactory(Mock<HttpMessageHandler>)` overload so new tests can use a call-counting handler mock (existing overload took a fixed `HttpResponseMessage`).
  - Four new `[Fact]` tests:
    - `SearchAsync_RetriesOn429_ThenSucceeds` — 429, 429, 200 → returns parsed hits, handler invoked exactly 3 times.
    - `SearchAsync_RetriesOn503_ThenThrowsAfterExhaustingRetries` — 503 on every call → throws `HttpRequestException` with `StatusCode == ServiceUnavailable`, handler invoked exactly 4 times (`MaxRetryAttempts + 1`).
    - `SearchAsync_DoesNotRetryOn400` — 400 → throws immediately, handler invoked exactly once (no retry, proving non-retriable statuses still fail fast).
    - `SearchAsync_RetriesOnTransportLevelFailure` — handler throws `HttpRequestException` with no `StatusCode` on every call → still retried to exhaustion (proves the pre-existing `ex.StatusCode is null` branch keeps working after the restructuring).
  - The three original tests (`SearchAsync_ReturnsHits_WhenSerpApiReturnsValidOrganicResults`, `SearchAsync_ReturnsEmptyHits_WhenOrganicResultsIsMissing`, `SearchAsync_Throws_WhenApiKeyIsEmpty`) were left unmodified — they construct the client with 3 args and are unaffected by the new optional 4th parameter.

## How to verify

```bash
export PATH="$PATH:/Users/rem/.dotnet"   # dotnet not on default PATH in this environment
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~WebSearch|FullyQualifiedName~Anthropic" --no-build
dotnet format Anela.Heblo.sln --include \
  backend/src/Adapters/Anela.Heblo.Adapters.WebSearch/SerpApiWebSearchClient.cs \
  backend/test/Anela.Heblo.Tests/Adapters/WebSearch/SerpApiWebSearchClientTests.cs
```

## Results

- `dotnet build Anela.Heblo.sln` — succeeded, 0 errors (251 pre-existing warnings, none introduced by this change, none in touched files).
- `dotnet test ... --filter "FullyQualifiedName~WebSearch|FullyQualifiedName~Anthropic"` — **Passed! Failed: 0, Passed: 24, Skipped: 0, Total: 24** (7 SerpApi tests incl. the 4 new ones + 17 Anthropic tests, confirming no regression to the reference pattern).
- `dotnet format --include <changed files>` — no changes needed; diff was already format-clean.

## Scope notes

- No behavior change to the success path or to `MaxRetryAttempts`/delay/backoff/retriable-status-set — only *where* the check runs, exactly as scoped in plan-01.md/design-01.md.
- No `Retry-After` handling was added for 429 (matches `AnthropicChatClient`'s 529 `DelayGenerator`) — explicitly out of scope per the plan; not requested by the finding.
- No changes to `MockWebSearchClient`, `WebSearchAdapterOptions`, `IWebSearchClient`, or any caller of `SearchAsync`.
