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

