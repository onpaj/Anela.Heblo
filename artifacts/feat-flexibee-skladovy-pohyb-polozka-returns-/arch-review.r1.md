```markdown
# Architecture Review: FlexiBee `skladovy-pohyb-polozka` 503 resilience

## Architectural Fit Assessment

The change aligns cleanly with patterns already in the codebase. There are two precedents to anchor against:

1. **`FlexiStockClient.cs:51-83`** — the canonical "typed catch + structured log + rethrow" shape inside an adapter. The new code in `FlexiManufactureHistoryClient` should match this shape line-for-line, only diverging where retry is added.
2. **`CatalogResilienceService.cs`** — the canonical Polly v8 pipeline shape (`ResiliencePipelineBuilder` + `AddRetry` + `OnRetry` logging). Polly v8 is already a dependency of `Anela.Heblo.Application`.

The integration points are:
- **Adapter boundary**: `FlexiManufactureHistoryClient` — the only file gaining new behavior.
- **DI/module wiring**: none. The class is constructor-injected; we do not introduce a new abstraction.
- **Caller (`GetManufactureOutputHandler`)**: untouched by the spec's "preserve existing behavior" requirement; on final failure after retries, the exception still propagates.

The fit is good. The risk of architectural drift is low because the spec is deliberately scoped to a single method.

## Proposed Architecture

### Component Overview

```
GetManufactureOutputHandler
   │ await GetHistoryAsync(...)
   ▼
FlexiManufactureHistoryClient.GetHistoryAsync
   │
   │  ┌──── ResiliencePipeline (private static, lazy) ─────┐
   │  │  AddRetry:                                          │
   │  │    handles HttpRequestException for transient 5xx   │
   │  │    MaxRetryAttempts = 2                             │
   │  │    Delay = 200ms exponential, jitter                │
   │  │    OnRetry → _logger.LogWarning(structured)         │
   │  └─────────────────────────────────────────────────────┘
   │       │ pipeline.ExecuteAsync(ct => SDK.GetAsync(ct))
   │       ▼
   │  IStockItemsMovementClient.GetAsync (FlexiBee SDK)
   │       │
   │       ├─ 503 → HttpRequestException → retried
   │       ├─ 4xx/5xx (non-503) → HttpRequestException → caught, logged, rethrown
   │       └─ HttpClient timeout → OperationCanceledException → caught, logged, rethrown
   ▼
