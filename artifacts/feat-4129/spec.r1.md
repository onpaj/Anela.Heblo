# Specification: FeatureFlagsController identity resolution fix (ADR-005 compliance)

## Summary

`FeatureFlagsController.Put()` resolves the acting user's identity itself (via `User.Identity?.Name`) and stamps the result onto `UpsertFlagOverrideRequest.UpdatedBy` before dispatching to MediatR. This violates ADR-005 (User Identity Resolution), which requires all identity resolution to happen inside the handler via `ICurrentUserService`, and forbids request DTOs from carrying identity/audit fields such as `UpdatedBy`. This spec covers moving identity resolution into `UpsertFlagOverrideHandler`, removing `UpdatedBy` from the request DTO, and simplifying the controller back to a straight-through dispatcher consistent with the other actions in the same controller.

## Background

`Anela.Heblo.API.Controllers.FeatureFlagsController` exposes CRUD-style admin endpoints for feature flag overrides. Three of its four actions (`Get`, `GetAdmin`, `Delete`) are plain dispatchers that construct a MediatR request from route/body values and call `_mediator.Send(...)`. The fourth, `Put` (`PUT /api/feature-flags/admin/{key}`), additionally reads `User.Identity?.Name` directly in the controller, logs a warning if it is null, falls back to the literal string `"unknown"`, and stamps the result onto `UpsertFlagOverrideRequest.UpdatedBy` — an audit field consumed only by `UpsertFlagOverrideHandler` to pass to `IFeatureFlagOverrideRepository.UpsertAsync`.

ADR-005 (`docs/architecture/development_guidelines.md`) establishes the canonical pattern used elsewhere in the codebase (e.g. `UpdatePackingMaterialQuantityHandler`, `ScanPackingOrderHandler`): handlers inject `Anela.Heblo.Domain.Features.Users.ICurrentUserService`, call `GetCurrentUser()`, and use `CurrentUserExtensions.GetDisplayName()` / `GetIdentifier()` for display/audit values. `CurrentUserService.GetCurrentUser()` follows a claim-priority chain (oid → NameIdentifier → sub) that `User.Identity?.Name` does not replicate, and `GetDisplayName()` already provides the canonical fallback ("System" for unauthenticated users) in place of the controller's ad hoc `"unknown"` fallback.

Because `UpsertFlagOverrideBodyDto` (the actual client-facing request body) does not carry `UpdatedBy` — it is stamped by the controller itself, not sent by the caller — there is no client-spoofing exposure today. The violation is structural/architectural: identity resolution logic has leaked into the controller layer and bypasses the shared resolution chain, which is exactly the drift ADR-005 exists to prevent.

## Functional Requirements

### FR-1: Resolve `UpdatedBy` inside `UpsertFlagOverrideHandler`, not the controller

`UpsertFlagOverrideHandler` must inject `ICurrentUserService` and resolve the acting user's display name itself, using `_currentUserService.GetCurrentUser().GetDisplayName()`, in place of the value currently carried on `request.UpdatedBy`.

**Acceptance criteria:**
- `UpsertFlagOverrideHandler` has a constructor dependency on `ICurrentUserService` (in addition to the existing `IFeatureFlagOverrideRepository` and `IMemoryCache`).
- `Handle()` calls `_currentUserService.GetCurrentUser().GetDisplayName()` and passes that value to `_repo.UpsertAsync(request.Key, request.IsEnabled, <resolved name>, ct)`.
- No behavior change to the not-found branch (`FeatureFlagRegistry.ByKey.ContainsKey` check) or the cache-invalidation call.

### FR-2: Remove `UpdatedBy` from `UpsertFlagOverrideRequest`

The MediatR request DTO must no longer carry an identity/audit field.

**Acceptance criteria:**
- `UpsertFlagOverrideRequest.UpdatedBy` property is removed.
- `UpsertFlagOverrideRequest` retains only `Key` and `IsEnabled`.
- No remaining reference to `request.UpdatedBy` anywhere in the codebase.

### FR-3: Simplify `FeatureFlagsController.Put()` to a straight-through dispatcher

The controller action must no longer resolve identity, log identity-resolution warnings, or apply an identity fallback.

