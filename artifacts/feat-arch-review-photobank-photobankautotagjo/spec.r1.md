# Specification: Unify PhotobankAutoTagJob enable/disable with IRecurringJobStatusChecker

## Summary
Replace the static `AutoTagOptions.Enabled` configuration flag on `PhotobankAutoTagJob` with a runtime check via `IRecurringJobStatusChecker`, mirroring the gating mechanism already used by `PhotobankIndexJob`. This unifies how operators enable and disable recurring jobs across the Photobank module and removes the need for an app restart to toggle auto-tagging.

## Background
The Photobank module hosts two recurring jobs registered through the `IRecurringJob` abstraction with `RecurringJobMetadata`:

- `PhotobankIndexJob` (`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`) — guarded by `IRecurringJobStatusChecker.IsJobEnabledAsync(...)` (runtime, dashboard-toggleable).
- `PhotobankAutoTagJob` (`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs`) — guarded by `_options.Enabled` from `AutoTagOptions` (`Photobank:AutoTag:Enabled` in `appsettings.json`, requires restart to change).

This asymmetry has three concrete problems:

1. **Operational inconsistency.** An operator who can toggle `photobank-index` from the Hangfire dashboard or admin UI has no equivalent control over `photobank-auto-tag`. Toggling auto-tag requires a config change and full application restart.
2. **Metadata bypass.** `RecurringJobMetadata.DefaultIsEnabled` (set to `false` for the auto-tag job) exists so the recurring-job framework owns the enabled/disabled state. Reading a parallel `Enabled` flag from config defeats the metadata contract and risks conflicting state between the two sources of truth.
3. **Unclear runbook for temporary enabling.** If the team needs to run auto-tag temporarily (e.g., a bulk re-tag run after a vocabulary change), the procedure differs from every other recurring job in the system.

The remaining fields on `AutoTagOptions` (`BatchSize`, `MaxPhotosPerRun`, `Model`, `MaxTagsPerPhoto`) are genuine tuning knobs and stay as options. Only the `Enabled` field is being relocated.

## Functional Requirements

### FR-1: Replace Enabled guard with runtime status check
`PhotobankAutoTagJob.ExecuteAsync` must consult `IRecurringJobStatusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken)` instead of `_options.Enabled` to determine whether to run.

**Acceptance criteria:**
- `PhotobankAutoTagJob` accepts `IRecurringJobStatusChecker` as a constructor dependency.
- The first statement in `ExecuteAsync` is the status-checker gate that logs `"Job {JobName} is disabled. Skipping."` and returns early when the checker returns `false`. The log message and shape mirror `PhotobankIndexJob` exactly.
- When the checker returns `true`, `ExecuteAsync` proceeds to load tags, page through pending photos, and process batches as it does today.
- The reference to `_options.Enabled` is removed from `ExecuteAsync`.

### FR-2: Remove the Enabled field from AutoTagOptions
`AutoTagOptions.Enabled` must be removed from the options class because the job framework — via `IRecurringJobStatusChecker` seeded from `RecurringJobMetadata.DefaultIsEnabled = false` — now owns enabled/disabled state.

**Acceptance criteria:**
- `Enabled` is removed from `backend/src/Anela.Heblo.Application/Features/Photobank/AutoTagOptions.cs`.
- The remaining fields (`BatchSize`, `MaxPhotosPerRun`, `Model`, `MaxTagsPerPhoto`) and the `SectionName` constant are unchanged.
- All `appsettings*.json` files in the repository drop the `Photobank:AutoTag:Enabled` key. Configuration sources outside the repo (e.g., Azure App Service settings, Key Vault) are surveyed; the brief contains no evidence the key was set there, but any stale value would simply be ignored after this change — no migration required.
- A solution-wide search confirms no remaining references to `AutoTagOptions.Enabled` in production code or tests.

### FR-3: Preserve default-disabled behavior
The auto-tag job must remain disabled by default after deployment, so existing environments do not silently start running LLM calls.

