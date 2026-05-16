# Backmerge main + ScanInput Convention Doc — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge `origin/main` (2 commits) back into `feature/pr-1298-base` and add a project-memory file that mandates `ScanInput.tsx` for all terminal barcode entry.

**Architecture:** `feature/pr-1298-base` is 9 ahead / 2 behind `origin/main`. The two missing commits are (a) the squash merge of PR #1298 (`bb42cb0f`) — whose individual commits already live on this branch — and (b) an unrelated smartsupp commit (`334a41c3`). Conflicts will be in `frontend/src/components/terminal/*` because Git sees the squash and the originals as different changes to the same files; resolution is always "keep this branch's version" since it is a strict superset. The smartsupp commit merges cleanly.

**Tech Stack:** Git (merge strategy), Markdown (convention doc), React/TypeScript (frontend verification)

---

## File map

| Action | Path |
|--------|------|
| No change (reference only) | `frontend/src/components/terminal/ScanInput.tsx` |
| No change (reference only) | `frontend/src/components/terminal/TransportBoxCheck.tsx` |
| No change (reference only) | `frontend/src/components/terminal/TransportBoxReceive.tsx` |
| **Create** | `memory/patterns/terminal-scan-input.md` |

---

### Task 1 — Merge `origin/main` into the feature branch

**Files:**
- Modify: git index (merge commit, no source file changes expected beyond conflict resolution)

- [ ] **Step 1: Verify starting state**

```bash
git status
git log --oneline HEAD..origin/main
```

Expected output of the second command:
```
bb42cb0f feat(terminal): add transport box check workflow (#1298)
334a41c3 feat(smartsupp): AI draft reply from KnowledgeBase (#1286)
```

If the list differs, stop and investigate before continuing.

- [ ] **Step 2: Start the merge**

```bash
git merge origin/main
```

Git will likely report conflicts in one or more of:
- `frontend/src/components/terminal/ScanInput.tsx`
- `frontend/src/components/terminal/TransportBoxCheck.tsx`
- `frontend/src/components/terminal/TransportBoxReceive.tsx`
- `frontend/src/components/terminal/BoxDetail.tsx`
- `frontend/src/components/terminal/TransportBoxDetail.tsx`
- `frontend/src/routes/TerminalRoutes.tsx` (or similar routing file)
- `frontend/src/components/terminal/TerminalHome.tsx`

The smartsupp commit touches unrelated files and should auto-merge.

- [ ] **Step 3: Resolve each conflicted file — keep THIS branch's version**

For every conflicted file, our branch is the superset: it contains everything the squash commit (`bb42cb0f`) added **plus** the receive workflow that was built on top. So the correct resolution for every terminal conflict is to accept the current-branch ("ours") side.

Check which files are conflicted:
```bash
git diff --name-only --diff-filter=U
```

For each conflicted file, check out our branch's version:
```bash
# Repeat for every file listed by the command above
git checkout --ours -- frontend/src/components/terminal/ScanInput.tsx
git checkout --ours -- frontend/src/components/terminal/TransportBoxCheck.tsx
git checkout --ours -- frontend/src/components/terminal/TransportBoxReceive.tsx
# ... add any other conflicted paths here
```

**Critical:** open each file after checking it out and confirm it contains the receive-workflow code (e.g., `TransportBoxReceive` import in routing, receive tile on the home screen). If any file looks incomplete, use `git diff HEAD -- <file>` to verify it matches your pre-merge version.

- [ ] **Step 4: Stage resolved files and commit**

```bash
git add frontend/   # stages all terminal files
git status          # confirm no remaining conflicts (no "both modified" lines)
git commit          # accept the auto-generated merge commit message
```

Expected: clean working tree with a new merge commit.

- [ ] **Step 5: Verify branch is now 0 behind origin/main**

```bash
git log --oneline HEAD..origin/main
```

Expected: no output (empty — we are fully caught up).

---

### Task 2 — Add terminal ScanInput convention doc

