# Remove Duplicate Type Declarations in `useUserManagement.ts` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the hand-rolled `UserDto` and `GetGroupMembersResponse` interfaces in `frontend/src/api/hooks/useUserManagement.ts` and consume the generated symbols from `frontend/src/api/generated/api-client.ts` instead, so the hook can never drift from the backend OpenAPI contract.

**Architecture:** Pure type-system refactor. The generated client classes (`UserDto`, `GetGroupMembersResponse`) replace the local interfaces; the hook's runtime body is untouched (FR-4 behavioral parity). The generated `UserDto` has **optional** fields (`id?`, `displayName?`, `email?`), so the consumer `ResponsiblePersonCombobox.tsx` will fail TypeScript compilation at lines 46–49 and needs nullish-coalescing fallbacks (`?? ''`) — the only allowed deviation from "type-only" per the architecture review's FR-4 caveat. No backend, infra, lint, test, or runtime changes.

**Tech Stack:** TypeScript, React, `@tanstack/react-query`, NSwag-generated API client.

---

## Context the executor needs

- **Spec:** `artifacts/feat-arch-review-usermanagement-frontend-hook/spec.r1.md` (see Inputs)
- **Architecture review:** `artifacts/feat-arch-review-usermanagement-frontend-hook/arch-review.r1.md` (see Inputs)
- **Project rule:** API hooks use absolute URLs (`${apiClient.baseUrl}${relativeUrl}`) — unchanged here, just noting that the existing pattern in the hook body must not be "improved" during this refactor.
- **Generated client is regenerated on every build.** Do not edit `frontend/src/api/generated/api-client.ts` by hand — it is produced from the backend OpenAPI spec.
- **Surgical changes only.** Every changed line must trace directly to the spec. Do not migrate the raw `(apiClient as any).http.fetch(...)` call to `apiClient.manufactureOrder_GetResponsiblePersons()` — that is explicitly Out of Scope (arch review Decision 3).
- **Working directory:** All commands below assume the repo root `frontend/` subfolder is the JS workspace. Run `cd frontend` before `npm` commands or use `npm --prefix frontend ...`.

## File Structure

Files modified by this plan:

- `frontend/src/api/hooks/useUserManagement.ts` — delete 12 lines of manual type declarations (lines 4–15); add one type-only import.
- `frontend/src/components/common/ResponsiblePersonCombobox.tsx` — add `?? ''` fallbacks on lines 46–49 **only if** TypeScript reports errors there after the swap. May be unchanged.

Files read but not modified:

- `frontend/src/api/generated/api-client.ts` — verify `UserDto` (line ~26267) and `GetGroupMembersResponse` (line ~26226) exports exist.
- `frontend/src/api/hooks/__tests__/useUserManagement.test.tsx` — existing tests must continue to pass without edits.

No new files. No deleted files.

---

## Task 1: Establish a clean baseline

**Why:** Any new TypeScript or lint error after the refactor must be unambiguously attributable to this change. If `main` is already dirty, you can't tell.

**Files:** None (read-only verification).

- [ ] **Step 1: Verify the working tree is clean**

```bash
git status
```

Expected: `nothing to commit, working tree clean` on branch `feat-arch-review-usermanagement-frontend-hook`.

- [ ] **Step 2: Verify the frontend builds cleanly before any edits**

```bash
cd frontend && npm run build
```

Expected: build completes with no TypeScript errors. If the build fails on `main`, STOP and report — the failure is pre-existing and outside this plan's scope.

- [ ] **Step 3: Verify lint is clean before any edits**

```bash
cd frontend && npm run lint
```

Expected: lint exits 0 (warnings on unrelated files are fine; record the count so you can compare after the refactor).

- [ ] **Step 4: Verify the existing useUserManagement test passes**

```bash
cd frontend && npx jest src/api/hooks/__tests__/useUserManagement.test.tsx
```

Expected: all tests pass. If they fail on baseline, STOP and report.

---

## Task 2: Confirm the generated symbols exist and inspect their shape

**Why:** The plan assumes `UserDto` and `GetGroupMembersResponse` are exported from `../generated/api-client` with the optional-field shape described in the architecture review. Verify, don't trust.

**Files:** None (read-only).

- [ ] **Step 1: Confirm `UserDto` export shape**

```bash
grep -n "^export class UserDto\b" frontend/src/api/generated/api-client.ts
grep -n "^export interface IUserDto\b" frontend/src/api/generated/api-client.ts
```

Expected: both grep hits return one line each. Read the surrounding 10 lines and confirm `id?: string;`, `displayName?: string;`, `email?: string;` are all optional. This is the shape the consumer will see after the swap.

- [ ] **Step 2: Confirm `GetGroupMembersResponse` export shape**

```bash
grep -n "^export class GetGroupMembersResponse\b" frontend/src/api/generated/api-client.ts
```

