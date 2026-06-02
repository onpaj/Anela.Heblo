## Module
Dashboard

## Finding
`DashboardService` in `backend/src/Anela.Heblo.Xcc/Services/Dashboard/DashboardService.cs` contains the actual domain logic of the Dashboard module:

- **Lines 36–94** `GetUserSettingsAsync`: creates default settings for new users, auto-provisions `AutoShow` tiles to existing users, acquires per-user locks before writing — all business decisions.
- **Lines 121–187** `GetTileDataAsync`: decides which tiles are visible, orders them, controls parallel loading concurrency.

Meanwhile, the MediatR handlers in `Application/Features/Dashboard/UseCases/` are all one-liners that just delegate to `IDashboardService`. For example, `GetUserSettingsHandler.cs` (line 18) calls `_dashboardService.GetUserSettingsAsync(userId)` and maps the result — zero business logic lives in the handler.

Xcc is designated for technical cross-cutting concerns only (`CLAUDE.md`: "Use Xcc for technical concerns only"). `DashboardService` is not technical infrastructure — it is the feature's application service.

## Why it matters
- Violates the project rule: "Business logic should be in MediatR handlers, NOT in controllers" — and by extension not in Xcc.
- MediatR handlers become untestable no-ops: unit tests for handlers only test delegation, not logic.
- Blurs Xcc's role; future developers may incorrectly treat Xcc as a valid home for application logic.
- The auto-provisioning write hidden inside `GetUserSettingsAsync` (a read method) creates an implicit side-effectful read that is hard to trace.

## Suggested fix
Move the provisioning and concurrency logic from `DashboardService` into the relevant MediatR handlers:

- `GetUserSettingsHandler` — include the new-user creation and auto-show tile provisioning logic directly.
- `GetTileDataHandler` — own the parallel loading loop (or delegate to a lightweight `ITileLoader` in Application, not Xcc).
- Demote `DashboardService` to a pure infrastructure helper or delete it entirely, keeping only `ITileRegistry` in Xcc (it is genuinely cross-cutting).

The per-user locking concern (`_userLocks`) can move into a small `IUserSettingsLock` abstraction registered in Application.

---
_Filed by daily arch-review routine on 2026-05-28._