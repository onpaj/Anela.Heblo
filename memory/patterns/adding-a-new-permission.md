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

**`seedGroups` does not update existing environments.** `JsonGroupSeeder` is insert-if-missing
only, so a role added to an already-existing group never reaches a deployed DB. After deploying a
new permission, grant it in the app at `/admin/access` (Groups tab); the resolver's 5-minute
permission cache is invalidated on every grant change, so it takes effect immediately.

Per-feature **Entra app roles are unused** since the in-app permissions cutover (only `super_user`
matters), so the regenerated `access-matrix-entra.generated.json` needs no Azure action.
`super_user` bypasses every gate.