Expected: one hit. Read the surrounding 15 lines and confirm `members?: UserDto[];` is the only declared field and that the class `extends BaseResponse` (which supplies the `success`/`errorCode`/`params` fields the old manual interface had).

- [ ] **Step 3: Verify no other file in `frontend/src` imports `UserDto` or `GetGroupMembersResponse` from `useUserManagement`**

```bash
grep -rn "from ['\"].*useUserManagement['\"]" frontend/src
```

Expected: two hits — `ResponsiblePersonCombobox.tsx` and `useUserManagement.test.tsx`, both importing only `useResponsiblePersonsQuery` (the hook function). No consumer imports the DTO types, so FR-3 has no rewrite work to do. If a third hit appears or one of these imports a DTO symbol, STOP and add a consumer-rewrite task before proceeding.

---

## Task 3: Replace manual types with generated imports

**Files:**
- Modify: `frontend/src/api/hooks/useUserManagement.ts:1-15`

- [ ] **Step 1: Apply the type-system swap**

Edit `frontend/src/api/hooks/useUserManagement.ts` so the top of the file changes from:

```typescript
import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export interface UserDto {
  id: string;
  displayName: string;
  email: string;
}

export interface GetGroupMembersResponse {
  success: boolean;
  errorCode?: number;
  params?: Record<string, string>;
  members: UserDto[];
}

export const useResponsiblePersonsQuery = () => {
```

to:

```typescript
import { useQuery } from '@tanstack/react-query';
import type { GetGroupMembersResponse } from '../generated/api-client';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export const useResponsiblePersonsQuery = () => {
```

Notes for the executor:
- Do **not** import `UserDto`. It is not referenced anywhere else in this file (only `GetGroupMembersResponse` is annotated on the `queryFn` return type at line 20). Importing an unused symbol will trip `no-unused-vars`.
- Use `import type` (not `import`) so the import is erased at compile time. The generated symbol is a class, but we are using it purely in type positions, so the type-only import is correct and consistent with project convention.
- Keep import order: external (`@tanstack/react-query`) → generated client → relative project imports.
- Leave everything from `export const useResponsiblePersonsQuery` downward (current lines 17–39) **byte-for-byte unchanged**, including the `(apiClient as any)` cast on the fetch call. The cast cleanup is explicitly Out of Scope per the architecture review.

- [ ] **Step 2: Confirm the file now has 31 lines and no `export interface` declarations**

```bash
wc -l frontend/src/api/hooks/useUserManagement.ts
grep -c "^export interface" frontend/src/api/hooks/useUserManagement.ts
```

Expected: `31 frontend/src/api/hooks/useUserManagement.ts` and grep count `0`.

---

## Task 4: Verify the build and fix consumer narrowing if needed

**Why:** The generated `UserDto.displayName` and `UserDto.email` are `string | undefined`, but `ResponsiblePersonCombobox.tsx:46-49` currently assigns them to fields typed `string` in `ResponsiblePersonSelectOption`. TypeScript will emit errors. The arch review authorizes a `?? ''` narrowing at the call site as the minimum-scope fix.

**Files:**
- Possibly modify: `frontend/src/components/common/ResponsiblePersonCombobox.tsx:45-50`

- [ ] **Step 1: Run the build**

```bash
cd frontend && npm run build
```

Expected outcomes (handle whichever you get):

- **Outcome A (build succeeds):** TypeScript inferred the narrowing on its own (unlikely but possible). Skip Step 2, go to Step 3.
- **Outcome B (build fails with errors only in `ResponsiblePersonCombobox.tsx` around lines 46–49 saying `Type 'string | undefined' is not assignable to type 'string'` for `value`, `label`, `displayName`, or `email`):** This is the expected outcome. Proceed to Step 2.
- **Outcome C (build fails with errors elsewhere or with different error messages):** STOP. The plan's assumptions are wrong. Report the error verbatim before changing anything else.

- [ ] **Step 2: Apply the narrowing fallback in the consumer (only if Outcome B)**

Edit `frontend/src/components/common/ResponsiblePersonCombobox.tsx` lines 45–50, changing:

```typescript
    const memberOptions: ResponsiblePersonSelectOption[] = members.map((member) => ({
      value: member.displayName,
      label: `${member.displayName} (${member.email})`,
      displayName: member.displayName,
      email: member.email,
    }));
```

to:

```typescript
    const memberOptions: ResponsiblePersonSelectOption[] = members.map((member) => ({
      value: member.displayName ?? '',
      label: `${member.displayName ?? ''} (${member.email ?? ''})`,
      displayName: member.displayName ?? '',
      email: member.email ?? '',
    }));
```

Notes:
- Do not touch any other line in this file. The `members` variable on line 44 (`response?.members || []`) already handles the `members?` optionality on the response.
- Do not introduce a helper function, a filter for missing `displayName`, or any other "improvement". The single-line `?? ''` fallback is the minimum-scope change authorized by the arch review (FR-4 caveat).

