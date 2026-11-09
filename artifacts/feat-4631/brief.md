## Module / File
`frontend/src/components/knowledge-base/KnowledgeBaseDocumentsTab.tsx`

## Coverage
Line coverage: 0% (0/133 lines). Filter threshold: 60%

## What's not tested
The URL→state `useEffect` (back/forward navigation sync across page, pageSize, sortBy, sortDesc, and three filters) has no regression coverage — a param rename silently breaks navigation. The pagination sliding window (pages ≤ 5 / near-start / near-end / middle) has four distinct branches, all dark. The sort-toggle (same column flips desc, different column resets to asc) is also uncovered.

## Why it matters
The URL sync is what preserves filter state across navigation; a regression forces users to re-enter their filters after every back-navigation. A bug in the pagination window calculation would skip or duplicate page numbers visible to all users with no test catching it.

## Suggested approach
React Testing Library + MemoryRouter tests covering each URL→state branch; extract and unit-test the page-number window calculation in isolation for each of the four boundary cases. Estimated effort: ~half day.

---
_Filed by weekly coverage-gap routine on 2026-11-09. Based on CI nightly run #42456500800 (16643224c30a67e613312ac689cd872155b43bd1)._
