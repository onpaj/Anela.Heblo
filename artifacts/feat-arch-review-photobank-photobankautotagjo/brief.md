## Module
Photobank

## Finding
The two recurring jobs in this module use different mechanisms to guard against running when disabled:

**`PhotobankIndexJob.ExecuteAsync`** (line 38–42 in `PhotobankIndexJob.cs`):
```csharp
if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
{
    _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
    return;
}
```
Uses `IRecurringJobStatusChecker` — a runtime-queryable mechanism (presumably backed by a database record), so operators can enable/disable the job without touching config or restarting the app.

**`PhotobankAutoTagJob.ExecuteAsync`** (line 45–50 in `PhotobankAutoTagJob.cs`):
```csharp
if (!_options.Enabled)
{
    _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
    return;
}
```
Uses `AutoTagOptions.Enabled` — read from `appsettings.json` at startup (`Photobank:AutoTag:Enabled`). Changing this requires editing config and restarting the application.

`PhotobankAutoTagJob.Metadata.DefaultIsEnabled = false`, which means the job is off by default; but if someone ever wants to enable it via a Hangfire dashboard or admin UI (the same way they'd manage `PhotobankIndexJob`), they cannot — the `IRecurringJobStatusChecker` gate is simply absent.

## Why it matters
- Operational inconsistency: a user who can toggle `photobank-index` at runtime has no equivalent control over `photobank-auto-tag` — they would need a config change and app restart.
- The `DefaultIsEnabled` on `Metadata` exists precisely so that the job framework can own the enabled/disabled state. Bypassing this with a static config option defeats the purpose.
- If the auto-tag feature ever needs to be enabled temporarily (e.g., for a bulk re-tag run), the correct procedure is unclear and differs from every other job in the system.

## Suggested fix
Replace the `_options.Enabled` guard with `IRecurringJobStatusChecker`, exactly as `PhotobankIndexJob` does:

```csharp
// 1. Add to constructor
private readonly IRecurringJobStatusChecker _statusChecker;

public PhotobankAutoTagJob(
    IPhotobankRepository repo,
    IChatClient chat,
    IOptions<AutoTagOptions> options,
    ILogger<PhotobankAutoTagJob> logger,
    IPhotobankTagsCache cache,
    IRecurringJobStatusChecker statusChecker)   // add
{
    ...
    _statusChecker = statusChecker;
}

// 2. In ExecuteAsync, replace the Enabled check:
if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
{
    _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
    return;
}
```

The `AutoTagOptions.Enabled` field can be removed once the check is unified. `DefaultIsEnabled = false` on the metadata already ensures the job starts disabled.

---
_Filed by daily arch-review routine on 2026-06-14._