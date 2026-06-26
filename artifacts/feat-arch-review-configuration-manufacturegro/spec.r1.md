# Specification: Decouple ManufactureGroupId from Configuration Module

## Summary
Remove the `ManufactureGroupId` Entra group identifier from the cross-cutting Configuration module's response and relocate it to a new Manufacture-owned bootstrap endpoint. This restores Vertical Slice module boundaries (Configuration must not carry Manufacture-specific domain knowledge) and eliminates the raw-string config key, replacing it with a typed constant scoped to the Manufacture module.

## Background
The Configuration module currently exposes a `ManufactureGroupId` property via `GetConfigurationResponse` (Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs:37). The value is read by `GetConfigurationHandler` directly from `IConfiguration` using a raw string literal `"ManufactureGroupId"` (line 34) with no defined constant.

The value is an Azure Entra group identifier identifying "responsible persons" for Manufacture orders — semantically owned by the Manufacture module. The property exists on the Configuration response because Configuration is the anonymous bootstrap endpoint the SPA calls before authentication.

Two boundary violations result:
1. **Domain leakage:** Configuration carries Manufacture-specific knowledge. Any future Manufacture change to how it identifies its Entra group forces edits to the Configuration module and its DTO contract.
2. **Implicit shared bag:** Without intervention, every module that needs a bootstrap-time value will repeat this pattern, making `GetConfigurationResponse` a junk drawer for cross-module config.

A secondary defect: the raw `"ManufactureGroupId"` literal is not centralised in a constant, so typos in either the `appsettings.json` key or the handler would fail silently (returning a default value rather than throwing).

The fix aligns with the Vertical Slice organisation documented in `docs/architecture/development_guidelines.md` and `docs/📘 Architecture Documentation – MVP Work.md`: modules own their DTOs and do not share domain types across module boundaries.

## Functional Requirements

### FR-1: Remove ManufactureGroupId from Configuration Response
The `GetConfigurationResponse` DTO must no longer expose the `ManufactureGroupId` property. The `GetConfigurationHandler` must no longer read the `"ManufactureGroupId"` configuration key.

**Acceptance criteria:**
- `GetConfigurationResponse` has no `ManufactureGroupId` property after the change.
- `GetConfigurationHandler` contains no reference to `"ManufactureGroupId"` (string literal) or `IConfiguration["ManufactureGroupId"]`.
- The Configuration module's existing tests pass without referencing `ManufactureGroupId`.
- The generated OpenAPI TypeScript client no longer carries `manufactureGroupId` on the configuration response.

### FR-2: Expose ManufactureGroupId via a Manufacture-Owned Bootstrap Endpoint
Introduce a new endpoint owned by the Manufacture module that returns Manufacture-specific bootstrap settings, starting with `manufactureGroupId`. The endpoint must be anonymously accessible (same as the existing Configuration endpoint) because the SPA needs the value before authentication completes.

**Acceptance criteria:**
- A new endpoint `GET /api/manufacture/settings` exists, owned by the Manufacture module under `backend/src/Anela.Heblo.Application/Features/Manufacture/`.
- The endpoint is decorated `[AllowAnonymous]` (or equivalent) and returns HTTP 200 with a JSON body containing at least `manufactureGroupId` (string, nullable).
- The endpoint follows the existing MediatR + MVC controller pattern used elsewhere in the codebase (request, handler, response DTO; controller action delegates via `IMediator`).
- DTO is a `class`, not a `record` (per project rule on OpenAPI client generation).
- A `GetManufactureSettingsHandler` reads the configuration value via the typed constant defined in FR-3.
- The handler returns `null` (not an exception) when the configuration value is missing — preserving current Configuration handler behaviour.
- The endpoint is registered in the generated OpenAPI client.

### FR-3: Introduce a Typed Constant for the Configuration Key
Replace the raw `"ManufactureGroupId"` string literal with a typed constant scoped to the Manufacture module.

**Acceptance criteria:**
- A constants class (e.g. `ManufactureConfigurationKeys`) lives under `backend/src/Anela.Heblo.Application/Features/Manufacture/` (or the existing Manufacture domain folder) and exposes a `public const string GroupId = "ManufactureGroupId";` (or equivalent naming).
- `GetManufactureSettingsHandler` references this constant — no raw string literal for the key remains anywhere in production code.
- The `appsettings.json` / Azure Key Vault key name `ManufactureGroupId` is unchanged (no deployment-time change required).

### FR-4: Update the SPA to Call the New Endpoint
The frontend bootstrap flow must fetch `manufactureGroupId` from the new Manufacture endpoint instead of from `/api/configuration`.

**Acceptance criteria:**
- The SPA calls the new `GET /api/manufacture/settings` endpoint during startup, in parallel with the existing `/api/configuration` call.
- Wherever the frontend currently consumes `configuration.manufactureGroupId`, it now consumes the value from the Manufacture settings response.
- The frontend handles a `null`/missing `manufactureGroupId` with the same behaviour it has today (degraded UX, no crash).
- `npm run build` and `npm run lint` succeed after the change.
- The TypeScript types regenerated from OpenAPI reflect the new endpoint shape; no stale `manufactureGroupId` references remain.

