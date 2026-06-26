# Relocate `useRunExpeditionListPrintFix` to ExpeditionList Module — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Relocate the `useRunExpeditionListPrintFix` React Query mutation hook out of `useExpeditionListArchive.ts` (wrong module) into a new `useExpeditionList.ts` file (correct module), updating the page import and the page's Jest mocks, with zero behavioral change.

**Architecture:** Frontend-only, pure source-file relocation. The hook calls `/api/expedition-list/run-fix` (owned by `ExpeditionListController`) but currently lives in the file dedicated to `/api/expedition-list-archive/...` hooks. The fix creates a new sibling hook file under `frontend/src/api/hooks/` following the established 1-file-per-backend-module convention, then redirects the single runtime consumer (`ExpeditionListArchivePage.tsx`) and the matching Jest mock factory in `__tests__/ExpeditionListArchivePage.test.tsx`. No backend changes, no new tests, no signature changes.

**Tech Stack:** React 18 + TypeScript, `@tanstack/react-query` (mutations), Jest + React Testing Library (existing tests), raw `fetch` via `getAuthenticatedApiClient()` (unchanged style).

---

## File Structure

| Path | Action | Responsibility |
|------|--------|----------------|
| `frontend/src/api/hooks/useExpeditionList.ts` | **Create** | Hosts hooks targeting `/api/expedition-list/...` endpoints. Initial export: `useRunExpeditionListPrintFix`. |
| `frontend/src/api/hooks/useExpeditionListArchive.ts` | **Edit** | Remove the `useRunExpeditionListPrintFix` export (current lines 130–152) and the now-unused `useMutation` import if it becomes orphaned. Leave all archive hooks untouched. |
| `frontend/src/pages/ExpeditionListArchivePage.tsx` | **Edit** | Remove `useRunExpeditionListPrintFix` from the `useExpeditionListArchive` import block (current lines 5–12) and add a new dedicated import from `useExpeditionList`. |
| `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` | **Edit** | Remove `useRunExpeditionListPrintFix` from the `useExpeditionListArchive` `jest.mock` factory and `require(...)` destructure (current lines 8–14, 27–32), then add a new `jest.mock("../../api/hooks/useExpeditionList", ...)` block plus `require(...)` for the symbol. `beforeEach` setup at line 77 is unchanged because it references the local `useRunExpeditionListPrintFix` binding, which still exists. |

After this change, `useMutation` is still used by `useReprintExpeditionList` in `useExpeditionListArchive.ts`, so the existing top-line `import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";` does not change.

---

## Task 1: Create `useExpeditionList.ts` with the relocated hook

**Files:**
- Create: `frontend/src/api/hooks/useExpeditionList.ts`

- [ ] **Step 1: Create the new hook file**

Create `frontend/src/api/hooks/useExpeditionList.ts` with this exact content (verbatim relocation of `useExpeditionListArchive.ts` lines 130–152, with imports trimmed to only what this hook uses — no `useQuery`, no `useQueryClient`, no `QUERY_KEYS`):

```typescript
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

- [ ] **Step 2: Verify the file exists and TypeScript can parse it**

Run from `frontend/`:
```
npx tsc --noEmit -p tsconfig.json
```
Expected: clean exit code 0. No errors mentioning `useExpeditionList.ts`. (At this stage the old hook still exists in `useExpeditionListArchive.ts` too — that duplication is intentional and removed in Task 2.)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/useExpeditionList.ts
git commit -m "feat: add useExpeditionList hooks file for ExpeditionList module"
```

---

## Task 2: Remove `useRunExpeditionListPrintFix` from `useExpeditionListArchive.ts`

**Files:**
- Modify: `frontend/src/api/hooks/useExpeditionListArchive.ts` (delete lines 130–152 — the `useRunExpeditionListPrintFix` export block)

- [ ] **Step 1: Delete the hook block**

In `frontend/src/api/hooks/useExpeditionListArchive.ts`, remove the entire `useRunExpeditionListPrintFix` export — the 23 lines starting at the `export const useRunExpeditionListPrintFix = () => {` line and ending at the closing `};` of that export. Also delete the blank line that separates it from `getExpeditionListDownloadUrl` if it leaves a double blank line behind, so the result has a single blank line between `useReprintExpeditionList` and `getExpeditionListDownloadUrl`.

Exact block to delete (must match verbatim):

