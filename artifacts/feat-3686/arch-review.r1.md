# Architecture Review: Propagate Developer-Owned Metadata Updates in RecurringJobSeeder

## Skip Design: true

## Architectural Fit Assessment
This is a one-method behavioral fix inside an existing, well-isolated application service (`RecurringJobSeeder`, in `Anela.Heblo.Application.Features.BackgroundJobs.Services`). It touches no module boundary, no contract, no persistence schema, and no UI. The fix stays entirely within the vertical slice that already owns this concern (`BackgroundJobs`), and reuses machinery that already exists for exactly this purpose:

- `RecurringJobConfiguration.UpdateConfiguration(displayName, description, cronExpression, modifiedBy)` — the domain entity already models "developer-owned fields get overwritten, cron is passed through" as its update contract. It is already exercised by `UpdateRecurringJobCronHandler` conceptually (admin path) and unit-tested in `RecurringJobConfigurationTests.UpdateConfiguration_ShouldUpdateProperties`.
- `IRecurringJobConfigurationRepository.UpdateAsync` — already implemented in `RecurringJobConfigurationRepository` (`backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs`) as a straightforward `Update` + `SaveChangesAsync`, and already used elsewhere (e.g. `UpdateRecurringJobCronHandler`, `UpdateRecurringJobStatusHandler`) for admin-driven writes.

No new interface, no new repository method, no new domain method, and no schema change is needed — confirming the spec's stated scope. This aligns with the project's Vertical Slice rules in `docs/architecture/development_guidelines.md` (repository binding lives in `BackgroundJobsModule.cs`, already registered; no changes needed there either).

The `RecurringJobConfiguration()` domain constructor path (used for the insert branch) is untouched. The only change is what happens in the branch of `SeedDefaultConfigurationsAsync` where `existing != null` — today a no-op, going forward an update call.

## Proposed Architecture

### Component Overview
No new components. Existing flow, one branch changed:

```
API startup (ServiceCollectionExtensions.cs ~line 463)
        │
        ▼
IRecurringJobSeeder.SeedDefaultConfigurationsAsync(discoveredJobs)
        │
        ▼
RecurringJobSeeder  (Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs)
        │  for each job.Metadata → build in-memory RecurringJobConfiguration
        │
        ├─ existing == null ─────────────► repository.AddAsync(config)          [unchanged]
        │
        └─ existing != null ─────────────► existing.UpdateConfiguration(        [NEW]
                                                config.DisplayName,
                                                config.Description,
                                                existing.CronExpression,  ◄─ preserve admin value
                                                "System")
                                            repository.UpdateAsync(existing)
                │
                ▼
        IRecurringJobConfigurationRepository (Domain interface)
                │
                ▼
        RecurringJobConfigurationRepository (Persistence, EF Core, ApplicationDbContext)
```

### Key Design Decisions

#### Decision 1: Where the split happens
**Options considered:**
1. Change `RecurringJobSeeder.SeedDefaultConfigurationsAsync` directly (as spec proposes).
2. Push the merge logic into the domain entity as a new method, e.g. `ReconcileWithCode(...)`.
3. Push it into the repository as an `UpsertAsync` method.

**Chosen approach:** Option 1 — modify only the seeder's loop body.

**Rationale:** The seeder is the only place that knows about the "code wins for X, admin wins for Y" policy — it's an application-layer concern, not a domain invariant or a persistence concern. `UpdateConfiguration` already expresses the right domain contract (it takes `cronExpression` as a parameter rather than assuming it, which is precisely what lets the seeder pass through the *existing* value instead of the *incoming* one). Adding a new domain or repository method would duplicate logic that the seeder can express in three lines, and would expand the change surface beyond what the bug requires. This matches the project rule to make surgical, traceable changes — every new line here maps directly to the brief.

#### Decision 2: Unconditional update vs. diff-and-skip
**Options considered:**
1. Always call `UpdateAsync` for existing rows, even when `DisplayName`/`Description` are unchanged.
2. Compare incoming vs. stored values first and only call `UpdateAsync` when something actually differs.

**Chosen approach:** Option 1, per spec NFR-1.

**Rationale:** Startup-time seeding runs once, against a low tens of rows; the cost of an always-write is negligible and the code stays simpler (no extra branching, no equality-comparison bugs to get wrong). This is explicitly called out as acceptable and preferred in the spec — no deviation proposed.

## Implementation Guidance

