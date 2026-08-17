# Remove dead `useTransportBoxTransitions` hook and its orphaned React Query plumbing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the unreachable `useAllowedTransitionsQuery` hook, the `QUERY_KEYS.transportBoxTransitions` plumbing that existed only to serve it (source + three Jest mock literals), and the architecture-map line that lists the file — with zero runtime behaviour change.

**Architecture:** Allowed transport-box state transitions are computed server-side by `TransportBoxStateNode.GetAllTransitions()` and projected inline onto every `TransportBoxDto` by `TransportBoxMappingProfile`, so `TransportBoxActions.tsx` reads them straight off the box DTO — no network call for transitions has ever been needed. A parallel, never-imported hook raw-fetches `GET /api/transport-boxes/{boxId}/allowed-transitions`, a route that does not exist on the backend, and declares a fictional response shape. This plan removes that entire dead mechanism in **one atomic commit** applied in a mandatory order (call site → key → file → test mocks → doc), because `useTransportBoxes.ts:189` *spreads* the key (`[...QUERY_KEYS.transportBoxTransitions, variables.boxId]`) — stripping a Jest mock entry before the source call site would evaluate `[...undefined]` and throw a `TypeError` at test runtime.

**Tech Stack:** React 18 + TypeScript 4.9 (CRA / `react-scripts` 5), `@tanstack/react-query` 5, Jest via `react-scripts test`, ESLint. Backend (.NET 8) is read-only for this work. `gh` CLI for the required follow-up issue.

**Working directory:** the git worktree at `/home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti`, on branch `feature/3889-Arch-Review-Transportboxes-Usetransportboxtransiti`. All frontend commands run from the `frontend/` subdirectory unless stated otherwise.

**Merge gate (arch review A-6 — no E2E gate):** `npm run build` succeeds, `npm run lint` reports no new warnings, and the frontend Jest suite passes via `CI=true npm test -- --coverage --watchAll=false`. A pre-merge `./scripts/run-playwright-tests.sh` run is **not** required and must **not** block this PR — the harness hardcodes `https://heblo.stg.anela.cz` (`scripts/run-playwright-tests.sh:27,77`), so it exercises the deployed staging build rather than this branch and can produce no evidence about this change. The nightly staging E2E run for the `transport` project is the post-deploy regression backstop.

---

## File Structure

Six files change. **No file is created.** A dead-code removal that adds a file has failed.

| File | Action | Responsibility after the change |
|---|---|---|
| `frontend/src/api/hooks/useTransportBoxTransitions.ts` | **Delete** (49 lines, whole file) | — gone. It owned `ApiClientWithInternals` (file-local), `AllowedTransition`, `GetAllowedTransitionsResponse`, `useAllowedTransitionsQuery`, all with zero importers. |
| `frontend/src/api/hooks/useTransportBoxes.ts` | Modify (remove lines 187–191) | Unchanged responsibility: owns every transport-box query/mutation hook and the `transportBoxKeys` factory. After the edit, `useChangeTransportBoxState.onSuccess` performs exactly five cache operations (see task `remove-dead-transitions-hook-and-plumbing`). |
| `frontend/src/api/client.ts` | Modify (remove line 490) | Unchanged responsibility: owns the shared `QUERY_KEYS` registry (~40 members). Only `transportBoxTransitions` is removed; the neighbouring `transportBox: ["transport-boxes"]` on line 489 is the root namespace for all transport-box caching and must survive untouched. |
| `frontend/src/api/hooks/__tests__/useTransportBoxes.test.ts` | Modify (remove line 17) | Unchanged responsibility: unit tests for the transport-box hooks, including `useChangeTransportBoxState › should call API and invalidate queries on success` (line 181) — the regression guard for this change. Its `jest.mock("../../client")` literal drops the stale key. |
| `frontend/src/components/pages/__tests__/TransportBoxList.test.tsx` | Modify (remove line 71) | Unchanged responsibility: component tests for the transport box list. Its `jest.mock("../../../api/client")` literal drops the stale key; `catalog`, `transportBox`, `stockUpOperations` stay. |
| `frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx` | Modify (remove line 60) | Same as above for the stock-up gate suite. |
| `docs/architecture/module-map.md` | Modify (edit line 258) | Unchanged responsibility: the numbered partition of the repo that `/arch-review` samples from. Module #7's "Owns" bullet drops the deleted file. |

