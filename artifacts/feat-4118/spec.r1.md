# Specification: Enforce CRON expression format validation in the `RecurringJobConfiguration` domain entity

## Summary
`RecurringJobConfiguration` (the domain entity governing scheduled background jobs) currently accepts any non-empty string as a CRON expression on its constructor, `UpdateConfiguration`, and `UpdateCronExpression` methods. The only structural validation that exists today lives in `UpdateRecurringJobCronHandler` (Application layer), so any other caller of the entity — a future service, a test, a seeder, an admin script — can put the entity into a state with a syntactically invalid CRON expression, which then gets persisted. This change moves the "must be a syntactically valid CRON expression" invariant into the domain entity itself, so it is enforced on every code path, not only the one HTTP-triggered handler.

## Background
`RecurringJobConfiguration` (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`) is written via three entry points that each accept a `cronExpression` argument:
- the public constructor (used by `RecurringJobSeeder.SeedDefaultConfigurationsAsync`, with values from code-defined `RecurringJobMetadata.CronExpression` constants across ~25 job classes),
- `UpdateConfiguration` (also called by `RecurringJobSeeder` when refreshing developer-owned fields, and preserves the existing `CronExpression` value rather than accepting a new one from an external caller today),
- `UpdateCronExpression` (called only by `UpdateRecurringJobCronHandler`, the MediatR handler behind the admin "update job schedule" use case).

All three currently validate only `string.IsNullOrWhiteSpace`. The only *format* check — parsing the string as a real CRON expression — is implemented in `UpdateRecurringJobCronHandler.IsValidCronExpression`, using `NCrontab.Advanced.CrontabSchedule.Parse()`. `NCrontab.Advanced` is referenced by the `Anela.Heblo.Application` project but **not** by `Anela.Heblo.Domain` (confirmed: `Anela.Heblo.Domain.csproj` has no such package reference).

This means the domain invariant "a CRON expression must be syntactically parseable" is enforced only when configuration changes flow through that one MediatR handler. The constructor and `UpdateConfiguration` paths (seeding, tests, any future direct caller) have no such guarantee, and the repository will persist whatever the entity is holding.

## Functional Requirements

### FR-1: Domain-level CRON format validation
`RecurringJobConfiguration` must reject a syntactically invalid CRON expression at the point it is assigned, in the constructor, `UpdateConfiguration`, and `UpdateCronExpression` alike — regardless of caller.

**Acceptance criteria:**
- Constructing `RecurringJobConfiguration` with a `cronExpression` that is not a valid 5-field (standard) or 6-field (Quartz-style, leading seconds field) whitespace-delimited CRON expression throws a `ValidationException`, matching the existing exception type used for the other required-field checks in this entity.
- Calling `UpdateConfiguration(...)` with an invalid `cronExpression` throws the same `ValidationException` and leaves the entity's existing `CronExpression` value unchanged (no partial mutation before the throw — matches current ordering, where all validation happens before any field is assigned).
- Calling `UpdateCronExpression(...)` with an invalid `cronExpression` throws the same `ValidationException` and leaves `CronExpression` unchanged.
- A null/empty/whitespace-only value continues to throw exactly as it does today ("CronExpression is required") — this existing check is preserved, not replaced.
- All ~25 existing `RecurringJobMetadata.CronExpression` constants across the job classes (e.g. `"0 2 * * *"`, `"*/15 * * * *"`, `"0 6,18 * * *"`) remain valid and continue to seed successfully — the new check must not be stricter than what the codebase already relies on.
- All CRON expressions already exercised by existing tests (e.g. `"0 0 * * *"`, `"0 2 * * *"`) continue to pass.

### FR-2: Validation implementation stays free of the `NCrontab.Advanced` dependency in the Domain project
Per the brief's stated preference and the project's Clean Architecture boundary (Domain must not depend on Application-layer or infrastructure libraries), the validation must be implemented as a lightweight structural check owned by the Domain project, not by adding `NCrontab.Advanced` as a Domain package reference.

**Acceptance criteria:**
- `Anela.Heblo.Domain.csproj` gains no new `PackageReference`.
- The validation logic (field-count check on whitespace split, accepting 5 or 6 fields per the brief's suggested approach) lives in `RecurringJobConfiguration.cs` (or a small Domain-owned helper/value object in the same feature folder), not in the Application project.
- The existing Application-layer `UpdateRecurringJobCronHandler.IsValidCronExpression` (NCrontab-based, semantically stronger) is left in place unless the architect phase determines it should be removed as now-redundant; this spec does not mandate removing it, only that a new domain-level guard is added underneath it. (See Open Questions.)

### FR-3: Error messaging and type consistency
The new validation failure must be visible to a domain-entity caller as an actionable exception, consistent with the entity's existing validation style.

**Acceptance criteria:**
- Throws `System.ComponentModel.DataAnnotations.ValidationException` (the type already used by every other guard clause in this entity), with a message that includes the offending value, e.g. `"'{cronExpression}' is not a valid CRON expression."` (per the brief's suggested snippet).
- The check is a private static helper (e.g. `ValidateCronFormat(string cronExpression)`) called from all three write paths (constructor, `UpdateConfiguration`, `UpdateCronExpression`) so the rule is defined once.

## Non-Functional Requirements

### NFR-1: Performance
Negligible — a `string.Split` and a length check on a short string (max 50 chars per the `[MaxLength(50)]` attribute on `CronExpression`), invoked only on writes, not reads.

### NFR-2: Security
None beyond input-validation hardening already described; no new attack surface introduced.

### NFR-3: Backward compatibility
Existing persisted rows are not touched by this change (no migration). Any row already in the database with a malformed `CronExpression` (none are currently known — all seeded values come from trusted code constants) would only become an issue if that row were re-loaded and re-saved through `UpdateConfiguration`/`UpdateCronExpression` with the same invalid value; that is out of scope to remediate retroactively.

## Data Model
No schema change. `RecurringJobConfiguration.CronExpression` remains `string`, `[Required]`, `[MaxLength(50)]`. No new entity or value object is required by this spec (the brief's `CronExpression` value-object alternative is presented as an option, not a requirement — see Open Questions).

## API / Interface Design
No public API/contract changes. `UpdateRecurringJobCronHandler` continues to call `job.UpdateCronExpression(...)`; when the entity now throws `ValidationException` for a malformed value that previously reached the entity (it currently cannot, because the handler already rejects invalid input before calling the entity — see Dependencies), that exception is already caught by the handler's existing `catch (Exception ex)` block and surfaced as `ErrorCodes.RecurringJobUpdateFailed`. No behavior change is expected on that path for currently-valid or currently-invalid inputs, since the handler already blocks invalid CRON strings earlier via `ErrorCodes.InvalidCronExpression`.

`RecurringJobSeeder.SeedDefaultConfigurationsAsync` is unaffected for existing job metadata (all current constants are valid 5-field expressions) but will now throw at startup/seed time if a future developer introduces a malformed `CronExpression` constant on a new `IRecurringJob` implementation — this is the intended fail-fast behavior the brief asks for.

## Dependencies
- `System.ComponentModel.DataAnnotations.ValidationException` — already used throughout the entity, no new dependency.
- Existing Application-layer dependency on `NCrontab.Advanced` in `UpdateRecurringJobCronHandler` is unaffected and out of scope to change.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs` — all existing test fixtures use valid 5-field CRON strings (`"0 2 * * *"`, `"0 0 * * *"`), so no existing test will need its CRON literal changed; new tests should be added covering the invalid-format case for the constructor, `UpdateConfiguration`, and `UpdateCronExpression`.

## Out of Scope
- Removing or refactoring `UpdateRecurringJobCronHandler.IsValidCronExpression` / its NCrontab-based check.
- Introducing a dedicated `CronExpression` value object (offered by the brief as an alternative implementation strategy, not required).
- Semantic CRON validation (e.g. rejecting `"99 99 99 99 99"`, which has the right field count but out-of-range values) — the brief's suggested fix is explicitly a structural (field-count) check, not a full parse; closing that gap would require pulling in a parser, which FR-2 rules out for the Domain project.
- Retroactive validation/cleanup of already-persisted `CronExpression` values.
- Any change to `RecurringJobMetadata` or the ~25 job classes that define CRON constants.

## Open Questions
None — the brief is specific and the suggested fix (field-count structural check, Domain-owned, no new package reference) is directly actionable. The one design choice explicitly left open by the brief itself — plain static helper on the entity vs. a separate `CronExpression` value object — is deferred to the architecture phase, consistent with FR-2's "unless the architect phase determines otherwise" wording above.

## Status: COMPLETE
