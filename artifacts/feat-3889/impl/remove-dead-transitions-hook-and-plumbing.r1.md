# Implementation: remove-dead-transitions-hook-and-plumbing

## What was implemented

Removed the dead `useTransportBoxTransitions.ts` hook and every piece of its
supporting plumbing, in the mandatory dependency order (call site → key →
file → test mocks → doc), landed as a single atomic commit per the task's
ORDER-CRITICAL constraint.

## Files created/modified

- `frontend/src/api/hooks/useTransportBoxes.ts` — removed the 5-line dead
  `invalidateQueries({ queryKey: [...QUERY_KEYS.transportBoxTransitions, ...] })`
  call from `useChangeTransportBoxState`'s `onSuccess`. The remaining four
  `invalidateQueries` + one `refetchQueries` calls are unchanged and in the
  same order.
- `frontend/src/api/client.ts` — deleted the
  `transportBoxTransitions: ["transportBoxTransitions"] as const,` entry
  from `QUERY_KEYS`. The `transportBox` root key and every other entry are
  untouched.
- `frontend/src/api/hooks/useTransportBoxTransitions.ts` — deleted (`git rm`).
  Removed the file-local `ApiClientWithInternals` interface, the exported
  `AllowedTransition` / `GetAllowedTransitionsResponse` interfaces, and the
  exported `useAllowedTransitionsQuery` hook. No replacement module created.
- `frontend/src/api/hooks/__tests__/useTransportBoxes.test.ts` — removed the
  `transportBoxTransitions` line from the `jest.mock("../../client", ...)`
  `QUERY_KEYS` literal.
- `frontend/src/components/pages/__tests__/TransportBoxList.test.tsx` —
  removed the `transportBoxTransitions` line from the equivalent mock.
- `frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx`
  — removed the `transportBoxTransitions` line from the equivalent mock.
- `docs/architecture/module-map.md` — module #7 (Transport Boxes) "Owns"
  list: dropped `useTransportBoxTransitions.ts` from the hooks bullet
  (ordinary dead-reference line edit, not a RETIRED marker; module keeps its
  number, title, and every other field).

## Tests

No new tests written — this is a pure dead-code deletion with no new
behaviour to specify. The TDD guard was the pre-existing
`useChangeTransportBoxState › should call API and invalidate queries on
success` test, which passed unchanged.

Ran the three directly affected suites:

```
CI=true npm test -- --watchAll=false --testPathPattern="(api/hooks/__tests__/useTransportBoxes\.test\.ts|pages/__tests__/TransportBoxList\.test\.tsx|pages/__tests__/TransportBoxList\.stockUpGate\.test\.tsx)"
```

Result: `Test Suites: 3 passed, 3 total`, `Tests: 39 passed, 39 total`, exit
code 0. `useTransportBoxes.test.ts` alone: 12/12 passed, matching the
baseline recorded in the previous task's Step 5 exactly, including the
target test.

## How to verify

1. `grep -rn "useTransportBoxTransitions\|useAllowedTransitionsQuery\|GetAllowedTransitionsResponse\|transportBoxTransitions" frontend/src frontend/test` → zero output, exit 1.
2. `grep -rn "useTransportBoxTransitions" docs/` → zero output, exit 1.
3. `ls frontend/src/api/hooks/useTransportBoxTransitions.ts` → no such file.
4. `npx tsc --noEmit -p tsconfig.json` (from `frontend/`) → no errors reference `frontend/src/**`; the only errors are pre-existing `node_modules/react-i18next/*.d.ts` parse failures (confirmed present on the unmodified baseline too, via `git stash` before/after comparison — pre-existing `typescript@^4.9.5` vs `react-i18next@15.7.4`'s `typescript@^5` peer-dependency mismatch, unrelated to this task).
5. `npm run lint` (from `frontend/`) → no errors in any of the 6 files this task touched; the 175 errors/13 warnings reported are pre-existing baseline issues in unrelated files (verified via `grep` filtering the lint output for the touched paths).
6. `git show --stat HEAD` → all 7 changed paths (6 task files + `useTransportBoxes.ts`) in one commit, 1 insertion / 59 deletions, no intermediate commit.

## Notes

- `frontend/node_modules` was absent at task start (same pre-existing
  condition noted in the previous task); `npm ci --legacy-peer-deps`
  installed it without modifying `package-lock.json`.
- `tsc --noEmit` and `npm run lint` both surface pre-existing,
  change-unrelated noise (a `react-i18next`/`typescript` peer-dependency
  type-declaration parse failure, and ~175 baseline `testing-library`/`import`
  lint errors across the wider test suite). Verified both are present
  identically on the pre-task commit via `git stash` / `git stash pop`
  around the `tsc` run, and confirmed via targeted `grep` that none of the
  lint errors are in the 6 files this task modified. Neither blocks this
  task; both are pre-existing repo conditions outside its scope.
- Diff reviewed line-by-line before committing (task Step 9): pure deletions
  plus the single one-line `module-map.md` replacement, exactly 7 files,
  matching the task's expected `--stat`. No changes under `backend/`,
  `frontend/src/api/generated/`, `docs/superpowers/`, or
  `frontend/src/components/transport/`.

## PR Summary
Deleted the dead `useTransportBoxTransitions.ts` hook (a fictional client for a backend route, `GET /api/transport-boxes/{boxId}/allowed-transitions`, that has never existed) along with its `QUERY_KEYS.transportBoxTransitions` registry entry, the one dead `invalidateQueries` call site that referenced it, and the matching key in three `jest.mock` test doubles. Also dropped the stale file reference from the architecture module map.

This is a pure no-op removal: nothing ever produced a query under that key, so the removed `invalidateQueries` call always matched zero cached queries. Allowed transitions are already served inline on `TransportBoxDto` via `TransportBoxMappingProfile`, and `TransportBoxActions.tsx` already reads them from there — this dead hook had zero importers.

### Changes
- `frontend/src/api/hooks/useTransportBoxes.ts` — removed dead `invalidateQueries` call from `onSuccess`
- `frontend/src/api/client.ts` — removed `QUERY_KEYS.transportBoxTransitions`
- `frontend/src/api/hooks/useTransportBoxTransitions.ts` — deleted (dead hook, types, zero importers)
- `frontend/src/api/hooks/__tests__/useTransportBoxes.test.ts` — removed stale mock key
- `frontend/src/components/pages/__tests__/TransportBoxList.test.tsx` — removed stale mock key
- `frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx` — removed stale mock key
- `docs/architecture/module-map.md` — dropped stale hook reference from module #7's "Owns" list

## Status
DONE
