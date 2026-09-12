# Architecture Review: Enforce CRON expression format validation in the `RecurringJobConfiguration` domain entity

## Skip Design: true

## Architectural Fit Assessment
This is a backend-only, single-file domain-invariant hardening change with no UI/UX surface. It aligns directly with the project's Clean Architecture / Vertical Slice conventions already documented in `docs/architecture/development_guidelines.md`: `Anela.Heblo.Domain` has no reference to `NCrontab.Advanced` today (confirmed by reading `Anela.Heblo.Domain.csproj`), while `Anela.Heblo.Application` does. Adding a package reference to Domain to reuse `NCrontab.Advanced.CrontabSchedule.Parse()` would be a first-of-its-kind Domain→NuGet-infrastructure-library dependency in this codebase pattern (Domain currently depends only on MediatR abstractions, ASP.NET auth abstractions, DI/logging abstractions, and `Anela.Heblo.Xcc`) — none of those are parsing/format libraries. The brief's own suggested fix (a hand-rolled field-count check) is therefore the right-sized solution and requires no new project reference, no new interfaces, and touches exactly one existing file plus its test file. This is a pure invariant-strengthening change to an existing entity, not a new feature — there is no new module, no new contract, no new persistence shape.

The three write paths into `CronExpression` (constructor, `UpdateConfiguration`, `UpdateCronExpression`) already share a consistent guard-clause style (`string.IsNullOrWhiteSpace(...) → throw new ValidationException(...)`), so the new check slots into that exact idiom rather than introducing a new validation mechanism (no FluentValidation, no Data Annotations custom attribute — matching how the other required-field checks are hand-written, not attribute-driven, despite `[Required]` being present only for EF/OpenAPI metadata purposes on this class).

## Proposed Architecture

### Component Overview
```
Anela.Heblo.Domain/Features/BackgroundJobs/
└── RecurringJobConfiguration.cs
    ├── RecurringJobConfiguration(...)      ─┐
    ├── UpdateConfiguration(...)             ├─→ each calls private static
    └── UpdateCronExpression(...)           ─┘   ValidateCronFormat(cronExpression)
                                                       │
                                                       ▼
                                     throws ValidationException if field count
                                     is not 5 (standard) or 6 (Quartz-style)

Anela.Heblo.Application/.../UpdateRecurringJobCronHandler.cs
    └── IsValidCronExpression(...)   ← UNCHANGED, still the first (stronger,
                                        NCrontab-based) line of defense on the
                                        one HTTP-triggered write path; the new
                                        Domain check becomes a second,
                                        universal line of defense underneath it
```
No new components. No new module. No new DI registration.

### Key Design Decisions

