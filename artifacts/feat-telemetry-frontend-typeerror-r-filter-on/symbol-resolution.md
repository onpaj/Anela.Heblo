# Symbol Resolution Report: `r.filter is not a function` Error

## Error Context
- **Error**: `TypeError: r.filter is not a function`
- **Minified Symbol**: `Yq` (found in bundle)
- **Trigger**: SPA root path `/`, Dashboard initial render
- **Browser**: Chrome 148.0
- **Date**: 2026-06-12

## Build Details
- **Build Command**: `GENERATE_SOURCEMAP=true npm run build`
- **Bundle Hash**: `main.745fb5e3.js`
- **Bundle Size**: 1.21 MB (gzipped)
- **Sourcemap Size**: 20 MB (uncompressed)
- **CRA Version**: react-scripts 5.0.1
- **Node Version**: 20 (Alpine)

## Symbol Resolution Findings

### Direct `Yq` Symbol Lookup
**Result**: Found 2 occurrences on line 2 of minified bundle

#### Occurrence 1
- **Minified Position**: Line 2, Column 2505291
- **Source File**: `components/dashboard/SizeBadge.tsx`
- **Source Line**: 8
- **Source Column**: 6
- **Original Identifier**: `sizeColors`
- **Type**: `Record<string, string>` (object literal with color CSS classes)
- **Context**: Color mapping object for size badges, not an array

#### Occurrence 2
- **Minified Position**: Line 2, Column 2505451
- **Source File**: `components/dashboard/SizeBadge.tsx`
- **Source Line**: 17
- **Source Column**: 21
- **Original Identifier**: `sizeColors` (lookup access)
- **Type**: Record property access `sizeColors[size]`
- **Context**: Used to retrieve color class for badge

### Analysis
The minified symbol `Yq` does NOT correspond to a `.filter()` call site. Instead, it represents the `sizeColors` object in `SizeBadge.tsx`, which is:
1. **Not an array** - it's a Record/object literal
2. **Not directly involved in the error** - SizeBadge is used to render tile size labels, not to process tile arrays

## Investigation Strategy

Since `Yq` doesn't map to the actual `.filter()` call, the error likely originates from:

1. **Dashboard.tsx:44** — `allTileData.filter(...)` 
   - Processes the main tile array
   - Minified variable: Unknown (need reverse lookup)
   - **Risk**: If `allTileData` is undefined/null instead of array

2. **DashboardSettings.tsx** — `userSettings?.tiles.filter(...)`
   - Filters user settings tiles
   - Minified variable: Unknown (need reverse lookup)
   - **Risk**: If `tiles` property is not an array after recent PRs

3. **JournalList.tsx:~202** — entries derivation
   - Likely filtering/reducing data
   - **Risk**: If data shape changed in recent PRs

## Root Cause Assessment

The error `r.filter is not a function` indicates that **a variable meant to be an array is not an array**. Given that:

1. Three PRs were merged same day:
   - PR #2962: "open dashboard to all users with per-tile permission enforcement"
   - PR #2943: "Move Journal Search Presentation Logic to Frontend"
   - PR #2948: "Remove Manual refetch Calls from JournalList"

2. The error fires on Dashboard initial render (`/` route)

3. Sourcemap resolution of `Yq` (sizeColors) is a false lead

**Most likely cause**: One of the PRs changed the API response structure or data transformation in a way that causes `allTileData` (or `userSettings.tiles`) to be passed as something other than an array to `.filter()`.

## Recommended Next Steps

1. **Array Guard Tests** — Write regression tests that verify:
   - `allTileData` is an array before calling `.filter()`
   - `userSettings?.tiles` is an array before filtering
   - `entries` in JournalList is an array

2. **PR Review Focus** — Check diffs for:
   - Changes to API response structure
   - Changes to hook return types (useTileData, useUserDashboardSettings, etc.)
   - Introduction of destructuring that might return undefined

3. **Data Validation** — Add runtime checks:
   - Use `Array.isArray()` before calling array methods
   - Log the actual type/value when it fails
   - Emit detailed error context to Application Insights

## Files to Audit
- `frontend/src/components/pages/Dashboard.tsx` (line 44: filter call)
- `frontend/src/components/dashboard/DashboardSettings.tsx` (filter on tiles)
- `frontend/src/components/pages/Journal/JournalList.tsx` (line ~202)
- `frontend/src/api/hooks/useDashboard.ts` (data transformation)
- `frontend/src/api/hooks/useHealth.ts` (hook implementation)

## Resolution Method
- **Tool**: Local sourcemap build with `GENERATE_SOURCEMAP=true`
- **Library**: Node.js `source-map` module (built-in to react-scripts)
- **Reverse**: Manual inspection + programmatic SourceMapConsumer lookup
- **Verification**: Sourcemap correctly maps symbols back to original source files
