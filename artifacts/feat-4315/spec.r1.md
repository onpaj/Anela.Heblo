# Specification: Bulk mutation support in IUserDashboardSettingsMutator to eliminate SaveUserSettingsHandler's duplicated scaffold

## Summary
`SaveUserSettingsHandler` currently re-implements, by hand, the exact provision → lock → load
scaffold that `IUserDashboardSettingsMutator` was built to encapsulate for `EnableTile` /
`DisableTile`, because the mutator's `MutateAsync` only supports mutating a single tile per
call. This feature adds a bulk-mutation entry point to `IUserDashboardSettingsMutator` /
`UserDashboardSettingsMutator` so `SaveUserSettingsHandler` can update an arbitrary batch of
tiles atomically, then collapses the handler down to a thin caller of the mutator. This is a
pure internal refactor: no externally observable behavior, request/response contract, or API
route changes.

## Background
`backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsMutator.cs`
encodes a subtle, load-bearing invariant: `GetUserSettingsRequest` (provisioning) must be sent
via MediatR **before** `IUserDashboardSettingsLock.AcquireAsync` is called, because the lock is
non-reentrant and `GetUserSettingsHandler` itself acquires the same per-user lock. `EnableTile`
and `DisableTile` already delegate to the mutator and never see this invariant directly.
`SaveUserSettingsHandler` (`.../UseCases/SaveUserSettings/SaveUserSettingsHandler.cs`, lines
36-41) duplicates the same three lines itself because `MutateAsync(userId, tileId, onTileFound,
onTileMissing, ct)` is shaped around a single `tileId` and cannot express "upsert N tiles in one
locked, atomically-persisted pass" — calling it once per tile would acquire/release the lock N
times and issue N redundant provisioning round-trips, breaking the atomicity the batch save
currently gets from doing the load/mutate/save itself, inline, once.

The fix is to widen the mutator's contract with a batch-shaped sibling method rather than
changing `MutateAsync`'s single-tile signature (which `EnableTile`/`DisableTile` still need
unchanged), so the invariant is encoded exactly once for both call shapes.

## Functional Requirements

### FR-1: Add `MutateBulkAsync` to `IUserDashboardSettingsMutator`
Add a new method to the existing `internal interface IUserDashboardSettingsMutator`
(`backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs`):

```csharp
Task<UserDashboardSettingsMutationResult> MutateBulkAsync(
    string? userId,
    IReadOnlyList<UserDashboardTileDto> tiles,
    CancellationToken cancellationToken);
```

- `userId` normalization (`null`/empty → `"anonymous"`) follows the exact same rule already
  documented on `MutateAsync`.
- `tiles` is the full desired tile set for the user, exactly as `SaveUserSettingsRequest.Tiles`
  is used today: for each entry, update the existing `UserDashboardTile` with matching `TileId`
  (`IsVisible`, `DisplayOrder`), or append a new `UserDashboardTile` when no match exists. Tiles
  present in storage but absent from `tiles` are left untouched (matches current handler
  behavior — this is not a full-replace/delete-missing operation).
- `tiles` may be an empty list; this is a valid, common call (no visible tiles) and must not be
  treated as an error or as "no tiles supplied."

**Acceptance criteria:**
- Interface compiles with the new method; existing `MutateAsync` signature is unchanged.
- XML doc comment on the interface (and/or the new method) documents the provisioning/lock
  ordering invariant for `MutateBulkAsync` exactly as it is documented for `MutateAsync`, so the
  invariant remains discoverable from a single place regardless of which method a reader looks
  at first.

### FR-2: Implement `MutateBulkAsync` in `UserDashboardSettingsMutator`
Implement the method in `UserDashboardSettingsMutator`
(`.../Infrastructure/UserDashboardSettingsMutator.cs`) reusing the same scaffold already used by
`MutateAsync`:
1. Normalize `userId` (empty/null → `"anonymous"`).
2. `await _mediator.Send(new GetUserSettingsRequest(), cancellationToken)` — provisioning,
   **outside** the lock (same invariant as `MutateAsync`).
