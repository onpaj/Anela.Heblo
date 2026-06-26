## Module
OrgChart

## Finding
`frontend/src/pages/OrgChartPage.tsx` (512 lines) holds at least six distinct concerns in a single component:

| Concern | Location |
|---------|----------|
| Hierarchy calculation | `calculateLevels` (lines 36–65) |
| Parent-chain traversal | `getAllParentPositionIds` (lines 161–175) |
| Filter logic | `filteredPositions` IIFE (lines 178–209) |
| Tree root detection | `buildTree` / `getChildren` (lines 238–253) |
| DOM geometry measurement | `getElementPosition` + `useEffect` (lines 82–132) |
| SVG line rendering | `renderConnections` (lines 361–404) |
| Card rendering | `renderPositionCard` (lines 256–358) |

The tree algorithms (`calculateLevels`, `buildTree`, `getAllParentPositionIds`) are pure functions with no React dependencies — they operate on plain data and have deterministic outputs. They are currently untestable without mounting the full page component.

`renderPositionCard` is a 100-line recursive render function defined inside the page, making it impossible to snapshot-test or reuse in isolation.

## Why it matters
Violates Single Responsibility: any change to filtering logic, SVG positioning, or card appearance requires reading 512 lines of mixed concerns to find the right spot. The pure tree functions can't be unit-tested without a full React render environment. Adding a new filter type or a second card style means editing an already large file.

## Suggested fix
Two targeted extractions — no architectural overhaul needed:

1. Move the four pure functions (`calculateLevels`, `getAllParentPositionIds`, `buildTree`, `getChildren`) into `frontend/src/pages/orgChartUtils.ts`. They have no React or hook dependencies and move verbatim.

2. Extract `renderPositionCard` into a `PositionCard.tsx` sub-component in `frontend/src/components/OrgChart/PositionCard.tsx`, accepting `position`, `children`, and `getLevelColor` as props.

The page component shrinks to ~150 lines focused on data fetching, filter state, and layout wiring.

---
_Filed by daily arch-review routine on 2026-05-19._