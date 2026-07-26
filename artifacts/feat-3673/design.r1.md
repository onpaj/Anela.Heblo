# Design: Move `getLevelColor` from `OrgChartPage` into `PositionCard`

## Component Design

No new components. `PositionCard.tsx` gains one private, non-exported helper:

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

`PositionCardProps` narrows from `{ position, getChildren, getLevelColor }` to `{ position, getChildren }`. `OrgChartPage.tsx` loses its `getLevelColor` definition and the corresponding prop at the `<PositionCard>` call site. No other component's contract changes.

## Data Schemas

Not applicable — no data model, API, or DTO changes. `position.level: number` (already part of `PositionDto`) is the only input, consumed identically before and after.

---

`Skip Design: true` per `arch-review.r1.md` — this is a pure code-organization refactor with no UI/UX change (rendered DOM and Tailwind classes are byte-for-byte identical before and after), so no UX/UI section is included per the designer agent's output rules.