#### Decision 1: Static helper on the entity vs. a `CronExpression` value object
**Options considered:**
1. A private static `ValidateCronFormat(string)` helper method inside `RecurringJobConfiguration`, called from all three write paths (the brief's primary suggestion).
2. A dedicated `CronExpression` value object (in Domain, structural-check only, or in Application, NCrontab-backed) that `RecurringJobConfiguration.CronExpression` becomes typed as, replacing the current `string` property.

**Chosen approach:** Option 1 — a private static helper method on `RecurringJobConfiguration`, matching the existing guard-clause style of the class exactly.

**Rationale:** Option 2 changes the public shape of the entity (`CronExpression` typed as `string` today, referenced as a bare string by `RecurringJobDto`, `UpdateJobCronRequestBody`, `HangfireRecurringJobScheduler`, `RecurringJobNextRunCalculator`, `RecurringJobDiscoveryService`, and the EF Core mapping in `RecurringJobConfigurationConfiguration`) — a much larger blast radius for a fix the brief itself frames as a lightweight structural guard, not a full modeling exercise. The spec (FR-2) already scoped this down to "no new Domain package reference"; a value object doesn't change that dependency question but does add a type-mapping concern (EF Core `HasConversion` for a value object property) with no corresponding benefit here, since the check is intentionally structural (field count), not semantic. Keep the fix proportional: one static method, called from three places, zero new types.

#### Decision 2: Field-count check (structural) vs. reusing `NCrontab.Advanced` semantics in Domain
**Options considered:**
1. Field-count structural check (5 or 6 whitespace-delimited tokens), as given in the brief's code snippet — catches gross malformation (`"not-a-cron"`, empty after trim, wrong token count) but not out-of-range values within a field (e.g. `"99 99 99 99 99"` has 5 fields and would pass).
2. Add `NCrontab.Advanced` to `Anela.Heblo.Domain` and call `CrontabSchedule.Parse()` directly, matching the Application-layer handler's semantics exactly.

**Chosen approach:** Option 1, per the brief's explicit preference ("A lightweight option is to count CRON fields... without requiring an external library") and the spec's FR-2, which rules out a new Domain package reference.

**Rationale:** This is a deliberate, documented trade-off, not an oversight: the Domain-level check is a coarser backstop than the Application-layer NCrontab parse, and that asymmetry is intentional — the handler remains the stronger, semantically-correct gate on the one user-facing write path; the Domain check exists purely to make it *impossible* (not merely *validated-in-one-place*) for any caller to construct/mutate the entity with an obviously-malformed string. Recorded explicitly in Specification Amendments below so it isn't mistaken for an incomplete implementation later.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. Single file changed:
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`

Test file to extend (existing, no new test project/file needed):
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs`

### Interfaces and Contracts
No public interface or contract changes. The private static helper:

```csharp
private static void ValidateCronFormat(string cronExpression)
{
    var fields = cronExpression.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    if (fields.Length is not (5 or 6))
        throw new ValidationException($"'{cronExpression}' is not a valid CRON expression.");
}
```

Note: use `ValidationException` (not `ArgumentException` as the brief's snippet shows) — this matches every other guard clause already in `RecurringJobConfiguration` (`"JobName is required"`, `"CronExpression is required"`, etc.) and keeps the exception type callers already handle (the Application handler's generic `catch (Exception ex)` in `UpdateRecurringJobCronHandler` swallows either type identically, but consistency within the entity itself is the deciding factor — a caller catching `ValidationException` for the existing empty-string case should not need a second catch clause for the new format case).

Call sites — insert immediately after the existing `IsNullOrWhiteSpace` check for `cronExpression` in each of the three methods, so the "required" and "well-formed" checks read as one contiguous validation block per field, consistent with how `jobName`, `displayName`, etc. are each validated in one place before assignment:

1. Constructor (after line 64's `IsNullOrWhiteSpace(cronExpression)` check, before line 65's `timeZoneId` check — or after all `IsNullOrWhiteSpace` checks and before assignment; either ordering satisfies FR-1, but keep all cron-related checks adjacent for readability).
2. `UpdateConfiguration` (after its `IsNullOrWhiteSpace(cronExpression)` check at line 93).
3. `UpdateCronExpression` (after its `IsNullOrWhiteSpace(cronExpression)` check at line 130).

All validation in each method must still complete (throw or pass) **before any field is assigned** — this ordering already exists in all three methods today and must be preserved exactly (call `ValidateCronFormat` alongside the other pre-assignment guard clauses, not after).

### Data Flow
Unchanged. Writers of `CronExpression`:
- `RecurringJobSeeder.SeedDefaultConfigurationsAsync` → constructor (new configs) / `UpdateConfiguration` (existing configs, passing back `existing.CronExpression` unchanged — already-valid values re-validate as a no-op).
- `UpdateRecurringJobCronHandler.Handle` → `UpdateCronExpression`, only after its own `IsValidCronExpression` (NCrontab) gate has already passed — so the new Domain check is provably a no-op on this path for any request that reaches the entity; it only bites callers that bypass the handler.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A currently-seeded `RecurringJobMetadata.CronExpression` constant does not split into exactly 5 or 6 whitespace tokens (e.g. an unexpected format, extra internal whitespace) and startup seeding now throws where it didn't before | Medium | Verified during this review: all ~25 `CronExpression` constants found via repo-wide search (`"0 2 * * *"`, `"*/15 * * * *"`, `"0 6,18 * * *"`, `"15 6,18 * * *"`, `"0 * * * *"`, etc.) are standard 5-field expressions with single-space separators — none use tabs, leading/trailing whitespace, or a 6th field. All pass the new check. Still, run the full BackgroundJobs test suite (`RecurringJobConfigurationTests`, `RecurringJobSeeder`-related tests if any, and any startup/integration test that exercises seeding) before merging, per CLAUDE.md's validation gate. |
| A row already persisted in the database (e.g. via manual DB edit, or a future path that bypasses both gates) has a malformed `CronExpression` and later gets re-loaded and re-saved through `UpdateConfiguration` (which round-trips `existing.CronExpression` unchanged in the seeder's update branch) or any other write path, now throwing where it previously wouldn't | Low | Out of scope per spec (NFR-3); no known such rows exist today (confirmed no ad-hoc DB scripts touch this table per repo search). If it ever occurs, the seeder would fail loudly at startup instead of silently persisting worse data — an acceptable fail-fast trade-off consistent with the brief's stated goal. |
| Developer confusion later about why a "valid-looking" cron with out-of-range values (e.g. `"99 99 * * *"`) still passes the Domain check but is rejected by the Application handler | Low | Documented explicitly in this review's Decision 2 and must be carried into the code as a one-line comment above `ValidateCronFormat` noting it is a structural (not semantic) check and that `UpdateRecurringJobCronHandler` performs the stronger semantic check for the admin-facing path. |

## Specification Amendments
- **FR-1 acceptance criteria clarification:** the "5-field (standard) or 6-field (Quartz-style, leading seconds field)" wording in the spec should be read as *field count only* — this review confirms (Decision 2) that no semantic/range validation is added at the Domain layer, and the spec's Out of Scope section already excludes this; no contradiction, just making explicit that FR-1's acceptance criteria are satisfied by field-count checking alone.
- **Exception type:** the spec's FR-3 already specifies `ValidationException`, matching the brief's own text (the brief's *code snippet* uses `ArgumentException`, but the brief's prose and the spec both correctly identify `ValidationException` as the entity's established convention — implementers should follow the spec/prose, not the snippet's exact exception type).
- **Add one line to the implementation**: a short code comment on `ValidateCronFormat` documenting the structural-only scope (see Risks table, row 3) — not a spec change, but should be called out to the planner as an implementation task, since it isn't captured by any FR as written.

## Prerequisites
None. No migration, no config, no new infrastructure. Ready to implement directly against `main`/this feature branch.
