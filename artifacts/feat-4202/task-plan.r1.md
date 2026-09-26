# Move Meta Ads Access Token to Authorization Header — Implementation Plan

**Goal:** Stop sending the Meta Graph API OAuth access token as an `access_token` URL query parameter in `MetaAdsTransactionSource` and send it instead as a standard `Authorization: Bearer <token>` HTTP header, removing the now-unnecessary `RedactToken` helper.

**Architecture:** No new components, files, or DI changes. The fix is confined to two private methods of `MetaAdsTransactionSource` (`Anela.Heblo.Adapters.MetaAds` project): `BuildInitialUrl` drops the `access_token` query fragment, and `FetchPageAsync` builds an explicit `HttpRequestMessage` with `Headers.Authorization` set instead of calling `_httpClient.GetAsync`. Because `GetTransactionsAsync`'s pagination loop re-enters the same `FetchPageAsync` for every `paging.next` cursor, this single change point covers both the initial request and all paginated requests. `RedactToken` is deleted along with its only call site. This matches the existing Bearer-header idiom already used elsewhere in the codebase (e.g. `Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs:242`), per `arch-review.r1.md` Decision 1.

**Tech Stack:** .NET 8, `System.Net.Http` / `System.Net.Http.Headers.AuthenticationHeaderValue` (BCL), Polly `ResiliencePipeline` (unchanged), xUnit + FluentAssertions for tests.

---

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

---

### task: move-access-token-to-authorization-header

Implement the production change: stop putting the access token in the URL, send it via the `Authorization` header instead, and delete the now-unused `RedactToken` helper. Make the tests from the previous task pass.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs:1-9` (using block), `:94-107` (`FetchPageAsync`), `:109-113` (`BuildInitialUrl`), `:115-123` (`RedactToken`, to be deleted)

- [ ] **Step 1: Add the `System.Net.Http.Headers` using directive**

Change the top of the file from:

```csharp
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
```

to:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
```

- [ ] **Step 2: Remove `access_token` from `BuildInitialUrl`**

Change:

```csharp
    private string BuildInitialUrl(DateTime from, DateTime to) =>
        $"https://graph.facebook.com/{_settings.ApiVersion}/{_settings.AccountId}/transactions" +
        $"?fields=id,time,amount,currency,payment_type" +
        $"&access_token={_settings.AccessToken}" +
        $"&time_range={{\"since\":\"{from.ToUniversalTime():yyyy-MM-dd}\",\"until\":\"{to.ToUniversalTime():yyyy-MM-dd}\"}}";
```

to:

```csharp
    private string BuildInitialUrl(DateTime from, DateTime to) =>
        $"https://graph.facebook.com/{_settings.ApiVersion}/{_settings.AccountId}/transactions" +
        $"?fields=id,time,amount,currency,payment_type" +
        $"&time_range={{\"since\":\"{from.ToUniversalTime():yyyy-MM-dd}\",\"until\":\"{to.ToUniversalTime():yyyy-MM-dd}\"}}";
```

- [ ] **Step 3: Make `FetchPageAsync` send the token via the `Authorization` header instead of the URL**

Change:

```csharp
    private async Task<MetaTransactionsResponse> FetchPageAsync(string url, CancellationToken ct)
    {
        _logger.LogDebug("MetaAds: GET {Url}", RedactToken(url));

        return await _pipeline.ExecuteAsync(async innerCt =>
        {
            var response = await _httpClient.GetAsync(url, innerCt);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(innerCt);
            return JsonSerializer.Deserialize<MetaTransactionsResponse>(json, JsonOptions)
                   ?? throw new InvalidOperationException("MetaAds API returned null response body.");
        }, ct);
    }
```

to:

```csharp
    private async Task<MetaTransactionsResponse> FetchPageAsync(string url, CancellationToken ct)
    {
        _logger.LogDebug("MetaAds: GET {Url}", url);

        return await _pipeline.ExecuteAsync(async innerCt =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);

            var response = await _httpClient.SendAsync(request, innerCt);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(innerCt);
            return JsonSerializer.Deserialize<MetaTransactionsResponse>(json, JsonOptions)
                   ?? throw new InvalidOperationException("MetaAds API returned null response body.");
        }, ct);
    }
```

Note: the `HttpRequestMessage` is constructed *inside* the `_pipeline.ExecuteAsync` lambda (not once, outside, and reused). This matters because a single `HttpRequestMessage` instance cannot be sent twice — `HttpClient.SendAsync` throws `InvalidOperationException` on a second send of the same instance. The resilience pipeline retries by re-invoking this lambda, so each retry attempt must build its own fresh `HttpRequestMessage`, exactly as `GetAsync(url, innerCt)` implicitly did before (it built a new request internally on every call). This preserves the behavior verified by `GetTransactionsAsync_RateLimitRetry_SucceedsOnSecondAttempt`.

- [ ] **Step 4: Delete `RedactToken`**

Remove this method entirely from the class:

