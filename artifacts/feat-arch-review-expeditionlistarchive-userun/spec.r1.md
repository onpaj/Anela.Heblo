Now I have all the context I need. Producing the spec.

```markdown
# Specification: Relocate `useRunExpeditionListPrintFix` to ExpeditionList Module

## Summary
The `useRunExpeditionListPrintFix` React Query mutation hook currently lives in `frontend/src/api/hooks/useExpeditionListArchive.ts` but calls an endpoint (`/api/expedition-list/run-fix`) owned by the **ExpeditionList** backend module, not **ExpeditionListArchive**. This spec defines a pure relocation of that hook to a new `useExpeditionList.ts` file, restoring correct module boundaries with zero functional change.

## Background
A daily architecture review (2026-06-04) flagged a module-boundary leak in `frontend/src/api/hooks/useExpeditionListArchive.ts` (lines 130–152):

```typescript
const relativeUrl = `/api/expedition-list/run-fix`;
```

`/api/expedition-list/...` is served by `ExpeditionListController`, while the file it lives in is dedicated to hooks for `ExpeditionListArchiveController`. The hook is consumed by `frontend/src/pages/ExpeditionListArchivePage.tsx` (line 9), giving the Archive page a hidden dependency on the ExpeditionList module via its own hook file.

Why this matters:
- **Module boundary leakage** — the Archive hook file owns a concern that belongs to a different module. If the ExpeditionList endpoint contract changes, the affected hook is in the wrong place.
- **Discoverability** — developers searching `useExpeditionList*` for callers of the run-fix endpoint will miss this hook.
- **Cohesion** — `useExpeditionListArchive.ts` currently has two responsibilities: archive data access and triggering ExpeditionList operations.

The fix is a low-risk relocation with no behavior change.

## Functional Requirements

### FR-1: Create `useExpeditionList.ts` hooks file
Create a new file `frontend/src/api/hooks/useExpeditionList.ts` to host hooks that target endpoints under `/api/expedition-list/...` (the ExpeditionList module).

**Acceptance criteria:**
- File exists at `frontend/src/api/hooks/useExpeditionList.ts`.
- File follows the same conventions as `useExpeditionListArchive.ts` (imports `useMutation` / `useQueryClient` from `@tanstack/react-query`, uses `getAuthenticatedApiClient` from `../client`, constructs absolute URLs via `${apiClient.baseUrl}${relativeUrl}` per the project rule in CLAUDE.md).

### FR-2: Move `useRunExpeditionListPrintFix` to the new file
Move the `useRunExpeditionListPrintFix` hook (currently at `useExpeditionListArchive.ts` lines 130–152) into `useExpeditionList.ts` verbatim. The hook body, endpoint URL, HTTP method, headers, error handling, and return shape must remain identical.

**Acceptance criteria:**
- `useRunExpeditionListPrintFix` is exported from `frontend/src/api/hooks/useExpeditionList.ts`.
- The hook still issues `POST` to `/api/expedition-list/run-fix` with `Content-Type: application/json` and no request body.
- Error handling matches the original: read JSON error payload, fall back to `HTTP error! status: ${status}`.
- The hook no longer exists in `useExpeditionListArchive.ts` (no duplicate export).

### FR-3: Update import in `ExpeditionListArchivePage.tsx`
Update the import statement in `frontend/src/pages/ExpeditionListArchivePage.tsx` so that `useRunExpeditionListPrintFix` is imported from `../api/hooks/useExpeditionList` instead of `../api/hooks/useExpeditionListArchive`.

**Acceptance criteria:**
- `useRunExpeditionListPrintFix` is imported from `../api/hooks/useExpeditionList`.
- The remaining imports from `../api/hooks/useExpeditionListArchive` (`useExpeditionDates`, `useExpeditionListsByDate`, `useReprintExpeditionList`, `getExpeditionListDownloadUrl`, `ExpeditionListItemDto`) stay on that line.
- Page behavior is unchanged: triggering the "run print fix" action still calls the same endpoint and surfaces the same success/error states.

### FR-4: Update all other consumers (if any)
Search the frontend codebase for any other imports of `useRunExpeditionListPrintFix` from `useExpeditionListArchive` and redirect them to the new file.

**Acceptance criteria:**
- `grep -r "useRunExpeditionListPrintFix" frontend/src` shows the symbol only defined in `useExpeditionList.ts` and imported from that path.
- No file imports `useRunExpeditionListPrintFix` from `useExpeditionListArchive`.

### FR-5: No behavioral change
This is a pure relocation. No new functionality, no signature change, no new tests required beyond what already exists.

**Acceptance criteria:**
- `npm run build` succeeds.
- `npm run lint` passes.
- Any existing unit/component tests that touched `useRunExpeditionListPrintFix` or `ExpeditionListArchivePage` continue to pass without modification (beyond updating import paths in mocks/spies if needed).
- Manual smoke test: opening the Expedition List Archive page and clicking the "run print fix" trigger still hits `/api/expedition-list/run-fix` and shows the existing success/error toast.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact. Bundle size is essentially unchanged (one new ~25-line file, equivalent code removed from another file). No runtime overhead.

### NFR-2: Security
No security impact. The endpoint, authentication mechanism, and headers are unchanged. The hook continues to use `getAuthenticatedApiClient()` for authenticated requests.

### NFR-3: Maintainability
After this change, the rule "hooks for endpoint `/api/<module>/...` live in `use<Module>.ts`" holds across the expedition-list domain, improving discoverability and reducing cross-module coupling.

### NFR-4: Backward compatibility
External callers (the React app at runtime) see no behavior change. There are no public package consumers — only intra-repo imports — so updating the imports at the same commit is sufficient.

## Data Model
No data model changes. The endpoint contract, request payload (none), and response shape remain whatever `ExpeditionListController.RunFix` returns today.

## API / Interface Design

### Files affected
| File | Change |
|------|--------|
| `frontend/src/api/hooks/useExpeditionList.ts` | **NEW** — contains `useRunExpeditionListPrintFix` |
| `frontend/src/api/hooks/useExpeditionListArchive.ts` | Remove `useRunExpeditionListPrintFix` (lines 130–152) |
| `frontend/src/pages/ExpeditionListArchivePage.tsx` | Update import line for `useRunExpeditionListPrintFix` |

### Exported surface
- `useExpeditionList.ts` exports: `useRunExpeditionListPrintFix` (named export).
- `useExpeditionListArchive.ts` exported surface shrinks by exactly one symbol.

### Backend
No backend changes. `ExpeditionListController.RunFix` and the `/api/expedition-list/run-fix` route remain untouched.

## Dependencies
- `@tanstack/react-query` (already in use, no version change).
- `../client` for `getAuthenticatedApiClient` (already in use).
- No new npm packages.
- No backend changes; no coordination with `ExpeditionListController` needed.

## Out of Scope
- Renaming, refactoring, or changing the behavior of `useRunExpeditionListPrintFix`.
- Refactoring the other hooks in `useExpeditionListArchive.ts` (`useExpeditionDates`, `useExpeditionListsByDate`, `useReprintExpeditionList`, `getExpeditionListDownloadUrl`).
- Generating typed request/response models for the run-fix endpoint via the OpenAPI client. (The hook uses raw `fetch` today; that style is preserved.)
- Adding query keys or cache-invalidation logic for the run-fix mutation beyond what exists today.
- Adding new tests; only updating any test imports that break due to the move.
- Changing `ExpeditionListController` or its route.
- Adding an `index.ts` barrel file for `frontend/src/api/hooks/`.

## Open Questions
None.

## Status: COMPLETE
```