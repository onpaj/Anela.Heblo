# Feature Flags

## What this system is

Feature flags are evaluated in order: **DB override → `appsettings.json` → registry default**.

- `Microsoft.FeatureManagement` reads `appsettings.json` under `FeatureManagement:` section.
- A `FeatureFlagOverrides` Postgres table stores runtime overrides set via the admin UI.
- `HebloFeatureProvider` (OpenFeature) merges both layers for business code.
- Admin endpoints are protected by `super_user` role, same as Photobank settings.

## How to add a new flag (two steps)

**Step 1 — Add to the registry:**
```csharp
// backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagKeys.cs
public const string MyFeature = "is-my-feature-enabled";

// backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagRegistry.cs
new(FeatureFlagKeys.MyFeature,
    Description: "One sentence: what this flag controls.",
    DefaultValue: false),
```

**Step 2 — Add default to appsettings.json:**
```json
{ "FeatureManagement": { "is-my-feature-enabled": false } }
```

Naming: `is-<feature>-enabled`, lowercase kebab-case, boolean only in v1.

**Step 3 — Mirror in frontend:**
```typescript
// frontend/src/features/feature-flags/featureFlags.ts
MyFeature: "is-my-feature-enabled",
```

## How to check a flag

**Backend — business code:**
```csharp
public class MyHandler(IFeatureFlagChecker flags)
{
    public async Task Handle(..., CancellationToken ct)
    {
        if (await flags.IsEnabledAsync(FeatureFlagKeys.MyFeature, ct))
        {
            // feature on
        }
    }
}
```

**Backend — controller/endpoint gating (config-only, v1 limitation):**
```csharp
[FeatureGate(FeatureFlagKeys.MyFeature)]
public class MyController : ControllerBase { ... }
```

Note: `[FeatureGate]` reads `appsettings.json` only — it does NOT see DB overrides.
This is a v1 accepted limitation. Use `IFeatureFlagChecker` in business logic instead.

**Frontend:**
```tsx
const enabled = useFeatureFlag(FeatureFlagKeys.MyFeature, false);
```

## Anti-patterns

- Do **not** call OpenFeature SDK directly — always use `IFeatureFlagChecker`.
- Do **not** hard-code flag key strings — always use `FeatureFlagKeys` constants.
- Do **not** place `[FeatureGate]` on admin/infrastructure endpoints (lockout risk).

## Admin endpoints

`GET /api/feature-flags/admin` — list all flags + current value + override metadata  
`PUT /api/feature-flags/admin/{key}` — upsert a DB override  
`DELETE /api/feature-flags/admin/{key}` — clear override (reverts to `appsettings.json`)

All admin endpoints require `super_user` role. Navigate to `/admin/feature-flags` in the app.

## Lockout protection

1. Admin endpoints (`FeatureFlagsController`) are **never** `[FeatureGate]`-d — a lint test enforces this.
2. Flag evaluation always fail-opens to the supplied default — never throws.
3. `appsettings.json` is the last-resort recovery surface: edit + restart restores any flag.
4. The provider caches DB overrides for 30 seconds; cache is invalidated immediately on admin writes.

## Flag lifecycle

When a feature is fully launched, remove the flag in a cleanup PR:
1. Delete the `FeatureFlagKeys` constant
2. Delete the `FeatureFlagRegistry.All` entry
3. Delete the `appsettings.json` line
4. Delete any DB override row
5. Delete call sites

Stale flags are technical debt. Review the registry quarterly.

## Current flag inventory

See `FeatureFlagRegistry.cs` as the source of truth. Do not duplicate the list here.
