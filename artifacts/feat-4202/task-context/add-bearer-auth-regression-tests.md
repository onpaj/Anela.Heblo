### task: add-bearer-auth-regression-tests

Extend the test double used for multi-request capture so tests can assert on the `Authorization` header, then add failing (RED) assertions that pin down the target behavior before touching production code.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs`

- [ ] **Step 1: Add the `System.Net.Http.Headers` using directive**

At the top of the test file, add the missing `using` needed for `AuthenticationHeaderValue`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Anela.Heblo.Adapters.MetaAds;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Xunit;
```

- [ ] **Step 2: Extend `CapturingSequentialHandler` to also capture the `Authorization` header of every request**

Replace the existing `CapturingSequentialHandler` class (at the bottom of the file) with a version that additionally records `request.Headers.Authorization` for every captured request:

```csharp
/// <summary>Captures every request's URL and Authorization header, and returns responses in sequence; repeats the last on exhaustion.</summary>
file sealed class CapturingSequentialHandler : HttpMessageHandler
{
    private readonly List<string> _capturedUrls;
    private readonly List<AuthenticationHeaderValue?> _capturedAuthHeaders;
    private readonly Queue<(HttpStatusCode, string)> _responses;

    public CapturingSequentialHandler(
        List<string> capturedUrls,
        List<AuthenticationHeaderValue?> capturedAuthHeaders,
        params (HttpStatusCode, string)[] responses)
    {
        _capturedUrls = capturedUrls;
        _capturedAuthHeaders = capturedAuthHeaders;
        _responses = new Queue<(HttpStatusCode, string)>(responses);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _capturedUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
        _capturedAuthHeaders.Add(request.Headers.Authorization);
        var (status, body) = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
```

This adds a required constructor parameter, so the two existing call sites (Step 3) must be updated to compile.

- [ ] **Step 3: Update existing `CapturingSequentialHandler` call sites to pass a captured-headers list**

In `GetTransactionsAsync_InitialUrl_ContainsTimeRange`, change:

```csharp
        var capturedUrls = new List<string>();
        var responseBody = """{"data":[],"paging":{"cursors":{"before":"","after":""}}}""";
        var handler = new CapturingSequentialHandler(
            capturedUrls,
            (HttpStatusCode.OK, responseBody));
```

to:

```csharp
        var capturedUrls = new List<string>();
        var capturedAuthHeaders = new List<AuthenticationHeaderValue?>();
        var responseBody = """{"data":[],"paging":{"cursors":{"before":"","after":""}}}""";
        var handler = new CapturingSequentialHandler(
            capturedUrls,
            capturedAuthHeaders,
            (HttpStatusCode.OK, responseBody));
```

(The rest of that test — arrange/act/assert — is unchanged; `capturedAuthHeaders` is unused there, which is fine since the header assertions live in the pagination test in Step 4.)

- [ ] **Step 4: Rewrite `GetTransactionsAsync_PaginationUrl_UsedVerbatim` to assert the Authorization header and the absence of `access_token` in any URL**

Replace the whole test with:

```csharp
    [Fact]
    public async Task GetTransactionsAsync_PaginationUrl_UsedVerbatim()
    {
        // Arrange
        var capturedUrls = new List<string>();
        var capturedAuthHeaders = new List<AuthenticationHeaderValue?>();
        var firstPageBody = """{"data":[{"id":"1","time":1709251200,"amount":100,"currency":"CZK","payment_type":"card"}],"paging":{"next":"https://graph.facebook.com/next-page?cursor=abc123"}}""";
        var secondPageBody = """{"data":[],"paging":{"cursors":{"before":"","after":""}}}""";
        var handler = new CapturingSequentialHandler(
            capturedUrls,
            capturedAuthHeaders,
            (HttpStatusCode.OK, firstPageBody),
            (HttpStatusCode.OK, secondPageBody));

        var source = CreateSource(handler);

        var from = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2024, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        // Act
        await source.GetTransactionsAsync(from, to, CancellationToken.None);

        // Assert
        capturedUrls.Should().HaveCount(2);
        capturedUrls[1].Should().Be("https://graph.facebook.com/next-page?cursor=abc123");
        capturedUrls.Should().OnlyContain(url => !url.Contains("access_token="),
            because: "the access token must never appear in the request URL — it is sent via the Authorization header instead");
        capturedAuthHeaders.Should().HaveCount(2);
        capturedAuthHeaders.Should().AllSatisfy(header =>
            header.Should().Be(new AuthenticationHeaderValue("Bearer", "test-token")));
    }
```

This test now exercises both the initial request and the one paginated request, and asserts (a) neither URL contains `access_token=`, and (b) both requests carry `Authorization: Bearer test-token`.

- [ ] **Step 5: Update the stale `access_token` fixture value in `GetTransactionsAsync_Pagination_AllPagesCollected`**

In that test's `page1Json`, the mocked `paging.next` value still embeds `&access_token=test-token` — a leftover example of the old (pre-fix) URL shape that Meta would no longer echo back once the initial request stops sending the token as a query parameter. Change:

```csharp
              "paging": {
                "next": "https://graph.facebook.com/v21.0/act_123456789/transactions?after=cursor1&access_token=test-token"
              }
```

to:

```csharp
              "paging": {
                "next": "https://graph.facebook.com/v21.0/act_123456789/transactions?after=cursor1"
              }
```

The test's own assertions (pagination collects both pages' transactions) are unaffected — `FetchPageAsync` follows `paging.next` verbatim regardless of its content.

- [ ] **Step 6: Run the test suite and confirm the new/updated assertions fail (RED)**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MetaAdsTransactionSourceTests"
```

Expected: `GetTransactionsAsync_PaginationUrl_UsedVerbatim` FAILS. Specifically:
- The `capturedUrls.Should().OnlyContain(url => !url.Contains("access_token="))` assertion fails, because `BuildInitialUrl` still appends `&access_token=test-token` to the initial URL (capturedUrls[0]).
- The `capturedAuthHeaders.Should().AllSatisfy(...)` assertion fails, because `FetchPageAsync` still calls `_httpClient.GetAsync(url, innerCt)` with no `Authorization` header set, so every captured header is `null`.

All other tests in the file (including the newly-fixture-updated `GetTransactionsAsync_Pagination_AllPagesCollected` and the signature-updated `GetTransactionsAsync_InitialUrl_ContainsTimeRange`) continue to PASS at this point — only production code for FR-1 is still outstanding.

- [ ] **Step 7: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs
git commit -m "test: add failing regression tests for MetaAds Bearer auth header"
```
