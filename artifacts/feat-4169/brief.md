## Module / File
`backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppAgentCache.cs`

## Coverage
Line coverage: 20.6% (filter threshold: 60%)

## What's not tested
`SmartsuppAgentCache` uses double-checked locking with a TTL. When the API call fails, the `catch` block returns `_cache` if it is non-null (stale data) or `new Dictionary<>()` if `_cache` is null. Neither fallback variant is covered by tests: no test exercises the "API fails while cache is warm" path (should return stale data) or the "API fails before any successful call" path (should return an empty dictionary). The interaction between TTL expiry and a concurrent API failure is also unverified.

## Why it matters
Callers treat the returned dictionary as authoritative. If the stale-vs-empty distinction silently regresses — e.g. the null check is inverted — all Smartsupp agent lookups will return empty while the API is degraded, surfacing as "agent not found" errors that are hard to trace.

## Suggested approach
Unit tests with a mock `ISmartsuppApiClient`: (1) API throws, cache never populated → returns empty dict; (2) API succeeds once, then throws → returns stale cached dict; (3) TTL expires, API throws → returns stale dict (not refreshed). Estimated effort: ~2h.

---
_Filed by weekly coverage-gap routine on 2026-09-14. Based on CI run #34699120372 (722ec6efc4c1f7e5db235606c763a2f2b4a9a374)._