**Acceptance criteria:**
- `Put()` no longer reads `User.Identity?.Name`, no longer logs the "resolved to null" warning, and no longer computes `updatedBy`.
- `Put()` constructs `UpsertFlagOverrideRequest { Key = key, IsEnabled = body.IsEnabled }` and calls `_mediator.Send(...)`, matching the dispatcher shape of `Get`, `GetAdmin`, and `Delete` in the same controller.
- The `[HttpPut("admin/{key}")]` route, `[FeatureAuthorize(Feature.Admin_FeatureFlags, AccessLevel.Write)]` attribute, and response types (`UpsertFlagOverrideResponse` / 200 / 404 / 403) are unchanged.

### FR-4: Preserve observable behavior for the audit trail

The value ultimately persisted as the "updated by" identity for a flag override must be equivalent in practice to today's behavior for the normal (authenticated) case, and must use the ADR-005 canonical fallback instead of the ad hoc `"unknown"` string for the edge case.

**Acceptance criteria:**
- For an authenticated request with a resolvable identity, the persisted `updatedBy` value is the user's resolved display name (via the claim-priority chain in `CurrentUserService`), which in the common case is the same person's name as before.
- For a request where no identity claim can be resolved, the persisted `updatedBy` value is `"System"` (via `GetDisplayName()`'s existing fallback), replacing the previous `"unknown"` literal. This is a deliberate, documented change in the literal fallback string — not a regression — and is called out explicitly in this spec so it isn't mistaken for an accidental behavior change during review.

## Non-Functional Requirements

### NFR-1: Performance

No measurable performance impact. `ICurrentUserService.GetCurrentUser()` is the same call already used by every other handler in the codebase in the same request pipeline (`HttpContext`-scoped, no additional I/O).

### NFR-2: Security

This is a defense-in-depth / architectural-consistency fix, not a vulnerability remediation — the client cannot already spoof `UpdatedBy` today, since `UpsertFlagOverrideBodyDto` never exposed that field. The fix ensures the identity-resolution code path is the single, audited, claim-priority-chain implementation (`CurrentUserService`) rather than a second, divergent implementation in the controller, closing the gap where future changes to claim handling in `CurrentUserService` would silently fail to apply to this one endpoint.

## Data Model

No schema or persisted-entity changes. `IFeatureFlagOverrideRepository.UpsertAsync(string key, bool isEnabled, string updatedBy, CancellationToken ct)` keeps its existing signature; only the caller-side source of the `updatedBy` argument changes (from `request.UpdatedBy` to a value resolved inside the handler).

## API / Interface Design

- **Route/contract-visible surface: unchanged.** `PUT /api/feature-flags/admin/{key}` keeps its existing request body (`UpsertFlagOverrideBodyDto { IsEnabled }`) and response (`UpsertFlagOverrideResponse`). No OpenAPI/TypeScript client regeneration is required — `UpdatedBy` was never part of the client-facing body DTO, only of the internal MediatR request.
- **Internal MediatR contract change:** `UpsertFlagOverrideRequest` drops the `UpdatedBy` property (internal to the Application layer, not exposed via API).
- **Handler dependency change:** `UpsertFlagOverrideHandler` gains a constructor dependency on `ICurrentUserService` (already registered in DI; used by numerous other handlers).

## Dependencies

- `Anela.Heblo.Domain.Features.Users.ICurrentUserService` / `CurrentUser` / `CurrentUserExtensions.GetDisplayName()` — already implemented and DI-registered; no new dependency introduced, only reused per the existing canonical pattern.
- No new external services, packages, or migrations.

## Out of Scope

- Any change to `ClearFlagOverrideHandler`, `ListFlagsHandler`, or `EvaluateFlagsForClientHandler` — these do not stamp identity and are unaffected.
- Any change to `IFeatureFlagOverrideRepository` or `FeatureFlagOverrideRepository`'s persistence logic/signature.
- Broader ADR-005 compliance sweep across other modules/controllers — this spec is scoped to the one finding raised in the linked arch-review issue (#4129).
- Any change to the `UpsertFlagOverrideBodyDto` client contract or frontend feature-flags admin UI.
- Adding automated tests beyond what's needed to verify this fix (see Open Questions).

## Open Questions

None.