```csharp
    private static string RedactToken(string url)
    {
        var idx = url.IndexOf("access_token=", StringComparison.Ordinal);
        if (idx < 0) return url;
        var end = url.IndexOf('&', idx);
        return end < 0
            ? url[..idx] + "access_token=***"
            : url[..idx] + "access_token=***" + url[end..];
    }
```

After this step, `RedactToken` has no remaining references anywhere in the class (its only call site was removed in Step 3).

- [ ] **Step 5: Run the MetaAds test suite and confirm everything passes (GREEN)**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MetaAdsTransactionSourceTests"
```

Expected: all 8 tests in `MetaAdsTransactionSourceTests` PASS, including:
- `GetTransactionsAsync_PaginationUrl_UsedVerbatim` (now asserts both no `access_token=` in any URL and `Authorization: Bearer test-token` on both requests).
- `GetTransactionsAsync_RateLimitRetry_SucceedsOnSecondAttempt` (confirms the fresh-`HttpRequestMessage`-per-attempt fix in Step 3 doesn't break retry).
- `GetTransactionsAsync_Pagination_AllPagesCollected`, `GetTransactionsAsync_ValidResponse_ParsesFieldsCorrectly`, `GetTransactionsAsync_Amount_ConvertedFromCentsToDecimal`, `GetTransactionsAsync_UsesAccountIdFromSettings_InRequestUrl`, `GetTransactionsAsync_InitialUrl_ContainsTimeRange` (all unaffected behaviorally, still green).

- [ ] **Step 6: Build the backend and run `dotnet format` per project validation gate**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln
```

Expected: build succeeds with no errors (confirms `using System.Net.Http.Headers;` resolves `AuthenticationHeaderValue` and no dangling `RedactToken` reference remains anywhere in the codebase); `dotnet format` reports no unformatted files (or applies formatting — re-run `dotnet build` afterward if it changes anything).

- [ ] **Step 7: Run the full backend test project to confirm no unrelated regressions**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: all tests pass (no test outside `MetaAdsTransactionSourceTests` references `RedactToken`, `BuildInitialUrl`, or `FetchPageAsync`, so no other test should be affected).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs
git commit -m "fix: send MetaAds access token via Authorization header instead of URL query param"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (send token via `Authorization: Bearer` header, for both initial and paginated requests, other query params preserved) — covered by `move-access-token-to-authorization-header` Steps 2–3 (removes `access_token` from `BuildInitialUrl`, keeps `fields`/`time_range`; adds header in the single `FetchPageAsync` call site that services both initial and paginated fetches per the arch review's Decision/finding), verified by `add-bearer-auth-regression-tests` Step 4's rewritten `GetTransactionsAsync_PaginationUrl_UsedVerbatim`.
- FR-2 (remove `RedactToken` and its call site, log full URL, no dangling references) — covered by `move-access-token-to-authorization-header` Steps 3–4, verified by Step 6's `dotnet build` (a dangling reference to a deleted private method would be a compile error).
- FR-3 (preserve pagination/error handling/resilience-pipeline behavior) — no changes to `GetTransactionsAsync`'s loop, `EnsureSuccessStatusCode()`, or the null-body `InvalidOperationException`; `_pipeline.ExecuteAsync` still wraps the HTTP call. Verified by the untouched existing tests (`GetTransactionsAsync_RateLimitRetry_SucceedsOnSecondAttempt`, `GetTransactionsAsync_Pagination_AllPagesCollected`, etc.) all passing in Step 5, plus the explicit fresh-`HttpRequestMessage`-per-retry-attempt note in Step 3 to avoid a retry regression.
- NFR-2 (token never in URL; not logged in plaintext) — the URL-absence assertion in the rewritten pagination test plus the debug log now logging only the (token-free) URL, never `request.Headers` or the header value itself.
- Out-of-scope items (token provisioning/refresh, other Meta integrations, telemetry scrubbing policy, MarketingInvoices domain logic, historical log cleanup) — plan touches none of these; `MetaAdsSettings`, DI registration, and `IMarketingTransactionSource` are left untouched per arch-review Decision 2.

**Placeholder scan:** No "TBD"/"TODO"/"handle appropriately" language; every step shows complete, concrete code (full before/after snippets) or an exact runnable command with a stated expected result.

**Type consistency:** `BuildInitialUrl(DateTime from, DateTime to) : string` and `FetchPageAsync(string url, CancellationToken ct) : Task<MetaTransactionsResponse>` signatures are unchanged and consistent between the arch review, design doc, and this plan. `_settings.AccessToken` (from `MetaAdsSettings`, unchanged) is the single source used both by the production `AuthenticationHeaderValue("Bearer", _settings.AccessToken)` and by the test fixtures' `AccessToken = "test-token"` / `AuthenticationHeaderValue("Bearer", "test-token")` assertions — consistent across both tasks. `CapturingSequentialHandler`'s new constructor parameter (`List<AuthenticationHeaderValue?> capturedAuthHeaders`) is used identically at both of its call sites after Steps 2–4 of the first task.
