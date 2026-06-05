# Specification: Consolidate User Identity Resolution via `ICurrentUserService`

## Summary
`BaseApiController.GetCurrentUserId()` duplicates the claim-priority chain (`NameIdentifier` → `sub` → `oid`) already implemented in `CurrentUserService.GetCurrentUser()`. Three controllers (`DashboardController`, `CarrierCoolingController`, `GiftSettingsController`) use this helper to stamp a user identifier onto MediatR requests, while every other module resolves identity inside its handler through `ICurrentUserService`. This work removes the controller-level helper and migrates those three controllers/handlers to the canonical handler-side pattern so user-identity resolution lives in exactly one place.

## Background
The codebase enforces a Vertical Slice / Clean Architecture rule (`docs/architecture/development_guidelines.md`) that business logic — including identity-resolution — belongs in MediatR handlers, not controllers. The bulk of the codebase already follows this: handlers in Purchase, Logistics, InvoiceClassification, Article, Leaflet, Manufacture, etc. inject `ICurrentUserService` and resolve the current user themselves.

Three controllers diverged from that pattern by using a controller-level helper `GetCurrentUserId()` on `BaseApiController`:

- `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs:75-79`
- Call-sites:
  - `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs:37,47,57,71,85`
  - `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs:34`
  - `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs:33`

The helper reimplements the exact same claim chain as `CurrentUserService.GetCurrentUser()` at `backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs:26-29`. The `oid` fallback was added in `CurrentUserService` to support Entra ID guest users (per comment in the source); the same fallback was silently re-added to `BaseApiController` without a comment explaining why, and there is no shared test that guarantees the two implementations stay aligned. If the chain changes again, both files must be updated and both can drift.

Goal: a single owner for "who is the caller?" — `ICurrentUserService` — accessed from handlers, with no controller-level shortcut.

## Functional Requirements

### FR-1: Remove `GetCurrentUserId()` from `BaseApiController`
The `protected string GetCurrentUserId()` helper at `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs:75-79` and its associated `using System.Security.Claims;` import (line 3, if no longer used elsewhere in the file) must be deleted. After this requirement is complete, no controller may resolve a user identifier from claims directly.

**Acceptance criteria:**
- `BaseApiController` no longer declares `GetCurrentUserId()`.
- A repo-wide search for `GetCurrentUserId` returns zero hits outside historical git context.
- A repo-wide search for `User.FindFirst("sub")`, `User.FindFirst("oid")`, and `User.FindFirst(ClaimTypes.NameIdentifier)` returns zero hits inside `backend/src/Anela.Heblo.API/Controllers/`.
- Solution still builds (`dotnet build`).

### FR-2: Migrate `DashboardController` to handler-side identity resolution
All five Dashboard endpoints currently pass `UserId` from the controller into the request:

- `GET /api/dashboard/settings` → `GetUserSettingsRequest.UserId`
- `POST /api/dashboard/settings` → `SaveUserSettingsRequest.UserId`
- `GET /api/dashboard/data` → `GetTileDataRequest.UserId`
- `POST /api/dashboard/tiles/{tileId}/enable` → `EnableTileRequest.UserId`
- `POST /api/dashboard/tiles/{tileId}/disable` → `DisableTileRequest.UserId`

After this change, the controller must not touch `UserId` on those requests. Each of the five handlers (`GetUserSettingsHandler`, `SaveUserSettingsHandler`, `GetTileDataHandler`, `EnableTileHandler`, `DisableTileHandler`) must inject `ICurrentUserService` and resolve the current user ID itself, mirroring the pattern used in Purchase/Logistics handlers.

The `UserId` property on the five request DTOs is consumed only by these handlers (verified by search). The property must be removed from the request DTO contracts so it cannot be supplied by clients. Removal is required because these DTOs are part of the generated OpenAPI client and exposing a settable `UserId` invites confusion.

