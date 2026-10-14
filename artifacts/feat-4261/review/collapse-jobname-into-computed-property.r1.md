# Code Review: collapse-jobname-into-computed-property

## Summary
The implementation matches the task-context spec exactly: `JobName` is now a
computed `Id`-backed property, its EF Core column mapping and unique index
are removed, and the repository's two queries reference `Id` directly. The
new regression test correctly guards against `JobName` reappearing as a
mapped EF column. Full `BackgroundJobs` test suite (115/115) and a full
solution build (0 errors) both pass.

## Review Result: PASS

### task: collapse-jobname-into-computed-property
**Status:** PASS

## Docs to Update
(none — this is an internal persistence refactor with no change to public
behavior, CLI, or agent/pipeline docs)

## Overall Notes
- Diff is minimal and surgical: 4 files touched, exactly the ones the task
  context named, no unrelated changes.
- `GetByJobNameAsync`'s method name/signature were correctly left unchanged
  per the spec's explicit instruction ("only the query predicate's column
  reference changes").
- Verified no other `.JobName` write sites exist in `backend/src`; the only
  other reader (`RecurringJobSeeder.cs`) compiles unchanged against the new
  computed property, as anticipated by the task context.
- Migration generation is correctly deferred to the separate
  `generate-and-verify-migration` task later in the plan — not in scope here.
