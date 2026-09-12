# Code Review: add-domain-cron-format-validation

## Summary
The implementation matches the task-context's spec closely: `ValidateCronFormat`
is a private static structural check (5- or 6-field split), wired into the
constructor, `UpdateConfiguration`, and `UpdateCronExpression` exactly where
specified, with no new dependency introduced into the Domain layer. The
required tests were added verbatim and pass. The one deviation — fixing two
pre-existing `GetRecurringJobsListHandlerTests` fixtures that used
non-whitespace CRON tokens (`"NOT_A_CRON"`, `"INVALID_CRON"`) to exercise the
handler's own invalid-CRON handling path — was not anticipated by the task
context, but is a necessary and correctly-scoped fix: it was required to keep
`dotnet test Anela.Heblo.sln` passing (the task-context's own Step 5), and the
replacement values (`"99 99 * * *"`, `"60 60 * * *"`) preserve the original
tests' intent by remaining structurally valid but semantically invalid,
verified directly against the real NCrontab.Advanced 1.3.28 package.

## Review Result: PASS

### task: add-domain-cron-format-validation
**Status:** PASS

## Docs to Update
(Omit — this is an internal domain-invariant change with no public API,
CLI, environment variable, or operational-behavior change. No architecture
or feature doc describes CRON format validation at the level of detail this
change would affect.)

## Overall Notes
- Verified: `dotnet format Anela.Heblo.sln --verify-no-changes` (clean),
  `dotnet build Anela.Heblo.sln` (0 errors, pre-existing warnings only),
  `dotnet test Anela.Heblo.sln` (105 failures, down from a 107-failure
  baseline before this task's fix to `GetRecurringJobsListHandlerTests`; all
  105 remaining failures are Testcontainers/Docker-socket failures in
  unrelated integration-test classes — confirmed via `docker ps` failing in
  this sandbox — none in the `BackgroundJobs` namespace or mentioning CRON).
- The structural-only check (field count, not per-field range) is a
  deliberate and correctly-documented scope boundary matching the issue's
  suggested fix — full semantic validation intentionally stays in
  `UpdateRecurringJobCronHandler` via `NCrontab.Advanced`, so this does not
  duplicate that library into the Domain layer.
- No other caller across `backend/src` or `backend/test` constructs a
  `RecurringJobConfiguration` (or calls its two mutators) with a CRON value
  that fails the new structural check — the full solution test run is the
  evidence for this, not just a grep.

**Status:** PASS
