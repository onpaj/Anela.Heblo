# Architecture Review: Test coverage for KnowledgeBaseDocumentsTab

## Skip Design: true
Test-only change plus a behaviour-preserving extraction of a pure helper. No new visual components.

## Architectural Fit Assessment
Fits existing frontend conventions: Jest + RTL tests under `__tests__/` next to the component, hooks module mocked via `jest.mock('../../../api/hooks/useKnowledgeBase')` (see `KnowledgeBaseUploadTab.test.tsx`), `MemoryRouter` already used elsewhere (terminal tests). The component uses `useSearchParams`, so tests need a router.

## Proposed Architecture

### Component Overview
- `pageWindow.ts` (new): pure `getPageWindow(totalPages, currentPage)`.
- `KnowledgeBaseDocumentsTab.tsx`: replace the inline `Array.from(...)` index computation with `getPageWindow(totalPages, pageNumber).map(...)`. Nothing else changes.
- `__tests__/pageWindow.test.ts` (new): table-driven unit tests.
- `__tests__/KnowledgeBaseDocumentsTab.test.tsx` (new): RTL tests with MemoryRouter and a `LocationProbe` helper component that renders `location.search` in a test id and exposes navigation.

### Key Design Decisions

#### Decision 1: Extract window calc to a separate file
Options: export from the component file; separate file. Chosen: separate `pageWindow.ts` (keeps react-refresh/lint clean, trivially testable). Rationale: brief explicitly suggests extraction.

#### Decision 2: Assert via hook call arguments and URL
Options: assert DOM only; assert mock call args. Chosen: mock `useKnowledgeBaseDocumentsQuery` with `jest.fn()` and check the last call's argument plus `LocationProbe` search string. This pins param names and state mapping directly.

#### Decision 3: Back/forward simulation
Use a `NavButton` helper in the test wrapper calling `useNavigate()` to push new search strings (and `navigate(-1)` for history), since the URL->state effect reacts to `searchParams` regardless of mechanism.

## Implementation Guidance

### Directory / Module Structure
All under `frontend/src/components/knowledge-base/`. Mock `lucide-react` icons only if needed (UploadTab test does; lucide works in jest otherwise - check existing moduleNameMapper before mocking). Mock `./ChunkDetailModal` to a stub with a test id.

### Interfaces and Contracts
`export function getPageWindow(totalPages: number, currentPage: number): number[]` - length min(totalPages,5); branches identical to the original (<=5 total; current<=3; current>=total-2; else current-2+i). Return [] for totalPages <= 0.

### Data Flow
Router search -> useState initialisers -> hook args; state change -> sync effect -> `setSearchParams(replace)`; router change -> URL->state effect -> state.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Two effects can ping-pong on mount | Medium | Assert final stable values using `waitFor`; do not assert call counts |
| `SortableHeader` is defined inside the component and remounts each render | Low | Query headers by text each time; do not hold element references; do not fix (out of scope) |
| `doc.createdAt?.toLocaleDateString` needs Date objects | Low | Build fixtures with `new Date()` |
| Refactor changes rendering | Low | Component tests for pagination assert page buttons before and after |

## Specification Amendments
None. Optionally record in Open Questions of the final report that the sort handler sets `sortDescending=false` on column change (matches spec FR-5).

## Prerequisites
None; `cd frontend && npm test -- KnowledgeBaseDocumentsTab pageWindow --watchAll=false --coverage` should run locally.