```typescript
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

- [ ] **Step 2: Confirm `useMutation` is still used**

Grep within the file:
```
grep -n "useMutation" frontend/src/api/hooks/useExpeditionListArchive.ts
```
Expected output (exactly one usage line plus the import line):
```
1:import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
103:  return useMutation<ReprintExpeditionListResponse, Error, ReprintExpeditionListRequest>({
```
If `useMutation` appears only on the import line and nowhere else, remove it from the import. Based on the verified file, it must stay (used by `useReprintExpeditionList`). Do not change the imports.

- [ ] **Step 3: Verify TypeScript still compiles**

Run from `frontend/`:
```
npx tsc --noEmit -p tsconfig.json
```
Expected: a single error in `frontend/src/pages/ExpeditionListArchivePage.tsx` complaining that `useRunExpeditionListPrintFix` has no exported member in `../api/hooks/useExpeditionListArchive`. Any other error means the deletion went too far — revert and try again. (The expected page error is fixed in Task 3.)

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/useExpeditionListArchive.ts
git commit -m "refactor: remove useRunExpeditionListPrintFix from useExpeditionListArchive"
```

---

## Task 3: Update the page import in `ExpeditionListArchivePage.tsx`

**Files:**
- Modify: `frontend/src/pages/ExpeditionListArchivePage.tsx:5-12` (the import block)

- [ ] **Step 1: Remove the symbol from the existing import**

Replace lines 5–12 (the multi-line import from `useExpeditionListArchive`):

```typescript
import {
  useExpeditionDates,
  useExpeditionListsByDate,
  useReprintExpeditionList,
  useRunExpeditionListPrintFix,
  getExpeditionListDownloadUrl,
  ExpeditionListItemDto,
} from "../api/hooks/useExpeditionListArchive";
```

with:

```typescript
import {
  useExpeditionDates,
  useExpeditionListsByDate,
  useReprintExpeditionList,
  getExpeditionListDownloadUrl,
  ExpeditionListItemDto,
} from "../api/hooks/useExpeditionListArchive";
import { useRunExpeditionListPrintFix } from "../api/hooks/useExpeditionList";
```

The new dedicated import goes on its own line immediately after the existing `useExpeditionListArchive` import block, before the `useTriggerRecurringJobMutation` import that follows on line 13. This preserves the file's existing import ordering style (relative-path imports grouped together).

- [ ] **Step 2: Verify TypeScript compiles cleanly**

Run from `frontend/`:
```
npx tsc --noEmit -p tsconfig.json
```
Expected: exit code 0, no errors.

- [ ] **Step 3: Run the frontend lint**

Run from `frontend/`:
```
npm run lint
```
Expected: pass with no new warnings or errors against `ExpeditionListArchivePage.tsx`, `useExpeditionListArchive.ts`, or `useExpeditionList.ts`. In particular, no `unused import` or `import/no-unresolved` complaints.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/ExpeditionListArchivePage.tsx
git commit -m "refactor: import useRunExpeditionListPrintFix from useExpeditionList in archive page"
```

---

## Task 4: Update Jest mocks in `ExpeditionListArchivePage.test.tsx`

**Files:**
- Modify: `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx:8-14, 27-32`

**Why this task exists:** The page test mocks the page's imports. After Task 3, the page imports `useRunExpeditionListPrintFix` from `../../api/hooks/useExpeditionList`. The existing mock factory still lists it under `useExpeditionListArchive`, so the test would either fail to intercept the hook (page hits real `getAuthenticatedApiClient`, no network in jsdom → throw) or report `undefined` for the destructured local. We move that one mock entry to a new `jest.mock(...)` call against the new module path.

- [ ] **Step 1: Remove `useRunExpeditionListPrintFix` from the existing `useExpeditionListArchive` mock factory**

Replace lines 8–14:

```typescript
jest.mock("../../api/hooks/useExpeditionListArchive", () => ({
  useExpeditionDates: jest.fn(),
  useExpeditionListsByDate: jest.fn(),
  useReprintExpeditionList: jest.fn(),
  useRunExpeditionListPrintFix: jest.fn(),
  getExpeditionListDownloadUrl: jest.fn(),
}));
```

with:

```typescript
jest.mock("../../api/hooks/useExpeditionListArchive", () => ({
  useExpeditionDates: jest.fn(),
  useExpeditionListsByDate: jest.fn(),
  useReprintExpeditionList: jest.fn(),
  getExpeditionListDownloadUrl: jest.fn(),
}));

jest.mock("../../api/hooks/useExpeditionList", () => ({
  useRunExpeditionListPrintFix: jest.fn(),
}));
```

- [ ] **Step 2: Update the `require(...)` destructure to source `useRunExpeditionListPrintFix` from the new module**

Replace lines 27–32:

```typescript
const {
  useExpeditionDates,
  useExpeditionListsByDate,
  useReprintExpeditionList,
  useRunExpeditionListPrintFix,
} = require("../../api/hooks/useExpeditionListArchive");
```

with:

```typescript
const {
  useExpeditionDates,
  useExpeditionListsByDate,
  useReprintExpeditionList,
} = require("../../api/hooks/useExpeditionListArchive");

const { useRunExpeditionListPrintFix } = require("../../api/hooks/useExpeditionList");
```

The `beforeEach` body at line 77 (`(useRunExpeditionListPrintFix as jest.Mock).mockReturnValue({ mutateAsync: jest.fn().mockResolvedValue({ totalCount: 5 }), isPending: false, });`) requires no change — the local binding name is identical, only its source module differs.

- [ ] **Step 3: Run the page test in isolation to confirm it still passes**

Run from `frontend/`:
```
npx jest src/pages/__tests__/ExpeditionListArchivePage.test.tsx
```
Expected: all 4 tests pass:
- `renders the refresh button`
- `invalidates expedition archive queries when refresh is clicked`
- `disables the refresh button while invalidation is in progress`
- `re-enables the refresh button after invalidation completes`

No new mock-related warnings (no `Cannot find module`, no `is not a function`).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx
git commit -m "test: point useRunExpeditionListPrintFix mock at useExpeditionList in archive page test"
```

---

## Task 5: Whole-project validation gates

**Files:** None modified — this task only runs verification commands.

- [ ] **Step 1: Verify no consumer still imports `useRunExpeditionListPrintFix` from the old location**

Run from repo root:
```
grep -rn "from .*useExpeditionListArchive" frontend/src | grep "useRunExpeditionListPrintFix"
```
Expected: no output (exit code 1 from `grep` is fine — it means no matches). If anything prints, follow that file path and move the import to `../api/hooks/useExpeditionList` matching the pattern from Task 3.

- [ ] **Step 2: Verify the symbol now resolves to exactly the expected locations**

Run from repo root:
```
grep -rn "useRunExpeditionListPrintFix" frontend/src
```
Expected exactly six lines:
- `frontend/src/api/hooks/useExpeditionList.ts:` 1 line — the `export const useRunExpeditionListPrintFix = () => {` definition.
- `frontend/src/pages/ExpeditionListArchivePage.tsx:` 2 lines — the new dedicated import line and the `useRunExpeditionListPrintFix()` call.
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx:` 3 lines — the new mock factory key, the new `require(...)` destructure, and the `beforeEach` setup line.

Zero matches in `useExpeditionListArchive.ts`. If any other file matches, redirect its import per Task 3.

- [ ] **Step 3: Run the full frontend build**

Run from `frontend/`:
```
npm run build
```
Expected: build succeeds (exit code 0). The TypeScript compile inside the build must report no errors.

- [ ] **Step 4: Run the full frontend lint**

Run from `frontend/`:
```
npm run lint
```
Expected: pass with no errors. Any pre-existing warnings unrelated to this change are out of scope — leave them.

- [ ] **Step 5: Run the full Jest suite to confirm no other test broke**

Run from `frontend/`:
```
npx jest
```
Expected: full pass. If any previously-passing test fails and the only file it touches is `useExpeditionListArchive` or `useExpeditionList` or `ExpeditionListArchivePage`, the failure is in scope — fix by updating that test's mock factory the same way as Task 4. Any failure outside those files is unrelated and out of scope.

- [ ] **Step 6: Manual smoke check (optional but recommended for the solo-dev workflow)**

If a local backend is already running, start the frontend dev server and exercise the relocated hook:
1. Open the Expedition List Archive page (`/logistika/expedice-archiv` per the routing convention).
2. Click the **"Spustit tisk oprav"** button (amber-colored, between "Obnovit" and "Spustit tisk").
3. Confirm:
   - The button briefly shows a spinner (`runFixMutation.isPending === true`).
   - On success, a toast `"Spuštěno — Tisk oprav dokončen. Celkem objednávek: N."` appears.
   - On error, a toast `"Chyba — Nepodařilo se spustit tisk oprav."` appears.
   - The Network tab shows a single `POST /api/expedition-list/run-fix` request (same URL as before the change).

If no local backend is available, skip this step — the unit test in Task 4 covers the wiring and `npm run build` covers compilation.

- [ ] **Step 7: Final no-op commit gate**

This task created no file changes. If the working tree is clean after Steps 1–6, do not create an empty commit. Move on.

---

## Self-Review Notes (for the executor)

- **Spec FR-1 (new file `useExpeditionList.ts`)** → Task 1.
- **Spec FR-2 (move `useRunExpeditionListPrintFix` verbatim)** → Task 1 (create) + Task 2 (remove from old location).
- **Spec FR-3 (update `ExpeditionListArchivePage.tsx` import)** → Task 3.
- **Spec FR-4 (no other consumer imports the symbol from the old path)** → Task 5 Steps 1–2 (the strengthened grep gates from the arch review's amendment).
- **Spec FR-5 + arch-review Specification Amendment #1 (Jest mock relocation)** → Task 4.
- **Validation gates (`npm run build`, `npm run lint`, Jest suite)** → Task 5 Steps 3–5.
- The relocated hook body, URL, HTTP method, headers, error handling, and return shape match the verified source at `frontend/src/api/hooks/useExpeditionListArchive.ts:130-152` byte-for-byte. The plan does not introduce typing, query keys, cache invalidation, or any other behavioral change — those are explicitly out of scope per the spec.
