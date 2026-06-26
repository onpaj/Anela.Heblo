## Module
Journal

## Finding
`SortableHeader` is defined as a `const` inside the body of the `JournalList` functional component (`frontend/src/components/pages/Journal/JournalList.tsx`, lines 247–274):

```tsx
const JournalList: React.FC = () => {
  // ... state, hooks ...

  // Sortable header component  ← defined INSIDE JournalList
  const SortableHeader: React.FC<{ column: string; children: React.ReactNode }> = ({ column, children }) => {
    // ...
  };
```

Because `SortableHeader` is declared inside `JournalList`, React sees a **new component type** on every render of `JournalList`. React uses referential identity of the component function to decide whether to update or replace a subtree. When the identity changes every render, React unmounts the old `<th>` elements and mounts new ones from scratch instead of updating them.

## Why it matters
- **Correctness**: any focused element inside a remounted `SortableHeader` loses focus. Today the header only renders `<th>` text and chevrons with no inputs, so there is no visible bug — but this is fragile. Adding a tooltip, dropdown, or accessible button inside the header would silently break focus management.
- **Performance**: React discards the previous DOM subtree and re-creates six `<th>` elements on every sort, page change, or modal open — any state change that re-renders `JournalList`.
- **Pattern consistency**: `JournalRow` is correctly defined outside `JournalList` (lines 38–116). `SortableHeader` should follow the same pattern.

## Suggested fix
Move `SortableHeader` outside the `JournalList` component, alongside the already-correctly-placed `JournalRow`:

```tsx
// Outside JournalList — same file, same place as JournalRow
const SortableHeader: React.FC<{
  column: string;
  sortBy: string;
  sortDescending: boolean;
  onSort: (column: string) => void;
  children: React.ReactNode;
}> = ({ column, sortBy, sortDescending, onSort, children }) => {
  const isActive = sortBy === column;
  // ...
};
```

Pass `sortBy`, `sortDescending`, and `onSort` as explicit props rather than capturing them via closure.

---
_Filed by daily arch-review routine on 2026-05-27._