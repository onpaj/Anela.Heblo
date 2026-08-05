# Review: SerpApi retry pipeline fix

## Verdict: done

## What was checked

- Read `plan-01.md`, `design-01.md`, `architecture-01.md`, `development-01.md`.
- Read the actual diff (`git diff HEAD~1 HEAD`) for both changed files.
- Read the full current `SerpApiWebSearchClient.cs` and the reference `AnthropicChatClient.cs` to confirm the mirrored pattern is faithful, not just described that way.
- Ran the build and tests myself rather than trusting the development step's report:
  - `dotnet build Anela.Heblo.sln` — **Build succeeded, 0 errors** (one pre-existing, unrelated warning from the `Anela.Heblo.AccessMatrixGen` codegen tool crashing on a malformed JSON artifact — present regardless of this change, not touched by it).
  - `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~WebSearch|FullyQualifiedName~Anthropic" --no-build` — **Passed: 24, Failed: 0, Skipped: 0** (1s — confirms the zero-delay pipeline test seam actually works, not just 14s of real Polly backoff).
  - `dotnet format Anela.Heblo.sln --verify-no-changes --include <the two changed files>` — **exit 0**, no formatting diffs.

## Conformance to the finding

The finding's root cause and suggested direction are addressed precisely:

- The `!httpResponse.IsSuccessStatusCode` check now runs **inside** the `_pipeline.ExecuteAsync` delegate (`SerpApiWebSearchClient.cs:61-76`), throwing `HttpRequestException` with `StatusCode` set before the delegate returns — the pipeline can now observe and retry 429/503, matching `AnthropicChatClient.cs:84-111` structurally (inline status check → log → throw with `StatusCode`, same message shape, same "no URL in the log" discipline).
- The dead-code branches (`TooManyRequests`, `ServiceUnavailable`) in `ShouldHandle` are now reachable — proven by new tests, not just asserted.
- The old post-hoc `response.EnsureSuccessStatusCode()` call is removed entirely, not left as redundant dead code.
- Retry policy values (`MaxRetryAttempts = 3`, exponential backoff, base delay 2s in production) are unchanged — confirms the fix is scoped to *where* the check runs, not a policy redesign, as required by the plan's explicit out-of-scope list.
- The "never log the URL, it contains the API key" constraint (comment at line 56) is respected by the new error-path log line, which logs status + body only.

## Test coverage vs. acceptance criteria

All four FR-1/FR-2/FR-3 acceptance criteria have a corresponding test, and I confirmed they pass:

1. 429 → 429 → 200 succeeds, handler called exactly 3 times (`SearchAsync_RetriesOn429_ThenSucceeds`).
2. 503 exhausts retries, throws with `StatusCode == ServiceUnavailable`, handler called 4 times (`SearchAsync_RetriesOn503_ThenThrowsAfterExhaustingRetries`).
3. 400 fails fast, handler called exactly once — proves non-retriable statuses aren't accidentally retried (`SearchAsync_DoesNotRetryOn400`).
4. Transport-level exception (no `StatusCode`) still retries to exhaustion — proves the pre-existing null-status branch survives the restructuring (`SearchAsync_RetriesOnTransportLevelFailure`).

FR-4 (no regression to the success path) holds: the three original tests are untouched in the diff and pass.

## Scope / architecture

- No DI registration changes were needed or made — confirmed the optional `ResiliencePipeline? pipeline = null` constructor parameter is additive and the container correctly resolves it to `null` → `DefaultPipeline`, exactly as `AnthropicChatClient` already does in this module.
- No interface, DTO, or data-model changes — matches the "no data model changes" claim in plan/design.
- Test-only additions (`InstantRetryPipeline`, a handler-mock `CreateHttpClientFactory` overload, 4 new facts) are scoped to the one test file for the adapter under fix; no unrelated files touched.

## Minor, non-blocking observation

`SearchAsync_RetriesOn503_ThenThrowsAfterExhaustingRetries` and `SearchAsync_RetriesOnTransportLevelFailure` don't assert on `_logger` calls, so the new `LogError` line is exercised but not directly asserted. This is a coverage nicety, not a gap against any stated acceptance criterion — the finding didn't ask for logging assertions, and the behavior that matters (retry count, final exception, status code) is fully covered. Not a blocker.

## Outcome

Implementation is a faithful, correctly-scoped, verified fix for the exact defect described in the finding, following the plan and design without deviation. No functional requirement is unmet, no architectural conflict, all required tests present and passing, no correctness bugs found.
