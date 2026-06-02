## Module
Dashboard

## Finding
Two domain entities and one repository interface are placed in the `Xcc` cross-cutting concerns project instead of the Domain layer:

- `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardSettings.cs`
- `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardTile.cs`
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/IUserDashboardSettingsRepository.cs`

These are Dashboard-specific types — they have no use or meaning outside the Dashboard feature. Every other module places its domain entities in `Anela.Heblo.Domain/Features/{Module}/` and its repository interfaces there as well (as established by filesystem.md and development_guidelines.md). The Dashboard module is the only one that skips this.

## Why it matters
- Violates Clean Architecture layering: domain entities must live in the Domain project; placing them in Xcc pollutes the cross-cutting concerns project with feature-specific domain logic.
- `Xcc` is documented as the home for technical concerns only (`CLAUDE.md`: "Don't create 'Common' or 'Shared' projects — Use Xcc for technical concerns only"). `UserDashboardSettings` is not a technical concern.
- `DashboardService` (already tracked in issue #1943) depends on these Xcc-hosted entities; if the entities moved to Domain they could be cleanly accessed from Application without Xcc being a middleman.
- When the codebase splits to per-module DbContexts (ADR-001 Phase 2), these entities will need to migrate anyway — having them in the right place now avoids a double move.

## Suggested fix
Move the two entities and the repository interface to their canonical locations:

```
backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardSettings.cs
backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardTile.cs
backend/src/Anela.Heblo.Domain/Features/Dashboard/IUserDashboardSettingsRepository.cs
```

Update `using` statements in `DashboardService.cs`, `SaveUserSettingsHandler.cs`, `EnableTileHandler.cs`, the persistence configuration files, and the repository implementation in `Persistence/Dashboard/`. Remove the `Xcc/Domain/` files and the `Xcc/Services/Dashboard/IUserDashboardSettingsRepository.cs` file.

---
_Filed by daily arch-review routine on 2026-05-28._