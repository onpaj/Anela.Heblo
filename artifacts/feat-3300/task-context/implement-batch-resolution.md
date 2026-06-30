### task: implement-batch-resolution

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`

Replace lines 385–404 (Step 5) of `GraphService.cs`. The exact existing block to replace is:

```csharp
            // Step 5: resolve display name + email for each user id
            var users = new List<UserDto>();
            foreach (var userId in directUserIds)
            {
                var userUrl = $"https://graph.microsoft.com/v1.0/users/{userId}?$select=id,displayName,mail,userPrincipalName";
                using var userRequest = new HttpRequestMessage(HttpMethod.Get, userUrl);
                userRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);
                var userResponse = await httpClient.SendAsync(userRequest, cancellationToken);
                if (!userResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Could not resolve user {UserId}", userId);
                    continue;
                }
                var userJson = await userResponse.Content.ReadAsStringAsync(cancellationToken);
                using var userDoc = System.Text.Json.JsonDocument.Parse(userJson);
                var displayName = userDoc.RootElement.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
                var mail = userDoc.RootElement.TryGetProperty("mail", out var m) ? m.GetString() : null;
                var upn = userDoc.RootElement.TryGetProperty("userPrincipalName", out var u) ? u.GetString() : null;
                users.Add(new UserDto { Id = userId, DisplayName = displayName, Email = mail ?? upn ?? "" });
            }
```

- [ ] **Step 1: Add the batch size constant**

Add `private const int GraphBatchSize = 20;` to the class-level constants block (near the existing `private const int SearchResultLimit = 25;` on line 24). Open `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` and edit:

```csharp
    private const int SearchResultLimit = 25;
```

becomes:

```csharp
    private const int SearchResultLimit = 25;
    private const int GraphBatchSize = 20;
```

- [ ] **Step 2: Replace the Step 5 loop with batch resolution**

Locate the exact comment `// Step 5: resolve display name + email for each user id` and replace the entire block through `users.Add(...)` with the following. The replacement ends just before `_cache.Set(cacheKey, users, _cacheExpiration);`.

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
```

- [ ] **Step 3: Build to verify no compile errors**

```bash
dotnet build /home/user/Anela.Heblo/backend/src/Anela.Heblo.Application/
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
cd /home/user/Anela.Heblo
git add backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs
git commit -m "perf: replace N+1 Graph user-resolution loop with \$batch in GetAppRoleMembersAsync"
```

---

