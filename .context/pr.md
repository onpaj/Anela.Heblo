# PR Context

- **PR**: #3216 — #3184: Fix 403 on GET /api/UserManagement/group-members for manufacture users
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3216
- **Branch**: `feature/feat-3184` → `main`
- **State**: open
- **Author**: onpaj
- **Changes**: +205 / -5 across 6 files
- **Absorbed**: already up to date with `main`, frontend test fixed, all tests passing

## Description

`GET /api/UserManagement/group-members` was returning 403 to authenticated manufacture users. Root cause: class-level `[FeatureAuthorize(Feature.Admin_Administration)]` on `UserManagementController`, but the endpoint is used by `ResponsiblePersonCombobox` which only needs manufacture permissions.

**Fix summary:**
- Backend: moved auth to method level with three-feature OR (Admin_Administration | Manufacture_ManufactureOrders | Manufacture_BatchPlanning)
- Frontend hook: retry function returns false on 403 (no burst-polling)
- Frontend component: shows "Přístup odepřen" for 403, generic error otherwise
- Tests: reflection-based auth regression test + Jest retry-behaviour tests

**Auto-absorb fix:** SwaggerException.isSwaggerException(queryError) crashed when queryError was undefined (test mock had no error property). Added !!queryError null guard in ResponsiblePersonCombobox.tsx.