**Explicitly not touched:**

- All of `backend/` — read-only. No `dotnet build` / `dotnet format` requirement, no migration, no config, no Key Vault entry, no feature flag.
- `frontend/src/api/generated/api-client.ts` — generated from an unchanged backend contract. Do not regenerate, never hand-edit.
- `frontend/src/components/transport/box-detail/TransportBoxActions.tsx` and every other transport UI component.
- `docs/superpowers/plans/**` — point-in-time records. `docs/superpowers/plans/2026-06-13-telemetry-stockupoperations-summary-403-storm.md:629` quotes `transportBoxTransitions` inside a `jest.mock` literal; that hit is **expected to remain** and must not be edited.
- The other six independent local declarations of `ApiClientWithInternals` (`useTransportBoxes.ts:15`, `TransportBoxTypes.tsx:15`, `useBoxFill.ts`, `usePackingUsers.ts`, `printLabelPdf.ts`, `TransportBoxDetail.tsx`). Deleting the hook removes only its own copy and leaves no unresolved import. Do **not** consolidate them — that is a separate refactor and violates the surgical-changes rule in `CLAUDE.md`.
- Do **not** implement `GET /api/transport-boxes/{id}/allowed-transitions`. The inline DTO field is the single source of truth; a second read path would be a second contract to keep in sync.

**Commit structure:** exactly one commit for the code + doc change (mandated by arch review FR-6 / A-2), plus a separate `gh issue create` follow-up that touches no files.

---

### task: verify-dead-code-preconditions

Read-only proof step. Establishes the evidence that the code being deleted is genuinely unreachable **before** anything is removed, so the removal task can proceed without hedging. No file is modified and nothing is committed in this task.

**Files:** none modified. Files inspected:
- `frontend/src/api/hooks/useTransportBoxTransitions.ts`
- `frontend/src/api/hooks/useTransportBoxes.ts`
- `frontend/src/api/client.ts`
- `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs`

- [ ] **Step 1: Confirm you are in the right worktree and on the right branch**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
git rev-parse --abbrev-ref HEAD
git status --short
```

Expected: branch is `feature/3889-Arch-Review-Transportboxes-Usetransportboxtransiti`, and `git status --short` shows no modifications under `frontend/` or `docs/` (files under `artifacts/` may appear — that is fine).

- [ ] **Step 2: Prove the backend route does not exist**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
grep -rn "allowed-transitions\|GetAllowedTransitions" backend/src --include=*.cs
```

Expected: **zero output** (exit code 1). If this prints anything, STOP — the premise of the change is wrong; report it and do not proceed.

- [ ] **Step 3: Prove the hook has zero importers and enumerate every reference to the plumbing**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
grep -rn "useTransportBoxTransitions\|useAllowedTransitionsQuery\|GetAllowedTransitionsResponse\|transportBoxTransitions" frontend/src frontend/test docs/
```

Expected: exactly these 10 hits and no others.

```
frontend/src/components/pages/__tests__/TransportBoxList.test.tsx:71
frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx:60
frontend/src/api/hooks/__tests__/useTransportBoxes.test.ts:17
frontend/src/api/hooks/useTransportBoxTransitions.ts:17
frontend/src/api/hooks/useTransportBoxTransitions.ts:24
frontend/src/api/hooks/useTransportBoxTransitions.ts:29
frontend/src/api/hooks/useTransportBoxTransitions.ts:30
frontend/src/api/hooks/useTransportBoxes.ts:189
frontend/src/api/client.ts:490
docs/architecture/module-map.md:258
```

Plus **one expected extra hit that must NOT be edited**: `docs/superpowers/plans/2026-06-13-telemetry-stockupoperations-summary-403-storm.md:629` (a quoted `jest.mock` literal inside a historical plan). Note it and move on.

Critically: no line in that output is an `import` of `useTransportBoxTransitions` from any other module. There is also no barrel file — verify:

```bash
ls /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti/frontend/src/api/hooks/index.ts
```

Expected: `No such file or directory`. Without a barrel, the hook's exports are reachable only by a direct path import, and the grep above proves none exists.

- [ ] **Step 4: Record the pre-change baseline of the mutation's `onSuccess` handler**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
sed -n '177,201p' frontend/src/api/hooks/useTransportBoxes.ts
```