catch HttpRequestException when StatusCode == 503  → log warning, rethrow (after retries exhausted)
catch HttpRequestException                          → log error, rethrow (non-transient)
catch OperationCanceledException (internal timeout) → log warning, rethrow  [unchanged]
catch OperationCanceledException (caller cancel)    → log info, rethrow     [unchanged]
```

### Key Design Decisions

#### Decision 1: Polly pipeline location

**Options considered:**
- (a) Add a `DelegatingHandler` to the `HttpClient` used by `IStockItemsMovementClient` via DI.
- (b) Inline `ResiliencePipeline` field on `FlexiManufactureHistoryClient`.
- (c) Reuse/extract a shared `IFlexiResilienceService`.

**Chosen approach:** (b) Inline `ResiliencePipeline` field on `FlexiManufactureHistoryClient`.

**Rationale:** (a) is intercepted by the third-party `Rem.FlexiBeeSDK.Client` package — the SDK manages its own `HttpClient` and we don't have a clean DI seam to inject a handler without forking the SDK. (c) is explicitly out-of-scope per spec ("does not generalize to other Flexi clients"). (b) keeps the change surgical, reads naturally next to the existing `try/catch` block, and matches `CatalogResilienceService.cs` in pipeline shape without creating an abstraction we'd later have to delete.

#### Decision 2: What to retry

**Options considered:**
- (a) Retry only HTTP 503.
- (b) Retry HTTP 5xx (502, 503, 504).
- (c) Retry 5xx + transient `OperationCanceledException` (HttpClient internal timeout).

**Chosen approach:** (b) — retry on `HttpRequestException` where `StatusCode` is `502`, `503`, or `504`.

**Rationale:** The brief observed only 503, but the underlying cause ("FlexiBee occasionally becomes temporarily unavailable") is the same class as 502/504. The cost of broadening is one extra status-code comparison; the cost of narrowing is missing the next outage variant. We exclude (c) because the spec explicitly leaves the timeout question open and existing behavior treats `OperationCanceledException` as terminal — changing that is a separate decision and out of scope for this fix.

#### Decision 3: Retry budget

**Options considered:**
- 1 retry / 100ms; 2 retries / 200ms exponential; 3 retries / 1s (matching `CatalogResilienceService`).

**Chosen approach:** 2 retries, base delay 200 ms, exponential backoff, jitter on. Worst case ≈ 200 ms + 400 ms ≈ 600 ms added latency, well under the 1.5 s NFR budget.

**Rationale:** The spec NFR caps retry latency at ≤1.5 s. Three retries with 1 s base (the `CatalogResilienceService` setting) blows the budget; two retries with sub-second delays land safely inside it and still cover the typical "FlexiBee restarts mid-request" pattern. Jitter prevents thundering-herd if the same handler invocation is parallelized in the future.

#### Decision 4: Catch ordering and the "fallback" `HttpRequestException` block

**Chosen approach:** After the retry pipeline, two `HttpRequestException` catches:
1. `when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)` → `LogWarning` (this is the expected failure mode after retries).
2. Bare `HttpRequestException` → `LogError` (non-transient or unexpected status — different operational signal).

**Rationale:** Distinct severities give ops a clean Application Insights filter: warnings = "FlexiBee was flapping, we tried again," errors = "FlexiBee returned something unexpected, investigate." Reusing `LogWarning` for both would lose this signal.

#### Decision 5: Pipeline lifetime

**Chosen approach:** `private static readonly ResiliencePipeline _pipeline` constructed once via static initializer. The pipeline is stateless and thread-safe per Polly v8 docs; per-instance construction would waste allocations.

**Rationale:** Matches Polly v8 guidance. `CatalogResilienceService` instantiates per-instance because it's DI-registered as scoped/transient and reads logger context for its `OnRetry` — but our pipeline doesn't need to capture the logger inside the lambda; we can use the instance logger via closure on a non-static field. **Refinement:** keep `_pipeline` as an instance field (not static) so `OnRetry` can call `_logger.LogWarning` directly. Cost is negligible (one builder call per client construction; clients are scoped).

## Implementation Guidance

### Directory / Module Structure

No new files. All changes are within:

- **Edit** `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureHistoryClient.cs`
- **Edit** `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj` — add `<PackageReference Include="Polly" Version="8.4.1" />` (matching existing version in `Anela.Heblo.Application.csproj` line 27).
- **Edit** `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureHistoryClientTests.cs` — add 5+ new tests next to the existing two.

No changes to `GetManufactureOutputHandler.cs`, no changes to DI modules, no changes to `IManufactureHistoryClient`.

### Interfaces and Contracts

- **Public signature unchanged**: `Task<List<ManufactureHistoryRecord>> GetHistoryAsync(DateTime, DateTime, string?, CancellationToken)` — preserved per NFR.
- **Domain interface `IManufactureHistoryClient`**: untouched. No new abstraction added.
- **Logger contract**: `ILogger<FlexiManufactureHistoryClient>` already injected. No new logger needed.
- **Constructor signature**: unchanged. Polly pipeline built lazily inside the constructor or in a private factory method.

### Data Flow

Happy path:
```
Handler → client.GetHistoryAsync → pipeline.ExecuteAsync → SDK.GetAsync → 200 OK
                                                                ↓
                                       movements → LINQ projection → return List<ManufactureHistoryRecord>
