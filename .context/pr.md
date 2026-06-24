# PR Context

- **PR**: #3312 — #3300: Eliminate N+1 Graph API calls in GetAppRoleMembersAsync
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3312
- **Branch**: `feature/3300-Arch-Review-Usermanagement-N-1-Graph-Api-Calls-In` → `main`
- **State**: open
- **Author**: onpaj
- **Changes**: +1949 / -14 across 13 files
- **Absorbed**: backmerged with `main`, pushed

## Description

`GraphService.GetAppRoleMembersAsync` resolved display name and email for directly-assigned users with a separate HTTP call per user — an O(N) chattiness problem. For a role with 10 directly-assigned users, this produced 10 sequential Graph round-trips, each 100–300 ms, stacking latency linearly on every cold-cache request.

Replaced the per-user `foreach`/`await` loop (Step 5, lines 385–404) with Microsoft Graph's `$batch` endpoint (`POST /v1.0/$batch`). User IDs are grouped into chunks of up to 20, and each chunk is resolved in a single HTTP call. For the common case of ≤ 20 directly-assigned users, this reduces Step 5 from N calls to 1 call.

- Added `private const int GraphBatchSize = 20;` (hard Graph API limit, matches the existing `SearchResultLimit` constant pattern)
- Sub-response failures (non-200) are logged as `LogWarning` and skipped — same as before
- Batch-level HTTP failure logs `LogError` and returns an empty list — consistent with surrounding error handling
- No public interfaces, caching logic, or API contracts changed

Artifacts committed under `artifacts/feat-3300/`.