### FR-5: No Regression in Anonymous Access
Both `/api/configuration` and `/api/manufacture/settings` must remain reachable without authentication so the SPA can complete its bootstrap before Entra ID sign-in.

**Acceptance criteria:**
- A test (unit or integration) asserts that `GET /api/manufacture/settings` returns 200 when called without an `Authorization` header.
- Existing tests for anonymous access to `/api/configuration` continue to pass.

### FR-6: Documentation Update
Update relevant architecture documentation to reflect the new module boundary.

**Acceptance criteria:**
- `docs/architecture/development_guidelines.md` (or the appropriate architecture doc) mentions that module-specific bootstrap values belong to that module's anonymous settings endpoint, not to the Configuration module.
- If a per-feature spec exists for Configuration or Manufacture under `docs/features/`, it is updated to reference the new endpoint.

## Non-Functional Requirements

### NFR-1: Performance
The added HTTP call increases SPA cold-start by one parallel request. Targets:
- New endpoint p95 response time ≤ 50 ms on a warm app (it reads one config value).
- Frontend must issue `/api/configuration` and `/api/manufacture/settings` in parallel (not sequentially) so total bootstrap latency is bounded by the slower call, not the sum.

### NFR-2: Security
- Endpoint is intentionally anonymous (same posture as `/api/configuration`).
- The returned `manufactureGroupId` is a non-secret identifier (a public Entra group OID). It must not be sourced from Azure Key Vault as a secret; it stays in `appsettings.json` / App Settings as it is today.
- No additional CORS or rate-limit changes required; existing anonymous-endpoint policies apply.

### NFR-3: Backwards Compatibility
- The `ManufactureGroupId` config key name in `appsettings.json` does not change — no deployment-time edits needed.
- Frontend and backend ship together (single Docker image), so no API-versioning concern: the old field can be removed in the same release that adds the new endpoint.

### NFR-4: Testability
- Unit test for `GetManufactureSettingsHandler` verifying it returns the configured value and `null` when missing.
- Integration test (or controller test) verifying anonymous access to `/api/manufacture/settings`.
- Existing Configuration handler tests updated to remove `ManufactureGroupId` assertions; no new ones added there.

## Data Model
No database changes. The change is at the contract/DTO level:

- **Removed:** `GetConfigurationResponse.ManufactureGroupId` (string).
- **Added:** `GetManufactureSettingsResponse.ManufactureGroupId` (string, nullable) — new DTO class owned by the Manufacture module.
- **Added:** `ManufactureConfigurationKeys.GroupId` constant (string `"ManufactureGroupId"`).

Configuration source-of-truth is unchanged: the value lives at `appsettings.json` (or environment-specific override) under key `ManufactureGroupId`.

## API / Interface Design

### New endpoint
```
GET /api/manufacture/settings
Authorization: not required (anonymous)

200 OK
Content-Type: application/json
{
  "manufactureGroupId": "00000000-0000-0000-0000-000000000000"  // or null
}
```

### Modified endpoint
```
GET /api/configuration
Authorization: not required (anonymous)

200 OK
{
  // ...existing fields, with manufactureGroupId removed
}
```

### Frontend flow
At application bootstrap, the SPA dispatches `/api/configuration` and `/api/manufacture/settings` in parallel (`Promise.all` or equivalent). The Manufacture settings response is stored alongside the existing configuration state in whatever store/context the frontend uses today for bootstrap data. Consumers of `manufactureGroupId` are updated to read from the new location.

### MediatR / Controller pattern
Following the existing pattern in the codebase:
- `GetManufactureSettingsRequest : IRequest<GetManufactureSettingsResponse>`
- `GetManufactureSettingsHandler : IRequestHandler<GetManufactureSettingsRequest, GetManufactureSettingsResponse>`
- `GetManufactureSettingsResponse` (class, not record)
- `ManufactureController.GetSettings()` action delegating via `IMediator`, decorated `[AllowAnonymous]`

## Dependencies
- **`Microsoft.Extensions.Configuration.IConfiguration`** — existing dependency, used by the new handler to read the config value.
- **MediatR + ASP.NET Core MVC controllers** — existing project patterns.
- **OpenAPI client generation** (`docs/development/api-client-generation.md`) — runs on build; the regenerated TypeScript client will surface the new endpoint and remove the stale field.
- **No new NuGet or npm packages.**
- No external service dependency. No database migration.

## Out of Scope
- Migrating other Configuration response properties to their respective module endpoints (each is a separate decision; this spec covers `ManufactureGroupId` only).
- Adding additional Manufacture bootstrap values beyond `ManufactureGroupId` (the new endpoint is shaped to allow it but only `manufactureGroupId` is delivered now).
- Caching the new endpoint response on the frontend beyond what the existing Configuration call already does.
- Moving the `ManufactureGroupId` value into Azure Key Vault — it is a non-secret identifier and stays in App Settings / `appsettings.json`.
- Renaming the underlying configuration key `ManufactureGroupId` in `appsettings.json` or Azure App Settings.
- Versioning or deprecation shims for the removed field — single-repo deploy means the frontend and backend ship together.

## Open Questions
None.

## Status: COMPLETE