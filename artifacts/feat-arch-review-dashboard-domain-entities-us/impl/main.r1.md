The implementation is complete. Here's the summary:

---

## Implementation: Relocate Dashboard Domain Entities

**Status: DONE** — both spec compliance and code quality reviews passed.

### What changed

17 files modified across 3 areas:

**New files created** in `backend/src/Anela.Heblo.Domain/Features/Dashboard/`:
- `UserDashboardSettings.cs` — namespace `Anela.Heblo.Domain.Features.Dashboard`, inherits `Entity<int>` from Xcc
- `UserDashboardTile.cs` — same namespace
- `IUserDashboardSettingsRepository.cs` — same namespace, no Xcc.Domain using needed (co-located types)

**Original Xcc files deleted**: `UserDashboardSettings.cs`, `UserDashboardTile.cs`, `IUserDashboardSettingsRepository.cs`

**`using` directives updated** in 4 Application handlers, 5 Persistence files, and 4 test files. `GetUserSettingsHandler.cs` and `GetUserSettingsHandlerTests.cs` retain `using Anela.Heblo.Xcc.Services.Dashboard;` because they reference `ITileRegistry` which stays in Xcc.

**Model snapshot**: 5 CLR type name strings updated from `Anela.Heblo.Xcc.Domain.UserDashboard*` to `Anela.Heblo.Domain.Features.Dashboard.UserDashboard*`. Historical `.Designer.cs` migration files left unchanged.

**No project references changed** — `Domain → Xcc` already existed; `Xcc → Domain` was never introduced.