**Acceptance criteria:**
- `RecurringJobMetadata.DefaultIsEnabled = false` is retained on `PhotobankAutoTagJob.Metadata` (no change).
- On a freshly seeded environment, the status checker returns `false` for job name `"photobank-auto-tag"` until an operator explicitly enables it through the same mechanism used for other recurring jobs.
- Existing environments where the job has not been explicitly toggled in the recurring-job status store also evaluate to disabled (i.e., the seeding logic for previously unknown jobs defers to `DefaultIsEnabled`). If `RecurringJobStatusChecker` does not already seed records from metadata on startup, the implementation must ensure first-time resolution falls back to `DefaultIsEnabled` rather than throwing or defaulting to `true`.

### FR-4: Update unit tests to match the new gating mechanism
`PhotobankAutoTagJobTests` currently constructs the job with five dependencies and asserts disabled behavior by passing `AutoTagOptions { Enabled = false }`. The tests must be updated for the new constructor signature and gating.

**Acceptance criteria:**
- The `CreateJob` test helper takes an additional `IRecurringJobStatusChecker` mock (default returns `true` so existing happy-path tests remain meaningful) and passes it to the constructor.
- `ExecuteAsync_WhenDisabled_DoesNotCallLlmOrRepository` is updated so the status-checker mock returns `false`; it must still assert that neither `IChatClient.GetResponseAsync` nor `IPhotobankRepository.GetTagsWithCountsAsync` is invoked.
- `AutoTagOptions` instances built in tests omit the `Enabled` field (since it no longer exists) and continue to populate `BatchSize`, `MaxPhotosPerRun`, `Model`, and `MaxTagsPerPhoto` as before.
- All existing tests in `PhotobankAutoTagJobTests` pass without behavioral regressions; the four scenarios (`WhenDisabled`, `WhenNoPendingPhotos`, `StampsAllPhotosInBatch_EvenWhenLlmReturnsEmptyTags`, `RespectsMaxTagsPerPhoto_Cap`, `AppliesValidTagsAndDropsHallucinations`) all stay green.

### FR-5: Keep ExecuteForPhotosAsync ungated
`PhotobankAutoTagJob` exposes a secondary entrypoint `ExecuteForPhotosAsync(IReadOnlyList<PhotoAutoTagCandidate>, CancellationToken)` used by ad-hoc/use-case-driven re-tagging flows. This path is intentionally not subject to the recurring-job toggle.

**Acceptance criteria:**
- `ExecuteForPhotosAsync` does **not** call `IRecurringJobStatusChecker`. It continues to process the supplied candidates unconditionally so that operator-driven bulk re-tag use cases still function while the recurring schedule is paused.
- This is verified by an explicit unit test: even when the status-checker mock returns `false`, `ExecuteForPhotosAsync` still invokes `IChatClient.GetResponseAsync` and stamps the supplied candidates.

### FR-6: Wire up DI registration
`PhotobankAutoTagJob` already resolves through DI. The change introduces a new dependency on `IRecurringJobStatusChecker`, which is already registered for `PhotobankIndexJob`.

**Acceptance criteria:**
- `PhotobankAutoTagJob` continues to be registered in `PhotobankModule.AddPhotobankModule` (`services.AddScoped<PhotobankAutoTagJob>()` line unchanged).
- No additional registration of `IRecurringJobStatusChecker` is needed in the Photobank module — it is registered globally by the background-jobs infrastructure. A startup smoke test (or `dotnet build` followed by container start) confirms the job resolves with all six dependencies wired.

## Non-Functional Requirements

### NFR-1: Performance
Runtime status check adds a single call to `IRecurringJobStatusChecker.IsJobEnabledAsync` per scheduled run (once per cron firing, at 04:00 Europe/Prague). This is the same pattern `PhotobankIndexJob` uses and has no measurable impact on job execution time. There is no per-batch overhead.