Identity resolution inside handlers must use `ICurrentUserService.GetCurrentUser().Id`. The existing "anonymous" fallback behavior in three handlers (`GetUserSettingsHandler:30`, `SaveUserSettingsHandler:29`, `GetTileDataHandler:32`) must be preserved: when `CurrentUser.Id` is null or empty, use the string `"anonymous"`. The `[Authorize]` attribute on `DashboardController` means an anonymous caller should never reach the handler in practice; the fallback exists only as defense-in-depth and must continue to behave identically.

Each handler inlines `var userId = _currentUserService.GetCurrentUser().Id ?? "anonymous";` (or the equivalent null/empty check). No new convenience method is added to `ICurrentUserService` — see Out of Scope.

**Acceptance criteria:**
- `DashboardController` contains no references to `GetCurrentUserId`, `User.FindFirst`, or `ClaimTypes`.
- All five Dashboard handlers receive `ICurrentUserService` via constructor injection.
- The `UserId` property is removed from `GetUserSettingsRequest`, `SaveUserSettingsRequest`, `GetTileDataRequest`, `EnableTileRequest`, and `DisableTileRequest`.
- Internal helpers (e.g. `UserDashboardSettingsMutator`) that previously received `userId` as a method parameter now resolve it the same way (via injected `ICurrentUserService`) or accept it from the handler that resolved it; behavior unchanged.
- The `"anonymous"` fallback applies when `CurrentUser.Id` is null/empty; the resulting `userId` value used in repository calls is identical to today.
- Existing Dashboard unit tests continue to pass after updating their handler construction to supply a mocked `ICurrentUserService`.
- Generated OpenAPI/TypeScript client no longer exposes `userId` on the affected request DTOs (frontend hooks that previously sent it must be updated to stop sending it — see FR-5).

### FR-3: Migrate `CarrierCoolingController` / `SetCarrierCoolingHandler`
`CarrierCoolingController.SetCooling` (`backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs:34`) sets `request.ModifiedBy = GetCurrentUserId()`. The handler `SetCarrierCoolingHandler` (`backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingHandler.cs`) must inject `ICurrentUserService` and populate `ModifiedBy` itself when constructing the `CarrierCoolingSetting` domain entity. The `ModifiedBy` property must be removed from `SetCarrierCoolingRequest` (`backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingRequest.cs:13`).

When `CurrentUser.Id` is null or empty (defense-in-depth — endpoint is `[Authorize]`), the handler must return a `SetCarrierCoolingResponse` with `Success = false` and `ErrorCode = ErrorCodes.Unauthorized`. The handler must not throw `UnauthorizedAccessException` and must not fall back to `"system"` via `CurrentUserExtensions.GetIdentifier()` — silently attributing audit writes to an unidentified caller is unacceptable. `ErrorCodes.Unauthorized` (value 0013, declared at `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:38-39`) is already mapped to HTTP 401 by its `HttpStatusCode` attribute, preserving observable behavior. This matches the established pattern in `CreateMarketingActionHandler` (`backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs:40-45`) and other Marketing/Journal handlers.

**Acceptance criteria:**
- `CarrierCoolingController` contains no reference to `GetCurrentUserId` or to `request.ModifiedBy`.
- `SetCarrierCoolingRequest` no longer exposes `ModifiedBy`.
- `SetCarrierCoolingHandler` receives `ICurrentUserService` via constructor and uses it to populate the `modifiedBy` argument of `CarrierCoolingSetting`.
- When `CurrentUser.Id` is null/empty, the handler returns a response with `Success = false` and `ErrorCode = ErrorCodes.Unauthorized`. Verified by a new unit test.
- Unit tests for the handler are updated to inject a mocked `ICurrentUserService` and assert that `ModifiedBy` on the persisted entity equals the mocked user's ID.

### FR-4: Migrate `GiftSettingsController` / `SetGiftSettingCommandHandler`
`GiftSettingsController.SetGiftSetting` (`backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs:33`) sets `command.ModifiedBy = GetCurrentUserId()`. The handler (`SetGiftSettingHandler` in `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs`) must inject `ICurrentUserService` and populate `ModifiedBy` itself when constructing the `GiftSetting` domain entity. The `ModifiedBy` property must be removed from `SetGiftSettingCommand`.

