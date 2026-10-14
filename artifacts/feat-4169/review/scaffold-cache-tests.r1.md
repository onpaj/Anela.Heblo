# Code Review: scaffold-cache-tests

## Summary
The new test file matches the task-context specification verbatim, wires the
mock chain the way `SmartsuppAgentCache` actually consumes
`IServiceScopeFactory`, and both tests were confirmed to pass against the
real, unmodified `SmartsuppAgentCache` implementation. No production code
was touched.

## Review Result: PASS

### task: scaffold-cache-tests
**Status:** PASS

Verified against `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppAgentCache.cs`:
- Null-`Name` filtering test matches the production `.Where(a => a.Name is not null).ToDictionary(a => a.Id, a => a.Name!)` logic exactly (agent-2 with `Name = null` correctly excluded from the expected dictionary).
- TTL fast-path test matches the production `_cache is not null && DateTime.UtcNow - _cachedAt < CacheTtl` short-circuit — a second call within the 1-hour TTL is asserted to hit `GetAgentsAsync` only once.
- The `BuildScopeFactory()` mock chain (`IServiceScopeFactory.CreateScope() -> IServiceScope.ServiceProvider -> IServiceProvider.GetService(typeof(ISmartsuppApiClient))`) matches how `GetCacheAsync` actually resolves the dependency (`scope.ServiceProvider.GetRequiredService<ISmartsuppApiClient>()`).
- Test run confirms `Passed! - Failed: 0, Passed: 2, Skipped: 0` for both `GetAgentNamesAsync_SuccessfulFetch_ExcludesAgentsWithNullName` and `GetAgentNamesAsync_CalledTwiceWithinTtl_OnlyFetchesFromApiOnce`.
- File is created at the exact path the task context specified, no other files touched.
- The `ExpireCache()` helper (unused by this task's own two tests) is dead code for now, but is explicitly scoped by the task context as shared infrastructure for the two follow-up tasks (`cold-cache-failure-test`, `warm-cache-failure-tests`) that reuse this same file — not a defect.

## Docs to Update
(none — test-only addition, no public behavior or docs affected)

## Overall Notes
No blocking issues. This is the first of three tasks in the plan; the actual
coverage-gap tests (API-failure fallback behavior) are added by the next two
tasks.
