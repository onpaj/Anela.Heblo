# Design: SerpApi retry pipeline fix

No UI is involved — this is an internal control-flow correction inside one backend adapter (`Anela.Heblo.Adapters.WebSearch`). No wireframes/UX section applies.

## Component design

### `SerpApiWebSearchClient` (backend/src/Adapters/Anela.Heblo.Adapters.WebSearch/SerpApiWebSearchClient.cs)

Responsibility stays exactly as today: translate a `WebSearchOptions` query into a SerpApi HTTP GET, retry transient failures, and parse the JSON body into a `WebSearchResult`. The only responsibility being added is *where* "is this response usable?" is decided — that decision moves from after the resilience boundary to inside it, mirroring `AnthropicChatClient` (`backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicChatClient.cs:18-39,84-111`) in the same module.

**Static shape** (replaces the current `private static readonly ResiliencePipeline Pipeline` field):

```
private static readonly ResiliencePipeline DefaultPipeline = BuildPipeline(TimeSpan.FromSeconds(2));

public static ResiliencePipeline BuildPipeline(TimeSpan baseDelay) =>
    new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = baseDelay,
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(ex =>
                ex.StatusCode == HttpStatusCode.TooManyRequests ||
                ex.StatusCode == HttpStatusCode.ServiceUnavailable ||
                ex.StatusCode is null)
        })
        .Build();
```

`BuildPipeline` is `public static` purely as a testability seam (lets tests build a zero-delay pipeline), matching the existing `AnthropicChatClient.BuildPipeline` convention exactly — same visibility, same generic shape, same parameter name.

**Instance shape** — add an optional constructor parameter, defaulting to the static instance:

```
private readonly ResiliencePipeline _pipeline;

public SerpApiWebSearchClient(
    IOptions<WebSearchAdapterOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<SerpApiWebSearchClient> logger,
    ResiliencePipeline? pipeline = null)
{
    _options = options.Value;
    _httpClientFactory = httpClientFactory;
    _logger = logger;
    _pipeline = pipeline ?? DefaultPipeline;
}
```

No change to `WebSearchAdapterServiceCollectionExtensions.AddWebSearchAdapter` is needed: it registers `SerpApiWebSearchClient` via plain `services.AddScoped<IWebSearchClient, SerpApiWebSearchClient>()` with no explicit factory delegate. The built-in DI container resolves the unmatched `ResiliencePipeline?` parameter to its default value (`null`) since no `ResiliencePipeline` service is registered, so production wiring is untouched — this is the same mechanism `AnthropicChatClient` relies on implicitly (its two registrations use explicit factories that simply omit the `pipeline` argument, which achieves the same "use `DefaultPipeline`" outcome).

**`SearchAsync` control flow** — the success check moves inside the delegate passed to `_pipeline.ExecuteAsync`:

```
var response = await _pipeline.ExecuteAsync(async token =>
{
    var httpResponse = await client.GetAsync(url, token);

    if (!httpResponse.IsSuccessStatusCode)
    {
        var errorBody = await httpResponse.Content.ReadAsStringAsync(token);
        _logger.LogError("SerpApi error {Status}: {Body}", httpResponse.StatusCode, errorBody);
        throw new HttpRequestException(
            $"SerpApi returned {(int)httpResponse.StatusCode}: {errorBody}",
            null,
            httpResponse.StatusCode);
    }

    return httpResponse;
}, ct);

var json = await response.Content.ReadAsStringAsync(ct);
return ParseSerpApiResponse(query, json);
```

The standalone `response.EnsureSuccessStatusCode()` call after `ExecuteAsync` is removed — by the time control reaches that line, `response` is guaranteed successful (a non-success response already threw inside the delegate, and the pipeline either retried it away or propagated the `HttpRequestException` out of `ExecuteAsync` entirely, never returning to the caller).

Everything else in the class — the API-key guard, URL construction (including the "never log this URL, it contains the API key" constraint at the current line 50), `ParseSerpApiResponse`, and the `WebSearchResult`/`WebSearchHit` shapes — is unchanged. The new error-path log line logs status code and response body only, never the request URL, consistent with that existing constraint.

**Field/member visibility summary:**

| Member | Before | After |
|---|---|---|
| `Pipeline` | `private static readonly ResiliencePipeline` | removed, replaced by `DefaultPipeline` + `_pipeline` |
| `BuildPipeline(TimeSpan)` | — | new `public static` factory |
| `DefaultPipeline` | — | new `private static readonly`, built via `BuildPipeline(TimeSpan.FromSeconds(2))` |
| `_pipeline` | — | new `private readonly` instance field |
| constructor | 3 required params | 3 required params + 1 optional (`ResiliencePipeline? pipeline = null`) |

No other class in the module (`MockWebSearchClient`, `WebSearchAdapterServiceCollectionExtensions`, `WebSearchAdapterOptions`) changes.

### Test component (`SerpApiWebSearchClientTests`)

Adds test cases as a peer to the existing three, following the file's existing `Moq.Protected` handler-mock helper (`CreateHttpClientFactory`) and the sequential-response pattern already established in `AnthropicChatClientTests` (`ReturnsAsync(() => { ...; return ...; })` closures keyed on a call counter). Constructs `SerpApiWebSearchClient` with the 4th constructor argument set to `SerpApiWebSearchClient.BuildPipeline(TimeSpan.Zero)` so retry tests run without real delay.

New cases (one `[Fact]` each):
1. 429, 429, 200 (organic results body) → `SearchAsync` returns the parsed `WebSearchResult`; handler invoked exactly 3 times.
2. 503 on every call → `SearchAsync` throws `HttpRequestException` with `StatusCode == HttpStatusCode.ServiceUnavailable`; handler invoked exactly 4 times (`MaxRetryAttempts + 1`).
3. 400 → `SearchAsync` throws `HttpRequestException` with `StatusCode == HttpStatusCode.BadRequest`; handler invoked exactly once (no retry).
4. Handler throws a transport-level `HttpRequestException` (no `StatusCode`, i.e. `null`) on every call → still retried up to exhaustion (proves the `ex.StatusCode is null` branch, currently the only reachable one, keeps working after the restructuring).

No changes to the three existing tests' arrange/act/assert — they exercise the 200 and API-key-empty paths, which are unaffected by moving the check inside the delegate.

## Data schemas

No wire-format, DTO, or event payload changes — this fix is entirely internal control flow within one adapter method. For completeness, the two shapes that mediate between the pipeline and its caller:

**Exception shape thrown from inside the pipeline delegate** (new — carries the same information `EnsureSuccessStatusCode` used to carry, just constructed one layer down):

```
HttpRequestException
  Message    = "SerpApi returned {statusCodeAsInt}: {responseBody}"
  InnerException = null
  StatusCode = HttpStatusCode   // e.g. TooManyRequests, ServiceUnavailable, BadRequest
```

This is the same exception type (`HttpRequestException`) `SearchAsync` already surfaces to callers today via `EnsureSuccessStatusCode()`, so callers of `IWebSearchClient.SearchAsync` (article-generation fact-gathering, RAG query expander) see no change to the exception type or the interface contract — only to whether/when retries happen before it's thrown.

**`ShouldHandle` predicate input** (unchanged, now actually reachable for 429/503):

```
ex.StatusCode == HttpStatusCode.TooManyRequests   // 429
ex.StatusCode == HttpStatusCode.ServiceUnavailable // 503
ex.StatusCode is null                              // transport-level failure
```

`WebSearchResult`, `WebSearchHit`, `WebSearchOptions`, `WebSearchAdapterOptions` — all unchanged, no fields added or removed.
