## Module / File
`frontend/src/utils/urlUtils.ts`

## Coverage
Line coverage: 50% (filter threshold: 60%)

## What's not tested
**`createFilteredUrl` — drops non-empty falsy values:**
The filter condition `value !== null && value !== undefined && value !== ''` passes `false` and `0` through. However, no test verifies that `false` or `0` filter values ARE included in the query string. If the condition were widened to a simple truthiness check (`if (value)`), both `false` and `0` would be silently dropped — a common refactor mistake that would strip valid filter values (e.g. `enabled=false`, `page=0`) from API calls.

**`isTileClickable` — requires both `enabled` and `filters`:**
The function returns `true` only when `drillDown.enabled === true` AND `drillDown.filters` is truthy. An empty object `{}` evaluates as truthy, so a tile with `enabled: true, filters: {}` would be considered clickable even with no filters set. No test covers this edge case.

## Why it matters
`createFilteredUrl` is used throughout the app to build API query strings. A regression that drops `false` or `0` filter values would silently send incorrect queries — stock status `0`, boolean `false` flags, or page-zero pagination would all be omitted without any observable error.

## Suggested approach
- Unit tests for `createFilteredUrl`: assert `false` and `0` values appear in the output; assert `null`, `undefined`, and `''` do not.
- Test `isTileClickable` with `{ enabled: true, filters: {} }`, `{ enabled: false, filters: { x: 1 } }`, and `{ enabled: true }` (no filters property). ~0.5 day effort.

---
_Filed by weekly coverage-gap routine on 2026-07-06. Based on CI run #28716987459 (2ad2a2593e1834798a3def9ac2551b46c2e595cb)._
