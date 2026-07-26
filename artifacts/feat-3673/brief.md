# [arch-review] OrgChart: `getLevelColor` belongs in `PositionCard`, not prop-drilled from the page

## Module
OrgChart

## Finding
`getLevelColor` is a pure presentation function that maps a hierarchy level to a Tailwind border-color class. It is defined in `OrgChartPage.tsx` (lines 168–181) and injected into `PositionCard` as a required prop:

```typescript
// OrgChartPage.tsx:168-181
const getLevelColor = (level: number) => {
  switch (level) {
    case 1: return 'border-l-4 border-red-500';
    case 2: return 'border-l-4 border-orange-500';
    case 3: return 'border-l-4 border-yellow-500';
    case 4: return 'border-l-4 border-green-500';
    default: return 'border-l-4 border-gray-500';
  }
};
```

```typescript
// PositionCard.tsx:6-7 — accepts it as a prop
export interface PositionCardProps {
  position: PositionDto;
  getChildren: (parentId: string) => PositionDto[];
  getLevelColor: (level: number) => string;   // ← leaks page internals into the card's contract
}
```

`PositionCard` is the **only consumer** of `getLevelColor`. The function is never overridden or varied — the page always passes the same implementation.

## Why it matters
- **Single Responsibility**: `OrgChartPage` is responsible for fetching and laying out the chart; the color scheme of an individual card is `PositionCard`'s concern.
- **Cohesion**: A reader of `PositionCard` must jump to the page to understand how level colors are determined.
- **Unnecessary prop surface**: The extra prop widens `PositionCardProps`, makes tests for `PositionCard` harder to write (they must supply a stub), and adds a call-site argument that carries zero information.

## Suggested fix
Move `getLevelColor` inside `PositionCard.tsx` (or a co-located `levelColors.ts` if you later want to theme it), remove it from `PositionCardProps`, and delete it from `OrgChartPage`:

```typescript
// PositionCard.tsx — add internally
const getLevelColor = (level: number): string => {
  switch (level) {
    case 1: return 'border-l-4 border-red-500';
    case 2: return 'border-l-4 border-orange-500';
    case 3: return 'border-l-4 border-yellow-500';
    case 4: return 'border-l-4 border-green-500';
    default: return 'border-l-4 border-gray-500';
  }
};

// Remove from PositionCardProps; remove prop from OrgChartPage's JSX
```

---
_Filed by daily arch-review routine on 2026-07-16._
