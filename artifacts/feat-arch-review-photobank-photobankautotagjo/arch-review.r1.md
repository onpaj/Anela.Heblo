Now I have enough context. Let me write the architecture review.

# Architecture Review: Unify PhotobankAutoTagJob enable/disable with IRecurringJobStatusChecker

## Skip Design: true

## Architectural Fit Assessment
The proposal aligns cleanly with an existing, well-established pattern in the codebase. `IRecurringJobStatusChecker` is the canonical gating mechanism for `IRecurringJob` implementations (used today by `PhotobankIndexJob` and registered globally in `BackgroundJobsModule.cs:21`). The `RecurringJobConfiguration` table is already seeded at startup from `IRecurringJob` metadata via reflection (`AddRecurringJobs()` in `ServiceCollectionExtensions.cs:376`, then `app.SeedRecurringJobConfigurationsAsync()` in `Program.cs:158`), so `PhotobankAutoTagJob` will be discovered, seeded with `IsEnabled = DefaultIsEnabled = false`, and toggleable through the same UseCases (`UpdateRecurringJobStatusHandler`, `UpdateRecurringJobCronHandler`) that already drive `photobank-index`.

Integration points are narrow: the job constructor, the first statement of `ExecuteAsync`, the options class, two `appsettings*.json` entries, and the test fixture. No new contracts, no API changes, no migration, no new DI registration.

There is **one architectural risk that contradicts spec FR-3**, surfaced below in *Risks* and *Specification Amendments*: `RecurringJobStatusChecker.IsJobEnabledAsync` currently fails *open* (returns `true`) when no configuration row exists, instead of falling back to `DefaultIsEnabled`. The spec's "out of scope: refactoring `IRecurringJobStatusChecker`" clause conflicts with FR-3's "first-time resolution falls back to `DefaultIsEnabled` rather than defaulting to true." This must be resolved before implementation.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────┐
│ Hangfire scheduler (cron: 0 4 * * *, Europe/Prague)      │
└──────────────────────┬───────────────────────────────────┘
                       ▼
┌──────────────────────────────────────────────────────────┐
│ PhotobankAutoTagJob.ExecuteAsync                         │
│   1. IRecurringJobStatusChecker.IsJobEnabledAsync ───┐   │
│   2. (if enabled) load tags, page, batch, LLM, stamp │   │
└──────────────────────────────────────────────────────┼───┘
                                                       ▼
┌──────────────────────────────────────────────────────────┐
│ RecurringJobStatusChecker                                │
│  → IRecurringJobConfigurationRepository.GetByJobNameAsync│
└──────────────────────┬───────────────────────────────────┘
                       ▼
┌──────────────────────────────────────────────────────────┐
│ ApplicationDbContext.RecurringJobConfigurations          │
│ row "photobank-auto-tag" (seeded at startup, default off)│
└──────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────┐
│ Operator UI / API                                        │
│  → UpdateRecurringJobStatusHandler (MediatR)             │
│    flips IsEnabled, persists via repository              │
└──────────────────────────────────────────────────────────┘

Parallel ad-hoc path (unchanged, deliberately ungated):
RetagPhotosHandler → IBackgroundWorker.Enqueue<PhotobankAutoTagJob>
                  → PhotobankAutoTagJob.ExecuteForPhotosAsync
