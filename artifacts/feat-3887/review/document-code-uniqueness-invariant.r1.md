# Code Review: document-code-uniqueness-invariant

## Summary
The new memory gotcha file matches the task context's required content and shape exactly: front
matter, Symptom/Root cause/Fix/Rules/Related-files sections mirroring
`memory/gotchas/postgres-partial-index-active-states.md`, all three consuming call sites named by
full path, the verbatim read-only detection query, the binding A9 note, and the binding A8 follow-up
with all three required prerequisites. All referenced paths were verified to exist. No source or
test file was modified, as required.

## Review Result: PASS

### task: document-code-uniqueness-invariant
**Status:** PASS

## Docs to Update
(None — this task's entire deliverable is the documentation file itself.)

## Overall Notes
- Acceptance criteria checked one-by-one against the file:
  - Front matter (`name` / `description` / `type: project`) present and shape matches the sibling
    file — confirmed.
  - Symptom / Root cause / Fix / Rules / Related files sections all present in that order —
    confirmed.
  - All three consuming call sites named by full path
    (`TransportBoxRepository.IsBoxCodeActiveAsync`/`GetByCodeAsync`,
    `OpenOrResumeBoxByCodeHandler`) — confirmed.
  - Detection query present verbatim and read-only (no `UPDATE`/`DELETE`/DDL) — confirmed byte-for-byte
    against the task context.
  - A9 note present immediately after the query, explicitly calling the `'Closed'`/`'Stocked'`
    literals a deliberate unavoidable second copy — confirmed.
  - A8 follow-up present with `suppressTransaction: true`, string-literal requirement, and
    "run the detection query first" precondition — confirmed, plus it correctly cites the specific
    rule (rule 1 of `postgres-partial-index-active-states.md`) and the `SQLSTATE 25001` reason.
  - Every referenced path resolves — verified via the task's own Step 2 grep/xargs check on
    `backend/`, `frontend/`, and `memory/` path fragments; all passed.
  - No source or test file modified — confirmed via `git status`/diff scope (only the new memory file
    plus this task's own artifacts under `artifacts/feat-3887/`).
- Step 3's full-solution validation gate ran clean apart from the pre-existing,
  already-documented Docker/Testcontainers unavailability in this sandbox (5 SQL-shape/atomicity
  integration test failures, identical to what the prior `add-code-occupancy-sql-shape-test` task
  already reported) — not a regression from this documentation-only change. `dotnet build` and
  `dotnet format` were correctly run against the root `Anela.Heblo.sln` since no solution file exists
  under `backend/`; this is a faithful execution of the task's intent despite the literal `cd backend`
  in its command text, and does not affect the PASS verdict.