### NFR-2: Security
No new attack surface. Disabling the job via the existing recurring-job status mechanism is gated by the same admin/operator controls already used for `PhotobankIndexJob`. No new secrets, endpoints, or external dependencies are introduced.

### NFR-3: Backwards compatibility
- **Config drift:** Existing deployments may still have `Photobank:AutoTag:Enabled` set in `appsettings.*.json` or environment overrides. After this change the value is silently ignored because the binding target no longer exists. The release notes for this change should explicitly call out that the key can be deleted from environment-specific configuration. No runtime warning is emitted; .NET's options binder ignores unmapped JSON properties.
- **Operational equivalence:** Operators currently toggling auto-tag via config + restart must learn to toggle it via the same UI/dashboard used for `photobank-index`. Because the job has been default-disabled in production this far, no live runs are interrupted by the cut-over.

### NFR-4: Observability
The log line emitted on the disabled path keeps the same template and structured property name (`{JobName}`) as `PhotobankIndexJob`, so existing log queries and dashboards keyed on `"Job {JobName} is disabled. Skipping."` continue to work for both jobs.

## Data Model
No schema changes. The recurring-job status store that backs `IRecurringJobStatusChecker` already supports both jobs:

- The store is keyed by `RecurringJobMetadata.JobName` (`"photobank-auto-tag"`, `"photobank-index"`).
- On first observation of a job name, the seeding logic must respect `DefaultIsEnabled` from metadata (per FR-3).

The `AutoTagOptions` configuration shape changes by one field — removing `Enabled` — which is a code-level change, not a data change.

## API / Interface Design
No HTTP API changes. No new MediatR requests/responses. No new endpoints.

The internal interface affected is `PhotobankAutoTagJob`'s constructor:

```csharp
public PhotobankAutoTagJob(
    IPhotobankRepository repo,
    IChatClient chat,
    IOptions<AutoTagOptions> options,
    ILogger<PhotobankAutoTagJob> logger,
    IPhotobankTagsCache cache,
    IRecurringJobStatusChecker statusChecker); // new
```

And the body of `ExecuteAsync`:

```csharp
public async Task ExecuteAsync(CancellationToken cancellationToken = default)
{
    if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
    {
        _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
        return;
    }

    // existing logic unchanged from here
}
```

`AutoTagOptions` becomes:

```csharp
public sealed class AutoTagOptions
{
    public const string SectionName = "Photobank:AutoTag";

    public int BatchSize { get; init; } = 50;
    public int MaxPhotosPerRun { get; init; } = 5_000;
    public string Model { get; init; } = "claude-haiku-4-5-20251001";
    public int MaxTagsPerPhoto { get; init; } = 5;
}
```

## Dependencies
- **Existing:** `IRecurringJobStatusChecker` and its concrete implementation (`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobStatusChecker.cs`) are already in the codebase and DI graph.
- **Existing:** `RecurringJobMetadata.DefaultIsEnabled` semantics — relied on for seeding new job records as disabled.
- **No new libraries, NuGet packages, or services.**

## Out of Scope
- Refactoring or generalizing `RecurringJobMetadata` or `IRecurringJobStatusChecker`. The status-checker abstraction stays as-is; this change only adopts it from a second consumer.
- Building or modifying the operator UI / Hangfire dashboard control to toggle the job. That mechanism is assumed to already exist for `photobank-index`; making it work for `photobank-auto-tag` is a free consequence of using the same checker.
- Migrating other recurring jobs outside the Photobank module that may still use config-based enable flags. Those are tracked separately if and when discovered.
- Changing the cron schedule (`0 4 * * *`), batch size, model, or other tuning knobs on the auto-tag job.
- Adding a deprecation warning or runtime log when a stale `Photobank:AutoTag:Enabled` key is still present in configuration. The key is simply ignored.

## Open Questions
None.

## Status: COMPLETE