When `CurrentUser.Id` is null or empty, the handler must return a `SetGiftSettingResponse` with `Success = false` and `ErrorCode = ErrorCodes.Unauthorized` — same rationale and pattern as FR-3.

**Acceptance criteria:**
- `GiftSettingsController` contains no reference to `GetCurrentUserId` or to `command.ModifiedBy`.
- `SetGiftSettingCommand` no longer exposes `ModifiedBy`.
- `SetGiftSettingHandler` receives `ICurrentUserService` via constructor and uses it to populate the `modifiedBy` argument of `GiftSetting`.
- When `CurrentUser.Id` is null/empty, the handler returns a response with `Success = false` and `ErrorCode = ErrorCodes.Unauthorized`. Verified by a new unit test.
- Unit tests for the handler are updated to inject a mocked `ICurrentUserService` and assert behavior.

### FR-5: Update affected frontend callers
Removing `UserId` from the five Dashboard request DTOs and `ModifiedBy` from `SetCarrierCoolingRequest` / `SetGiftSettingCommand` will regenerate the TypeScript OpenAPI client. A direct audit of the three affected hooks (`frontend/src/api/hooks/useDashboard.ts`, `frontend/src/api/hooks/useCarrierCooling.ts`, `frontend/src/api/hooks/useGiftSetting.ts`) confirms no handcrafted frontend code currently sets `userId` or `modifiedBy` in outgoing request bodies — the contract change is confined to the generated client. The implementer must still re-run `grep -rn "userId\|modifiedBy" frontend/src` after regenerating the API client and resolve any TypeScript compile errors surfaced by the regenerated request DTO types.

**Acceptance criteria:**
- After backend build regenerates the API client, `npm run build` succeeds in `frontend/`.
- `npm run lint` reports no errors related to the removed properties.
- Post-regeneration grep for `userId` / `modifiedBy` in `frontend/src` reveals no stray references to the removed request-body fields (matches in unrelated features — feedback, stock-taking, knowledge-base, leaflet, journal — are expected and ignored).
- Manual smoke test (or existing E2E) of Dashboard tile enable/disable, settings save, and gift/carrier-cooling save endpoints succeeds against a running backend.

### FR-6: Replace controller-level `GetCurrentUserId` test coverage with `CurrentUserService` test coverage
`backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs` currently has six tests that exercise the claim-priority chain on the controller helper. After FR-1, those tests no longer compile. They must be deleted. Equivalent coverage must exist (or be added) for `CurrentUserService.GetCurrentUser()` covering the same six scenarios:

1. Returns `NameIdentifier` when present.
2. Returns `sub` when only `sub` is present.
3. Returns `oid` when only `oid` is present.
4. Prioritizes `NameIdentifier` over `sub` and `oid`.
5. Prioritizes `sub` over `oid` when `NameIdentifier` absent.
6. Returns a `CurrentUser` with `Id = null` when no supported claim is present (note: behavior change — `CurrentUserService` returns null rather than throws, which is the existing documented behavior).

**Acceptance criteria:**
- `BaseApiControllerTests` is deleted.
- `CurrentUserServiceTests` exists (creating it if absent) with one test per scenario above, using `IHttpContextAccessor` with a `DefaultHttpContext` whose `User` carries the relevant claims.
- All listed tests pass.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance change is expected or acceptable. `ICurrentUserService` is already a scoped service in the DI container; injecting it into seven additional handlers does not introduce new I/O or allocations beyond a single field assignment per request. No new database calls.

### NFR-2: Security
- No change to authentication or authorization. All affected endpoints retain their existing `[Authorize]` attribute.
- `ModifiedBy` and `UserId` fields are no longer client-supplied → eliminates a (currently low-impact) class of spoofing where an authenticated user could submit a payload claiming to be acting on behalf of another user. The previous implementation also overwrote these in the controller, so the practical security posture is unchanged in production; this change closes the theoretical hole and makes the contract explicit.
- In `SetCarrierCoolingHandler` and `SetGiftSettingHandler`, a missing `CurrentUser.Id` results in a typed `Unauthorized` failure (HTTP 401) rather than a silent fallback to a placeholder string — preventing unidentified writes to `ModifiedBy` audit columns.
- No claims are logged.
- No change to secret handling.