### Directory / Module Structure
No new files. Single file changes to:
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` — replace the `if (existing == null) { AddAsync }` body with the if/else from the spec (and update the method's XML doc comment, which currently says "Only creates configurations for jobs that don't already exist" — that line is now incorrect and must be revised as part of this change, since it directly describes the behavior being fixed).

Existing tests to revisit (not necessarily rewrite, but verify against the new behavior) in `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs`:
- `SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate` currently only asserts row count stays at 9. It will still pass unchanged, but it no longer exercises the interesting new behavior (metadata propagation) — the acceptance criteria in the spec (FR-1, FR-2) imply new test cases are expected here: metadata-drift-is-corrected, cron-is-preserved, isEnabled-is-preserved. Per the spec's "Out of Scope" section, exact test authorship is left to the implementer, but the existing test file is clearly the right home for them — no new test file/module needed.

### Interfaces and Contracts
No interface changes. Confirmed by reading the actual signatures in this checkout:
- `RecurringJobConfiguration.UpdateConfiguration(string displayName, string description, string cronExpression, string modifiedBy)` — `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs:71-91`. Validates all four args are non-blank (throws `ValidationException` otherwise); since `job.Metadata` values are code-defined string literals, these guards will not trip in practice.
- `IRecurringJobConfigurationRepository.UpdateAsync(RecurringJobConfiguration, CancellationToken)` — `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs:8`, implemented via EF Core `Update()` + `SaveChangesAsync()` in `RecurringJobConfigurationRepository`.

One implementation nuance worth flagging explicitly for the developer: `existing` is fetched via `GetByJobNameAsync` on the *same* `ApplicationDbContext`/scope the seeder runs in (both seeder and repository are registered `Scoped`, and seeding runs once per scope at startup — see `ServiceCollectionExtensions.cs` around line 463). That means `existing` is already a change-tracked entity by the time `UpdateAsync` calls `_context.RecurringJobConfigurations.Update(existing)` — this is harmless (EF allows re-marking an already-tracked entity as `Modified`) but means the explicit `UpdateAsync` call is technically redundant with EF's own change tracking. Do not "optimize" this away by removing the `UpdateAsync` call — keep calling it explicitly, both for symmetry with the insert branch and because it keeps the seeder decoupled from assumptions about EF tracking/scope lifetimes.

### Data Flow
1. At startup, `ServiceCollectionExtensions` resolves discovered `IRecurringJob` implementations and calls `IRecurringJobSeeder.SeedDefaultConfigurationsAsync`.
2. For each job, the seeder builds an in-memory `RecurringJobConfiguration` from `job.Metadata` (used only as a values carrier for the insert path and as the source of `DisplayName`/`Description` for the update path — it is never persisted directly in the update path).
3. `GetByJobNameAsync` checks for an existing row.
   - Missing → `AddAsync(config)` inserts the freshly-built entity as today.
   - Present → call `existing.UpdateConfiguration(config.DisplayName, config.Description, existing.CronExpression, "System")`, then `UpdateAsync(existing)`. Note `existing.CronExpression` (not `config.CronExpression`) is passed — this is the crux of the fix: it makes `UpdateConfiguration` a no-op on the field it must not touch, while the entity's own current `IsEnabled` is left alone entirely because `UpdateConfiguration` does not accept it as a parameter.
4. `LastModifiedAt`/`LastModifiedBy` are stamped by `UpdateConfiguration` itself (`"System"`), giving auditors a way to distinguish seeder-driven writes from admin-driven writes (which use different `modifiedBy` values via `UpdateRecurringJobCronHandler`/`UpdateRecurringJobStatusHandler`).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Every app restart now issues an `UPDATE` per existing job row, increasing write volume vs. today's read-only steady state | Low | Accepted per spec NFR-1; volume is low tens of rows, startup-only, well within EF/SQLite-or-SQL-Server write budget |
| A future developer adds a genuinely admin-editable field to `RecurringJobMetadata`/`RecurringJobConfiguration` and wires it through `UpdateConfiguration` without realizing the seeder would then silently overwrite it | Low-Medium | Keep the doc-comment on `SeedDefaultConfigurationsAsync` accurate and explicit about "code-owned vs. admin-owned" so the next change respects the same split; this review calls out updating that comment as part of this change |
| XML doc comment on `SeedDefaultConfigurationsAsync` becomes stale/misleading if not updated alongside the code | Low | Update the comment as part of this change (see Directory/Module Structure section) |

## Specification Amendments
None to the functional scope. One clarifying addition: the spec's "Out of Scope" section defers test authorship to the implementer; this review recommends (not requires, since the brief is architecture-only) that the implementer add test cases to the existing `RecurringJobSeederTests.cs` file directly covering FR-1's three acceptance criteria (metadata updated, cron/isEnabled preserved, insert path unchanged) rather than creating a new test file — this keeps all seeder behavior tests co-located as they are today.

## Prerequisites
None. No migrations, no config, no new infrastructure — this is a pure in-place code change to `RecurringJobSeeder.SeedDefaultConfigurationsAsync`, ready to implement directly against `main`/the current worktree.
