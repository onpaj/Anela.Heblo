### task: update-process-doc

**Files:**
- Modify: `docs/processes/sync-plaud-recordings.md`

- [ ] **Step 1: Add the Known quirk**

Under `## Known quirks` in `docs/processes/sync-plaud-recordings.md`, add:

```markdown
- **The CLI timeout kill must never throw.** `PlaudCliClient` kills the CLI process tree from a
  `CancellationToken` callback (after `ProcessTimeoutSeconds`, default 60 s). That callback runs on a
  timer thread, so an exception escaping it terminates the whole host. Until #4462 an unguarded
  `Process.Kill` on an already-exited process threw `InvalidOperationException` and caused the
  2026-10-09 production outage (container exit 139, ~3 min restarts, 502/503). The kill now goes
  through `TryKillProcessTree`, which swallows and logs kill failures, and the registration is
  disposed before the `Process`.
```

- [ ] **Step 2: Bump `verified_at`**

Set the `verified_at:` front-matter value to the current `git rev-parse --short HEAD`, quoted as in the existing line.

- [ ] **Step 3: Check the index**

Run: `python3 scripts/process-docs/check.py index`
Expected: exits 0 with no staleness or index errors.

- [ ] **Step 4: Commit**

```bash
git add docs/processes/sync-plaud-recordings.md
git commit -m "docs(plaud): document timeout kill-callback quirk (#4462)"
```

