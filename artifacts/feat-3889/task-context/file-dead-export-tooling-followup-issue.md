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
