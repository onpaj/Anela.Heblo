## Module / File
`backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs`

## Coverage
Line coverage: 3.8% (6/157 lines — filter threshold: 60%)

## What's not tested
**`ParseSummaryJson` (public static)** — this method is absent from `PlaudCliClientParserTests.cs` despite `ParseFilesOutput` and `ParseFileDetail` being tested there. Specifically uncovered:
- The `header.headline` path — when the field is missing, `TryGetProperty` returns a default, yielding an empty headline rather than an error.
- The `ai_content` missing-field path.
- The `JsonException` catch branch: when the JSON is malformed, the method falls back to returning `(string.Empty, rawJson)`. This silent fallback is never asserted.

**`RunCliAsync` AUTH_FAILED retry** — the token-refresh-and-retry sequence has three untested branches:
1. `RefreshAsync` itself throws → a new `PlaudAuthExpiredException("token refresh failed", ex)` is raised; nothing asserts this wrapping behavior.
2. Retry after a successful refresh also throws `PlaudAuthExpiredException` → the exception propagates as-is; nothing confirms the retry is not attempted again.
3. `SyncToKeyVaultAsync` after a normal (non-auth) call is best-effort fire-and-forget; a failure there silently swallows the error, and no test verifies this doesn't affect the return value.

## Why it matters
A regression in `ParseSummaryJson`'s fallback would cause meeting-task ingestion to silently return empty summaries. The AUTH_FAILED retry path is the only recovery mechanism for an expired Plaud token; if it wraps the wrong exception type, callers expecting `PlaudAuthExpiredException` would miss it.

## Suggested approach
Extend `PlaudCliClientParserTests.cs` (≈ low effort):
1. `ParseSummaryJson` with valid JSON — verify headline and content extraction.
2. `ParseSummaryJson` with missing `header`/`ai_content` — verify empty strings returned.
3. `ParseSummaryJson` with malformed JSON — verify `(string.Empty, rawJson)` fallback.

For the retry path, mock `IPlaudTokenRefresher` in `PlaudCliClientRunTests.cs`:
4. First call throws `PlaudAuthExpiredException`; `RefreshAsync` throws — verify the wrapping `PlaudAuthExpiredException` is raised.
5. First call throws `PlaudAuthExpiredException`; refresh succeeds; second call also throws — verify exception propagates without infinite retry.

---
_Filed by weekly coverage-gap routine on 2026-07-20. Based on CI run #29525794843 (bba537b141de1dba71a2c6853c4ff3f7e96153b2)._
