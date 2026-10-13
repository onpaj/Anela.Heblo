# Extract Helper Methods from GraphService.GetAppRoleMembersAsync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce `GraphService.GetAppRoleMembersAsync` from ~190 lines with five inline sequential Graph API steps to a short orchestrating sequence of calls, by extracting each step into a `private` helper method on the same class, with zero behavior change.

**Architecture:** Four new `private` methods are added to `GraphService` — `ResolveServicePrincipalAsync`, `FindAppRoleId` (synchronous — no I/O), `CollectRoleAssigneesAsync`, and `BatchResolveUserDtosAsync` — each moved verbatim from the corresponding inline block, preserving every log message, status check, and return value exactly. Each helper signals failure via a `null`/nullable-tuple return (not an exception) and logs the failure itself, matching the current "log and return empty list" behavior at the call site. The group-expansion loop (recursive `GetGroupMembersAsync` call) stays inline in the orchestrator per the issue's explicit instruction — it is not extracted. `IGraphService` and all callers are unaffected; no test file needs to change.

**Tech Stack:** .NET 8, xUnit + Moq + FluentAssertions, `System.Text.Json`.

---

## Reference material (read before Task 1)

**Current method under refactor:** `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`, method `GetAppRoleMembersAsync` (lines 193–384 as of this plan's writing — line numbers will shift after each task; always re-locate by method/comment name, not by line number, from Task 2 onward).

**Regression test command used throughout this plan** (both test files exercise `GetAppRoleMembersAsync` end-to-end through a fake `HttpMessageHandler` and assert only on its public return value / logged messages / HTTP call count — neither constrains internal method structure):
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetAppRoleMembersTests|FullyQualifiedName~GraphServiceTests"
```
Expected baseline (before any change in this plan): **12 passing** — 3 tests in `GetAppRoleMembersTests.cs` + 9 tests in `GraphServiceTests.cs` (6 `GetAppRoleMembersAsync_*` + 3 unrelated `GetGroupMembersAsync_*`/constructor tests already in that file, which must also keep passing since this plan touches the same file).

**Key invariants any extraction step must preserve** (see `arch-review.r1.md` for full rationale):
1. Token acquisition (`AcquireGraphTokenAsync`-equivalent inline block) happens strictly before `_httpClientFactory.CreateClient(...)` is called — enforced by `GetAppRoleMembersAsync_TokenAcquisitionMsalException_Throws` (`factoryMock.Verify(f => f.CreateClient(...), Times.Never)`).
2. No new `try`/`catch` around any `httpClient.SendAsync(...)` call inside a helper — a raw `HttpRequestException` from the transport must propagate unwrapped out of `GetAppRoleMembersAsync` (only caught by the existing outer `catch (Exception ex) { ...; throw; }`) — enforced by `GetAppRoleMembersAsync_TransportThrows_Throws`.
3. User-id batching (`BatchResolveUserDtosAsync`) is called exactly once, after the full paginated assignment walk **and** all group expansions complete — enforced by `GetAppRoleMembersAsync_TwentyOneUsers_IssuesTwoBatchCalls` (`handler.Requests.Should().HaveCount(4)`).
4. A `JsonElement` returned from `ResolveServicePrincipalAsync` (the `appRoles` array) must remain readable after the method returns — its parent `JsonDocument`'s `using` block must not dispose it first. Use `.Clone()` before returning.
5. Every existing `_logger.LogError`/`LogWarning`/`LogInformation` call — exact message template and exact arguments — must appear unchanged, just possibly inside a different method.

---

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

### task: extract-batch-resolve-user-dtos

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`

- [ ] **Step 1: Add the `BatchResolveUserDtosAsync` helper**

Add this new `private` method after `CollectRoleAssigneesAsync`:

```csharp
    /// <summary>
    /// Resolves display name and email for each user id via Graph $batch, chunked by GraphBatchSize.
    /// Returns null and logs the failure when a batch request itself fails; individual unresolvable
    /// users within an otherwise-successful batch are skipped with a warning, not treated as a failure.
    /// </summary>
    private async Task<List<UserDto>?> BatchResolveUserDtosAsync(
        IReadOnlyCollection<string> userIds, string graphToken, HttpClient httpClient, CancellationToken cancellationToken)
    {
        var users = new List<UserDto>();
        var userIdList = userIds.ToList();
        for (var chunkStart = 0; chunkStart < userIdList.Count; chunkStart += GraphBatchSize)
        {
            var chunk = userIdList.Skip(chunkStart).Take(GraphBatchSize).ToList();

            var batchRequests = chunk.Select((uid, i) => new
            {
                id = i.ToString(),
                method = "GET",
                url = $"/users/{uid}?$select=id,displayName,mail,userPrincipalName"
            }).ToList();

            var batchBody = System.Text.Json.JsonSerializer.Serialize(new { requests = batchRequests });
            using var batchRequest = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/$batch");
            batchRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);
            batchRequest.Content = new StringContent(batchBody, System.Text.Encoding.UTF8, "application/json");

            var batchResponse = await httpClient.SendAsync(batchRequest, cancellationToken);
            var batchJson = await batchResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!batchResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Graph $batch request failed. Status: {Status}, Body: {Body}", batchResponse.StatusCode, batchJson);
                return null;
            }

            using var batchDoc = System.Text.Json.JsonDocument.Parse(batchJson);
            if (!batchDoc.RootElement.TryGetProperty("responses", out var responses))
                continue;

            foreach (var response in responses.EnumerateArray())
            {
                var status = response.TryGetProperty("status", out var st) ? st.GetInt32() : 0;
                if (status != 200)
                {
                    var responseId = response.TryGetProperty("id", out var rid) ? rid.GetString() : "?";
                    var failedUserId = int.TryParse(responseId, out var idx) && idx < chunk.Count ? chunk[idx] : responseId;
                    _logger.LogWarning("Could not resolve user {UserId} — batch sub-response status {Status}", failedUserId, status);
                    continue;
                }

                if (!response.TryGetProperty("body", out var body))
                    continue;

                var displayName = body.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
                var mail = body.TryGetProperty("mail", out var m) ? m.GetString() : null;
                var upn = body.TryGetProperty("userPrincipalName", out var u) ? u.GetString() : null;
                var resolvedId = body.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(resolvedId))
                    users.Add(new UserDto { Id = resolvedId, DisplayName = displayName, Email = mail ?? upn ?? "" });
            }
        }

        return users;
    }
```

- [ ] **Step 2: Replace the inline Step 5 block (and the tail of the method) in `GetAppRoleMembersAsync` with a call to the helper**

Find:

```csharp
            // Step 5: resolve display name + email for each user id using Graph $batch
            var users = new List<UserDto>();
            var userIdList = directUserIds.ToList();
            for (var chunkStart = 0; chunkStart < userIdList.Count; chunkStart += GraphBatchSize)
            {
                var chunk = userIdList.Skip(chunkStart).Take(GraphBatchSize).ToList();

                var batchRequests = chunk.Select((uid, i) => new
                {
                    id = i.ToString(),
                    method = "GET",
                    url = $"/users/{uid}?$select=id,displayName,mail,userPrincipalName"
                }).ToList();

                var batchBody = System.Text.Json.JsonSerializer.Serialize(new { requests = batchRequests });
                using var batchRequest = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/$batch");
                batchRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);
                batchRequest.Content = new StringContent(batchBody, System.Text.Encoding.UTF8, "application/json");

                var batchResponse = await httpClient.SendAsync(batchRequest, cancellationToken);
                var batchJson = await batchResponse.Content.ReadAsStringAsync(cancellationToken);

                if (!batchResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("Graph $batch request failed. Status: {Status}, Body: {Body}", batchResponse.StatusCode, batchJson);
                    return new List<UserDto>();
                }

                using var batchDoc = System.Text.Json.JsonDocument.Parse(batchJson);
                if (!batchDoc.RootElement.TryGetProperty("responses", out var responses))
                    continue;

                foreach (var response in responses.EnumerateArray())
                {
                    var status = response.TryGetProperty("status", out var st) ? st.GetInt32() : 0;
                    if (status != 200)
                    {
                        var responseId = response.TryGetProperty("id", out var rid) ? rid.GetString() : "?";
                        var failedUserId = int.TryParse(responseId, out var idx) && idx < chunk.Count ? chunk[idx] : responseId;
                        _logger.LogWarning("Could not resolve user {UserId} — batch sub-response status {Status}", failedUserId, status);
                        continue;
                    }

                    if (!response.TryGetProperty("body", out var body))
                        continue;

                    var displayName = body.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
                    var mail = body.TryGetProperty("mail", out var m) ? m.GetString() : null;
                    var upn = body.TryGetProperty("userPrincipalName", out var u) ? u.GetString() : null;
                    var resolvedId = body.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(resolvedId))
                        users.Add(new UserDto { Id = resolvedId, DisplayName = displayName, Email = mail ?? upn ?? "" });
                }
            }

            _cache.Set(cacheKey, users, _cacheExpiration);
            _logger.LogInformation("Resolved {Count} app role members for role '{RoleValue}'", users.Count, appRoleValue);
            return users;
```

Replace with:

```csharp
            var users = await BatchResolveUserDtosAsync(directUserIds, graphToken, httpClient, cancellationToken);
            if (users is null)
            {
                return new List<UserDto>();
            }

            _cache.Set(cacheKey, users, _cacheExpiration);
            _logger.LogInformation("Resolved {Count} app role members for role '{RoleValue}'", users.Count, appRoleValue);
            return users;
```

(This is the last piece of `GetAppRoleMembersAsync`'s `try` body — it is immediately followed by the existing `catch (GraphServiceAuthException) { throw; }` / `catch (Exception ex) { ...; throw; }` block, which is untouched.)

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
git commit -m "refactor(usermanagement): extract BatchResolveUserDtosAsync from GetAppRoleMembersAsync"
```

---

### task: full-suite-validation

**Files:** none created/modified — validation only.

- [ ] **Step 1: Confirm `GetAppRoleMembersAsync`'s body shrank as intended**

Run:
```bash
awk '/public async Task<List<UserDto>> GetAppRoleMembersAsync/,/^    }$/' backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs | wc -l
```
Expected: roughly 60–90 lines (down from the ~190 lines / 6 steps the issue reported), now consisting of guard clauses, the try/catch scaffolding, the 5-step orchestration (4 helper calls + the un-extracted group-expansion loop), and caching/logging. This is a sanity check, not a hard gate — if it's a little outside this range that's fine as long as no single method (orchestrator or any of the 4 new helpers) is doing more than one of the six original concerns.

- [ ] **Step 2: Confirm each new helper is `private`, and `IGraphService` is untouched**

Run:
```bash
grep -n "ResolveServicePrincipalAsync\|FindAppRoleId\|CollectRoleAssigneesAsync\|BatchResolveUserDtosAsync" backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs
```
Expected: each of the 4 names appears exactly twice (its `private` declaration + its one call site inside `GetAppRoleMembersAsync`).

```bash
git diff --stat main...HEAD -- backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs
```
Expected: no output (file untouched).

- [ ] **Step 3: Full backend build**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or the same pre-existing warning count as on the base branch — this refactor must not introduce new warnings).

- [ ] **Step 4: Format check**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```
Expected: no formatting violations. If violations are reported, run `dotnet format backend/Anela.Heblo.sln`, confirm the diff is whitespace-only, then re-run Step 3.

- [ ] **Step 5: Full UserManagement-area test run**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.UserManagement"
```
Expected: PASS — includes `GetAppRoleMembersTests` (3), `GraphServiceTests` (9, incl. the 6 `GetAppRoleMembersAsync_*` and 3 unrelated `GetGroupMembersAsync_*`/constructor tests), `GetGroupMembersHandlerTests`, `GetGroupMembersValidationPipelineTests`, `MockGraphServiceTests`, `ParseMembersFromJsonTests`, `EntraAccessUserSourceAdapterTests` — all unmodified, all green.

- [ ] **Step 6: Full test project run (regression check for anything elsewhere referencing `GraphService`)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: PASS, same total pass count as on the base branch (no test added, none removed, none broken).

- [ ] **Step 7: Confirm no unrelated files changed**

Run:
```bash
git diff --stat main...HEAD
```
Expected: only `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` plus this plan's own `artifacts/feat-4219/` files. No test file changes, no `IGraphService.cs` change, no `MockGraphService.cs` change, no frontend/OpenAPI-client regeneration (no public contract changed), no `docs/integrations/mcp-server.md` change (this method has no MCP tool wrapper).

- [ ] **Step 8: Final commit (only if Step 4's format run produced changes not already committed)**

```bash
git status
git add -A
git commit -m "chore(usermanagement): dotnet format after GetAppRoleMembersAsync extraction" --allow-empty
```

---

## Self-Review Notes (recorded after writing this plan)

**Spec coverage map** (spec requirement → task):
- FR-1 (extract `ResolveServicePrincipalAsync`) → task `extract-resolve-service-principal`
- FR-2 (extract `FindAppRoleId`/`FindAppRoleIdAsync`) → task `extract-find-app-role-id`
- FR-3 (extract `CollectRoleAssigneesAsync`) → task `extract-collect-role-assignees`
- FR-4 (keep group expansion inline, un-extracted) → preserved as-is across all four extraction tasks (never touched)
- FR-5 (extract `BatchResolveUserDtosAsync`) → task `extract-batch-resolve-user-dtos`
- FR-6 (orchestrator becomes a short sequence; existing tests pass unmodified) → verified incrementally at the end of every task, and exhaustively in `full-suite-validation`
- NFR-1 (no behavior change) → every extraction step moves code verbatim; verified by the unmodified 12-test regression run after each task
- NFR-2 (consistent failure-signaling shape) → nullable/tuple returns with helper-owned logging, applied uniformly across all 4 helpers (arch-review Decision 1)
- NFR-3 (helpers structured for future testability) → each helper takes its dependencies as parameters (`HttpClient`, `graphToken`, ids) rather than reading ambient state beyond `_logger`
- NFR-4 (code style) → fully-qualified `System.Text.Json.*` matching the file's existing style (no new `using System.Text.Json;`); `private async Task<T>` for I/O steps, plain `private static` for the synchronous `FindAppRoleId`

**Arch-review amendments applied:**
1. Regression coverage correction (both `GetAppRoleMembersTests.cs` and `GraphServiceTests.cs`) — the test command used throughout this plan runs both files together.
2. `JsonElement` lifetime fix (`.Clone()` before `ResolveServicePrincipalAsync` returns) — applied in task `extract-resolve-service-principal`, Step 2.
3. Decision 1 (nullable/tuple failure signaling, helper-owned logging) — applied uniformly to all 4 helpers.
4. Decision 2 (`FindAppRoleId` synchronous) — applied in task `extract-find-app-role-id`.
5. Decision 4 (group-expansion loop stays inline) — never extracted in any task; the loop's `// Step 4: expand group members (reuse existing method)` comment and body are untouched throughout.

**Placeholder scan:** No "TBD"/"TODO"/"similar to Task N" placeholders — every step shows the complete before/after code verbatim, since this plan's entire content is a mechanical move of already-existing, already-read source code (not new logic being designed).

**Type consistency check:** `ResolveServicePrincipalAsync` returns `(string? SpId, JsonElement? AppRoles)`; `FindAppRoleId` takes `JsonElement? appRoles` — matches. `CollectRoleAssigneesAsync` returns `(HashSet<string>? DirectUserIds, List<string>? GroupIdsToExpand)`; the orchestrator's null-check (`directUserIds is null || groupIdsToExpand is null`) matches both being populated together. `BatchResolveUserDtosAsync` takes `IReadOnlyCollection<string> userIds`; the orchestrator passes `directUserIds` (a `HashSet<string>`, which implements `IReadOnlyCollection<string>`) — matches.
