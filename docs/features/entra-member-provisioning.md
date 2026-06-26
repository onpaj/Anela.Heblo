# Entra Member Provisioning

Allows admins to add users who hold the `heblo_user` Entra app role to a permission group before their first login.

## How it works

1. Admin opens a group's detail page in Access Management.
2. The **Add Entra user** search box lists all principals (users and group members) assigned the `heblo_user` app role on the application's service principal.
3. Selecting a person immediately provisions an `AppUser` row (if needed) and adds a `UserGroup` membership.
4. The member appears in the `MembersPicker` with a **"Never logged in"** badge until they sign in.
5. On first login, `PermissionResolver.ResolveAsync` finds the pre-provisioned user by `EntraObjectId`, sets `LastLoginAt`, and the badge disappears.

## Required Microsoft Graph permissions (application)

The app registration needs the following **application** permission with admin consent, in addition to the existing `User.Read.All` / `GroupMember.Read.All`:

| Permission | Reason |
|---|---|
| `Application.Read.All` | Read the service principal's app roles and role assignments via `/servicePrincipals/.../appRoleAssignedTo` |

Alternatively, `Directory.Read.All` grants the same access with broader scope.

### Granting consent

```bash
# Grant Application.Read.All and admin-consent via Azure CLI
# Replace <app-object-id> with the Object ID from Entra portal → App registrations
az ad app permission add \
  --id <app-object-id> \
  --api 00000003-0000-0000-c000-000000000000 \
  --api-permissions 9a5d68dd-52b0-4cc2-bd40-abcf44ac3a30=Role

az ad app permission admin-consent --id <app-object-id>
```

Or grant in the Entra portal: **App registrations → \<app\> → API permissions → Add a permission → Microsoft Graph → Application permissions → Application.Read.All → Grant admin consent**.

## Caching

Entra candidate results are cached for 20 minutes per app role value (same TTL as group member results). The cache key is `app_role_members_{appRoleValue}`. A server restart clears it.

## Database impact

- A new `AppUser` row is created with `LastLoginAt = null` when provisioning a never-logged-in user.
- A new `UserGroup` row is created. The insert is idempotent — adding the same person to the same group twice is a no-op at the repository level.
- No EF Core migrations are required; the schema is unchanged.

## Key files

| Layer | File |
|---|---|
| Graph service | `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` |
| Use case — list candidates | `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetEntraAccessUsers/` |
| Use case — provision + add | `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/AddGroupMember/` |
| Repository | `backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationRepository.cs` |
| Controller | `backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs` |
| FE hooks | `frontend/src/api/hooks/useAccessManagement.ts` |
| FE component | `frontend/src/components/access-management/EntraMemberSearch.tsx` |
