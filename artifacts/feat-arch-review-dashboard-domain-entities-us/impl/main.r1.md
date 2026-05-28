---

# Implementation: Relocate Dashboard Domain Entities from Xcc to Domain Layer

## What was implemented

Nothing was committed to the branch. The session is running in a restricted permissions mode where all file write operations (Write tool, Edit tool, Bash mkdir, Bash rm, git mv) require explicit user approval that is not being granted in this automated pipeline context. Only reads and the memory directory write are pre-approved.

## Files created/modified

None — all write attempts were blocked by the permission system.

## Tests

No tests were run (implementation did not proceed).

## How to verify

The implementation cannot be verified without executing the changes. The complete, fully-specified implementation plan is documented in memory at:
`/home/rem/.claude/projects/-home-rem-sandbox-Anela-Heblo--worktrees-feat-arch-review-dashboard-domain-entities-us/memory/project_dashboard_relocation.md`

To execute manually from the branch root:
```bash
# Move entity files (git mv preserves history)
git mv backend/src/Anela.Heblo.Xcc/Domain/UserDashboardSettings.cs backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardSettings.cs
git mv backend/src/Anela.Heblo.Xcc/Domain/UserDashboardTile.cs backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardTile.cs
git mv backend/src/Anela.Heblo.Xcc/Services/Dashboard/IUserDashboardSettingsRepository.cs backend/src/Anela.Heblo.Domain/Features/Dashboard/IUserDashboardSettingsRepository.cs
git mv backend/src/Anela.Heblo.Xcc/Services/Dashboard/IDashboardService.cs backend/src/Anela.Heblo.Domain/Features/Dashboard/IDashboardService.cs
git mv backend/src/Anela.Heblo.Xcc/Services/Dashboard/DashboardService.cs backend/src/Anela.Heblo.Application/Features/Dashboard/Services/DashboardService.cs

# Then update namespaces in the 5 moved files + update using statements in ~20 consumer files
# Then verify: cd backend && dotnet build && dotnet test Anela.Heblo.Tests --filter "Dashboard"
```

## Notes

**Root cause of blocker:** The session has no pre-approved write permissions in `.claude/settings.json`. The project settings file only has plugin enablement, no `"permissions": {"allow": [...]}` section. The memory directory (`~/.claude/projects/.../memory/`) is the only pre-approved write path.

**Key architectural finding documented in arch-review.r1.md (confirmed):** Moving only the 3 entities mentioned in the spec would create a circular reference (`Xcc → Domain + Domain → Xcc`). The minimum viable fix requires also moving `IDashboardService` to Domain and `DashboardService` to Application — exactly as specified in arch-review Amendment 1.

**ModelSnapshot FQN strings:** 5 string literals in `ApplicationDbContextModelSnapshot.cs` reference the old Xcc namespace and must be updated in lockstep to prevent spurious EF migrations.

**To unblock:** Add this to the project's `.claude/settings.json`:
```json
"permissions": {
  "allow": ["Write(*)", "Edit(*)", "Bash(git *)", "Bash(mkdir *)", "Bash(rm *)"]
}
```

## PR Summary

This PR relocates Dashboard domain entities (`UserDashboardSettings`, `UserDashboardTile`, `IUserDashboardSettingsRepository`, `IDashboardService`) from `Anela.Heblo.Xcc` to `Anela.Heblo.Domain/Features/Dashboard/`, and moves `DashboardService` to `Anela.Heblo.Application/Features/Dashboard/Services/`. This aligns Dashboard with the layering convention used by every other module and breaks a structural violation where Xcc contained feature-domain types.

The arch review identified that the spec's original scope (3 files) would produce a circular project reference — `IDashboardService` and `DashboardService` must move in the same change. The `ApplicationDbContextModelSnapshot.cs` FQN strings are also updated to prevent spurious EF migrations.

No schema changes, no API surface changes, no behavioral changes.

### Changes
- `Anela.Heblo.Domain/Features/Dashboard/` — 4 new files (entities + interfaces)
- `Anela.Heblo.Application/Features/Dashboard/Services/DashboardService.cs` — moved from Xcc
- `Anela.Heblo.Xcc/Domain/` — 2 files removed; `Anela.Heblo.Xcc/Services/Dashboard/` — 3 files removed
- ~20 consumer files — using statement updates only
- `ApplicationDbContextModelSnapshot.cs` — 5 FQN string literal replacements

## Status

BLOCKED

**Reason:** Session is in restricted read-only permissions mode. All file write operations require user approval that is not being granted in this automated pipeline. The complete implementation plan is preserved in memory for the next session.