Expected output (this is the exact text you will be editing in the next task):

```ts
    onSuccess: (data, variables) => {
      // Invalidate and refetch related queries
      queryClient.invalidateQueries({
        queryKey: transportBoxKeys.detail(variables.boxId),
      });
      queryClient.invalidateQueries({ queryKey: transportBoxKeys.lists() });
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.transportBox, "summary"],
      });

      // Also invalidate any transition-related queries
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.transportBoxTransitions, variables.boxId],
      });

      // Invalidate byCode cache so the scan lookup reflects the new state
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.transportBox, 'byCode'],
      });

      // Force refetch of the specific box detail to ensure fresh data
      queryClient.refetchQueries({
        queryKey: transportBoxKeys.detail(variables.boxId),
      });
    },
```

- [ ] **Step 5: Establish the green baseline for the regression guard**

The test `useChangeTransportBoxState › should call API and invalidate queries on success` (`frontend/src/api/hooks/__tests__/useTransportBoxes.test.ts:181`) exercises the handler you are about to edit. Confirm it is green *before* the change, so a later failure is unambiguously yours.

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti/frontend
CI=true npm test -- --watchAll=false --testPathPattern="api/hooks/__tests__/useTransportBoxes.test.ts"
```

Expected: `Tests: 12 passed` (or whatever the current count is — record it), `Test Suites: 1 passed`, exit code 0.

- [ ] **Step 6: Do not commit**

This task produces no diff. Confirm:

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
git status --short frontend docs
```

Expected: empty output. Proceed to the next task.

---

### task: remove-dead-transitions-hook-and-plumbing

**This is the whole change, and it MUST land as ONE commit.** Arch review FR-6 / A-2 makes the ordering mandatory: `frontend/src/api/hooks/useTransportBoxes.ts:189` *spreads* the query key (`[...QUERY_KEYS.transportBoxTransitions, variables.boxId]`). If a Jest mock literal loses the key while that source line still exists, the spread evaluates `[...undefined]` inside `onSuccess` and throws a `TypeError` at test runtime — not a silent `undefined`. Symmetrically, removing `client.ts:490` while any consumer remains is a hard TypeScript compile failure. **Do not commit between steps 1 and 5.** Do not let any intermediate state be independently checked out.

Order: (1) call site → (2) key → (3) file → (4) test mocks → (5) doc.

**Files:**
- Modify: `frontend/src/api/hooks/useTransportBoxes.ts:186-191`
- Modify: `frontend/src/api/client.ts:490`
- Delete: `frontend/src/api/hooks/useTransportBoxTransitions.ts`
- Modify: `frontend/src/api/hooks/__tests__/useTransportBoxes.test.ts:17`
- Modify: `frontend/src/components/pages/__tests__/TransportBoxList.test.tsx:71`
- Modify: `frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx:60`
- Modify: `docs/architecture/module-map.md:258`

There is **no new test to write** in this task. This is a pure deletion of unreachable code with no new behaviour to specify; the TDD guard is the pre-existing test `useChangeTransportBoxState › should call API and invalidate queries on success`, which must keep passing **unchanged**. Do not add, rename, or weaken any assertion in it.

- [ ] **Step 1 (ORDER-CRITICAL, FIRST): Remove the dead invalidation from the mutation's `onSuccess`**

In `frontend/src/api/hooks/useTransportBoxes.ts`, replace this exact text:

```ts
      // Also invalidate any transition-related queries
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.transportBoxTransitions, variables.boxId],
      });

      // Invalidate byCode cache so the scan lookup reflects the new state
```

with this exact text:

```ts
      // Invalidate byCode cache so the scan lookup reflects the new state
```

(That deletes lines 187–191 — the comment, the three-line call, and the trailing blank line — leaving the blank line 186 as the single separator before the byCode comment, so no double blank remains.)

The handler must now read **exactly** this, with exactly these five cache operations in this order and no others:

```ts
    onSuccess: (data, variables) => {
      // Invalidate and refetch related queries
      queryClient.invalidateQueries({
        queryKey: transportBoxKeys.detail(variables.boxId),
      });
      queryClient.invalidateQueries({ queryKey: transportBoxKeys.lists() });
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.transportBox, "summary"],
      });

      // Invalidate byCode cache so the scan lookup reflects the new state
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.transportBox, 'byCode'],
      });

      // Force refetch of the specific box detail to ensure fresh data
      queryClient.refetchQueries({
        queryKey: transportBoxKeys.detail(variables.boxId),
      });
    },
```

**Do not touch anything else in this file.** In particular, leave byte-identical:
- the `transportBoxKeys` factory at lines 45–52 and its `export { transportBoxKeys };` at line 205;
- the local `ApiClientWithInternals` interface at line 15;
- the second, unrelated `onSuccess` handler further down the file (~lines 238–240) that invalidates `manufacturedProductInventory`.

Why this is provably behaviour-neutral: React Query's `invalidateQueries` marks *matching cached queries* stale and refetches the active ones. The only producer of a `["transportBoxTransitions", …]` key was the never-imported hook, so the match set was empty on every invocation since the key was introduced. Removal turns an empty operation into no operation. Operation 5 (`refetchQueries` on the box detail) is what actually re-renders the transition buttons after a state change — the refetched `TransportBoxDto` carries the new state's `allowedTransitions`.

- [ ] **Step 2 (ORDER-CRITICAL, SECOND): Remove the query-key registry entry**

In `frontend/src/api/client.ts`, delete line 490 in its entirety:

```ts
  transportBoxTransitions: ["transportBoxTransitions"] as const,
```

The surrounding `QUERY_KEYS` block must otherwise be untouched — in particular line 489, `transportBox: ["transport-boxes"] as const,`, is the root namespace for **all** transport-box caching (detail, list, summary, byCode) and must survive. Do not reorder, reformat, or re-indent any other member. After the edit, that region reads:

```ts
  photobank: ["photobank"] as const,
  transportBox: ["transport-boxes"] as const,
  manufactureOutput: ["manufacture-output"] as const,
```

- [ ] **Step 3 (ORDER-CRITICAL, THIRD): Delete the dead hook file**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
git rm frontend/src/api/hooks/useTransportBoxTransitions.ts
```

Expected: `rm 'frontend/src/api/hooks/useTransportBoxTransitions.ts'`.

This removes, all with zero importers: the file-local `ApiClientWithInternals` (line 5), the exported `AllowedTransition` (line 10) and `GetAllowedTransitionsResponse` (line 17) interfaces — a fictional contract whose only field overlapping reality is `label` — and the exported `useAllowedTransitionsQuery` hook (line 24). No replacement module is created.

- [ ] **Step 4 (ORDER-CRITICAL, FOURTH): Remove the stale key from the three Jest mock literals**

These are `jest.mock` factories that hand-stub the `../client` module; they are test doubles of the `QUERY_KEYS` contract edited in Step 2. **Remove only the `transportBoxTransitions` line from each.** Every other key in each literal is still real and still read — leave `catalog`, `transportBox`, `stockUpOperations` alone, and do not touch `getAuthenticatedApiClient: jest.fn()` or the sibling `jest.mock(".../generated/api-client", …)` factories.

4a. `frontend/src/api/hooks/__tests__/useTransportBoxes.test.ts` — replace:

```ts
jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    transportBox: ["transport-boxes"],
    transportBoxTransitions: ["transportBoxTransitions"],
  },
}));
```

with:

```ts
jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    transportBox: ["transport-boxes"],
  },
}));
```

4b. `frontend/src/components/pages/__tests__/TransportBoxList.test.tsx` — replace:

```ts
jest.mock("../../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    catalog: ["catalog"],
    transportBox: ["transport-boxes"],
    transportBoxTransitions: ["transportBoxTransitions"],
    stockUpOperations: ["stock-up-operations"],
  },
}));
```

with:

```ts
jest.mock("../../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    catalog: ["catalog"],
    transportBox: ["transport-boxes"],
    stockUpOperations: ["stock-up-operations"],
  },
}));
```

4c. `frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx` — the mock literal there is identical to 4b's; apply the identical replacement (remove the single line `    transportBoxTransitions: ["transportBoxTransitions"],`).

- [ ] **Step 5 (ORDER-CRITICAL, FIFTH): Update the architecture module map**

In `docs/architecture/module-map.md`, line 258 (module #7, Transport Boxes, "Owns" list), replace:

```
- `frontend/src/api/hooks/useTransportBoxes.ts`, `useTransportBoxReceive.ts`, `useTransportBoxTransitions.ts`
```

with:

```
- `frontend/src/api/hooks/useTransportBoxes.ts`, `useTransportBoxReceive.ts`
```

This is a line edit, **not** a RETIRED marker. `docs/architecture/module-map-maintenance.md` reserves RETIRED for whole parts removed from the codebase and forbids renumbering; one file dropping out of a part's "Owns" list is an ordinary "dead reference" fix. Module #7 keeps its number, title, size band, every other "Owns" bullet, `**Depends on:** #1, #5.`, its "Analysis notes", and its summary-table row — all byte-identical. Do not touch any other module.