3. `await using var lockHandle = await _lock.AcquireAsync(resolvedUserId, cancellationToken)`.
4. `var settings = await _repository.GetByUserIdAsync(resolvedUserId)`.
5. If `settings == null`, return `new UserDashboardSettingsMutationResult(SettingsLoaded: false, TileFound: false, TileAppended: false)` without calling `UpdateAsync` — matching `MutateAsync`'s existing null-settings branch and the current `SaveUserSettingsHandler`'s early-return-on-null behavior.
6. Otherwise, for each `UserDashboardTileDto` in `tiles` (single `now = _timeProvider.GetUtcNow().DateTime` read reused for every touched/appended tile and for `settings.LastModified`, exactly as `SaveUserSettingsHandler` does today):
   - If a `UserDashboardTile` with matching `TileId` exists in `settings.Tiles`: update its `IsVisible` and `DisplayOrder`, and set its `LastModified` to `now`.
   - Else: append a new `UserDashboardTile { UserId = resolvedUserId, TileId, IsVisible, DisplayOrder, LastModified = now, DashboardSettings = settings }`.
7. Set `settings.UserId = resolvedUserId` and `settings.LastModified = now`, then call `await _repository.UpdateAsync(settings)` **unconditionally** whenever `settings != null` — even when `tiles` is empty or every tile in it already matched with identical values. This differs from `MutateAsync`'s conditional-write behavior and is required to preserve `SaveUserSettingsHandler`'s current, tested behavior (`Handle_WhenNoTiles_ShouldSaveEmptySettings`, `Handle_WhenNullTiles_ShouldNotMutateExistingTiles` both assert `UpdateAsync` is called exactly once even with no tiles to apply).
8. Return `new UserDashboardSettingsMutationResult(SettingsLoaded: true, TileFound: <true if any tile matched>, TileAppended: <true if any tile was appended>)`.

**Acceptance criteria:**
- Provisioning happens before lock acquisition (verifiable via call-order assertion, mirroring `Handle_SendsGetUserSettingsBeforeAcquiringLock`).
- The lock is acquired exactly once per `MutateBulkAsync` call regardless of tile count (mirroring `Handle_AcquiresLockOncePerCall`).
- `GetByUserIdAsync` is called exactly once per call.
- `UpdateAsync` is called exactly once whenever `settings` was found (including for an empty `tiles` list), and zero times when `settings` is `null`.
- A `null` `userId` or empty `userId` resolves to `"anonymous"` identically to `MutateAsync`.
- All touched/appended tiles in one call share the identical `LastModified` timestamp, equal to `settings.LastModified`.

### FR-3: Reduce `SaveUserSettingsHandler` to a thin caller of the mutator
Rewrite `SaveUserSettingsHandler`
(`.../UseCases/SaveUserSettings/SaveUserSettingsHandler.cs`) to:
- Inject only `IUserDashboardSettingsMutator` and `ICurrentUserService` (drop the direct
  `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider`, and
  `IMediator` dependencies — all of that responsibility now lives inside the mutator).
