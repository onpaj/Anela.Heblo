## Module / File
`backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ClearFlagOverride/ClearFlagOverrideHandler.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)
No test file exists for this handler.

## What's not tested
The handler has two paths:
1. **Not found** — `_repo.DeleteAsync` returns `false`; handler returns `ResourceNotFound` without touching the cache.
2. **Deleted** — `_repo.DeleteAsync` returns `true`; handler calls `_cache.Remove(HebloFeatureProvider.CacheKey)` and returns a success response.

The critical invariant — **cache must be invalidated when the override is deleted** — is not asserted anywhere. Neither is the inverse: **cache must NOT be touched when the record does not exist**.

## Why it matters
If the cache-remove call is accidentally dropped (e.g. during a refactor of `HebloFeatureProvider.CacheKey`), a deleted feature-flag override remains effective for operators until the memory-cache TTL expires. This means a flag that was supposed to be restored to its global default keeps forcing the overridden value, silently affecting feature availability. The not-found path is equally important: incorrectly clearing the cache on a missing record causes an unnecessary full re-evaluation on the next request.

## Suggested approach
Unit tests with mocked `IFeatureFlagOverrideRepository` and `IMemoryCache`:
- **Not found**: `DeleteAsync` returns `false` → response carries `ResourceNotFound`, `cache.Remove` is never called.
- **Deleted**: `DeleteAsync` returns `true` → response is success, `cache.Remove` is called exactly once with `HebloFeatureProvider.CacheKey`.

Effort: small — two test cases, Moq for both dependencies.

---
_Filed by weekly coverage-gap routine on 2026-09-07. Based on CI run #33791274852 (a21f134808e61a338c3261f8523316d2752ebea3)._
