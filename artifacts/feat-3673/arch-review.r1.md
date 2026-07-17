# Architecture Review: Move `getLevelColor` from `OrgChartPage` into `PositionCard`

## Skip Design: true

No new or changed UI/UX. Verified against the actual source: the border-color classes returned by `getLevelColor` (`frontend/src/pages/OrgChartPage.tsx:168-181`) are moved verbatim into `frontend/src/components/OrgChart/PositionCard.tsx`; rendered DOM and Tailwind classes are byte-for-byte identical before and after. `docs/design/ui_design_document.md` and `docs/design/layout_definition.md` govern visual design decisions and component styling choices — neither applies here since no styling, layout, or visual behavior changes. This is an internal code-organization change only.

## Architectural Fit Assessment

This aligns cleanly with existing frontend conventions in `docs/architecture/filesystem.md`: components live under `frontend/src/components/` with co-located `__tests__/`, and are expected to own their own presentation logic. `PositionCard` (`frontend/src/components/OrgChart/PositionCard.tsx`) is a self-contained recursive tree-node component; `OrgChartPage` (`frontend/src/pages/OrgChartPage.tsx`) owns data fetching, filtering, zoom, and connection-line layout. `getLevelColor` is a pure `number => string` mapping with no dependency on page state (filters, zoom, positions list) — it belongs with the component that renders the border it styles, not with the page that merely passes `level` down. The only integration point is the `PositionCardProps` contract and the two call sites (`OrgChartPage.tsx:333-338` and `PositionCard.tsx`'s own recursive self-render at lines 119-124), both already identified correctly in the spec.

There is no framework, module-boundary, or contract-generation concern here (no OpenAPI/DTO involvement — `PositionDto.level` already exists and is untouched).

## Proposed Architecture

### Component Overview

```
Before:
  OrgChartPage (owns getLevelColor) --getLevelColor prop--> PositionCard (consumes, recurses prop to self)

After:
  OrgChartPage (owns data/layout only) --position, getChildren--> PositionCard (owns getLevelColor internally, no self-prop-drilling of it)
```

No new components, files, or module boundaries are introduced.

### Key Design Decisions

#### Decision 1: Co-locate vs. extract to shared module
**Options considered:**
1. Move `getLevelColor` as an internal, non-exported helper inside `PositionCard.tsx`.
2. Extract to a new co-located file, e.g. `frontend/src/components/OrgChart/levelColors.ts`.

**Chosen approach:** Option 1 — internal, non-exported module-level function in `PositionCard.tsx`, matching the spec (FR-1).

**Rationale:** `PositionCard` is the sole consumer and the mapping is a small, unvarying switch statement (4 branches + default). A separate file adds an indirection with no current benefit — YAGNI. The spec already flags a `levelColors.ts` extraction as Out of Scope, correctly deferring it until an actual second consumer or theming requirement appears (`getInitials` in the same file is a precedent for this pattern: small pure helpers live inline in the component file, not extracted).

## Implementation Guidance

### Directory / Module Structure

No new files. Changes confined to the three files the spec identifies, plus the auto-regenerated snapshot:
- `frontend/src/components/OrgChart/PositionCard.tsx` — add internal `getLevelColor`, remove it from `PositionCardProps` (currently `position.tsx:4-8`) and from the destructured params (line 19) and the recursive self-render props (lines 119-124).
- `frontend/src/pages/OrgChartPage.tsx` — delete the function (lines 168-181) and the `getLevelColor={getLevelColor}` prop at the `<PositionCard>` call site (lines 333-338).
- `frontend/src/components/OrgChart/__tests__/PositionCard.test.tsx` — remove `stubLevelColor` (line 21) and the `getLevelColor={stubLevelColor}` prop at both call sites (lines 40, 76).
- `frontend/src/components/OrgChart/__tests__/__snapshots__/PositionCard.test.tsx.snap` — regenerate via test runner (`npm test -- -u` or equivalent), not hand-edited, per NFR/spec Out of Scope.

### Interfaces and Contracts

`PositionCardProps` narrows from three fields to two:

```typescript
// Before
export interface PositionCardProps {
  position: PositionDto;
  getChildren: (parentId: string) => PositionDto[];
  getLevelColor: (level: number) => string;
}

// After
export interface PositionCardProps {
  position: PositionDto;
  getChildren: (parentId: string) => PositionDto[];
}
```

`getLevelColor` becomes a private, non-exported `const` in `PositionCard.tsx`, called as `getLevelColor(position.level ?? 1)` in place of the current prop invocation — preserving the existing `?? 1` fallback exactly (do not change default-level semantics as part of this move).

No other public interface changes. `OrgChartPage` is not exported/consumed elsewhere with a different shape, per spec's Dependencies section (confirmed no other file references `getLevelColor`).

### Data Flow

Unchanged except for where the color decision is computed: `position.level` still flows from `OrgChartPage`'s fetched/filtered position data into `PositionCard` via the `position` prop (unchanged); `PositionCard` now derives its own border class from `position.level` internally instead of receiving a pre-selected class-producing function. No data crosses a new boundary; this is a pure locality-of-behavior fix.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Snapshot regeneration silently masks a real behavioral drift (not just the stub→real-class diff) | Low | Diff the regenerated `.snap` file by hand before committing; confirm only the border-color class strings change and no other markup shifts. |
| Recursive self-render (`PositionCard.tsx:119-124`) missed, leaving a stale `getLevelColor` prop passed to self | Low | TypeScript will fail to compile once `getLevelColor` is removed from `PositionCardProps` — the recursive call site becomes a type error, making this self-catching. Run `npm run build` to confirm. |

## Specification Amendments

None. The spec's line-number references were verified against current source and are accurate (interface at 4-8, destructure at 19, recursive render at 119-124, page definition at 168-181, page call site at 333-338, test stub/props at 21/40/76). No functional or scope changes needed.

## Prerequisites

None. No migrations, config, or infrastructure changes required. This can be implemented immediately as a standalone, self-verifying change (TypeScript compilation + existing test suite with regenerated snapshot are sufficient verification — no new tests required per spec's Out of Scope).