```

### Key Design Decisions

#### Decision 1: Single source of truth for "is this job enabled?"
**Options considered:**
- (A) Add the status-checker gate *in addition to* `_options.Enabled` (belt-and-braces).
- (B) Replace `_options.Enabled` with the status-checker gate; remove the option.

**Chosen approach:** B (matches spec FR-1/FR-2).
**Rationale:** Two flags would let the dashboard report a job as enabled while a stale config silently suppresses it — exactly the "metadata bypass" problem cited in the brief. The status-checker, backed by the `RecurringJobConfiguration` table seeded from `DefaultIsEnabled`, is already the canonical owner of this state across every other recurring job.

#### Decision 2: Where to enforce default-disabled semantics
**Options considered:**
- (A) Rely solely on the existing seeding path (`SeedDefaultConfigurationsAsync` → row with `IsEnabled = false`) and accept that `RecurringJobStatusChecker`'s "missing config → return true" fallback remains in place.
- (B) Tighten `RecurringJobStatusChecker` so a missing row consults `RecurringJobMetadata.DefaultIsEnabled` (requires the checker to know about jobs, or for callers to pass the default).
- (C) Pass `Metadata.DefaultIsEnabled` from the job to the checker, e.g. `IsJobEnabledAsync(jobName, defaultIfMissing, ct)`.

**Chosen approach:** **A is unsafe; C is the smallest correct change.** Implementers should add an optional `bool defaultIfMissing` parameter (default `true` for backwards-compat with `PhotobankIndexJob`) and pass `Metadata.DefaultIsEnabled` from `PhotobankAutoTagJob`.
**Rationale:** Option A relies on seeding completing before the cron fires. Seeding is best-effort within the scope of a successful app boot, but if a row is ever absent (manual DB cleanup, a future test scenario, a partial migration), the checker returns `true` and the auto-tag job runs — producing LLM cost and tag writes against the operator's expectation. Option B would require either reading job metadata inside the checker (cross-cutting) or hard-coding defaults. Option C is one new parameter with a backwards-compatible default; `PhotobankIndexJob`'s call site does not need to change.

#### Decision 3: Keep `ExecuteForPhotosAsync` ungated
**Chosen approach:** Pass `IRecurringJobStatusChecker` to the constructor but do **not** call it from `ExecuteForPhotosAsync` (spec FR-5).
**Rationale:** The ad-hoc path is operator-initiated through `RetagPhotosHandler` and represents an explicit "yes, run this now" decision. Coupling it to the recurring schedule's toggle would make targeted re-tagging impossible while the recurring run is paused — exactly the scenario the brief calls out as desirable.

#### Decision 4: Treat the obsolete `Photobank:AutoTag:Enabled` key as silent dead config
**Chosen approach:** Remove `Enabled` from `AutoTagOptions` and from `appsettings.json`. Do not add a deprecation warning. Mention removal in release notes (spec NFR-3).
**Rationale:** .NET's options binder ignores unmapped properties; emitting a warning would require either a custom validator or a startup probe, both of which add code for a one-time migration. Acceptable for a solo-dev-plus-AI-review project.

## Implementation Guidance

### Directory / Module Structure
No new files. Modifications confined to:

- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs` — add ctor dep, replace gate.
- `backend/src/Anela.Heblo.Application/Features/Photobank/AutoTagOptions.cs` — remove `Enabled`.
- `backend/src/Anela.Heblo.API/appsettings.json` — remove the `Photobank:AutoTag:Enabled` key (it is the only `appsettings*.json` carrying it — verified by `grep`).
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs` — note the spec's path (`.../Jobs/PhotobankAutoTagJobTests.cs`) is wrong; the file is at `.../Features/Photobank/PhotobankAutoTagJobTests.cs`.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobStatusChecker.cs` and `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobStatusChecker.cs` — per Decision 2, add the optional `defaultIfMissing` parameter (see *Specification Amendments*).

`PhotobankModule.AddPhotobankModule`'s `services.AddScoped<PhotobankAutoTagJob>()` registration stays as-is and is sufficient — the type is *also* auto-discovered by `AddRecurringJobs()` reflection, which is what makes it appear in `GetServices<IRecurringJob>()` for seeding and discovery.

### Interfaces and Contracts

```csharp
// Domain (Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobStatusChecker.cs)
public interface IRecurringJobStatusChecker
{
    Task<bool> IsJobEnabledAsync(
        string jobName,
        bool defaultIfMissing = true,          // ← new, defaults preserve existing callers
        CancellationToken cancellationToken = default);
}

// PhotobankAutoTagJob ctor
public PhotobankAutoTagJob(
    IPhotobankRepository repo,
    IChatClient chat,
    IOptions<AutoTagOptions> options,
    ILogger<PhotobankAutoTagJob> logger,
    IPhotobankTagsCache cache,
    IRecurringJobStatusChecker statusChecker);

// PhotobankAutoTagJob.ExecuteAsync — first statement
if (!await _statusChecker.IsJobEnabledAsync(
        Metadata.JobName,
        Metadata.DefaultIsEnabled,             // ← passes false for auto-tag
        cancellationToken))
{
    _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
    return;
}
```

`AutoTagOptions` becomes a pure tuning-knob class with `BatchSize`, `MaxPhotosPerRun`, `Model`, `MaxTagsPerPhoto`, and the `SectionName` constant. Per the project's "DTOs are classes, never records" rule and the existing style of the file, it stays a `sealed class` with `init` setters.

### Data Flow

**Scheduled run (cron fires at 04:00 Europe/Prague):**
1. Hangfire invokes `PhotobankAutoTagJob.ExecuteAsync`.
2. The job calls `IRecurringJobStatusChecker.IsJobEnabledAsync("photobank-auto-tag", defaultIfMissing: false, ct)`.
3. Checker reads `RecurringJobConfigurations` row. Disabled (default) → log and return. Enabled → proceed.
4. On the proceed path, existing behavior is unchanged: load vocabulary, page pending candidates, LLM call per batch, validate and stamp tags, persist.

