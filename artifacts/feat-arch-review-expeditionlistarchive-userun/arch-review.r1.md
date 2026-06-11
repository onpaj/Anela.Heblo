I have enough grounding. Producing the architecture review.

```markdown
# Architecture Review: Relocate `useRunExpeditionListPrintFix` to ExpeditionList Module

## Skip Design: true

No UI components, screens, or visual elements change. This is a pure source-file relocation behind an unchanged React component.

## Architectural Fit Assessment

The proposal is a textbook fit for the existing conventions. The verified facts:

- **Hook-per-module convention is already the norm.** `frontend/src/api/hooks/` contains ~70 files, all named `use<Domain>.ts`, each owning the hooks that call `/api/<domain>/...` endpoints (e.g. `useExpeditionListArchive.ts` ↔ `/api/expedition-list-archive/...`, `useRecurringJobs.ts` ↔ recurring jobs endpoints). No barrel file exists; pages import directly from a specific hook file.
- **Backend ownership is unambiguous.** `backend/src/Anela.Heblo.API/Controllers/ExpeditionListController.cs:20` owns `[HttpPost("run-fix")]`. `ExpeditionListArchiveController` does not.
- **The leak is real and singular.** `grep` confirms `useRunExpeditionListPrintFix` is referenced exactly four places: the definition (`useExpeditionListArchive.ts:130`), the page (`ExpeditionListArchivePage.tsx:9, 51`), and the test (`__tests__/ExpeditionListArchivePage.test.tsx:12, 31, 77`). No other callers, no barrel re-exports.
- **Hook style is consistent and trivial to port.** Lines 130–152 use the same `getAuthenticatedApiClient()` + raw-fetch pattern as the surrounding hooks. No query keys, no cache invalidation, no shared types.

Integration points: `ExpeditionListArchivePage.tsx` (the only runtime consumer) and `ExpeditionListArchivePage.test.tsx` (jest mock target — **the spec does not mention this and it must be updated**, see Specification Amendments).

## Proposed Architecture

### Component Overview

```
Before:
  ExpeditionListArchivePage.tsx
        │
        ├──> useExpeditionListArchive.ts ──> /api/expedition-list-archive/... (correct)
        └──> useExpeditionListArchive.ts ──> /api/expedition-list/run-fix     (LEAK)
                  ▲
                  └── owned by ExpeditionListArchive module, but calls ExpeditionList

After:
  ExpeditionListArchivePage.tsx
        │
        ├──> useExpeditionListArchive.ts ──> /api/expedition-list-archive/...
        └──> useExpeditionList.ts        ──> /api/expedition-list/run-fix
                  ▲
                  └── NEW file, owned by ExpeditionList module
```

### Key Design Decisions

#### Decision 1: New file `useExpeditionList.ts` vs. inline merge into an existing ExpeditionList hook file
**Options considered:**
- (a) Create `useExpeditionList.ts` as a new file.
- (b) Locate an existing ExpeditionList hook file and append the hook there.

**Chosen approach:** (a). Verified via `ls frontend/src/api/hooks/`: no file currently owns `/api/expedition-list/...`. The new file establishes the module's hook namespace.

**Rationale:** Matches the established 1-file-per-backend-module convention. The file will be small (~25 lines) but consistent with siblings like `useResetOrderShipment.ts` and `useLastAddedItem.ts` that also host a single hook.

#### Decision 2: Preserve raw-fetch style; do not migrate to typed OpenAPI client
**Options considered:**
- (a) Verbatim move, keeping raw `fetch` against a hardcoded relative URL.
- (b) Opportunistically switch to the generated TypeScript OpenAPI client.

**Chosen approach:** (a), per spec FR-5 and "Out of Scope". The surrounding hooks in `useExpeditionListArchive.ts` use the same raw-fetch style, so a one-off migration would diverge from local convention without solving the boundary leak.

**Rationale:** Surgical change (per CLAUDE.md). Style migration is a separate concern.

#### Decision 3: No barrel `index.ts` for `frontend/src/api/hooks/`
**Chosen approach:** Match existing convention — pages import directly from `../api/hooks/<file>`. Spec explicitly excludes a barrel.

**Rationale:** Adding a barrel here is out of scope and would create unrelated churn across ~70 hook files' consumers.

#### Decision 4: Keep the response shape untyped (returns `any` from `mutationFn`)
**Chosen approach:** Move the hook verbatim — `useMutation({ mutationFn: async () => {...} })` with no explicit generic, matching the original signature. The page consumes `result.totalCount` (`ExpeditionListArchivePage.tsx:98`) implicitly via the page test's mock (`{ totalCount: 5 }`).

**Rationale:** Spec FR-5 forbids signature changes. Adding a typed response is an enhancement that belongs in a separate ticket.

## Implementation Guidance

### Directory / Module Structure

| Path | Action | Notes |
|------|--------|-------|
| `frontend/src/api/hooks/useExpeditionList.ts` | **Create** | New file; only export is `useRunExpeditionListPrintFix`. |
| `frontend/src/api/hooks/useExpeditionListArchive.ts` | **Edit** | Delete lines 130–152 inclusive (the hook export). Leave everything else untouched, including the `useReprintExpeditionList` block immediately above and the `getExpeditionListDownloadUrl` block immediately below. |
| `frontend/src/pages/ExpeditionListArchivePage.tsx` | **Edit** | Add `import { useRunExpeditionListPrintFix } from "../api/hooks/useExpeditionList";` and remove the symbol from the existing `useExpeditionListArchive` import block (lines 5–12). Keep import ordering consistent with the file's existing style. |
| `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` | **Edit (spec gap)** | See Specification Amendments. |

### Interfaces and Contracts

The hook's external contract is **frozen by spec**:

```typescript
// frontend/src/api/hooks/useExpeditionList.ts
import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

