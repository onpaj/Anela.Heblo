### task: update-process-doc

Project rule (CLAUDE.md): a change to a process's behaviour updates its `docs/processes/` doc in the same PR; CI's "Process Docs" job fails otherwise. The owning doc is `docs/processes/module-feature-flags.md`. Depends on both code tasks (it documents their final behaviour).

**Files:**
- Modify: `docs/processes/module-feature-flags.md`
- Regenerate: `docs/processes/INDEX.md` (via script; expected unchanged)

- [ ] **Step 1: Update the cache description in "Data owned"**

In `docs/processes/module-feature-flags.md`, replace this bullet:

```markdown
- In-memory cache `feature_flag_overrides` (per app instance, `IMemoryCache`): the whole override
  table as a dictionary, absolute TTL 30 s, removed immediately by the upsert / clear handlers on
  the instance that handled the write.
```

with:

```markdown
- In-memory cache `feature_flag_overrides` (per app instance, `IMemoryCache`): the whole override
  table as a dictionary, absolute TTL 30 s, removed immediately by the upsert / clear handlers on
  the instance that handled the write. When the entry is missing, exactly **one** database query
  reloads it however many requests ask at the same moment (single-flight); the others wait for
  that one result. The reload does not belong to any request: it runs on its own 5-second timeout
  (`HebloFeatureProvider.RefreshTimeout`), so a browser aborting its request cannot cancel it. A
  failed reload is not cached — the next evaluation tries again.
```

- [ ] **Step 2: Add the new quirks to "Known quirks"**

In the same file, add these two bullets at the end of the `## Known quirks` list (directly after the bullet that starts "Only boolean flags are supported"):

```markdown
- **A cancelled request gets the default, quietly.** If the caller's own request is cancelled
  while it waits for the override reload, that one evaluation returns the caller-supplied default
  (same landing spot as the DB-error quirk above) and is logged at Debug with `cancelledBy=caller`.
  Real reload failures are logged at Warning with `cancelledBy=refresh` (the reload's own timeout
  or a DB-side cancellation) or `cancelledBy=none` (any other error). Before #3971 every one of
  these was a Warning and the aborted request also cancelled the DB command, which showed up in
  Application Insights as bursts of Npgsql `TaskCanceledException`.
- **A reload already running when an admin flips a flag can re-cache the old value.** The write
  handler removes the cache entry, but a reload that started just before the write may finish
  afterwards and store the pre-write table for up to 30 s. Same worst-case delay as on other
  instances.
```

- [ ] **Step 3: Regenerate the index and run the process-docs check**

Run:

```bash
python3 scripts/process-docs/check.py index
python3 scripts/process-docs/check.py check
git status --short docs/processes
```

Expected: `check.py index` rewrites `docs/processes/INDEX.md` with no content change (the doc's front-matter `summary` was not touched), `check.py check` reports no errors for `module-feature-flags`, and `git status` lists only `docs/processes/module-feature-flags.md` as modified. If `INDEX.md` does show a diff, include it in the commit. Do not edit the `verified_at` front-matter value: staleness is measured from the later of `verified_at` and the doc's own last commit, so committing this edit is sufficient.

- [ ] **Step 4: Commit**

```bash
git add docs/processes/module-feature-flags.md docs/processes/INDEX.md
git commit -m "docs(processes): feature-flag override cache single-flight refresh (#3971)"
```

---

## Spec coverage

| Requirement | Task | Evidence |
|---|---|---|
| FR-1 single-flight (cold cache, after expiry) | single-flight-refresh | `..._ConcurrentCallersOnColdCache_QueryDbOnce`, `..._ConcurrentCallersAfterCacheExpiry_QueryDbOnceMore` |
| FR-2 refresh not bound to caller token | single-flight-refresh | `..._CallerCancellation_DoesNotCancelLoadOrOtherCallers`, `..._LoadStartedByCancelledCaller_StillPopulatesCache` |
| FR-3 failure behaviour preserved | single-flight-refresh | 7 pre-existing tests unchanged, `..._FailedLoadIsNotCached_NextCallReloads` |
| FR-4 cancellation-source diagnostics | cancellation-source-diagnostics | `..._CallerCancelled_LogsDebugNotWarning`, `..._LogsWarningCancelledByRefresh`, `..._LogsWarningCancelledByNone`, pre-existing `DbRetryAttempts` log tests |
| NFR-1 lock-free hit path | single-flight-refresh | `TryGetValue` before `_gate.WaitAsync`; `..._CacheHit_DoesNotQueryDbAgain` |
| NFR-3 one connection per refresh | single-flight-refresh | same tests as FR-1 |
| Process-doc rule | update-process-doc | `check.py check` passes |

Known deviation from the spec text: the refresh token is a plain 5 s timeout token and is not linked to application shutdown (see "Scope notes"); this follows `design.r1.md`.
