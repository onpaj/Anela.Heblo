# PR Context

- **PR**: #3311 — #3299: remove unused groupId parameter from AcquireGraphTokenAsync
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3311
- **Branch**: `feature/3299-Arch-Review-Usermanagement-Unused-Groupid-Paramete` → `main`
- **State**: open
- **Author**: onpaj
- **Changes**: +396 / -2 across 11 files
- **Absorbed**: backmerged with `main` (clean, no conflicts), pushed

## Description

`GraphService.AcquireGraphTokenAsync` had a `string groupId` parameter that was never used. Removed the dead parameter and updated the single call site in `GetGroupMembersAsync` to pass only `CancellationToken`. No behavioral change.

Closes #3299
