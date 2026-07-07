# Design: Unit test coverage for `urlUtils.ts` (`createFilteredUrl` and `isTileClickable`)

## Component Design

No production components are created or modified. The sole deliverable is one new test module.

### `frontend/src/utils/__tests__/urlUtils.test.ts` (new)

- **Responsibility**: Exercise the existing, unmodified pure functions `createFilteredUrl` and `isTileClickable` from `../urlUtils` against the edge cases identified in the spec, closing the coverage gap without touching source behavior.
- **Location convention**: `__tests__/` subdirectory, matching every other sibling test in `frontend/src/utils/` (`dateUtils.test.ts`, `downloadTextFile.test.ts`, `errorHandler.test.ts`, `sharepointLink.test.ts`) — per the architecture review's Decision 1, this supersedes the spec's literal colocated-path wording.
- **Structure**: Two top-level `describe` blocks, one per function, each containing one `it(...)` per acceptance criterion:
  - `describe('createFilteredUrl', ...)`
  - `describe('isTileClickable', ...)`
- **Dependencies/imports**:
  ```typescript
  import { createFilteredUrl, isTileClickable } from '../urlUtils';
  ```
  No RTL, no MSW, no mocks, no test fixtures/factories — both functions are synchronous, side-effect-free, and take plain object literals as input.
- **Non-goals**: No changes to `urlUtils.ts` itself; no tests added for `getTileTooltip` (out of scope per spec); no changes to CI coverage threshold configuration.

### Test cases to implement

**`createFilteredUrl(baseUrl, filters)`** — one `it` per case:
1. `{ enabled: false }` → output contains `enabled=false`.
2. `{ page: 0 }` → output contains `page=0`.
3. `{ a: null }` → `a` excluded from output.
4. `{ a: undefined }` → `a` excluded from output.
5. `{ a: '' }` → `a` excluded from output.
6. All values excluded (or empty filters object) → returns `baseUrl` unchanged, no trailing `?`.
7. At least one valid filter present → returned string has the form `${baseUrl}?${queryString}`.
8. Mixed object combining includable (`false`, `0`, non-empty string) and excludable (`null`, `undefined`, `''`) values → only includable ones appear in output.

**`isTileClickable(tileData)`** — one `it` per case:
1. `{ drillDown: { enabled: true, filters: {} } }` → `true` (documents current behavior: empty-but-present `filters` is truthy).
2. `{ drillDown: { enabled: false, filters: { x: 1 } } }` → `false` (non-empty filters do not override `enabled: false`).
3. `{ drillDown: { enabled: true } }` (no `filters`) → `false`.
4. `{}` (no `drillDown`) → `false`.
5. `{ drillDown: { enabled: true, filters: { x: 1 } } }` → `true` (baseline positive case).

### Assertion strategy

- Single-filter cases (`createFilteredUrl` cases 1–5): exact-string or `toContain`/`not.toContain` assertions against the returned string, since param order is deterministic for one key.
- Whole-URL-shape cases (6–7): exact equality against `baseUrl` (no `?`) and a `${baseUrl}?...` pattern check, respectively.
- Mixed case (8): `toContain` for each includable `key=value` pair, `not.toContain` for each excludable key, avoiding brittleness from `URLSearchParams` ordering assumptions across multiple keys.
- `isTileClickable` cases: `toBe(true)` / `toBe(false)` boolean equality.

## Data Schemas

No database, API, or event schema changes. Tests exercise only the existing TypeScript interfaces already exported from `frontend/src/utils/urlUtils.ts`, unmodified:

```typescript
export interface DrillDownInfo {
  filters?: Record<string, any>;
  enabled: boolean;
  tooltip?: string;
}

export interface TileDataWithDrillDown {
  status?: string;
  data?: { count?: number; [key: string]: any };
  error?: string;
  drillDown?: DrillDownInfo;
  [key: string]: any;
}
```

Function signatures under test (unmodified, referenced for contract clarity only):

```typescript
export const createFilteredUrl = (baseUrl: string, filters: Record<string, any>): string => ...
export const isTileClickable = (tileData: TileDataWithDrillDown): boolean => ...
```

Test input fixtures are plain inline object literals conforming to the above shapes (e.g. `{ enabled: false }`, `{ page: 0 }`, `{ a: null, b: undefined, c: '' }` for `createFilteredUrl`; partial/complete `TileDataWithDrillDown` literals for `isTileClickable`) — no factory functions or shared fixture modules are introduced, consistent with sibling test files in `__tests__/`.