- [ ] **Step 3: Re-run the build and confirm it passes**

```bash
cd frontend && npm run build
```

Expected: build completes with no TypeScript errors. If errors remain, STOP and report.

---

## Task 5: Verify lint and tests are still clean

**Files:** None (verification only).

- [ ] **Step 1: Run lint**

```bash
cd frontend && npm run lint
```

Expected: lint exits 0 with the same warning count recorded in Task 1 Step 3. A new warning means we either left an unused import or violated a stylistic rule — fix before continuing.

- [ ] **Step 2: Run the useUserManagement unit test**

```bash
cd frontend && npx jest src/api/hooks/__tests__/useUserManagement.test.tsx
```

Expected: all tests pass without modification (FR-4 acceptance criterion). The tests assert on React Query lifecycle (`isLoading`, `failureCount`, etc.) and do not type-check the payload shape, so they should be unaffected by the swap.

- [ ] **Step 3: Run any tests covering the consumer (only if Step 2 of Task 4 modified the consumer)**

```bash
cd frontend && npx jest ResponsiblePersonCombobox
```

Expected: if a test exists, it passes. If no test exists, jest reports "no tests found" — that is acceptable; the spec does not require adding one.

- [ ] **Step 4: Sanity-diff the change**

```bash
git diff --stat
git diff frontend/src/api/hooks/useUserManagement.ts
```

Expected diff:
- `useUserManagement.ts`: −12 lines, +1 line (net −11), affecting only lines 1–15.
- `ResponsiblePersonCombobox.tsx` (if Outcome B in Task 4): 4 single-line edits within lines 46–49, no other changes.
- No other files modified.

If the diff shows changes outside these regions, revert them before proceeding.

---

## Task 6: Commit

**Files:** None (git operation).

- [ ] **Step 1: Stage and review**

```bash
git add frontend/src/api/hooks/useUserManagement.ts
# Only if Task 4 Step 2 ran:
git add frontend/src/components/common/ResponsiblePersonCombobox.tsx
git diff --cached
```

Confirm one final time that the staged diff matches the expectations in Task 5 Step 4.

- [ ] **Step 2: Commit**

```bash
git commit -m "$(cat <<'EOF'
refactor(frontend): consume generated UserDto types in useUserManagement hook

Removes the hand-rolled UserDto and GetGroupMembersResponse interfaces from
useUserManagement.ts and imports them from the auto-generated API client
instead. This was the only hook re-declaring generated DTOs, so the codebase
now uniformly trusts the OpenAPI → NSwag pipeline as the single source of
truth for these shapes and contract drift becomes structurally impossible.

The generated UserDto declares displayName/email as optional, so
ResponsiblePersonCombobox.tsx gets the minimum-scope narrowing fallback
(?? '') at the call site rather than widening the generated types.

Spec: artifacts/feat-arch-review-usermanagement-frontend-hook/spec.r1.md
EOF
)"
```

Expected: commit succeeds. If a pre-commit hook fails, fix the underlying issue and create a **new** commit — do not `--amend`.

- [ ] **Step 3: Note the deferred follow-ups in the PR description (not in this commit)**

The architecture review surfaced two adjacent cleanups that are explicitly Out of Scope here. Capture them in the PR description so they aren't lost:

1. Replace `(apiClient as any).http.fetch('/api/ManufactureOrder/responsible-persons', ...)` in `useUserManagement.ts:23-27` with `apiClient.manufactureOrder_GetResponsiblePersons()` — eliminates the `as any` cast and uses the generated request pipeline.
2. Audit `useMeetingTasks.ts` (already TODO'd) once `/api/meeting-tasks` is added to the NSwag spec; it still declares ~95 lines of manual DTOs for the same drift-hazard reason.

Do not file these as separate issues from inside this plan — the brief is a single PR. Just mention them in the PR body so the reviewer can decide whether to open follow-up issues.

---

## Self-review checklist (executor: skip; author: complete)

- **Spec coverage:** FR-1 (delete interfaces) → Task 3. FR-2 (add import + compile) → Task 3 + Task 4. FR-3 (consumer compatibility) → Task 2 Step 3 (verification only — no consumers reference the DTO symbols). FR-4 (behavioral parity) → Task 4 (narrowing fallback authorized by arch review amendment) + Task 5 Step 4 (diff scope check). NFR-1/2/3 — no work required, just don't violate them.
- **Placeholder scan:** No "TBD" / "implement later" / "add appropriate error handling" / "similar to Task N" anywhere. Every edit step contains the full before/after code.
- **Type consistency:** `GetGroupMembersResponse` and `UserDto` reference the generated symbols throughout. The narrowing pattern (`?? ''`) is the same in all four lines of the consumer edit.