export const useRunExpeditionListPrintFix = () => {
  return useMutation({
    mutationFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/expedition-list/run-fix`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => null);
        throw new Error(
          errorData?.errorMessage ?? `HTTP error! status: ${response.status}`
        );
      }

      return await response.json();
    },
  });
};
```

Verbatim from `useExpeditionListArchive.ts:130–152`. **Do not** import `useQueryClient` or `QUERY_KEYS` — the original hook does not use them.

### Data Flow

Unchanged: `ExpeditionListArchivePage.handleRunFix()` → `runFixMutation.mutateAsync()` → `POST /api/expedition-list/run-fix` → `ExpeditionListController.RunFix` → response `{ totalCount }` → page toast. Only the import path of the hook moves.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test file mocks `useExpeditionListArchive` and expects `useRunExpeditionListPrintFix` on that module. After the move, the page's real import resolves to `useExpeditionList`, leaving the test's mock orphaned and the page hitting the real (unmocked) hook → test failure or network attempt. | **HIGH** | Update the test's `jest.mock(...)` target and `require(...)` for `useRunExpeditionListPrintFix` to point at `../../api/hooks/useExpeditionList`. Mandatory; see Specification Amendments. |
| Stale auto-import suggestions in IDEs still pointing to the old file. | LOW | One-shot grep at the end (`grep -r "useRunExpeditionListPrintFix" frontend/src`) ensures no lingering references — already in spec FR-4. |
| Future hooks for `/api/expedition-list/...` (e.g. typed run-fix variants, status queries) get added back to `useExpeditionListArchive.ts` out of habit. | LOW | The new `useExpeditionList.ts` file makes the correct location obvious; no extra action needed beyond this commit. |
| Lint rule for unused imports flags any residual import that wasn't fully removed in `ExpeditionListArchivePage.tsx`. | LOW | `npm run lint` (already in the spec's done-criteria) catches this. |

## Specification Amendments

1. **Add a fifth file to "Files affected": `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx`.**
   - The test currently does:
     ```typescript
     jest.mock("../../api/hooks/useExpeditionListArchive", () => ({
       ...
       useRunExpeditionListPrintFix: jest.fn(),
       ...
     }));
     const { ..., useRunExpeditionListPrintFix } = require("../../api/hooks/useExpeditionListArchive");
     ```
   - After the move, this mock will no longer intercept the page's import. Required edits:
     - Remove `useRunExpeditionListPrintFix: jest.fn(),` from the `jest.mock("../../api/hooks/useExpeditionListArchive", ...)` factory (line 12).
     - Remove `useRunExpeditionListPrintFix` from the destructured `require(...)` of `useExpeditionListArchive` (line 31).
     - Add a new mock block:
       ```typescript
       jest.mock("../../api/hooks/useExpeditionList", () => ({
         useRunExpeditionListPrintFix: jest.fn(),
       }));
       const { useRunExpeditionListPrintFix } = require("../../api/hooks/useExpeditionList");
       ```
     - The `beforeEach` setup at line 77 remains unchanged.
   - Spec FR-5 already acknowledges "updating import paths in mocks/spies if needed"; this amendment makes that obligation concrete and surfaces it as a tracked file edit rather than an incidental fix.

2. **Tighten FR-4's grep acceptance check.** Replace the current phrasing with a two-step verification to avoid false positives from the test mock string and the new file's definition:
   - `grep -r "from.*useExpeditionListArchive.*useRunExpeditionListPrintFix\|useRunExpeditionListPrintFix.*useExpeditionListArchive" frontend/src` must return no results, **and**
   - `grep -rn "useRunExpeditionListPrintFix" frontend/src` must return exactly: 1 definition line in `useExpeditionList.ts`, 2 lines in `ExpeditionListArchivePage.tsx` (import + call), and 3 lines in the test file (mock factory key, destructured require, `beforeEach` setup) — six matches total.

3. **No spec change needed for FR-1's "follows the same conventions" clause** — verified against the sibling files. The new file must **not** import `useQuery`/`useQueryClient` or `QUERY_KEYS` because the moved hook uses neither.

## Prerequisites

None. No migrations, no infrastructure, no configuration, no backend coordination. The change is contained within the frontend codebase and can be applied in a single commit. Validation gates (`npm run build`, `npm run lint`, existing Jest suite for `ExpeditionListArchivePage`) are sufficient as a done-check.
```