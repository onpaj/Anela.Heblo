## Module / File
`backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUserEffectivePermissions/GetUserEffectivePermissionsHandler.cs`

## Coverage
Zero direct tests. The Authorization module has broad test coverage via `AuthorizationIntegrationTests.cs` and handler-specific files, but `GetUserEffectivePermissionsHandler` is not referenced in any test file.

## What's not tested
Three paths:
1. **User not found** — returns error response with `AuthorizationUserNotFound`
2. **User found but `IsActive == false`** — returns a response with an empty permissions list (silently, no error code)
3. **Active user** — resolves group closure, merges with `AccessRoles.Base`, returns ordered permission list

The inactive-user path (case 2) is the most security-relevant: a deactivated user must receive zero permissions. If this condition is accidentally removed or inverted, deactivated users retain their old permission set and no test fails.

## Why it matters
Access control correctness depends on this handler. The `IsActive` guard is the sole barrier between a deactivated user and their previously assigned permissions. A regression here is silent — the system continues operating normally while the wrong users have access.

## Suggested approach
Unit-test the handler with a mocked `IAuthorizationRepository`. Three tests: user not found returns error; inactive user returns empty list; active user returns merged + sorted permissions. ~1 hour.

---
_Filed by weekly coverage-gap routine on 2026-06-08. Based on CI run #27104028537 (6568feba33640ae063b2cb6af3c81da31b3720e1)._