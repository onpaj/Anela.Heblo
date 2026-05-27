# Specification: Extract `SortableHeader` Component to Module Scope

## Summary
Refactor `SortableHeader` in `frontend/src/components/pages/Journal/JournalList.tsx` so it is declared at module scope rather than inside the `JournalList` function body. This eliminates per-render component identity churn that causes React to remount the table header subtree on every state change, and aligns the file with the existing `JournalRow` pattern.

## Background
`SortableHeader` is currently defined as a `const` inside the `JournalList` functional component (lines 247–274). Because the component reference is recreated on every render of `JournalList`, React treats it as a new component type and unmounts/remounts the `<th>` subtree on every state change (sort, pagination, modal open, etc.).

Today there is no user-visible defect — the header renders only static text and chevrons with no focusable children — but the pattern is fragile: any future addition of a focusable element (tooltip, popover, accessible button, filter input) inside the header would silently break focus management and accessibility behavior. The sibling `JournalRow` component (lines 38–116) is already correctly defined at module scope; this change brings `SortableHeader` into consistency with that pattern.

This finding was filed by the daily arch-review routine on 2026-05-27.

## Functional Requirements

### FR-1: Move `SortableHeader` to module scope
Extract the `SortableHeader` component definition from inside the `JournalList` function body and place it at module scope in the same file, adjacent to `JournalRow`.

**Acceptance criteria:**
- `SortableHeader` is declared at module scope in `frontend/src/components/pages/Journal/JournalList.tsx`.
- `SortableHeader` is **not** declared inside the `JournalList` function body or any other component body.
- The component is placed in the file alongside `JournalRow` (immediately before or after it) to make the pattern consistency obvious.
- The component remains a non-exported declaration unless reuse outside the file is added in a later change (out of scope here).

### FR-2: Replace closure capture with explicit props
The current implementation captures `sortBy`, `sortDescending`, and the sort handler via the enclosing function's closure. After extraction, these values must be passed as explicit props.

**Acceptance criteria:**
- `SortableHeader`'s prop interface includes: `column: string`, `sortBy: string`, `sortDescending: boolean`, `onSort: (column: string) => void`, `children: React.ReactNode`.
- The `JournalList` render method passes `sortBy`, `sortDescending`, and the sort handler to every `<SortableHeader>` instance.
- No identifier inside `SortableHeader` is resolved via closure to a binding declared inside `JournalList`.

### FR-3: Preserve existing header rendering and behavior
The refactor is behavior-preserving. The visual output, sort-toggling behavior, and active-column indicator must be byte-equivalent for the user.

**Acceptance criteria:**
- Clicking a sortable header toggles sort direction and/or active column exactly as before.
- The active column's chevron orientation (ascending vs. descending) matches current behavior.
- The non-active columns render the same neutral indicator as before.
- Hover, focus-ring, and other interactive styling are unchanged.
- No new console warnings or React key warnings are introduced.

### FR-4: Type safety
The extracted component must be fully typed without introducing `any`.

**Acceptance criteria:**
- A named TypeScript interface or `type` describes the `SortableHeader` props.
- `npm run build` and `npm run lint` pass with no new errors or warnings attributable to this change.

## Non-Functional Requirements

### NFR-1: Performance
After the change, the table header subtree must not be unmounted/remounted on unrelated state changes in `JournalList`. This is verified by reasoning about referential stability rather than by a benchmark target — `SortableHeader`'s identity must be stable across renders of `JournalList`.

### NFR-2: Code quality / consistency
The file must follow the existing pattern established by `JournalRow`: presentational helper components used only by `JournalList` are declared at module scope in the same file, not nested inside the page component.

### NFR-3: Test coverage
Existing tests that exercise the Journal list (if any) must continue to pass. If unit-test coverage exists for sort interactions, it must remain green; no new tests are required for this purely structural refactor, but adding a smoke test that verifies sort-click invokes the handler is encouraged if no such coverage exists.

## Data Model
No changes. This refactor is presentational and does not alter data structures, API contracts, or persisted state.

## API / Interface Design

### Component shape (after refactor)

```ts
interface SortableHeaderProps {
  column: string;
  sortBy: string;
  sortDescending: boolean;
  onSort: (column: string) => void;
  children: React.ReactNode;
}

const SortableHeader: React.FC<SortableHeaderProps> = ({
  column,
  sortBy,
  sortDescending,
  onSort,
  children,
}) => {
  const isActive = sortBy === column;
  // ...existing JSX...
};
```

### Call site (inside `JournalList`)
Each `<SortableHeader column="...">…</SortableHeader>` call site receives the three additional props (`sortBy`, `sortDescending`, `onSort`) from the page component's existing state/handlers.

### File layout
```
JournalList.tsx
├── imports
├── JournalRow                    (already at module scope)
├── SortableHeader                (new — was inside JournalList)
└── JournalList (default export)
```

## Dependencies
- No new packages.
- No backend changes.
- No OpenAPI client regeneration required.
- Touches a single file: `frontend/src/components/pages/Journal/JournalList.tsx`.

## Out of Scope
- Moving `SortableHeader` into a shared/reusable component directory. Keep it co-located with `JournalList` unless and until a second consumer appears.
- Adding new functionality to the header (tooltips, filter dropdowns, accessible button semantics beyond what exists today).
- Adding new tests beyond keeping existing ones green.
- Refactoring `JournalRow` or any other part of the Journal module.
- Performance benchmarking. The justification is structural; we are not chasing a measured metric.
- Memoization (`React.memo`) on `SortableHeader`. Consider it only if a future change introduces measurable re-render cost; not required here.

## Open Questions
None.

## Status: COMPLETE