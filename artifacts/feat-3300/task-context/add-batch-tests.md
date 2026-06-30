### task: add-batch-tests

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`

The test file already has `BuildService(handler, ...)` helper and private `ThrowingHttpMessageHandler` / `DisposalTrackingHandler` classes. We add:

1. A `SequentialFakeHttpMessageHandler` private class (responses returned in order, one per call) — mirrors the `ThrowingHttpMessageHandler` / `DisposalTrackingHandler` pattern.
2. Four `[Fact]` tests covering N=1, N=21, non-200 sub-response, and batch-level failure.

The new tests exercise `GetAppRoleMembersAsync`, which requires more plumbing than `GetGroupMembersAsync`: the method resolves a service principal, an app role id, paginates assignments, then batches user lookups. We'll wire up `SequentialFakeHttpMessageHandler` to return canned responses in the exact call order the method issues them.

- [ ] **Step 1: Add `SequentialFakeHttpMessageHandler` private class**

Open `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`.

Append the following class **inside** the `GraphServiceTests` class body, immediately after the closing brace of `DisposalTrackingHandler` (before the final `}` that closes `GraphServiceTests`):

```csharp
    /// <summary>
    /// Returns responses in order, one per SendAsync call.
    /// Throws InvalidOperationException if more calls are made than responses provided.
    /// </summary>
    private sealed class SequentialFakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(System.Net.HttpStatusCode Status, string Body)> _responses;
        public List<(Uri? Uri, HttpMethod? Method, string Body)> Requests { get; } = new();

        public SequentialFakeHttpMessageHandler(params (System.Net.HttpStatusCode, string)[] responses)
        {
            _responses = new Queue<(System.Net.HttpStatusCode, string)>(responses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string requestBody = string.Empty;
            if (request.Content is not null)
                requestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add((request.RequestUri, request.Method, requestBody));

            if (_responses.Count == 0)
                throw new InvalidOperationException($"No more queued responses. Unexpected call to {request.RequestUri}");

            var (status, body) = _responses.Dequeue();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
```

- [ ] **Step 2: Add helper method `BuildServiceSequential`**

Add a second build-helper inside `GraphServiceTests` (before the private handler classes), so tests can pass a `SequentialFakeHttpMessageHandler` without repeating wiring:

```csharp
    private static GraphService BuildServiceSequential(
        SequentialFakeHttpMessageHandler handler,
        out Mock<IHttpClientFactory> factoryMock)
    {
        factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient(MicrosoftGraphClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var tokenMock = new Mock<ITokenAcquisition>();
        tokenMock
            .Setup(t => t.GetAccessTokenForAppAsync(
                "https://graph.microsoft.com/.default",
                It.IsAny<string?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("test-token");

        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<GraphService>>();

        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c["AzureAd:ClientId"]).Returns("test-client-id");

        return new GraphService(tokenMock.Object, cache, logger, factoryMock.Object, configuration.Object);
    }
```

Note the use of `Mock<IConfiguration>` with `AzureAd:ClientId` configured — `GetAppRoleMembersAsync` reads this at line ~275 and returns early if null.

- [ ] **Step 3: Add canned JSON constants**

Add these constants to the top of the `GraphServiceTests` class (alongside the existing `SampleGraphResponse`):

```csharp
    // --- Canned responses for GetAppRoleMembersAsync batch tests ---

    private const string SpResponse = """
{
  "id": "sp-id-001",
  "appRoles": [
    {
      "id": "role-id-abc",
      "value": "Admin"
    }
  ]
}
""";

    private const string AssignmentsPageResponse = """
{
  "value": [
    {
      "appRoleId": "role-id-abc",
      "principalType": "User",
      "principalId": "aaaaaaaa-0001-0001-0001-000000000001"
    }
  ]
}
""";

    // Assignments page with 21 users (ids aaaaaaaa-0001-...-0001 through ...-0021)
    private static string AssignmentsPage21Response()
    {
        var assignments = Enumerable.Range(1, 21).Select(i =>
            $$"""{"appRoleId":"role-id-abc","principalType":"User","principalId":"aaaaaaaa-{{i:D4}}-{{i:D4}}-{{i:D4}}-{{i:D12}}"}"""
        );
        return $$"""{"value":[{{string.Join(",", assignments)}}]}""";
    }

    private const string BatchResponse1User = """
{
  "responses": [
    {
      "id": "0",
      "status": 200,
      "body": {
        "id": "aaaaaaaa-0001-0001-0001-000000000001",
        "displayName": "Alice Admin",
        "mail": "alice@example.com",
        "userPrincipalName": "alice@example.com"
      }
    }
  ]
}
""";

    private static string BatchResponseFor21Users(int startIndex, int count)
    {
        var resps = Enumerable.Range(0, count).Select(i =>
        {
            var n = startIndex + i;
            return $$"""
{
  "id": "{{i}}",
  "status": 200,
  "body": {
    "id": "aaaaaaaa-{{n:D4}}-{{n:D4}}-{{n:D4}}-{{n:D12}}",
    "displayName": "User {{n}}",
    "mail": "user{{n}}@example.com",
    "userPrincipalName": "user{{n}}@example.com"
  }
}
""";
        });
        return $$"""{"responses":[{{string.Join(",", resps)}}]}""";
    }

    private const string BatchResponseWithOneNon200 = """
{
  "responses": [
    {
      "id": "0",
      "status": 404,
      "body": { "error": { "code": "Request_ResourceNotFound" } }
    }
  ]
}
""";
```

- [ ] **Step 4: Write the test — N=1 single user resolves via one batch call**

```csharp
    [Fact]
    public async Task GetAppRoleMembersAsync_SingleUser_IssuesOneBatchCall()
    {
        // Arrange
        // Call order: (1) SP lookup, (2) assignments page (no nextLink), (3) $batch for 1 user
        var handler = new SequentialFakeHttpMessageHandler(
            (System.Net.HttpStatusCode.OK, SpResponse),
            (System.Net.HttpStatusCode.OK, AssignmentsPageResponse),
            (System.Net.HttpStatusCode.OK, BatchResponse1User)
        );
        var service = BuildServiceSequential(handler, out _);

        // Act
        var result = await service.GetAppRoleMembersAsync("Admin");

        // Assert
        result.Should().HaveCount(1);
        result[0].Id.Should().Be("aaaaaaaa-0001-0001-0001-000000000001");
        result[0].DisplayName.Should().Be("Alice Admin");
        result[0].Email.Should().Be("alice@example.com");

        // Exactly 3 HTTP calls: SP, assignments, one batch
        handler.Requests.Should().HaveCount(3);
        handler.Requests[2].Uri!.ToString().Should().Be("https://graph.microsoft.com/v1.0/$batch");
        handler.Requests[2].Method.Should().Be(HttpMethod.Post);

        // Sub-request url is relative (no host)
        handler.Requests[2].Body.Should().Contain("/users/aaaaaaaa-0001-0001-0001-000000000001");
        handler.Requests[2].Body.Should().NotContain("https://graph.microsoft.com");
    }
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet test /home/user/Anela.Heblo/backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GetAppRoleMembersAsync_SingleUser_IssuesOneBatchCall" -v
```

Expected: PASS

- [ ] **Step 6: Write the test — N=21 users uses two batch calls**

```csharp
    [Fact]
    public async Task GetAppRoleMembersAsync_TwentyOneUsers_IssuesTwoBatchCalls()
    {
        // Arrange
        // Call order: (1) SP, (2) assignments page with 21 users, (3) batch chunk 1 (20), (4) batch chunk 2 (1)
        var handler = new SequentialFakeHttpMessageHandler(
            (System.Net.HttpStatusCode.OK, SpResponse),
            (System.Net.HttpStatusCode.OK, AssignmentsPage21Response()),
            (System.Net.HttpStatusCode.OK, BatchResponseFor21Users(1, 20)),
            (System.Net.HttpStatusCode.OK, BatchResponseFor21Users(21, 1))
        );
        var service = BuildServiceSequential(handler, out _);

        // Act
        var result = await service.GetAppRoleMembersAsync("Admin");

        // Assert
        result.Should().HaveCount(21);

        // Exactly 4 HTTP calls: SP, assignments, batch1, batch2
        handler.Requests.Should().HaveCount(4);
        handler.Requests[2].Uri!.ToString().Should().Be("https://graph.microsoft.com/v1.0/$batch");
        handler.Requests[3].Uri!.ToString().Should().Be("https://graph.microsoft.com/v1.0/$batch");

        // First batch body has 20 sub-requests, second has 1
        var body1 = System.Text.Json.JsonDocument.Parse(handler.Requests[2].Body);
        body1.RootElement.GetProperty("requests").GetArrayLength().Should().Be(20);

        var body2 = System.Text.Json.JsonDocument.Parse(handler.Requests[3].Body);
        body2.RootElement.GetProperty("requests").GetArrayLength().Should().Be(1);
    }
```

- [ ] **Step 7: Run the test to verify it passes**

```bash
dotnet test /home/user/Anela.Heblo/backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GetAppRoleMembersAsync_TwentyOneUsers_IssuesTwoBatchCalls" -v
```

Expected: PASS

- [ ] **Step 8: Write the test — non-200 sub-response is skipped with a warning**

```csharp
    [Fact]
    public async Task GetAppRoleMembersAsync_NonTwoHundredSubResponse_SkipsUserAndLogsWarning()
    {
        // Arrange
        // Call order: (1) SP, (2) assignments (1 user), (3) batch with 404 sub-response
        var handler = new SequentialFakeHttpMessageHandler(
            (System.Net.HttpStatusCode.OK, SpResponse),
            (System.Net.HttpStatusCode.OK, AssignmentsPageResponse),
            (System.Net.HttpStatusCode.OK, BatchResponseWithOneNon200)
        );

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient(MicrosoftGraphClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var tokenMock = new Mock<ITokenAcquisition>();
        tokenMock
            .Setup(t => t.GetAccessTokenForAppAsync(
                "https://graph.microsoft.com/.default",
                It.IsAny<string?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("test-token");

        var loggerMock = new Mock<ILogger<GraphService>>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c["AzureAd:ClientId"]).Returns("test-client-id");

        var service = new GraphService(tokenMock.Object, cache, loggerMock.Object, factoryMock.Object, configuration.Object);

        // Act
        var result = await service.GetAppRoleMembersAsync("Admin");

        // Assert: user is skipped, result is empty
        result.Should().BeEmpty();

        // LogWarning was called for the skipped sub-response
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Could not resolve user")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

- [ ] **Step 9: Run the test to verify it passes**

```bash
dotnet test /home/user/Anela.Heblo/backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GetAppRoleMembersAsync_NonTwoHundredSubResponse_SkipsUserAndLogsWarning" -v
```

Expected: PASS

- [ ] **Step 10: Write the test — batch-level HTTP failure returns empty list and logs error**

```csharp
    [Fact]
    public async Task GetAppRoleMembersAsync_BatchLevelFailure_ReturnsEmptyListAndLogsError()
    {
        // Arrange
        // Call order: (1) SP, (2) assignments (1 user), (3) batch returns 500
        var handler = new SequentialFakeHttpMessageHandler(
            (System.Net.HttpStatusCode.OK, SpResponse),
            (System.Net.HttpStatusCode.OK, AssignmentsPageResponse),
            (System.Net.HttpStatusCode.InternalServerError, "{\"error\":{\"code\":\"ServiceNotAvailable\"}}")
        );

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient(MicrosoftGraphClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var tokenMock = new Mock<ITokenAcquisition>();
        tokenMock
            .Setup(t => t.GetAccessTokenForAppAsync(
                "https://graph.microsoft.com/.default",
                It.IsAny<string?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("test-token");

        var loggerMock = new Mock<ILogger<GraphService>>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c["AzureAd:ClientId"]).Returns("test-client-id");

        var service = new GraphService(tokenMock.Object, cache, loggerMock.Object, factoryMock.Object, configuration.Object);

        // Act
        var result = await service.GetAppRoleMembersAsync("Admin");

        // Assert: returns empty list
        result.Should().BeEmpty();

        // LogError was called for the batch failure
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Graph $batch request failed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

- [ ] **Step 11: Run the test to verify it passes**

```bash
dotnet test /home/user/Anela.Heblo/backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GetAppRoleMembersAsync_BatchLevelFailure_ReturnsEmptyListAndLogsError" -v
```

Expected: PASS

- [ ] **Step 12: Run all GraphService tests to confirm no regressions**

```bash
dotnet test /home/user/Anela.Heblo/backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GraphServiceTests" -v
```

Expected: All tests PASS (existing 7 + new 4 = 11 total).

- [ ] **Step 13: Run dotnet format**

```bash
dotnet format /home/user/Anela.Heblo/backend/test/Anela.Heblo.Tests/
```

Expected: No changes or only whitespace/brace-style adjustments; no compile errors.

- [ ] **Step 14: Commit**

```bash
cd /home/user/Anela.Heblo
git add backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs
git commit -m "test: add batch-resolution unit tests for GetAppRoleMembersAsync"
```
