# Implementation: cold-cache-failure-test

## What was implemented

Added the FR-1 test case to `SmartsuppAgentCacheTests`: when the cache has
never been successfully populated and `ISmartsuppApiClient.GetAgentsAsync`
throws, `GetAgentNamesAsync` must return a non-null, empty dictionary
without rethrowing.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAgentCacheTests.cs` — appended
  `GetAgentNamesAsync_ColdCacheApiThrows_ReturnsEmptyDictionaryWithoutThrowing`, exactly as
  specified in the task context.

## Tests

- `GetAgentNamesAsync_ColdCacheApiThrows_ReturnsEmptyDictionaryWithoutThrowing` — mocks
  `ISmartsuppApiClient.GetAgentsAsync` to throw `HttpRequestException` on a cold
  (never-populated) cache, and asserts `GetAgentNamesAsync()` returns a non-null, empty
  dictionary instead of throwing.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SmartsuppAgentCacheTests.GetAgentNamesAsync_ColdCacheApiThrows_ReturnsEmptyDictionaryWithoutThrowing"
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SmartsuppAgentCacheTests"
```

Both runs confirmed locally:
- Single-test filter: `Passed! - Failed: 0, Passed: 1, Skipped: 0`
- Full-file filter: `Passed! - Failed: 0, Passed: 3, Skipped: 0`

## Notes

Implemented verbatim from the task-context file's Step 1 code snippet — no deviations.
`HttpRequestException` resolved without an extra `using System.Net.Http;` thanks to .NET 8
implicit usings, as anticipated in the task context.

## PR Summary
Adds unit test coverage for `SmartsuppAgentCache`'s cold-cache API-failure fallback path
(FR-1): when the Smartsupp API throws before any successful fetch, the cache must return an
empty dictionary rather than propagating the exception.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAgentCacheTests.cs` — added
  `GetAgentNamesAsync_ColdCacheApiThrows_ReturnsEmptyDictionaryWithoutThrowing`

## Status
DONE