- [ ] **Step 6: Verify no reference survives**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
grep -rn "useTransportBoxTransitions\|useAllowedTransitionsQuery\|GetAllowedTransitionsResponse\|transportBoxTransitions" frontend/src frontend/test
echo "frontend exit: $?"
grep -rn "useTransportBoxTransitions" docs/
echo "docs exit: $?"
ls frontend/src/api/hooks/useTransportBoxTransitions.ts
```

Expected:
- First grep: **zero output**, `frontend exit: 1`.
- Second grep: **zero output**, `docs exit: 1`.
- `ls`: `No such file or directory`.

Do **not** run `grep -rn "transportBoxTransitions" docs/` as a pass/fail check — it will legitimately still return `docs/superpowers/plans/2026-06-13-telemetry-stockupoperations-summary-403-storm.md:629`, a historical plan quoting an old `jest.mock` literal, which must not be edited (arch review A-3/A-4).

- [ ] **Step 7: Type-check and lint**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti/frontend
npx tsc --noEmit -p tsconfig.json
npm run lint
```

Expected: `tsc` produces no output and exits 0 (in particular, no `Property 'transportBoxTransitions' does not exist on type ...`, which is what you would see if Step 2 had run without Step 1). `npm run lint` exits 0 with no new warnings.

- [ ] **Step 8: Run the three directly affected test suites**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti/frontend
CI=true npm test -- --watchAll=false --testPathPattern="(api/hooks/__tests__/useTransportBoxes\.test\.ts|pages/__tests__/TransportBoxList\.test\.tsx|pages/__tests__/TransportBoxList\.stockUpGate\.test\.tsx)"
```

Expected: `Test Suites: 3 passed, 3 total`, all tests passing, exit code 0. The count in `useTransportBoxes.test.ts` must match the baseline recorded in the previous task's Step 5.

If you see `TypeError: ... is not iterable` or `Spread syntax requires ...iterable` inside `onSuccess`, you removed a mock key (Step 4) without having removed the source call site (Step 1). Fix by completing Step 1 — do not "fix" it by restoring the mock key.

- [ ] **Step 9: Review the diff for over-deletion before committing**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
git add -A frontend/src docs/architecture/module-map.md
git diff --cached --stat
git diff --cached
```

Expected `--stat`: exactly 6 files — `frontend/src/api/client.ts` (1 deletion), `frontend/src/api/hooks/useTransportBoxTransitions.ts` (49 deletions, file gone), `frontend/src/api/hooks/useTransportBoxes.ts` (5 deletions), the three test files (1 deletion each), and `docs/architecture/module-map.md` (1 insertion, 1 deletion).

Check line by line: the diff must contain **only deletions**, plus the single one-line replacement in `module-map.md`. Any added line of TypeScript, any reformatting, any renamed symbol, any change to `TransportBoxActions.tsx`, any change under `backend/`, `frontend/src/api/generated/`, or `docs/superpowers/plans/` means you have overstepped — revert it. Confirm the four surviving `invalidateQueries` and the one `refetchQueries` in `useTransportBoxes.ts` are all still present in the post-image.

```bash
git status --short docs/superpowers backend frontend/src/api/generated frontend/src/components/transport
```

Expected: empty output.

- [ ] **Step 10: Commit — one commit, all of it**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
git commit -m "$(cat <<'EOF'
refactor(frontend): remove dead useTransportBoxTransitions hook and its query-key plumbing

