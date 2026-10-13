# Implementation: warm-cache-failure-tests

## What was implemented

Added the FR-2 (single warm-then-fail) and FR-3 (repeated warm-then-fail) test cases to
`SmartsuppAgentCacheTests`, using the `ExpireCache` reflection helper from `scaffold-cache-tests`
to force the TTL fast-path to be bypassed between calls so each subsequent call re-enters the
refresh/catch path.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAgentCacheTests.cs` — appended
  `GetAgentNamesAsync_WarmCacheThenApiThrows_ReturnsStaleDictionary` and
  `GetAgentNamesAsync_RepeatedFailuresAfterWarmSuccess_KeepsReturningOriginalStaleDictionary`,
  exactly as specified in the task context.

## Tests

- `GetAgentNamesAsync_WarmCacheThenApiThrows_ReturnsStaleDictionary` — warms the cache with one
  successful fetch, expires `_cachedAt` via reflection, then has the API throw on the second
  call; asserts the second call returns the same (stale) dictionary as the first, not an empty
  one, and that the API was called exactly twice.
- `GetAgentNamesAsync_RepeatedFailuresAfterWarmSuccess_KeepsReturningOriginalStaleDictionary` —
  one successful warm fetch followed by two forced-expiry API failures; asserts every call keeps
  returning the original successful payload and the API was called exactly three times.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SmartsuppAgentCacheTests.GetAgentNamesAsync_WarmCacheThenApiThrows_ReturnsStaleDictionary"
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SmartsuppAgentCacheTests.GetAgentNamesAsync_RepeatedFailuresAfterWarmSuccess_KeepsReturningOriginalStaleDictionary"
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SmartsuppAgentCacheTests"
```

All runs confirmed locally:
- FR-2 single-test filter: `Passed! - Failed: 0, Passed: 1, Skipped: 0`
- FR-3 single-test filter: `Passed! - Failed: 0, Passed: 1, Skipped: 0`
- Full-file filter: `Passed! - Failed: 0, Passed: 5, Skipped: 0` (all tests from `scaffold-cache-tests`,
  `cold-cache-failure-test`, and this task)

Also ran, per the task's Step 6–8:
- `dotnet build` (from repo root, `Anela.Heblo.sln` — `backend/` alone has no project/solution
  file, so the literal `cd backend && dotnet build` in the task text doesn't resolve; built the
  solution from the repo root instead): 0 errors, 91 pre-existing warnings unrelated to this change.
- `dotnet format Anela.Heblo.sln --verify-no-changes --include backend/test/.../SmartsuppAgentCacheTests.cs`:
  exit 0, no formatting violations.
- `dotnet test backend/test/Anela.Heblo.Tests/` (full suite): `Failed: 110, Passed: 7146, Skipped: 4,
  Total: 7260`. All 110 failures are pre-existing `PostgresSharedContainerFixture` /
  Testcontainers failures ("Docker is either not running or misconfigured") across unrelated
  integration test classes (`ArticleRepositoryFeedbackProjectionSqlTests`,
  `SmartsuppPresenceRepositoryIntegrationTests`, `SmartsuppRepositoryUpsertIntegrationTests`,
  `LeafletRepositoryIntegrationTests`, `KnowledgeBaseRepositoryIntegrationTests`, etc.) — this
  sandbox has no Docker daemon. Verified: the count of "Docker is either not running or
  misconfigured" occurrences in the full log equals the failed-test count (110), and no failure
  is attributable to `SmartsuppAgentCache` or this change. This is a pre-existing environment
  limitation, not a regression.
- Step 7 (informational coverage check): `dotnet test /p:CollectCoverage=true
  /p:CoverageReporter=html` ran but produced no coverage report — this test project references
  `coverlet.collector` (invoked via `--collect:"XPlat Code Coverage"`), not `coverlet.msbuild`
  (which is what `/p:CollectCoverage=true` targets), so the MSBuild property is silently a
  no-op here and the tests just ran normally (5/5 passed). Per the task text this step is
  explicitly informational/non-blocking, so this was not pursued further.

## Notes

Both test methods implemented verbatim from the task-context file's Step 1 and Step 3 code
snippets — no deviations. No production code changed.

## PR Summary
Adds unit test coverage for `SmartsuppAgentCache`'s warm-cache API-failure fallback paths
(FR-2, FR-3): once the cache has been successfully populated at least once, a subsequent API
failure (single or repeated) must keep returning the last successfully-fetched dictionary
rather than throwing or clearing the cache.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAgentCacheTests.cs` — added
  `GetAgentNamesAsync_WarmCacheThenApiThrows_ReturnsStaleDictionary` and
  `GetAgentNamesAsync_RepeatedFailuresAfterWarmSuccess_KeepsReturningOriginalStaleDictionary`

## Status
DONE
