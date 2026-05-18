## Module
Marketing

## Finding
Both write handlers check `PushEnabled` via `IOptions<MarketingCalendarOptions>`, which is a startup snapshot and does not update at runtime:

- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs:25,70`
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs:25,70`

```csharp
private readonly IOptions<MarketingCalendarOptions> _options;
...
if (_options.Value.PushEnabled)   // ← always the startup snapshot
```

By contrast, `MarketingCategoryMapper` is registered as a singleton and explicitly uses `IOptionsMonitor<MarketingCalendarOptions>` with a snapshot-on-failure strategy, documenting that runtime config updates (e.g., via Azure App Config) are an intentional design goal for this module.

## Why it matters
`PushEnabled` is the only kill-switch for Outlook calendar sync. In an incident where outbound Graph calls need to be disabled without a restart (e.g., Graph API degradation, token issues), flipping `PushEnabled = false` in config has no effect because the handlers read the startup value. The `CategoryMappings` hot-reload pattern establishes intent — its absence on `PushEnabled` is the higher-impact omission.

## Suggested fix
Change both handlers to inject `IOptionsMonitor<MarketingCalendarOptions>` and access `_options.CurrentValue.PushEnabled`:

```csharp
// Constructor
private readonly IOptionsMonitor<MarketingCalendarOptions> _options;

public CreateMarketingActionHandler(..., IOptionsMonitor<MarketingCalendarOptions> options, ...)
{
    _options = options;
}

// Usage
if (_options.CurrentValue.PushEnabled)
```

Same change in `UpdateMarketingActionHandler`. `OutlookCalendarSyncService` also stores `options.Value` at construction (line 43), but since it is scoped and `GroupId`/`PushEnabled` are unlikely to change at runtime independently of the service lifetime, that is lower priority.

---
_Filed by daily arch-review routine on 2026-05-17._