The hook raw-fetched GET /api/transport-boxes/{boxId}/allowed-transitions,
a route that has never existed on the backend, and declared a response shape
(state/requiresCondition/conditionDescription) that matches no backend contract.
It had zero importers. Allowed transitions already ship inline on every
TransportBoxDto via TransportBoxMappingProfile, and TransportBoxActions.tsx
reads them from there.

Removed in dependency order so no intermediate state is broken:
the sole invalidateQueries call site in useTransportBoxes.ts, the
QUERY_KEYS.transportBoxTransitions entry in client.ts, the hook file itself,
the key from three jest.mock QUERY_KEYS literals, and the stale path in
docs/architecture/module-map.md.

No behaviour change: nothing ever registered a query under that key, so the
removed invalidation was always a no-op. No backend, generated-client, or
transport UI change.
EOF
)"
git log --oneline -1
```

Expected: one new commit. Verify the atomicity requirement holds:

```bash
git show --stat HEAD
```

Expected: all six file changes appear in that single commit. There must be no intermediate commit in which a `jest.mock` literal lacks the key while `useTransportBoxes.ts` still references it, nor one in which `client.ts:490` is gone while a consumer remains.

---

### task: validate-frontend-gates

Run the full merge gate defined by arch review A-6. This task changes no source file; if a gate fails, fix the cause in the previous task's files and amend that commit (`git commit --amend --no-edit`) so the removal stays a single commit.

**Files:** none modified (fixes, if any, go back into the six files listed in the previous task and are amended into its commit).

- [ ] **Step 1: Production build**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti/frontend
npm run build
```

Expected: `Compiled successfully.` (warnings that already existed on the branch before this change are acceptable; a *new* warning is not). Exit code 0. There must be no `Attempted import error` or `Property 'transportBoxTransitions' does not exist` — either would mean the removal order in the previous task was not followed.

Note: the build must not grow. There is no bundle-size budget in this repo, so no numeric threshold applies; a marginal decrease (~49 lines of source plus one `QUERY_KEYS` entry) is expected.

- [ ] **Step 2: Lint**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti/frontend
npm run lint
```

Expected: exit code 0, no new warnings versus the pre-change branch.

- [ ] **Step 3: Full Jest suite, matching CI exactly**

This is the same invocation as `.github/workflows/ci-feature-branch.yml:45`.

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti/frontend
CI=true REACT_APP_USE_MOCK_AUTH=true npm test -- --coverage --watchAll=false
```

Expected: `Test Suites: … passed`, `Tests: … passed`, zero failures, exit code 0.

There is no `coverageThreshold` in `frontend/package.json`'s `jest` block (only `transformIgnorePatterns`), so deleting an uncovered file cannot fail CI on a coverage gate. Do **not** add one defensively.

- [ ] **Step 4: Confirm no E2E gate is run**

Do **not** run `./scripts/run-playwright-tests.sh` as a merge gate. `scripts/run-playwright-tests.sh:27` hardcodes `STAGING_URL="https://heblo.stg.anela.cz"` and exports it as `PLAYWRIGHT_BASE_URL` (line 77), and `docs/architecture/testing-strategy.md:248-251` requires the suite to always target deployed staging — so a pre-merge run exercises the deployed build, not this branch, and can produce no evidence about this change. The nightly staging run for the `transport` project (`frontend/test/e2e/transport/box-workflow.spec.ts`, `box-management.spec.ts`, `boxes-basic.spec.ts`) is the post-deploy regression backstop, and being green on the first run after deployment is the acceptance criterion. Record this reasoning in the PR description.

- [ ] **Step 5: Confirm the backend is genuinely untouched**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
git diff --name-only origin/main...HEAD -- backend/ frontend/src/api/generated/ docs/superpowers/
```

Expected: empty output. No `dotnet build` or `dotnet format` run is required, because no `.cs` file changed.

- [ ] **Step 6: No commit for this task**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
git status --short frontend docs
```

Expected: empty output (the `frontend/build/` directory produced by Step 1 is gitignored; if it shows up, do not add it). If any gate required a source fix, that fix must have been amended into the single removal commit, not committed separately.

---

### task: file-dead-export-tooling-followup-issue

