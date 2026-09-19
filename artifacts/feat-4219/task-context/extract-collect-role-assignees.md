### task: extract-collect-role-assignees

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`

- [ ] **Step 1: Add the `CollectRoleAssigneesAsync` helper**

Add this new `private` method after `FindAppRoleId`:

```csharp
    /// <summary>
    /// Walks the paginated appRoleAssignedTo collection for the given service principal and app role,
    /// bucketing assignees into direct user ids and group ids to expand.
    /// Returns (null, null) and logs the failure when any page request fails.
    /// </summary>
    private async Task<(HashSet<string>? DirectUserIds, List<string>? GroupIdsToExpand)> CollectRoleAssigneesAsync(
        string spId, string appRoleId, string graphToken, HttpClient httpClient, CancellationToken cancellationToken)
    {
        var directUserIds = new HashSet<string>();
        var groupIdsToExpand = new List<string>();

        string? nextLink = $"https://graph.microsoft.com/v1.0/servicePrincipals/{spId}/appRoleAssignedTo?$top=100";
        while (nextLink != null)
        {
            using var assignRequest = new HttpRequestMessage(HttpMethod.Get, nextLink);
            assignRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);
            var assignResponse = await httpClient.SendAsync(assignRequest, cancellationToken);
            var assignJson = await assignResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!assignResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get app role assignments. Status: {Status}, Body: {Body}", assignResponse.StatusCode, assignJson);
                return (null, null);
            }

            using var assignDoc = System.Text.Json.JsonDocument.Parse(assignJson);
            if (assignDoc.RootElement.TryGetProperty("value", out var assignments))
            {
                foreach (var assignment in assignments.EnumerateArray())
                {
                    var roleId = assignment.TryGetProperty("appRoleId", out var rid) ? rid.GetString() : null;
                    if (roleId != appRoleId) continue;

                    var principalType = assignment.TryGetProperty("principalType", out var pt) ? pt.GetString() : null;
                    var principalId = assignment.TryGetProperty("principalId", out var pid) ? pid.GetString() : null;
                    if (string.IsNullOrEmpty(principalId)) continue;

                    if (principalType == "User")
                        directUserIds.Add(principalId);
                    else if (principalType == "Group")
                        groupIdsToExpand.Add(principalId);
                }
            }

            nextLink = assignDoc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
        }

        return (directUserIds, groupIdsToExpand);
    }
```

- [ ] **Step 2: Replace the inline Step 3 block in `GetAppRoleMembersAsync` with a call to the helper**

Find:

```csharp
            // Step 3: get all principals assigned to this role (paginated)
            var directUserIds = new HashSet<string>();
            var groupIdsToExpand = new List<string>();

            string? nextLink = $"https://graph.microsoft.com/v1.0/servicePrincipals/{spId}/appRoleAssignedTo?$top=100";
            while (nextLink != null)
            {
                using var assignRequest = new HttpRequestMessage(HttpMethod.Get, nextLink);
                assignRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);
                var assignResponse = await httpClient.SendAsync(assignRequest, cancellationToken);
                var assignJson = await assignResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!assignResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to get app role assignments. Status: {Status}, Body: {Body}", assignResponse.StatusCode, assignJson);
                    return new List<UserDto>();
                }

                using var assignDoc = System.Text.Json.JsonDocument.Parse(assignJson);
                if (assignDoc.RootElement.TryGetProperty("value", out var assignments))
                {
                    foreach (var assignment in assignments.EnumerateArray())
                    {
                        var roleId = assignment.TryGetProperty("appRoleId", out var rid) ? rid.GetString() : null;
                        if (roleId != appRoleId) continue;

                        var principalType = assignment.TryGetProperty("principalType", out var pt) ? pt.GetString() : null;
                        var principalId = assignment.TryGetProperty("principalId", out var pid) ? pid.GetString() : null;
                        if (string.IsNullOrEmpty(principalId)) continue;

                        if (principalType == "User")
                            directUserIds.Add(principalId);
                        else if (principalType == "Group")
                            groupIdsToExpand.Add(principalId);
                    }
                }

                nextLink = assignDoc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
            }
```

Replace with:

```csharp
            var (directUserIds, groupIdsToExpand) = await CollectRoleAssigneesAsync(spId, appRoleId, graphToken, httpClient, cancellationToken);
            if (directUserIds is null || groupIdsToExpand is null)
            {
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
git commit -m "refactor(usermanagement): extract CollectRoleAssigneesAsync from GetAppRoleMembersAsync"
```

---