```

Transient 503, recovered:
```
SDK.GetAsync → 503 (HttpRequestException) → pipeline retries (200ms) →
SDK.GetAsync → 503 → pipeline retries (400ms+jitter) →
SDK.GetAsync → 200 OK → projection → return
[OnRetry logs 2× warning with attempt#, status code]
```

Transient 503, exhausted:
```
SDK.GetAsync → 503 → retry → 503 → retry → 503 →
catch HttpRequestException when StatusCode == 503 → LogWarning(...) → rethrow → handler propagates to caller
```

Non-transient (e.g., 401, 500 with non-retried status):
```
SDK.GetAsync → 500/401 → no retry (predicate doesn't match) →
catch HttpRequestException (fallback) → LogError(...) → rethrow
```

Cancellation paths: **unchanged** from current implementation.

### Test Plan (concrete)

The two existing tests stay. Add the following 5 (covering the FRs):

| # | Name | Setup | Assert |
|---|------|-------|--------|
| 1 | `GetHistoryAsync_When503_RetriesAndSucceeds` | mock throws 503 twice, then returns empty list | `Returns successfully`; SDK called 3 times; warning logged ≥2× |
| 2 | `GetHistoryAsync_When503Persists_LogsWarningAndRethrows` | mock throws 503 always | throws `HttpRequestException`; SDK called 3× (1+2 retries); final `LogWarning` with status code |
| 3 | `GetHistoryAsync_When500_DoesNotRetry_LogsErrorAndRethrows` | mock throws 500 | throws; SDK called 1×; `LogError` logged |
| 4 | `GetHistoryAsync_When401_DoesNotRetry_LogsErrorAndRethrows` | mock throws 401 | same shape as above — proves predicate correctly excludes 4xx |
| 5 | `GetHistoryAsync_RetryRespectsCancellation` | mock throws 503; caller cancels mid-retry | throws `OperationCanceledException`; SDK called <3× |

Use `It.IsAny<...>` matchers consistent with the existing test style. Use `FluentAssertions` for the throw/contain assertions.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Polly v8 version drift between `Anela.Heblo.Application` (8.4.1) and the new reference | Low | Pin to exact same version `8.4.1`. Add a comment-free `PackageReference` line — Directory.Packages.props is not in use here. |
| Retry budget interacts badly with caller's overall timeout (e.g., HTTP request timeout in API layer) | Medium | Cap added latency at ≤600 ms; document in commit message. The caller's `CancellationToken` flows through `pipeline.ExecuteAsync` and aborts retries on caller cancel. |
| Static-field pipeline captures logger from first instance only (if we go static) | Medium | Decision 5 says **instance field**, not static. Avoids the capture problem entirely. |
| FlexiBee returns 503 with a `Retry-After` header that we're ignoring | Low | Out of scope per spec. Note for future work; do not add now. |
| `LogError` for non-503 5xx triggers existing alerting that paged on 503s before the change | Medium | Confirm with ops that severity reclassification is acceptable. The spec NFR ("no PII in logs") is independent of severity choice. |
| New tests are flaky due to real wall-clock waits in retry delays | Low–Medium | Polly v8 supports `TimeProvider` injection; if test latency becomes annoying, switch the pipeline to use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`. Initial implementation can use real delays — 600 ms × 5 tests = 3 s, acceptable. |
| Pipeline retries also trigger if FlexiBee's 503 includes a body that the SDK partially consumes | Low | The SDK throws `HttpRequestException` before producing a result; retry is safe. Verify by inspecting `Rem.FlexiBeeSDK.Client` source if behavior surprises. |

## Specification Amendments

1. **Resolve open question on retried statuses to "5xx subset {502, 503, 504}"** (not 503-only). Rationale in Decision 2.
2. **Resolve open question on Polly availability**: Polly 8.4.1 is already a transitive dependency of `Anela.Heblo.Application`; we add it explicitly to `Anela.Heblo.Adapters.Flexi.csproj`. No version negotiation needed.
3. **Resolve open question on retry tuning**: 2 retries, 200 ms base, exponential, jitter — fits the 1.5 s NFR with margin (Decision 3).
4. **Resolve open question on timeout retries**: do **not** retry `OperationCanceledException`. Existing behavior (log + rethrow) is preserved. Justification: HttpClient internal timeout already means the upstream is unhealthy; doubling the wait window exceeds the latency NFR.
5. **Resolve open question on graceful degradation in the handler**: stays out of scope. `GetManufactureOutputHandler` continues to propagate.
6. **Add NFR**: pipeline must be allocation-cheap — instance field, single builder call per construction, no per-call pipeline rebuilding. (Implicit in Decision 5; worth pinning explicitly.)
7. **Add test FR**: include a test that asserts `CancellationToken` aborts an in-flight retry sequence (test #5 above). The current spec's "5+ unit tests" is silent on which scenarios — pin the matrix.

## Prerequisites

1. **NuGet package**: add `<PackageReference Include="Polly" Version="8.4.1" />` to `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj`.
2. **No DB migration, no config keys, no env vars, no DI changes.**
3. **No infrastructure work.** Application Insights already captures the `LogWarning`/`LogError` calls via the existing logger pipeline. No new dashboards or alerts required for the change to function (though ops should be informed that 503s will now log as `Warning` after retries, not propagate as raw failures).
4. **CI**: existing `dotnet build` + `dotnet test` + `dotnet format` gates suffice. No new test project required.
5. **No frontend changes.** This is purely a backend resilience patch.
```