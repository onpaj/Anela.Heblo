### task: validate-full-build-and-import-confinement

**Files:**
- (no file changes — verification only)

- [ ] **Step 1: Confirm the cross-module import is confined to `ExpeditionJobControlsBar.tsx`**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi
grep -rln "useRunExpeditionListPrintFix" frontend/src/pages frontend/src/components/pages/ExpeditionListArchive
```

Expected: exactly one match — `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx`. This satisfies FR-4's acceptance criterion.

- [ ] **Step 2: Run the full frontend build**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi/frontend
npm run build
```

Expected: build succeeds with no TypeScript errors.

- [ ] **Step 3: Run lint**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi/frontend
npm run lint
```

Expected: no new lint errors introduced by this refactor (pre-existing warnings elsewhere in the codebase, if any, are out of scope).

- [ ] **Step 4: Run the full Jest suite one more time**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi/frontend
npm test -- --watchAll=false
```

Expected: all suites pass.

- [ ] **Step 5: Confirm `App.tsx` is untouched**

```bash
cd /home/user/worktrees/feature-4000-Arch-Review-Expeditionlistarchive-Frontend-Page-Mi
git diff --stat origin/main -- frontend/src/App.tsx
```

Expected: no output (empty diff) — the route `/logistics/expedition-archive` and the `ExpeditionListArchivePage` import path are unchanged, per FR-1's acceptance criteria.

- [ ] **Step 6: No commit needed**

This task is verification-only; no files were changed. If any step above fails, fix the issue in the relevant earlier task's files, re-run that task's test/build commands, then re-run this task's steps from Step 1.
