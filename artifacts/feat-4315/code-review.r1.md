# Code Review: feat-4315 (round 1)

## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes

Reviewed the full feature diff (backend/src and backend/test changes) against
`spec.r1.md`'s functional and non-functional requirements.

- `IUserDashboardSettingsMutator.MutateBulkAsync` signature and XML docs match
  spec FR-1 exactly, including documenting the provisioning-before-lock
  invariant and the "any tile in the batch" semantics of `TileFound`/
  `TileAppended`.
- `UserDashboardSettingsMutator.MutateBulkAsync` implementation matches spec
  FR-2 step-by-step: userId normalization to `"anonymous"`, provisioning via
  `_mediator.Send(new GetUserSettingsRequest(), ...)` before
  `_lock.AcquireAsync`, single `_repository.GetByUserIdAsync` call, early
  return with no write when `settings == null`, a single shared `now` read
  used for every touched/appended tile and for `settings.LastModified`,
  match-by-`TileId` update-or-append semantics, and an *unconditional*
  `_repository.UpdateAsync(settings)` call once `settings` is loaded
  (including for an empty `tiles` list) — this is the one place this method
  intentionally diverges from `MutateAsync`'s conditional-write behavior, and
  the divergence is correct per spec and is exercised by
  `MutateBulkAsync_WhenTilesEmpty_StillPersistsSettings`.
- `SaveUserSettingsHandler` is thinned to exactly the two dependencies
  (`IUserDashboardSettingsMutator`, `ICurrentUserService`) required by spec
  FR-3, passes `request.Tiles ?? Array.Empty<UserDashboardTileDto>()` through
  unchanged (no longer pre-normalizing `userId` — normalization now correctly
  lives solely inside the mutator, matching how `EnableTile`/`DisableTile`
  already work), and always returns `new SaveUserSettingsResponse()`.
- The class was changed from `public class` to `internal sealed class`
  because `IUserDashboardSettingsMutator` is `internal` (a public class can't
  expose an internal type as a constructor parameter — CS0051). This mirrors
  the existing, already-reviewed `EnableTileHandler`/`DisableTileHandler`
  pattern and does not affect MediatR handler resolution (reflection-based).
  Correct fix, not a spec violation (spec's acceptance criteria only
  constrains the constructor's dependency list, not the class's
  accessibility).
- No other production consumer of `IUserDashboardSettingsMutator` exists
  (`EnableTileHandler`/`DisableTileHandler` only call `MutateAsync`), and no
  DI registration change was needed — `DashboardModule.AddDashboardModule()`
  already registers the concrete `UserDashboardSettingsMutator` type, which
  now satisfies the widened interface automatically.
- New `UserDashboardSettingsMutatorTests.cs` directly covers the scaffold
  behaviors NFR-2 calls out as needing to move off of
  `SaveUserSettingsHandlerTests`: null-settings short-circuit, anonymous
  fallback, provisioning-before-lock ordering, lock-acquired-exactly-once
  regardless of tile count, match-vs-append semantics, unconditional
  persistence on empty tile list, and a single shared timestamp across all
  touched/appended tiles — matching the acceptance criteria in spec FR-2 and
  the architect's guidance in `arch-review.r1.md` almost verbatim.
- Rewritten `SaveUserSettingsHandlerTests.cs` mocks
  `IUserDashboardSettingsMutator` directly (matching the established
  `EnableTileHandlerTests`/`DisableTileHandlerTests` pattern) and asserts
  delegation with the correct `userId`/`tiles` arguments, null-tiles →
  empty-list conversion, userId pass-through unchanged (no
  pre-normalization), always-success response, and single-invocation —
  covering everything the old, scaffold-owning test file asserted, just
  against the new boundary.
- Confirmed `backend/src/Anela.Heblo.Application` builds cleanly against this
  branch (0 errors; only pre-existing, unrelated warnings across other
  modules).
- No API/HTTP contract, DTO, or database schema changes — matches spec's "no
  externally observable behavior change" framing (NFR-1/NFR-4). No new
  locking, retry, or parallelism introduced (NFR-3).

No correctness bugs found. No cleanup findings either — the implementation is
a faithful, minimal port of the existing scaffold into the new method, with
no duplicated logic, no unnecessary complexity, and no avoidable extra work.
