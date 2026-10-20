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

