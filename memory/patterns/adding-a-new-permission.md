# Adding a new permission (feature) to the access matrix

`access-matrix.json` at the repo root is the single source of truth. Steps:

1. Add the feature to `features`: `{ "key": "Module_Thing", "label": "<Czech label>", "hasWrite": true }`.
   The key converts to role strings automatically: `Module_Thing` → `module.thing.read` / `.write`.
   Omit `hasWrite` for a pure capability gate (see `Jobs_Trigger`).
2. Optionally add the roles to the relevant `seedGroups` entries (keep them next to the
   module's other roles).
3. Regenerate the five artifacts — a Debug build of `Anela.Heblo.API` does it, or run it directly:
   ```
   dotnet run --project backend/tools/Anela.Heblo.AccessMatrixGen -- access-matrix.json \
     backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs \
     backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs \
     backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs \
     frontend/src/auth/accessMatrix.generated.ts \
     access-matrix-entra.generated.json
   ```
4. Gate the endpoint: `[FeatureAuthorize(Feature.Module_Thing, AccessLevel.Write)]`. Class-level
   and method-level attributes both apply (AND).
5. Gate the UI with the literal role string via `usePermissionsContext().hasPermission(...)`.

**`seedGroups` does not update existing environments.** `JsonGroupSeeder` is insert-if-missing at
the *group* level, so a role added to an already-existing group never reaches a deployed DB. Until
someone grants it, only `super_user` holds the new permission — and if the endpoint previously sat
behind a different feature, moving it means **nobody** can use it in the meantime. Always ship a
deploy step with the change. Two ways to apply it:

- **Preferred:** grant the roles at `/admin/access` (Groups tab). Grant **both** `.read` and
  `.write` — they are independent items in the picker, and a write-only grant leaves a form that
  cannot load its current values.
- `scripts/seed-authorization.sh <env> --reset-group <Name>` also works, but it is **destructive**:
  `ResetGroupAsync` removes every existing permission on that group and re-adds only what the JSON
  lists, wiping any hand-added grants.

The resolver caches permissions per user for 5 minutes, but `UpdateGroupHandler` calls
`InvalidateCache` for every member of the edited group, so a grant made in the app takes effect
immediately.

Per-feature **Entra app roles are unused** since the in-app permissions cutover (only `super_user`
matters), so the regenerated `access-matrix-entra.generated.json` needs no Azure action.
`super_user` bypasses every gate.
