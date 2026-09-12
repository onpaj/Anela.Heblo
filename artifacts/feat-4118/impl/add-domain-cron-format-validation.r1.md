# Implementation: add-domain-cron-format-validation

## What was implemented

Added a private static `ValidateCronFormat` helper to `RecurringJobConfiguration`
(the domain entity) that performs a structural-only check on a CRON expression —
it must split into 5 (standard) or 6 (Quartz-style, leading seconds)
whitespace-separated fields, otherwise a `ValidationException` is thrown. The
helper is called from all three write paths that accept a CRON expression: the
constructor, `UpdateConfiguration`, and `UpdateCronExpression` — immediately
after each method's existing `IsNullOrWhiteSpace` check, before any field is
assigned. This closes the gap described in issue #4118: previously only
`UpdateRecurringJobCronHandler` (an Application-layer MediatR handler) enforced
CRON format via `NCrontab.Advanced.CrontabSchedule.Parse()`; any other caller of
the domain entity (`RecurringJobSeeder`, a future service, a test, an admin
script) could construct or mutate a `RecurringJobConfiguration` with an
arbitrary non-empty string.

The check is deliberately structural, not semantic — it does not validate
per-field value ranges (e.g. `"99 99 * * *"` still passes, since it has the
field count of a valid 5-field expression). The stronger semantic check
(`NCrontab.Advanced` parsing) remains solely in `UpdateRecurringJobCronHandler`,
the one user-facing write path, exactly as the issue's suggested fix specified.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`
  — added `ValidateCronFormat` and wired it into the constructor,
  `UpdateConfiguration`, and `UpdateCronExpression`.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs`
  — added 3 new `Fact` tests asserting `ValidationException` is thrown for a
  malformed CRON expression via each of the three write paths (and that
  `CronExpression` is left unchanged after a throw on the two mutator paths),
  plus a `Theory` with 6 `InlineData` cases covering every 5-field and 6-field
  CRON expression actually used by the codebase's `IRecurringJob`
  implementations, confirming none of them regress.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/GetRecurringJobsListHandlerTests.cs`
  — **unplanned but necessary fix**: two pre-existing tests
  (`Handle_WhenCronExpressionIsInvalid_SetsNextRunAtToNullAndLogsWarning` and
  `Handle_WhenCronExpressionIsInvalid_SetsNextRunAtToNull_DoesNotThrow`)
  constructed a `RecurringJobConfiguration` directly with the CRON values
  `"NOT_A_CRON"` / `"INVALID_CRON"` to exercise
  `RecurringJobNextRunCalculator`'s handling of a CRON string that fails
  `NCrontab.Advanced`'s parse. Both values are single tokens with no
  whitespace, so with `ValidateCronFormat` in place the domain constructor now
  throws before either test can even reach the calculator under test — a
  regression the task-context's own verification step (full solution test run)
  was designed to catch. Fixed by swapping both fixture values for
  `"99 99 * * *"` / `"60 60 * * *"` respectively: each has the field count of a
  valid 5-field CRON expression (so it passes the new structural domain-level
  check), but each contains an out-of-range minute/hour value that still makes
  `CrontabSchedule.Parse` throw `CrontabException` — preserving the original
  test intent (verify the handler-level graceful-degradation path) without
  weakening the new domain invariant. Verified directly against the real
  `NCrontab.Advanced` 1.3.28 package (the exact version this solution
  references) in a throwaway console program before landing the fix.

## Tests

- `RecurringJobConfigurationTests.cs`: 21/21 pass, including the 3 new malformed-
  input tests and the 6-case valid-input theory.
- `GetRecurringJobsListHandlerTests.cs` (and the rest of the `BackgroundJobs`
  test folder): 107/107 pass after the fixture fix above.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobConfigurationTests"
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BackgroundJobs"
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln
```

The full-solution `dotnet test` run has 105 pre-existing failures, all in
`Anela.Heblo.Adapters.Flexi.Tests`, `Anela.Heblo.Adapters.Shoptet.Tests`, and
integration-test classes across `Anela.Heblo.Tests` (Bank, Leaflet,
KnowledgeBase, Smartsupp, Photobank, TransportBox, etc.) — every one of them
throws `System.ArgumentException: Docker is either not running or
misconfigured` from a Testcontainers fixture constructor, confirmed against
`docker ps` in this sandbox failing with "no such file or directory" on the
Docker socket. None of the 105 remaining failures are in the `BackgroundJobs`
namespace or mention CRON/RecurringJob; this is a pre-existing environment
limitation, not a regression from this change.

## Notes

- The one deviation from the task-context is the `GetRecurringJobsListHandlerTests.cs`
  fixture fix described above — it was not anticipated by the task-context
  (which only called out `UpdateRecurringJobCronHandler`'s tests as a
  compatibility check) but was required to keep `dotnet test Anela.Heblo.sln`
  green, per the task-context's own Step 5 and CLAUDE.md's validation
  requirements.
- No other caller in `backend/src` or `backend/test` constructs a
  `RecurringJobConfiguration` (or calls `UpdateConfiguration` /
  `UpdateCronExpression`) with a CRON value that fails the new structural
  check — confirmed by the full solution test run showing no other
  BackgroundJobs/CRON-related failures.

## PR Summary
Closes an architecture-review finding (#4118): `RecurringJobConfiguration`'s
constructor and its two update methods (`UpdateConfiguration`,
`UpdateCronExpression`) previously accepted any non-empty string as a CRON
expression — the only structural check lived in
`UpdateRecurringJobCronHandler`, an Application-layer concern reachable
through only one write path. This adds a private static `ValidateCronFormat`
helper (a 5- or 6-field structural check, no new dependency) to the domain
entity itself and calls it from all three write paths, so the invariant "a
CRON expression must be structurally well-formed" now holds regardless of
caller.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs` — added `ValidateCronFormat`, wired into constructor, `UpdateConfiguration`, `UpdateCronExpression`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs` — new tests for the malformed-input rejection and known-valid-input acceptance
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/GetRecurringJobsListHandlerTests.cs` — updated two pre-existing test fixtures to use structurally-valid-but-semantically-invalid CRON strings, preserving their original intent under the new domain-level check

## Status
DONE