### NFR-3: Backward compatibility
- Request DTOs lose a property → this is a breaking change for any external API consumer. The Anela Heblo API is consumed only by the in-repo React frontend (generated client) and there is no third-party integration on these endpoints. Therefore the breaking change is acceptable. No deprecation period is required.
- HTTP routes, status codes, and response shapes are unchanged.

### NFR-4: Observability
- No new logs are required. If a handler ends up using the `"anonymous"` fallback in Dashboard, that already exists and is silent today — keep it silent.
- No new metrics or traces.

## Data Model
No database schema changes. The fields `ModifiedBy` on `CarrierCoolingSetting` and `GiftSetting` domain entities remain unchanged; only the way they are populated changes (handler resolves instead of trusting the request).

## API / Interface Design

### Affected HTTP endpoints (contract change: drop one field from the request body)

| Endpoint | Removed request field |
|---|---|
| `GET /api/dashboard/settings` | n/a (was query/route — no change in shape) |
| `POST /api/dashboard/settings` | `userId` |
| `GET /api/dashboard/data` | n/a (was query — `userId` was not a query param, came from controller) |
| `POST /api/dashboard/tiles/{tileId}/enable` | `userId` |
| `POST /api/dashboard/tiles/{tileId}/disable` | `userId` |
| `PUT /api/carrier-cooling` | `modifiedBy` |
| `PUT /api/gift-settings` | `modifiedBy` |

Response shapes are unchanged. `SetCarrierCoolingResponse` and `SetGiftSettingResponse` may now carry `Success = false` with `ErrorCode = ErrorCodes.Unauthorized` (→ HTTP 401) in the defense-in-depth case described in FR-3 / FR-4.

### Affected internal interfaces

`ICurrentUserService` is unchanged. New consumers via constructor injection:

- `GetUserSettingsHandler`
- `SaveUserSettingsHandler`
- `GetTileDataHandler`
- `EnableTileHandler`
- `DisableTileHandler`
- `SetCarrierCoolingHandler`
- `SetGiftSettingHandler`

`BaseApiController` is reduced: `GetCurrentUserId()` removed; everything else (`HandleResponse<T>`, `Logger`, `GetStatusCodeForError`) is unchanged.

## Dependencies

- `Anela.Heblo.Domain.Features.Users.ICurrentUserService` (existing)
- `Anela.Heblo.API.Features.Users.CurrentUserService` (existing, already registered in DI)
- `Anela.Heblo.Application.Shared.ErrorCodes.Unauthorized` (existing, value 0013, HTTP 401)
- MediatR (existing)
- Generated OpenAPI TypeScript client — must be regenerated as part of the BE build

No new packages, no new services to register.

## Out of Scope

- Refactoring other controllers that already follow the correct pattern.
- Changing how `CurrentUserService` resolves claims (no claim-chain changes — only deduplication of an existing chain).
- Changing the `"anonymous"` fallback semantics in Dashboard handlers — preserve as-is.
- Auditing whether `ModifiedBy` should also include a timestamp / who-modified-what metadata pattern across the codebase (separate concern).
- Touching `CurrentUserExtensions` (`GetDisplayName`, `GetIdentifier`).
- Adding new tests beyond the six listed in FR-6 plus the two new null-identity tests required by FR-3 / FR-4.
- Adding a `GetRequiredUserId()` (or any other convenience method) to `ICurrentUserService`. The three distinct null-handling policies in play (`"anonymous"` defense-in-depth, typed `Unauthorized` failure, `GetIdentifier()` → `"system"`) cannot be served by a single helper without forcing two of them to bypass it. Each migrated handler inlines its own resolution. Revisit only if a follow-up adds a fourth call site that shares one of the existing policies verbatim.

## Open Questions
None.

## Status: COMPLETE