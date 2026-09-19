### task: extract-find-app-role-id

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`

- [ ] **Step 1: Add the `FindAppRoleId` helper**

Add this new `private static` method (synchronous — no I/O, so no `Task`/`async`) after `ResolveServicePrincipalAsync`:

```csharp
    /// <summary>
    /// Finds the appRoleId matching the requested role value within an already-fetched appRoles array.
    /// Returns null when appRoles is null or contains no matching entry.
    /// </summary>
    private static string? FindAppRoleId(System.Text.Json.JsonElement? appRoles, string appRoleValue)
    {
        if (appRoles is null)
        {
            return null;
        }

        foreach (var role in appRoles.Value.EnumerateArray())
        {
            if (role.TryGetProperty("value", out var roleName) && roleName.GetString() == appRoleValue)
            {
                return role.TryGetProperty("id", out var rid) ? rid.GetString() : null;
            }
        }

        return null;
    }
```

- [ ] **Step 2: Replace the inline Step 2 block in `GetAppRoleMembersAsync` with a call to the helper**

Find (as left by Task 1):

```csharp
            // Step 2: find the appRoleId for the requested role value
            string? appRoleId = null;
            if (appRoles is not null)
            {
                foreach (var role in appRoles.Value.EnumerateArray())
                {
                    if (role.TryGetProperty("value", out var roleName) && roleName.GetString() == appRoleValue)
                    {
                        appRoleId = role.TryGetProperty("id", out var rid) ? rid.GetString() : null;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(appRoleId))
            {
                _logger.LogWarning("App role '{RoleValue}' not found on service principal {SpId}", appRoleValue, spId);
                return new List<UserDto>();
            }
```

Replace with:

```csharp
            var appRoleId = FindAppRoleId(appRoles, appRoleValue);
            if (string.IsNullOrEmpty(appRoleId))
            {
                _logger.LogWarning("App role '{RoleValue}' not found on service principal {SpId}", appRoleValue, spId);
                return new List<UserDto>();
            }
```

- [ ] **Step 3: Build and run tests to verify no behavior change**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: BUILD SUCCESS.

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetAppRoleMembersTests|FullyQualifiedName~GraphServiceTests"
```
Expected: 12 PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs
git commit -m "refactor(usermanagement): extract FindAppRoleId from GetAppRoleMembersAsync"
```

---

