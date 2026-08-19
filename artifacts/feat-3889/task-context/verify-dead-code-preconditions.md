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