- Resolve `userId` from `ICurrentUserService.GetCurrentUser().Id` (unchanged) and pass it,
  together with `request.Tiles` (or an empty array when `request.Tiles` is `null`, since
  `MutateBulkAsync`'s `tiles` parameter is non-nullable `IReadOnlyList<UserDashboardTileDto>`),
  straight to `_mutator.MutateBulkAsync(...)`.
- Return `new SaveUserSettingsResponse()` unconditionally (matching current behavior — the
  handler ignores the mutation result today, including the null-settings case, which already
  returns an empty success response).

**Acceptance criteria:**
- `SaveUserSettingsHandler`'s constructor takes exactly two dependencies:
  `IUserDashboardSettingsMutator`, `ICurrentUserService`.
- All behavior asserted by the existing `SaveUserSettingsHandlerTests` test suite continues to
  pass unmodified in *observable outcome* (call counts on the now-mocked
  `IUserDashboardSettingsMutator` replace the old mocks on repository/lock/mediator — see
  NFR-2/Dependencies below for how the existing test file must be adapted).
- No behavior change to the `SaveUserSettingsRequest`/`SaveUserSettingsResponse` DTOs or the
  MediatR request/response contract.

## Non-Functional Requirements

### NFR-1: Behavioral equivalence
This is a structural refactor. Every externally observable behavior of `SaveUserSettingsHandler`
today (anonymous-user fallback, per-tile upsert semantics, unconditional persistence including
for an empty/null tile list, single shared `LastModified` timestamp per call, lock-once-per-call,
provisioning-before-lock ordering) must be preserved exactly. No new validation, error codes, or
response fields are introduced.

### NFR-2: Test coverage carries over, not just passes
`backend/test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs` currently
mocks `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `IMediator`, and
`TimeProvider` directly against the handler, because today's handler owns that scaffold. Once
the scaffold moves into `UserDashboardSettingsMutator`, these tests must be rewritten to mock
`IUserDashboardSettingsMutator` instead (asserting `MutateBulkAsync` is called with the expected
`userId`/`tiles` arguments) — mirroring the existing pattern in
`backend/test/Anela.Heblo.Tests/Features/Dashboard/EnableTileHandlerTests.cs` /
`DisableTileHandlerTests.cs`, which already mock the mutator rather than its dependencies. The
scaffold-level behaviors this test file currently verifies directly (lock-once, provision-
before-lock, anonymous fallback, unconditional update) must instead be verified against
`UserDashboardSettingsMutator` itself, in a new or extended test fixture for
`MutateBulkAsync` (there is currently no dedicated test file for
`UserDashboardSettingsMutator`; `MutateAsync`'s equivalent scaffold behavior is currently
exercised only indirectly through `EnableTileHandlerTests`/`DisableTileHandlerTests` — the
architect/planner should confirm whether a direct `UserDashboardSettingsMutatorTests` fixture is
warranted for `MutateBulkAsync`, given it now owns logic no longer covered by the handler test).

### NFR-3: Concurrency
No change to actual concurrency behavior: the per-user lock is still acquired exactly once per
save, and provisioning still happens outside it. `MutateBulkAsync` must not introduce any new
locking, retry, or parallelism.

### NFR-4: Security
No change. Authorization and `ICurrentUserService` usage are unchanged; this is an internal
refactor with no new attack surface.

## Data Model
No changes to `UserDashboardSettings`, `UserDashboardTile`, `UserDashboardTileDto`,
`SaveUserSettingsRequest`, or `SaveUserSettingsResponse`. `UserDashboardSettingsMutationResult`
(the existing `internal readonly record struct` returned by `MutateAsync`) is reused unchanged
as `MutateBulkAsync`'s return type.

## API / Interface Design
No public API / HTTP contract changes. This only changes an `internal` interface
(`IUserDashboardSettingsMutator`) and an `internal sealed` implementation
(`UserDashboardSettingsMutator`), both already restricted to the `Anela.Heblo.Application`
assembly — no consumer outside `SaveUserSettingsHandler`, `EnableTileHandler`, and
`DisableTileHandler` exists today, and none is expected.

New method surface added to the internal interface:
```csharp
Task<UserDashboardSettingsMutationResult> MutateBulkAsync(
    string? userId,
    IReadOnlyList<UserDashboardTileDto> tiles,
    CancellationToken cancellationToken);
```

## Dependencies
- `IUserDashboardSettingsMutator` / `UserDashboardSettingsMutator`
  (`backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/`).
- `SaveUserSettingsHandler` and its existing test file
  (`backend/test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs`), which
  must be updated alongside the handler (see NFR-2).
- No new external services or libraries.

## Out of Scope
- Any change to `EnableTileHandler`, `DisableTileHandler`, or `MutateAsync`'s existing
  single-tile signature/behavior.
- Any change to `GetUserSettingsHandler` or the provisioning logic itself.
- Full-replace/delete-missing-tile semantics for `SaveUserSettings` — tiles omitted from the
  request continue to be left untouched, exactly as today.
- Any UI/frontend change — this is a backend-only internal refactor with no visible behavior
  change (dashboard save-settings frontend flow is unaffected).
- Performance optimization beyond what naturally falls out of removing duplicate code (no new
  batching of `UpdateAsync` calls, no change to `IUserDashboardSettingsRepository`).

## Open Questions
None.

## Status: COMPLETE
