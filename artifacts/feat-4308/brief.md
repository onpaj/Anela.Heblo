## Module
Configuration

## Finding
`GetConfigurationResponse` includes a `Timestamp` field (set to `DateTime.UtcNow` in the handler) that is not an application-wide configuration value.

File: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs`, line 27.
Set at: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`, line 36.

`docs/architecture/development_guidelines.md` explicitly defines the scope of this endpoint:

> The Configuration module exposes **only application-wide values (version, environment, mock-auth flag)**. Module-specific values live on a module-owned anonymous endpoint.

`Timestamp` is none of the three documented values — it is the server clock at handler execution time, not an application configuration property.

The sole consumer is `frontend/src/services/versionService.ts` (lines 98–99), where the field is used as:

```ts
timestamp: response.timestamp?.toISOString() || new Date().toISOString(),
```

The `|| new Date().toISOString()` fallback is already correct: the frontend can always use its own clock here, making the backend-supplied timestamp redundant.

## Why it matters
- Violates the documented scope rule, which exists to prevent the `/api/configuration` endpoint from becoming a catch-all bootstrap bag.
- Every future module that needs a bootstrap timestamp has a precedent to add its own field here instead of owning a dedicated endpoint.
- The frontend fallback demonstrates the value adds no real information.

## Suggested fix
1. Remove `Timestamp` from `GetConfigurationResponse` and the `Handler` assignment.
2. In `versionService.ts`, replace `response.timestamp?.toISOString() || new Date().toISOString()` with just `new Date().toISOString()` (the fallback already in place).

No behavior change — the frontend was already falling back to the local clock if the value was absent.

---
_Filed by daily arch-review routine on 2026-09-24._