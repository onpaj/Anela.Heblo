# Code Review: implement-expedition-job-controls-bar

## Summary
The new `ExpeditionJobControlsBar.tsx` matches the task-context spec verbatim — same imports, constants, handlers, permission gating, and JSX as prescribed, with the "Obnovit" button and archive-browsing code correctly excluded. The pre-existing failing test file (from the prior TDD task) now passes 9/9 with no changes needed, and typecheck/lint show zero issues attributable to the new file.

## Review Result: PASS

### task: implement-expedition-job-controls-bar
**Status:** PASS

## Docs to Update
(none — this is an internal component extraction with no change to public behavior, CLI, or environment variables)

## Overall Notes
- Verified the committed component file is byte-for-byte the content specified in the task-context's Step 1 (same handlers: `handleRunJob`, `handleToggleJob`, `handleRunFix`, `handlePrintOrderSuccess`; same permission constants `jobs.trigger.read` / `jobs.disable.read`; same `PrintOrderModal` wiring and query-invalidation comment).
- Confirmed the task correctly did not touch `ExpeditionListArchivePage.tsx` — wiring the new component into the page is explicitly a separate downstream task per the task plan, so no duplication/orphan-code risk yet.
- Test run (`react-scripts test`, the project's actual runner — the task-context's suggested bare `npx jest` command doesn't work in this repo without CRA's config) confirms all 9 tests pass across "expedition robot toggle" (4) and "permission gating" (5), matching the acceptance criteria's expected counts.
- The developer's implementation report flags a pre-existing React `act()` warning on the toggle-mutation test and pre-existing `tsc`/`lint` noise elsewhere in the repo (react-i18next type-def errors, unrelated testing-library lint violations in other files). Verified independently: these are not attributable to the new file and predate this change (the identical `act()` warning already exists in `ExpeditionListArchivePage.test.tsx` today). Not a blocking correctness issue for this task.
- Worktree required `npm install --legacy-peer-deps` to get `node_modules` in place before any of the above could run — consistent with what CI's own workflows already do; not a defect in this task's output.

**Status:** PASS