**Operator toggle:** `UpdateRecurringJobStatusHandler` flips `IsEnabled` on the row. Next cron firing observes the new value — no restart, no config edit.

**Ad-hoc re-tag (operator-driven):** `RetagPhotosHandler` resolves candidates and enqueues `j => j.ExecuteForPhotosAsync(candidates, ct)` via `IBackgroundWorker`. The status-checker is **not** consulted. Runs even when the cron toggle is off.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `RecurringJobStatusChecker` fails open on missing config row, contradicting spec FR-3 default-disabled guarantee for auto-tag. | **HIGH** | Adopt Decision 2 / Option C: thread `Metadata.DefaultIsEnabled` through the checker. Without this, a startup-seeding failure or absent row would silently enable LLM calls. |
| Stale `Photobank:AutoTag:Enabled` in environment-specific config (Azure App Service settings, Key Vault) is silently ignored. | LOW | NFR-3 already calls for release-note guidance. Optionally `grep` Key Vault (`az keyvault secret list --vault-name kv-heblo-stg --query "[?contains(name, 'AutoTag')]"`) before deploy. |
| Tests still passing `Enabled = true/false` in `AutoTagOptions` ctor break the build because the property no longer exists. | MED | Mechanical fix per FR-4; compiler catches every site. Add the explicit `ExecuteForPhotosAsync` ungated test from FR-5. |
| `PhotobankAutoTagJob` accidentally double-registered as both `IRecurringJob` (via reflection) and `PhotobankAutoTagJob` (explicit) — already true today, not introduced here. | LOW | Leave as-is; not on the critical path of this change. Worth noting in a follow-up cleanup. |
| Discovery via `Assembly.Load("Anela.Heblo.Application").GetTypes()` would silently miss the job if it were ever moved to a different assembly. | LOW | Not affected by this change; flag for awareness only. |

## Specification Amendments

1. **FR-3 vs. Out-of-Scope conflict — must be resolved.** The spec simultaneously requires "first-time resolution falls back to `DefaultIsEnabled` rather than defaulting to `true`" (FR-3) and forbids "refactoring or generalizing `IRecurringJobStatusChecker`" (Out of Scope). The minimal touch — adding an optional `defaultIfMissing` parameter (default `true`) — is genuinely additive, preserves all existing callers, and is the smallest correct way to satisfy FR-3. Update Out-of-Scope to read: *"Refactoring the persistence schema or read path of `IRecurringJobStatusChecker`; an additive `defaultIfMissing` parameter on the interface is in scope and required by FR-3."*

2. **FR-4 test file path is wrong.** Spec says `PhotobankAutoTagJobTests` lives somewhere implying a `Jobs/` subfolder. Actual location is `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs`. Update the spec accordingly.

3. **FR-5 add concrete test name.** Recommend explicit test `ExecuteForPhotosAsync_RunsEvenWhenStatusCheckerReturnsFalse` to lock in the deliberate ungated behavior.

4. **FR-6 wording slightly misleading.** `PhotobankAutoTagJob` is registered *both* explicitly in `PhotobankModule` (`AddScoped<PhotobankAutoTagJob>()`) *and* implicitly via `AddRecurringJobs()` reflection in `ServiceCollectionExtensions.cs`. The reflection-based registration is what makes it appear in `GetServices<IRecurringJob>()` for seeding and Hangfire discovery. The explicit one supports `IBackgroundWorker.Enqueue<PhotobankAutoTagJob>` from `RetagPhotosHandler`. Note this in FR-6 so it doesn't read as if one registration site is the whole story.

## Prerequisites

None beyond what already exists. Concretely:

- `IRecurringJobStatusChecker` and `IRecurringJobConfigurationRepository` are registered in `BackgroundJobsModule`. ✓
- `RecurringJobConfigurations` table and EF Core configuration exist (`RecurringJobConfigurationConfiguration.cs`). ✓
- `SeedDefaultConfigurationsAsync` is wired into the app boot path (`Program.cs:158`) and will pick up `PhotobankAutoTagJob` automatically. ✓
- No new migration. No new App Service / Key Vault settings.

On deploy, the new `photobank-auto-tag` row will be inserted with `IsEnabled = false` on first start; operators must use the same dashboard/API used for `photobank-index` to enable it.