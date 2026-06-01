# Architecture Review: Extract `SortableHeader` Component to Module Scope

## Skip Design: true

## Architectural Fit Assessment

This is a **structural, single-file refactor** that aligns the Journal page with its own established pattern: `JournalRow` (lines 38–116) is already at module scope, while `SortableHeader` (lines 247–274) is nested inside `JournalList`. After the change, both presentational helpers used only by `JournalList` will sit side-by-side at module scope in the same file — a textbook example of the existing convention.

Integration points are minimal:
- **Single file** — `frontend/src/components/pages/Journal/JournalList.tsx`.
- **Three call sites** — lines 384, 385, 386 (`title`, `entryDate`, `createdByUsername`).
- **Three state bindings to thread as props** — `sortBy`, `sortDescending` (`useState` in `JournalList`), and `handleSort` (defined at lines 197–204).
- **Test surface** — `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` does not assert sort-click behavior, so the refactor is invisible to existing tests.

**Important note for awareness, not for action:** The same anti-pattern (`SortableHeader` declared inside the page component) is replicated in at least six other list pages: `PurchaseOrderList.tsx`, `CatalogList.tsx`, `ProductMarginsList.tsx`, `ManufactureInventoryList.tsx`, `InventoryList.tsx`, `GiftPackageManufacturingList.tsx`. The spec explicitly scopes this change to Journal only, which is correct — but a follow-up to either fix the other six in the same way, or extract a shared `SortableHeader` once a second consumer requests true sharing, is justified. **Do not expand scope here**; flag it in the PR description instead.

## Proposed Architecture

### Component Overview

```
JournalList.tsx (module scope)
├── imports (unchanged)
├── interface JournalRowProps          (unchanged)
├── const JournalRow                   (unchanged — module scope)
├── interface SortableHeaderProps      (NEW — module scope)
├── const SortableHeader               (MOVED here from inside JournalList)
└── const JournalList (default export)
        ├── state: sortBy, sortDescending, ...
        ├── handler: handleSort
        └── render → <SortableHeader column sortBy sortDescending onSort=… />
```

Component identity is established **once at module load** for both `JournalRow` and `SortableHeader`. `JournalList` only renders them; it never redefines them. React's reconciler now sees stable component types across renders, so the `<th>` subtree is updated in place rather than unmounted/remounted on every state change.

### Key Design Decisions

#### Decision 1: Closure capture vs. explicit props

**Options considered:**
- (a) Pass `sortBy`, `sortDescending`, `onSort` as explicit props (spec-mandated).
- (b) Wrap state in a `SortContext` provider in `JournalList` and consume via `useContext` inside `SortableHeader`.

**Chosen approach:** (a) Explicit props.

**Rationale:** Context would be over-engineering for a single, file-local consumer. Explicit props keep the data flow obvious, match the existing `JournalRow` prop pattern in the same file, and impose zero runtime overhead. Context becomes interesting only if/when a deep tree of header descendants needs sort state — currently none does.

#### Decision 2: Where the component lives

**Options considered:**
- (a) Module scope in `JournalList.tsx` (spec-mandated).
- (b) New file `frontend/src/components/pages/Journal/SortableHeader.tsx`.
- (c) Shared location `frontend/src/components/shared/SortableHeader.tsx` for reuse across the six other list pages.

**Chosen approach:** (a).

**Rationale:** Spec explicitly puts (c) out of scope ("until a second consumer appears"). Option (b) creates a file for a ~25-line presentational helper used in exactly one place — extra navigation cost with no benefit. Co-location with `JournalRow` at module scope is the right granularity now and signals "presentational helper for this page" clearly.

#### Decision 3: Type definition style

**Options considered:**
- (a) Named `interface SortableHeaderProps` declared above the component.
- (b) Inline `React.FC<{…}>` generic argument (what the current nested version uses).

**Chosen approach:** (a) Named interface.

**Rationale:** Matches the `JournalRowProps` pattern already in this file (lines 27–36). A named interface is searchable, refactorable, and the spec requires it (FR-4). Note: although global TypeScript style discourages `React.FC`, this file already uses `React.FC` for `JournalRow`. Per the project rule *"Match existing style even if you'd do it differently"*, the refactored `SortableHeader` should also be typed as `React.FC<SortableHeaderProps>` for in-file consistency. Do **not** silently change `JournalRow` while you're here.

#### Decision 4: Whether to memoize

**Options considered:**
- (a) Plain function component.
- (b) `React.memo(SortableHeader)`.

**Chosen approach:** (a) — explicitly excluded by spec.

**Rationale:** Headers receive a new `onSort` reference on every `JournalList` render (because `handleSort` is not wrapped in `useCallback`). `React.memo` without also memoizing `onSort` would not prevent re-renders, and chasing this further crosses into optimization the spec explicitly rejects. The structural fix alone removes the unmount/remount; per-render prop updates are cheap and correct.

