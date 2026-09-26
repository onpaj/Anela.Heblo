# Implementation: remove-lastmodified-defaults

## What was implemented
Removed the non-zero `DateTime.UtcNow` property-initializer default on the `LastModified` property of both `UserDashboardTile` and `UserDashboardSettings`, letting it fall back to the struct default (`DateTime.MinValue`). This matches the project's `TimeProvider`-based time discipline: every production handler that creates or updates these entities already stamps `LastModified` explicitly via `TimeProvider.GetUtcNow()` before persistence, so removing the default only changes behavior for a hypothetical construction site that forgets to set it — which now surfaces as an obviously-wrong `0001-01-01` value instead of silently capturing real wall-clock construction time.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardTile.cs` — removed `= DateTime.UtcNow` initializer from `LastModified`.
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardSettings.cs` — removed `= DateTime.UtcNow` initializer from `LastModified`.
- `backend/test/Anela.Heblo.Tests/Domain/Dashboard/UserDashboardEntityDefaultsTests.cs` — new regression test asserting both entities default `LastModified` to `DateTime.MinValue` when constructed.

## Tests
- `UserDashboardEntityDefaultsTests.UserDashboardTile_Constructed_DefaultsLastModifiedToMinValue` — PASS
- `UserDashboardEntityDefaultsTests.UserDashboardSettings_Constructed_DefaultsLastModifiedToMinValue` — PASS
- Full `*Dashboard*` filtered suite (186 tests, includes `GetUserSettingsHandlerTests`, `EnableTileHandlerTests`, `DisableTileHandlerTests`, `SaveUserSettingsHandlerTests`) — all 186 PASS, no regressions (every existing construction site already assigns `LastModified` explicitly).

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UserDashboardEntityDefaultsTests"
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Dashboard"
dotnet build Anela.Heblo.sln
```
(Note: the solution file is at the repo root, `Anela.Heblo.sln`, not `backend/Anela.Heblo.sln` as the task context assumed.)

## Notes
- `dotnet build Anela.Heblo.sln` succeeded with 0 errors (93 pre-existing warnings, unrelated to this change).
- `dotnet format Anela.Heblo.sln --verify-no-changes` reported pre-existing WHITESPACE violations in `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/*HandlerTests.cs` — unrelated to any file touched by this task. Per the project's surgical-changes rule, these were left untouched.
- No handler, mutator, or other call site was modified — the fix is confined to the two property declarations plus the new regression test, exactly as scoped.

## PR Summary
Removed the non-zero `DateTime.UtcNow` property-initializer defaults on `UserDashboardTile.LastModified` and `UserDashboardSettings.LastModified`, replacing them with the implicit `DateTime.MinValue` default. This closes a silent-failure gap where a future handler that forgets to explicitly stamp `LastModified` via `TimeProvider` would otherwise capture real wall-clock construction time instead of an obviously-wrong sentinel value. Added a regression test covering both entities' default construction behavior; the full Dashboard test suite (186 tests) passes unchanged since every existing handler already sets `LastModified` explicitly.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardTile.cs` — removed non-zero `LastModified` default
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardSettings.cs` — removed non-zero `LastModified` default
- `backend/test/Anela.Heblo.Tests/Domain/Dashboard/UserDashboardEntityDefaultsTests.cs` — new regression test

## Status
DONE
