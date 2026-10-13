# Specification: Remove out-of-scope Timestamp field from GetConfigurationResponse

## Summary
`GetConfigurationResponse` currently exposes a `Timestamp` field set to `DateTime.UtcNow` at request time, which is not one of the three application-wide values (version, environment, mock-auth flag) that the Configuration module's documented contract permits. This change removes the field from the backend DTO and handler, and simplifies the sole frontend consumer to use its own local clock directly instead of relying on a backend-supplied value it already falls back away from.

## Background
`docs/architecture/development_guidelines.md` defines the Configuration module's `/api/configuration` endpoint as exposing only application-wide values: version, environment, and the mock-auth flag. The endpoint currently also returns `Timestamp`, the server's clock at handler execution time — not a configuration value at all. This sets a bad precedent (other modules could be tempted to bolt bootstrap-only fields onto this endpoint) and provides no real information to the frontend, which already has `|| new Date().toISOString()` as a fallback in `frontend/src/services/versionService.ts`. This is a pure architecture-alignment cleanup identified by the automated arch-review routine, with an already-verified no-op fallback path in the only consumer.

## Functional Requirements

### FR-1: Remove `Timestamp` from `GetConfigurationResponse`
Remove the `Timestamp` property from `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs` (currently at line 27).

**Acceptance criteria:**
- `GetConfigurationResponse` class no longer declares a `Timestamp` property.
- The class continues to expose exactly the documented application-wide values (version, environment, mock-auth flag) plus any other pre-existing documented fields — no functional fields besides `Timestamp` are touched.

### FR-2: Remove `Timestamp` assignment from `GetConfigurationHandler`
Remove the line in `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` (currently at line 36) that sets `Timestamp = DateTime.UtcNow` (or equivalent) when constructing the response.

**Acceptance criteria:**
- The handler no longer references `DateTime.UtcNow` for a `Timestamp` value it no longer needs to populate.
- All other fields the handler populates are unchanged.
- The handler still compiles and returns a valid `GetConfigurationResponse`.

### FR-3: Simplify frontend consumer
In `frontend/src/services/versionService.ts` (lines 98–99), replace:
```ts
timestamp: response.timestamp?.toISOString() || new Date().toISOString(),
```
with:
```ts
timestamp: new Date().toISOString(),
```
(or the minimal equivalent needed once `response.timestamp` no longer exists on the generated client type — the surrounding object/field name `timestamp` on the frontend-local model is unchanged, only its source expression changes).

**Acceptance criteria:**
- Frontend code no longer references `response.timestamp` (the field is gone from the generated OpenAPI client after regeneration).
- The local `timestamp` value used downstream by `versionService.ts` is populated from `new Date().toISOString()` unconditionally.
- No other logic in `versionService.ts` changes.

### FR-4: Regenerate the OpenAPI client
After the backend DTO change, regenerate the TypeScript OpenAPI client per `docs/development/api-client-generation.md` so the generated client no longer declares a `timestamp` field on the configuration response type, and the frontend build has no dangling reference to it.

**Acceptance criteria:**
- Generated TypeScript client types for the configuration response no longer include a `timestamp` field.
- `npm run build` succeeds with no TypeScript errors related to the removed field.

## Non-Functional Requirements

### NFR-1: No behavior change
The frontend's effective runtime behavior is unchanged: it already fell back to `new Date().toISOString()` whenever `response.timestamp` was falsy/absent, so removing the backend field and always using the local clock produces the same observable value shape (an ISO-8601 string) with no functional regression for any consumer of `versionService.ts`.

### NFR-2: Backward compatibility
No API contract version bump or migration is required — this is a narrowing of an existing response DTO. No other known consumer of `/api/configuration` reads `Timestamp` (confirmed by the issue's investigation); this spec does not require re-verifying that beyond what the architect/design review deems necessary.

## Data Model
`GetConfigurationResponse` (class, not record, per project DTO conventions) loses one property: `Timestamp` (type `DateTime`, previously required/settable). No other entities or persistence models are affected. This is a pure in-memory DTO change with no database or storage impact.

## API / Interface Design
- **Endpoint affected:** `GET /api/configuration` (Configuration module, MediatR handler `GetConfigurationHandler` behind `GetConfigurationResponse`).
- **Before:** Response includes `version`, `environment`, `mockAuthEnabled` (or equivalently named documented fields), and `timestamp`.
- **After:** Response includes only the documented application-wide values; `timestamp` is removed entirely from the wire contract.
- **Frontend caller:** `frontend/src/services/versionService.ts`, which maps the raw endpoint response into a local view-model object that also has a `timestamp` field — that field's value now always comes from `new Date().toISOString()` instead of ever reading from the response.

## Dependencies
- OpenAPI TypeScript client generation step (`docs/development/api-client-generation.md`) must be re-run as part of this change so the FE type no longer has a stale field or a type error.
- No external services, feature flags, or database migrations are involved.

## Out of Scope
- Any other fields on `GetConfigurationResponse` (version, environment, mock-auth flag) — unchanged.
- Any other consumer of `/api/configuration` beyond `versionService.ts` (none found per the issue; if the architect phase finds another consumer, that is a new finding, not part of this fix).
- Introducing a dedicated bootstrap-timestamp endpoint for any future module need — explicitly not needed here since no consumer needs a server-supplied timestamp.
- Any behavioral or visual change to whatever UI displays the version/timestamp info — this fix is contract-only and value-source-only, not presentation.

## Open Questions
None.

## Status: COMPLETE
