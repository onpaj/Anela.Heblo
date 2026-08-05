# Architecture Assessment: SerpApi retry pipeline fix

## Verdict

**Approved as designed.** No changes required to `design-01.md` or `plan-01.md`. This is a pure control-flow correction with zero architectural surface — no new components, no DI registration changes, no data-schema changes, no cross-module impact. I verified every factual claim in the design against the actual source files rather than taking them on faith.

## What I checked against the real codebase

**1. The reference pattern is real and matches the design's description exactly.**
Read `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicChatClient.cs:16-56, 84-111` directly. The design's quoted shape (`DefaultPipeline` built via `BuildPipeline(TimeSpan)`, `public static` factory, optional `ResiliencePipeline? pipeline = null` constructor parameter, `!response.IsSuccessStatusCode` checked and `HttpRequestException` thrown *inside* `_pipeline.ExecuteAsync`, carrying `StatusCode`) is a line-for-line match to what's actually in the file, not a paraphrase. The design is asking for a mechanical port of an existing, working, already-tested pattern — the lowest-risk shape a fix like this can take.

**2. The DI claim holds.** `WebSearchAdapterServiceCollectionExtensions.AddWebSearchAdapter` (`backend/src/Adapters/Anela.Heblo.Adapters.WebSearch/WebSearchAdapterServiceCollectionExtensions.cs:22-23`) registers `services.AddScoped<IWebSearchClient, SerpApiWebSearchClient>()` — no explicit factory delegate. The built-in constructor-injection resolver will supply the added `ResiliencePipeline? pipeline = null` parameter's default value since no `ResiliencePipeline` is registered in the container, exactly as the design states. This is the same mechanism already proven for `AnthropicChatClient`'s two registrations. No DI wiring changes needed anywhere.

**3. Existing tests are compatible, not just "unaffected."** All three current tests in `SerpApiWebSearchClientTests.cs` construct `SerpApiWebSearchClient` with exactly 3 arguments (`CreateOptions()`, `CreateHttpClientFactory(...)`, `NullLogger...Instance`). Adding a 4th *optional* constructor parameter is additive and source-compatible — these tests need zero modification, confirming FR-4.

**4. The test-pattern claims are real, not invented.** `AnthropicChatClientTests.cs` does contain exactly the patterns the design/plan cite as precedent: `InstantRetryPipeline = AnthropicChatClient.BuildPipeline(TimeSpan.Zero)` (line 26-27), a call-counter closure returning different status codes per invocation to simulate retry-then-succeed (`GetResponseAsync_529SucceedsAfterRetry_ReturnsAnswer`, lines 211-242), and a fail-fast-no-retry assertion via call-count (`GetResponseAsync_400Response_DoesNotRetry`, lines 148-176). The four new SerpApi test cases specified in the design map onto these same, already-proven idioms — nothing novel is being introduced into the test suite either.

**5. Module/architecture-doc alignment.** `docs/architecture/development_guidelines.md:174` lists Polly as the designated resilience mechanism for "External API calls" — this fix makes SerpApi's Polly usage actually functional, it doesn't introduce a new pattern needing doc updates. No `Anela.Heblo.Adapters.WebSearch` project file, namespace, or module-boundary rule is touched. `IWebSearchClient`'s interface contract, `WebSearchResult`/`WebSearchHit`/`WebSearchAdapterOptions` shapes are all untouched, and since these are consumed as internal domain/adapter types (not OpenAPI-generated DTOs), the "DTOs must be classes, not records" rule doesn't apply here regardless.

## Points worth flagging to the implementer (not blocking)

- The design's error-path logging (`_logger.LogError("SerpApi error {Status}: {Body}", ...)`) correctly avoids logging `url`, consistent with the existing `SerpApiWebSearchClient.cs:50` comment forbidding it (the URL carries the API key in a query param). This constraint is easy to violate accidentally when refactoring the delegate body — worth a deliberate check during implementation, not just trusting the design snippet gets copied verbatim.
- The plan's own open question (whether to add `Retry-After` handling for 429, mirroring `AnthropicChatClient`'s 529 `DelayGenerator`) is correctly scoped out — the arch-review finding only asks that the *existing* configured retry become reachable, not that the retry policy be enhanced. Agreed this stays out of scope; flagging it as a legitimate follow-up rather than scope creep to fold in now.
- No production behavior changes beyond "429/503 now actually retry as configured" — `MaxRetryAttempts`, delay, backoff type, and the retriable-status set are unchanged. This keeps the change reviewable as a pure bugfix rather than a policy change.

## Prerequisites before implementation

None outstanding. The plan's own step 1 ("read `development_guidelines.md`") is satisfied by this assessment — Polly-for-external-calls is confirmed as the sanctioned pattern, no additional consultation needed.