**Files:**
- Create: `memory/patterns/terminal-scan-input.md`

- [ ] **Step 1: Create the convention file**

Create `memory/patterns/terminal-scan-input.md` with this exact content:

```markdown
# Pattern: Terminal Barcode / Code Input

Every terminal screen that accepts a scanned or manually typed barcode,
box code, EAN, or similar identifier **MUST** use the shared component:

```
frontend/src/components/terminal/ScanInput.tsx
```

**Never** add a raw `<input>` or another ad-hoc text field for code entry.

## Props reference

| Prop | Type | Default | Purpose |
|------|------|---------|---------|
| `label` | `string` | — | Visible field label |
| `placeholder` | `string` | `'Naskenujte nebo zadejte kód...'` | Input hint |
| `onScan` | `(value: string) => void` | — | Called on Enter/submit |
| `loading` | `boolean` | `false` | Shows spinner, disables input |
| `uppercase` | `boolean` | `true` | Auto-uppercases typed value |
| `autoFocusOnMount` | `boolean` | `true` | Focuses input on render |
| `suppressKeyboard` | `boolean` | `false` | Hides software keyboard (scanner use) |
| `allowKeyboardToggle` | `boolean` | `false` | Shows keyboard toggle button |

## Existing consumers

- `TransportBoxCheck.tsx` — check workflow
- `TransportBoxReceive.tsx` — receive workflow

## Rationale

Physical barcode scanners emit keystrokes ending in Enter. ScanInput captures
these reliably (auto-focus, blur-refocus, uppercase normalisation) while also
supporting manual typing. All terminal screens need this behaviour; using a
raw input breaks scanner compatibility and inconsistency across screens.

## Future screens

When adding a new terminal screen (e.g. Inventura, Identifikace šarže) that
takes a code or barcode:

1. Import `ScanInput` from `../../components/terminal/ScanInput`.
2. Pass at minimum `label` and `onScan`.
3. Do **not** add a separate input element.
```

- [ ] **Step 2: Stage the new file**

```bash
git add memory/patterns/terminal-scan-input.md
git status
```

Expected: `memory/patterns/terminal-scan-input.md` listed under "Changes to be committed".

- [ ] **Step 3: Commit the convention doc**

```bash
git commit -m "docs(memory): add ScanInput convention for terminal screens"
```

---

### Task 3 — Verify build, lint, and tests

**Files:** (read-only verification, no edits)

- [ ] **Step 1: Frontend build**

```bash
cd frontend && npm run build
```

Expected: exits 0, no TypeScript or webpack errors.

- [ ] **Step 2: Frontend lint**

```bash
npm run lint
```

Expected: exits 0, no new lint errors compared to pre-merge.

- [ ] **Step 3: Terminal component unit tests**

```bash
npm test -- --testPathPattern=terminal --watchAll=false
```

Expected: all terminal tests pass (green).

- [ ] **Step 4: Spot-check the merge result in source**

Open `frontend/src/components/terminal/TransportBoxReceive.tsx` and confirm:
- `ScanInput` is imported and used (not a raw `<input>`)
- The receive workflow code is intact (loading state, API call, result rendering)

Open the routing file (e.g. `frontend/src/routes/TerminalRoutes.tsx` or equivalent) and confirm the `/terminal/receive` route is present.

If anything looks wrong, check `git diff origin/main HEAD -- <file>` to isolate the regression.

---

### Task 4 — Push the branch

- [ ] **Step 1: Push**

```bash
git push
```

Expected: branch pushed to `origin/feature/pr-1298-base`. If the remote rejects due to diverged history (because we merged), use `--force-with-lease` only if you are certain no other commits were pushed to this branch by anyone else:

```bash
git push --force-with-lease
```

- [ ] **Step 2: Confirm CI (if applicable)**

Check GitHub Actions / Azure CI to confirm the build is green on this branch. The E2E suite runs nightly so no E2E gate is expected here.