**This is a required deliverable, not an optional nicety** (arch review Decision 6 / A-5). The systemic cause of this finding is that nothing in the frontend toolchain detects unreachable modules — `frontend/package.json` contains no `knip`, `ts-prune`, or `depcheck`. Adding a detector is deliberately out of scope for this PR (it would surface an unbounded backlog of pre-existing unused exports across a ~40-key `QUERY_KEYS` and hundreds of modules, swallowing a five-line deletion), but dropping it silently guarantees recurrence. File the issue and link it from the PR description.

**Files:** none. This task creates a GitHub issue and edits the PR body. Per `CLAUDE.md`, GitHub access is via the `gh` CLI **only** — never use MCP GitHub tools.

- [ ] **Step 1: Check the issue does not already exist**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
gh issue list --state open --search "knip ts-prune dead export" --limit 20
```

If an open issue already proposes a frontend dead-export detector, skip Step 2 and use that issue's number in Step 3.

- [ ] **Step 2: Create the issue**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
gh issue create \
  --title "frontend: add a dead-export detector (knip) to catch unreachable modules" \
  --body "$(cat <<'EOF'
## Motivation

`frontend/src/api/hooks/useTransportBoxTransitions.ts` survived in the tree with **zero importers**, raw-fetching `GET /api/transport-boxes/{boxId}/allowed-transitions` — a route that has never existed on the backend — and declaring a response shape (`state`, `requiresCondition`, `conditionDescription`) that matches no backend contract. It also dragged along a `QUERY_KEYS.transportBoxTransitions` entry, a permanently no-op `invalidateQueries` call site, and three `jest.mock` literals advertising the key.

Nothing detected it. `frontend/package.json` has no `knip`, `ts-prune`, or `depcheck`. Removing the hook (feat-3889) fixes this one instance; it does not stop the next one accumulating the same way.

## Proposal

Add `knip` to the frontend toolchain to report unused files, exports, and dependencies.

- Wire it as a **non-blocking** CI step first (`continue-on-error: true` in `.github/workflows/ci-feature-branch.yml`), so the pre-existing backlog does not fail the build on day one.
- Publish the initial report, triage it, and burn the backlog down incrementally.
- Promote the step to blocking once the report is clean.

`ts-prune` is the lighter-weight alternative if `knip`'s config surface proves too heavy for a CRA project.

## Scope notes

- Deliberately **not** bundled into the feat-3889 deletion PR: adding a detector changes CI behaviour repo-wide and will surface an unbounded remediation scope across a ~40-key `QUERY_KEYS` and hundreds of modules.
- Expect known noise sources: the generated OpenAPI client (`frontend/src/api/generated/api-client.ts`) and test-only exports will need ignore rules.

## Acceptance

- [ ] `knip` (or `ts-prune`) installed as a frontend devDependency with a checked-in config.
- [ ] A non-blocking CI step runs it on feature branches and publishes the report.
- [ ] The initial backlog of unused files/exports is triaged into a follow-up list.
EOF
)"
```

Expected: the command prints the URL of the new issue. Record the issue number.

- [ ] **Step 3: Link the issue from the PR description**

Once the PR for this branch exists, add to its body (create it if it does not exist yet, targeting `main` from `feature/3889-Arch-Review-Transportboxes-Usetransportboxtransiti`):

```
Follow-up (required, arch review A-5): #<ISSUE_NUMBER> — add a frontend dead-export detector so this class of finding is caught automatically.

Not gating on E2E: `scripts/run-playwright-tests.sh` hardcodes the deployed staging URL, so a pre-merge run exercises staging rather than this branch and carries no information about this change. Gate is `npm run build` + `npm run lint` + the frontend Jest suite; the nightly staging `transport` E2E run is the post-deploy backstop.

Not implementing the missing endpoint: allowed transitions already ship inline on every `TransportBoxDto` via `TransportBoxMappingProfile`, and that is the intended single source of truth. A standalone `GET /api/transport-boxes/{id}/allowed-transitions` would be a second read path for identical data — a second contract to keep in sync and a second cache to invalidate on every state change.
```

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
gh pr view --json number,url,body
```

Expected: the PR body contains the follow-up issue reference. If no PR exists yet, add the text when opening it.

- [ ] **Step 4: Confirm no file was changed by this task**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
git status --short
```

Expected: no changes under `frontend/` or `docs/`. The follow-up is tracked in GitHub, not in the tree.
