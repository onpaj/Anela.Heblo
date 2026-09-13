# Code Review Round 1: feat-4129 (FeatureFlagsController identity resolution / ADR-005)

## Review Result: CLEAN

## Scope

Full diff between `main`'s merge-base (`b51fd3a`) and this branch's HEAD, restricted to
`spec.r1.md`'s acceptance criteria (FR-1 through FR-4). Both dev tasks
(`handler-identity-resolution`, `controller-simplification`) were already implemented,
committed, and passed their own task-level reviews before this round; this round is the
whole-feature check against the spec, plus a fresh build/test/format pass.

## Diff summary

- `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs`: `Put()` reduced to
  an expression-bodied dispatcher, matching `Get`/`GetAdmin`/`Delete` in the same file.
  `User.Identity?.Name` read, the "resolved to null" warning log, and the `updatedBy` local
  are all gone.
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideRequest.cs`:
  `UpdatedBy` property removed; only `Key` and `IsEnabled` remain.
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideHandler.cs`:
  constructor now takes `ICurrentUserService` in addition to `IFeatureFlagOverrideRepository`
  and `IMemoryCache`; `Handle()` resolves `_currentUserService.GetCurrentUser().GetDisplayName()`
  and passes that value to `_repo.UpsertAsync(...)` in place of `request.UpdatedBy`. The
  not-found branch and cache-invalidation call are untouched.
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UpsertFlagOverrideHandlerTests.cs`
  (new): covers authenticated resolution, the `"System"` fallback for an unauthenticated
  `CurrentUser`, and the not-found path (repo never called).

## Acceptance criteria verification (spec.r1.md)

- **FR-1** (identity resolved in handler): confirmed — `ICurrentUserService` injected,
  `GetCurrentUser().GetDisplayName()` used, not-found branch and cache invalidation
  unchanged.
- **FR-2** (`UpdatedBy` removed from request DTO): confirmed —
  `UpsertFlagOverrideRequest` now has only `Key`/`IsEnabled`. Repo-wide grep for
  `request.UpdatedBy` and for any other reference to `UpsertFlagOverrideRequest`'s old
  `UpdatedBy` property returns nothing outside `artifacts/`.
- **FR-3** (controller simplified to dispatcher): confirmed — `Put()` is now
  `=> HandleResponse(await _mediator.Send(new UpsertFlagOverrideRequest { Key = key, IsEnabled = body.IsEnabled }, ct));`,
  matching the shape of the controller's other three actions. Route
  (`[HttpPut("admin/{key}")]`), `[FeatureAuthorize(Feature.Admin_FeatureFlags, AccessLevel.Write)]`,
  and all `[ProducesResponseType]` attributes are unchanged.
- **FR-4** (observable audit behavior preserved / documented fallback change): confirmed —
  `UpsertFlagOverrideHandlerTests.Handle_AuthenticatedUser_PersistsResolvedDisplayNameAsUpdatedBy`
  and `Handle_UnauthenticatedCurrentUser_PersistsSystemFallback` exercise both paths; the
  `"unknown"` → `"System"` fallback change matches the spec's explicit, deliberate callout.
- **API/Interface Design** (client contract unchanged): confirmed — `UpsertFlagOverrideBodyDto`
  was never touched (it never carried `UpdatedBy`), so no OpenAPI/TypeScript client
  regeneration is required or present in the diff.
- **Out of scope items** (`ClearFlagOverrideHandler`, `ListFlagsHandler`,
  `EvaluateFlagsForClientHandler`, `IFeatureFlagOverrideRepository` signature, frontend
  admin UI): none touched by this diff, as required.

## Verification performed this round

- `dotnet build Anela.Heblo.sln` (from the worktree root, where the `.sln` actually lives) —
  **0 errors**, 183 pre-existing warnings unrelated to this change (nullable-reference
  warnings in test files predating this feature).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~UpsertFlagOverrideHandlerTests|FullyQualifiedName~FeatureFlagsControllerLintTests|FullyQualifiedName~FeatureFlags"` —
  **13/13 passed** (0 failed, 0 skipped), covering the three new handler tests plus the
  existing `FeatureFlagsControllerLintTests`, `FeatureFlagRegistryFrontendMirrorTests`, and
  `HebloFeatureProviderTests`.
- `dotnet format Anela.Heblo.sln whitespace --verify-no-changes` — clean, no output, exit 0.
- Repo-wide grep confirms no remaining reference to `request.UpdatedBy` or to a
  `UpdatedBy` property on `UpsertFlagOverrideRequest` anywhere in `backend/` or
  `frontend/` outside `artifacts/`.
- `ICurrentUserService` is already DI-registered
  (`AddSingleton<ICurrentUserService, CurrentUserService>()` in
  `Anela.Heblo.API/Features/Users/UsersModule.cs`), so no DI wiring changes were needed or
  made.

## Blocking

- None.

## Advisory

- None. The change is a clean, surgical, spec-conformant fix scoped exactly to the finding
  in issue #4129; no dead code, adjacent cleanup, or unrelated formatting was touched.

## Docs to update

- None — `spec.r1.md` already documents the fallback-string change (`"unknown"` → `"System"`)
  explicitly so it isn't mistaken for a regression during review; no other doc references
  this endpoint's identity-resolution mechanics.
