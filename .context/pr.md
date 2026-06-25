# PR Context

- **PR**: #3342 — #3339: Add admin permission guard to ResponsiblePersonCombobox
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3342
- **Branch**: `feature/3339-Add-Admin-Permission-Guard-To-Responsiblepersoncom` → `main`
- **State**: OPEN
- **Author**: onpaj
- **Changes**: +1178 / -3 across 16 files
- **Absorbed**: backmerged with `main` (clean merge, no conflicts), branch pushed

## Test status

- **Frontend**: `npm run build` ✅, PR-touched files lint clean (146 pre-existing lint errors in unrelated test files, identical to `main`), `useUserManagement` + `ResponsiblePersonCombobox` tests ✅ (16 passed)
- **Backend**: `dotnet build` ✅, full suite 5395 passed / 15 failed. The 15 failures are all `KnowledgeBaseRepositoryIntegrationTests` failing in `InitializeAsync` with EF Core `ManyServiceProvidersCreatedWarning` (>20 IServiceProvider instances). Pre-existing test-pollution: the class passes 15/15 in isolation. Unrelated to this frontend-only PR.

## Description

Closes #3339

### What the issue was

`ResponsiblePersonCombobox` unconditionally fired `GET /api/UserManagement/group-members` regardless of the caller's permissions, producing 113 avoidable 403 responses per week from non-admin Manufacture users. The endpoint is correctly protected by `[FeatureAuthorize(Feature.Admin_Administration)]` — the backend policy is fine. The bug was entirely on the frontend: the hook was called with no permission check, triggering the request (plus 2 retries) for every non-admin user who opened a Manufacture screen.

### How it was fixed / handled

Two minimal, backwards-compatible changes:

**`useResponsiblePersonsQuery` (`useUserManagement.ts`)** — added an optional `options?: { enabled?: boolean }` second argument. The internal `enabled` is now `Boolean(groupId) && (options?.enabled ?? true)`. Omitting the option preserves existing behavior for all other call sites.

**`ResponsiblePersonCombobox`** — added a single `usePermissionsContext().hasPermission('admin.administration.read')` check internally and passed the result as `enabled` to the hook. No new props; no changes to the three Manufacture call sites (`CreateManufactureOrderModal`, `BasicInfoSection`, `ManufactureOrderFilters`). For non-admins the query stays idle and the combobox degrades to the existing `allowManualEntry` free-text path — no error banner shown.

### Artifacts
- Brief, spec, arch-review, task-plan, impl, and review markdown are committed in this branch under `artifacts/feat-3339/`.
