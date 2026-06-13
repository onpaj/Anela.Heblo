# Telemetry Frontend TypeError r-filter-on Audit Notes

## JournalList audit

Array method calls found in `frontend/src/components/pages/Journal/JournalList.tsx`:

### Line 77: tags.slice() + map()
- **File:** JournalList.tsx:77
- **Variable:** `tags`
- **Method:** `slice(0, 2).map(...)`
- **Guard kind:** Guarded with conditional rendering (`{tags && tags.slice(...)`)
- **Context:** In JournalRow component, tags is a prop with optional array type
- **Verdict:** SAFE — Guarded by logical AND before use

### Line 99-100: associatedProducts.slice() + map()
- **File:** JournalList.tsx:99-100
- **Variable:** `associatedProducts`
- **Method:** `?.slice(0, 2).map(...)`
- **Guard kind:** Optional chaining (`?.slice()`) + logical AND check before rendering
- **Context:** In JournalRow component, associatedProducts is a prop with optional array type
- **Verdict:** SAFE — Protected by optional chaining

### Line 202: entries from OR pattern
- **File:** JournalList.tsx:202
- **Variable:** `entries`
- **Assignment:** `const entries = currentQuery.data?.entries || []`
- **Guard kind:** Fallback to empty array (`|| []`)
- **Context:** currentQuery.data is nullable, entries is extracted with optional chaining and OR guard
- **Verdict:** SAFE — entries is guaranteed to be an array (either from data.entries or empty array)

### Line 422: entries.map() for SearchJournalEntryDto
- **File:** JournalList.tsx:422
- **Variable:** `entries` (cast to SearchJournalEntryDto[])
- **Method:** `map(...)`
- **Guard kind:** Type assertion `(entries as SearchJournalEntryDto[])`
- **Context:** entries is already guarded at line 202; render is conditional inside `entries.length === 0 ? ... : ...`
- **Verdict:** SAFE — entries is guaranteed to be array from line 202 guard, type assertion is correct for ternary branch

### Line 435: entries.map() for JournalEntryDto
- **File:** JournalList.tsx:435
- **Variable:** `entries` (cast to JournalEntryDto[])
- **Method:** `map(...)`
- **Guard kind:** Type assertion `(entries as JournalEntryDto[])`
- **Context:** entries is already guarded at line 202; render is conditional inside `entries.length === 0 ? ... : ...`
- **Verdict:** SAFE — entries is guaranteed to be array from line 202 guard, type assertion is correct for alternate branch

## Summary

**All array method calls are SAFE.** No fixes required.

---

## FR-3 audit results

### PR file lists

PR #2962: frontend/src/api/generated/api-client.ts, frontend/src/api/hooks/useDashboard.ts, frontend/src/components/dashboard/tiles/TileContent.tsx, frontend/src/components/dashboard/tiles/UnauthorizedTile.tsx, frontend/src/components/pages/Dashboard.tsx

PR #2943: frontend/src/components/catalog/detail/tabs/JournalTab.tsx, frontend/src/components/pages/Journal/JournalList.tsx, frontend/src/components/pages/Journal/journalPreview.ts

PR #2948: frontend/src/components/pages/Journal/JournalList.tsx

### Sites reviewed

- Dashboard.tsx — `userSettings.tiles.reduce(...)`, `allTileData.filter(...)`, `.sort(...)` — fixed in Task 3
- DashboardSettings.tsx — `userSettings?.tiles.filter(...)`, `.find(...)` (×2), `availableTiles.filter(...)` (×2), `.length` (×2) — fixed in Task 4
- JournalList.tsx — `entries.map(...)` ×2 — audited in Task 5, SAFE (`entries = data?.entries || []` on line 202)
- JournalTab.tsx:30 — `data?.entries || []` — SAFE (|| [] guard)
- JournalTab.tsx:88 — `entries.map(...)` — SAFE (entries guarded at line 30)
- JournalTab.tsx:112 — `entry.tags.map(...)` — SAFE (guarded by `entry.tags && entry.tags.length > 0` on line 110)
- useDashboard.ts — no array-method calls in changed hunks
- TileContent.tsx — no array-method calls in changed hunks
- UnauthorizedTile.tsx — no array-method calls in changed hunks
- journalPreview.ts — no array-method calls in changed hunks
- api-client.ts — generated file, no array-method calls in changed hunks

### Sites fixed

None — all sites already safe or covered by Tasks 3/4/5.

### Sites left as-is with rationale

- JournalTab.tsx:88 — `entries.map(...)` — receiver is `entries = data?.entries || []`, guaranteed array
- JournalTab.tsx:112 — `entry.tags.map(...)` — guarded by truthiness check on line 110 (`entry.tags && entry.tags.length > 0`)
- JournalList.tsx:422,435 — `entries.map(...)` — receiver guarded at line 202

The critical guard is at line 202:
```typescript
const entries = currentQuery.data?.entries || [];
```

This pattern ensures `entries` is always an array (either the data or an empty array), making all downstream `.map()` calls safe regardless of shape drift in the React Query response.

The component properly follows the established pattern:
- Guards at the React Query consumer boundary (line 202)
- Subsequent array method calls operate on the guarded local variable
- Optional chaining used for nested optional arrays (tags, associatedProducts)
