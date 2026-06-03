Plan saved to `docs/superpowers/plans/2026-06-03-data-quality-dqt-doc-schedule-fix.md`.

**Summary of the plan:**

- Six tasks, all touching the single file `docs/features/data-quality-dqt.md`.
- Task 1 — Overview line: `weekly` → `daily` (one word).
- Task 2 — Schedule section: rewrite the automatic-trigger bullet to `daily at 05:00 Europe/Prague via Hangfire recurring job daily-invoice-dqt (InvoiceDqtJob). Each run covers the previous calendar day.`
- Task 3 — Architecture bullet for `InvoiceDqtJob`: `weekly runs` → `daily runs`.
- Task 4 — Delete the stale "Known constraints" bullet (per arch-review Decision 2: removal, not rewrite).
- Task 5 — Full-file grep sweep over the extended token list (`weekly`, `Monday`, `23:00`, `7 days`, `seven days`, `CEST`, `CET`, `UTC`, `server time`) — expects zero matches — plus positive assertions for `daily` and a Markdown render check.
- Task 6 — Verify the staged diff is exactly the four edit points, then a single conventional commit and push.

The plan locks in the architecture review's three amendments: timezone is stated as `Europe/Prague` (traced to `RecurringJobMetadata.DefaultTimeZoneId`), FR-4 is resolved as deletion, and FR-5's grep list is extended. No code, build, test, or deployment steps are required.

Per the pipeline note, skipping the execution-handoff prompt.