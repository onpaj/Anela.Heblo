# Specification: Move `getLevelColor` from `OrgChartPage` into `PositionCard`

## Summary
`getLevelColor` is a pure, unvarying presentation helper that maps a hierarchy level to a Tailwind border-color class. It currently lives in `OrgChartPage.tsx` and is prop-drilled into `PositionCard`, its only consumer. This change relocates the function into `PositionCard.tsx`, removes it from `PositionCardProps`, and removes the now-unused prop and definition from `OrgChartPage.tsx`.

## Background
This is an architecture-review finding (module: OrgChart, filed 2026-07-16) flagging a Single-Responsibility / cohesion violation: `OrgChartPage` should own data fetching and layout, while an individual card's color scheme is `PositionCard`'s own concern. The function is never overridden or parameterized differently by any caller, so the prop only adds surface area — it widens `PositionCardProps`, forces `PositionCard`'s tests to supply a stub, and requires readers of `PositionCard` to jump to the page to understand how its own border color is determined.

## Functional Requirements

### FR-1: Relocate `getLevelColor` into `PositionCard.tsx`
Move the function currently defined at `frontend/src/pages/OrgChartPage.tsx:168-181` into `frontend/src/components/OrgChart/PositionCard.tsx` as a module-level (non-exported) helper, with an explicit `number => string` signature and identical switch/case behavior (levels 1-4 map to red/orange/yellow/green respectively, any other level falls back to gray):

```typescript
const getLevelColor = (level: number): string => {
  switch (level) {
    case 1: return 'border-l-4 border-red-500';
    case 2: return 'border-l-4 border-orange-500';
    case 3: return 'border-l-4 border-yellow-500';
    case 4: return 'border-l-4 border-green-500';
    default: return 'border-l-4 border-gray-500';
  }
};
```

**Acceptance criteria:**
- `getLevelColor` is defined inside `PositionCard.tsx` and is not exported.
- For inputs 1, 2, 3, 4, and any other number (e.g. 0, 5, undefined-defaulted), the returned class string is byte-for-byte identical to the pre-change implementation.
- The component body calls the local `getLevelColor(position.level ?? 1)` in place of the current prop invocation (`PositionCard.tsx:27-29`), preserving the `?? 1` default-level fallback.

### FR-2: Remove `getLevelColor` from `PositionCardProps` and the component signature
`PositionCardProps` (`PositionCard.tsx:4-8`) no longer declares `getLevelColor`, and the destructured function parameter (`PositionCard.tsx:19`) no longer includes it.

**Acceptance criteria:**
- `PositionCardProps` contains only `position` and `getChildren`.
- The recursive self-render of `PositionCard` (`PositionCard.tsx:119-124`) no longer passes a `getLevelColor` prop.
- No `getLevelColor` prop appears anywhere in `PositionCard.tsx`'s public interface or JSX.

### FR-3: Remove `getLevelColor` and its call-site argument from `OrgChartPage.tsx`
Delete the function definition at `OrgChartPage.tsx:168-181` and remove the `getLevelColor={getLevelColor}` prop from the `<PositionCard>` invocation at `OrgChartPage.tsx:333-338`.

**Acceptance criteria:**
- `OrgChartPage.tsx` no longer defines or references `getLevelColor` anywhere.
- The `<PositionCard>` JSX call site passes only `position` and `getChildren`.
- No other behavior of `OrgChartPage` changes (data fetching, filtering, zoom, connection-line rendering are untouched).

### FR-4: Update existing `PositionCard` unit tests
`frontend/src/components/OrgChart/__tests__/PositionCard.test.tsx` currently defines a `stubLevelColor` helper and passes it as a `getLevelColor` prop in both test cases (lines 21, 40, 76). Since the prop no longer exists, the stub and both prop usages must be removed, and the tests must exercise the component's real (now-internal) color logic.

**Acceptance criteria:**
- `stubLevelColor` and all `getLevelColor={...}` props are removed from the test file.
- Existing snapshots in `frontend/src/components/OrgChart/__tests__/__snapshots__/PositionCard.test.tsx.snap` are regenerated to reflect the real border classes (e.g. `border-red-500` for level 1, `border-l-4 border-orange-500`... for level 2 in the child-rendering test) instead of the stub's `level-${level}` classes.
- Both existing test cases (`renders a leaf position...`, `renders a recursive position with one child`) pass unmodified in structure, only with the stub/prop removed.

## Non-Functional Requirements

### NFR-1: Behavioral equivalence
This is a pure refactor: rendered DOM output, CSS classes, and component behavior for all existing call sites must be identical before and after the change (aside from the test snapshot update required by FR-4, which reflects the tests now using real logic instead of a stub).

### NFR-2: No new dependencies or public API changes
No new npm packages, no changes to `PositionDto` or other API-generated types, and no changes to how `OrgChartPage` is consumed by any other module.

## Data Model
Not applicable — no data model changes. `getLevelColor` operates only on the `level: number` field already present on `PositionDto`/`Position`.

## API / Interface Design
- **Before:** `PositionCardProps = { position, getChildren, getLevelColor }`
- **After:** `PositionCardProps = { position, getChildren }`

No backend, HTTP, or MediatR changes. This is a frontend-only, intra-module refactor confined to:
- `frontend/src/components/OrgChart/PositionCard.tsx`
- `frontend/src/pages/OrgChartPage.tsx`
- `frontend/src/components/OrgChart/__tests__/PositionCard.test.tsx`
- `frontend/src/components/OrgChart/__tests__/__snapshots__/PositionCard.test.tsx.snap` (regenerated, not hand-edited)

## Dependencies
None. No other file references `getLevelColor` (confirmed via repo-wide search — the only occurrences are in `PositionCard.tsx`, `OrgChartPage.tsx`, and `PositionCard.test.tsx`).

## Out of Scope
- Extracting `getLevelColor` into a separate co-located `levelColors.ts` file (mentioned in the brief as a future option if theming is later needed) — not part of this change.
- Any change to the actual color mapping, level thresholds, or visual design of position cards.
- Any change to `OrgChartPage`'s other responsibilities (filtering, zoom, connection-line rendering, data fetching).
- Broader test coverage additions beyond adjusting the two existing tests and their snapshot for the prop removal.

## Open Questions
None.

## Status: COMPLETE
