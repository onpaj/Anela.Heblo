### task: extract-resolve-service-principal

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`

- [ ] **Step 1: Confirm the current baseline is green**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetAppRoleMembersTests|FullyQualifiedName~GraphServiceTests"
```
Expected: 12 PASS. Do not proceed until this is green — it is the baseline this plan's every later step compares against.

- [ ] **Step 2: Add the `ResolveServicePrincipalAsync` helper**

In `GraphService.cs`, add this new `private` method immediately after `GetAppRoleMembersAsync` (i.e., as the last member of the class, right before the closing `}` of the class body):

```csharp
    /// <summary>
    /// Resolves the service principal id and its configured app roles for the given Azure AD app
    /// registration client id. Returns (null, null) and logs the failure when the service principal
    /// cannot be resolved.
    /// </summary>
    private async Task<(string? SpId, System.Text.Json.JsonElement? AppRoles)> ResolveServicePrincipalAsync(
        string clientId, string graphToken, HttpClient httpClient, CancellationToken cancellationToken)
    {
        var spUrl = $"https://graph.microsoft.com/v1.0/servicePrincipals(appId='{clientId}')?$select=id,appRoles";
        using var spRequest = new HttpRequestMessage(HttpMethod.Get, spUrl);
        spRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);
        var spResponse = await httpClient.SendAsync(spRequest, cancellationToken);
        var spJson = await spResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!spResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to resolve service principal. Status: {Status}, Body: {Body}", spResponse.StatusCode, spJson);
            return (null, null);
        }

        using var spDoc = System.Text.Json.JsonDocument.Parse(spJson);
        var spId = spDoc.RootElement.TryGetProperty("id", out var spIdProp) ? spIdProp.GetString() : null;
        if (string.IsNullOrEmpty(spId))
        {
            _logger.LogError("Service principal id not found in Graph response for clientId {ClientId}", clientId);
            return (null, null);
        }

        // Clone so the returned JsonElement stays readable after spDoc (and its `using`) goes out of scope.
        var appRoles = spDoc.RootElement.TryGetProperty("appRoles", out var appRolesEl)
            ? appRolesEl.Clone()
            : (System.Text.Json.JsonElement?)null;

        return (spId, appRoles);
    }
```

- [ ] **Step 3: Replace Step 1 of `GetAppRoleMembersAsync` with a call to the new helper**

In `GetAppRoleMembersAsync`, find this block:

```csharp
            // Step 1: resolve the service principal id and app roles for this app registration
            var spUrl = $"https://graph.microsoft.com/v1.0/servicePrincipals(appId='{clientId}')?$select=id,appRoles";
            using var spRequest = new HttpRequestMessage(HttpMethod.Get, spUrl);
            spRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);
            var spResponse = await httpClient.SendAsync(spRequest, cancellationToken);
            var spJson = await spResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!spResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to resolve service principal. Status: {Status}, Body: {Body}", spResponse.StatusCode, spJson);
                return new List<UserDto>();
            }
            using var spDoc = System.Text.Json.JsonDocument.Parse(spJson);
            var spId = spDoc.RootElement.TryGetProperty("id", out var spIdProp) ? spIdProp.GetString() : null;
            if (string.IsNullOrEmpty(spId))
            {
                _logger.LogError("Service principal id not found in Graph response for clientId {ClientId}", clientId);
                return new List<UserDto>();
            }

            // Step 2: find the appRoleId for the requested role value
            string? appRoleId = null;
            if (spDoc.RootElement.TryGetProperty("appRoles", out var appRolesEl))
            {
                foreach (var role in appRolesEl.EnumerateArray())
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

Replace it with (Step 1 now delegates to the helper; Step 2 stays inline for now — it moves to its own helper in Task 2 — but its `spDoc.RootElement.TryGetProperty("appRoles", ...)` reference is updated here to `appRoles`, the value now returned by `ResolveServicePrincipalAsync`, since `spDoc` no longer exists in this method):

```csharp
            var (spId, appRoles) = await ResolveServicePrincipalAsync(clientId, graphToken, httpClient, cancellationToken);
            if (spId is null)
            {
                return new List<UserDto>();
            }

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

- [ ] **Step 4: Build and run tests to verify no behavior change**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: BUILD SUCCESS.

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetAppRoleMembersTests|FullyQualifiedName~GraphServiceTests"
```
Expected: 12 PASS (same count as Step 1 baseline).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs
git commit -m "refactor(usermanagement): extract ResolveServicePrincipalAsync from GetAppRoleMembersAsync"
```

---

