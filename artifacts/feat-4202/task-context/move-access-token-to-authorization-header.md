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