## Implementation Guidance

### Directory / Module Structure

No new files. Edit only:
- `frontend/src/components/pages/Journal/JournalList.tsx`

Place the new declarations between `JournalRow` (ends line 116) and `JournalList` (starts line 118):

```
… imports …
interface JournalRowProps { … }
const JournalRow: React.FC<JournalRowProps> = (…) => (…)

interface SortableHeaderProps { … }       // NEW
const SortableHeader: React.FC<SortableHeaderProps> = (…) => (…)   // MOVED

const JournalList: React.FC = () => { … }
```

Delete lines 246–274 (the nested declaration and its leading comment) from inside `JournalList`.

### Interfaces and Contracts

```ts
interface SortableHeaderProps {
  column: string;
  sortBy: string;
  sortDescending: boolean;
  onSort: (column: string) => void;
  children: React.ReactNode;
}
```

**Body of the component is byte-identical to the current implementation**, with two substitutions:
- `sortBy === column` (was: closed-over `sortBy`).
- `onClick={() => onSort(column)}` (was: `handleSort(column)`).

No other JSX, class names, icon usage, or markup may change.

### Data Flow

```
JournalList state (sortBy, sortDescending)
        │
        ├── prop: sortBy ─────────┐
        ├── prop: sortDescending ─┤
        └── prop: onSort=handleSort  ─┐
                                  │   │
        <SortableHeader column="entryDate" …> ── click ──> onSort("entryDate")
                                                                     │
                                  ┌──────────────────────────────────┘
                                  ▼
                            handleSort(column) in JournalList
                                  │
                                  ├── if sortBy === column → setSortDescending(!sortDescending)
                                  └── else                 → setSortBy(column); setSortDescending(false)
                                                                     │
                                                                     ▼
                                                            re-render JournalList
                                                                     │
                                                                     ▼
                                            same SortableHeader component identity
                                            → React updates <th> in place (no remount)
```

The crucial change: **`SortableHeader`'s function identity is constant across renders**, so React's reconciler matches old and new vDOM nodes by type identity and updates them, instead of unmounting/remounting.

### Call site changes (three lines, mechanical)

Each `<SortableHeader column="X">Label</SortableHeader>` becomes:

```tsx
<SortableHeader column="X" sortBy={sortBy} sortDescending={sortDescending} onSort={handleSort}>
  Label
</SortableHeader>
```

Apply to all three call sites (`title`, `entryDate`, `createdByUsername`).

### Verification checklist

Before declaring done:
1. `npm run build` passes with no new TypeScript errors.
2. `npm run lint` passes with no new warnings.
3. `npm test -- JournalList` — existing 17 tests in `__tests__/JournalList.test.tsx` remain green.
4. Manual smoke test in dev server: click each of the three sortable headers, confirm chevron orientation flips and sort applies (visually identical to before).
5. Grep the file to confirm zero references to `SortableHeader` remain inside the `JournalList` function body.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Forgetting to thread one of the three props at a call site → silent stale behavior | Medium | TypeScript will fail the build because `SortableHeaderProps` makes all three required (non-optional). Confirm `noImplicitAny`/strict mode is on by running `npm run build`. |
| Accidentally touching `JournalRow` or other unrelated code while editing the same file | Low | Surgical-change discipline: diff before commit; line-by-line review of the PR. The only deletions allowed are lines 246–274; the only insertions are the new interface + moved component above `JournalList`. |
| Refactor expanded to include the six other list pages with the same anti-pattern | Medium | Spec is explicit: Journal only. Mention the other files in the PR description as a *follow-up*, but do **not** include them in this PR. Reviewer should reject scope creep. |
| Style drift: changing `React.FC` to function-declaration style for the new component | Low | Match `JournalRow` in the same file (`React.FC<Props>`). Do not "improve" the typing style as a side effect. |
| Existing tests don't cover sort interactions, so a regression in sort behavior would not be caught by CI | Low | Spec encourages an optional smoke test (`fireEvent.click` on a header → assert `useJournalEntries` is called with updated `sortBy`/`sortDirection`). Treat as nice-to-have, not blocking. Manual verification (step 4 above) is the primary safety net. |

## Specification Amendments

None required. The spec is precise, scope is well-bounded, and the acceptance criteria are testable as written. One **clarification** worth adding to the PR description (not the spec):

- State explicitly that `JournalRow` will use `React.FC` and so will the moved `SortableHeader`, even though the global TypeScript style guide discourages `React.FC` — the rationale is in-file consistency, and changing `JournalRow` is out of scope.

## Prerequisites

None. No migrations, no config changes, no infrastructure work, no package additions, no OpenAPI regeneration. Implementation can begin immediately on this branch.