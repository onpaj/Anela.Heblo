using System.Net;
using System.Text;
using Anela.Heblo.Adapters.MetaAds;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.MetaAds;

public class MetaAdsTransactionSourceTests
{
    private static MetaAdsTransactionSource CreateSource(
        HttpMessageHandler handler,
        MetaAdsSettings? settings = null)
    {
        settings ??= new MetaAdsSettings
        {
            AccountId = "act_123456789",
            AccessToken = "test-token",
            ApiVersion = "v21.0"
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.facebook.com/")
        };

        return new MetaAdsTransactionSource(
            httpClient,
            Options.Create(settings),
            NullLogger<MetaAdsTransactionSource>.Instance);
    }

    [Fact]
    public async Task GetTransactionsAsync_ValidResponse_ParsesFieldsCorrectly()
    {
        // Arrange
        var json = """
            {
              "data": [
                {
                  "id": "TX-001",
                  "time": 1744300800,
                  "amount": 150000,
                  "currency": "CZK",
                  "payment_type": "THRESHOLD"
                }
              ],
              "paging": {
                "cursors": { "before": "abc", "after": "def" }
              }
            }
            """;

        var handler = new StaticResponseHandler(HttpStatusCode.OK, json);
        var source = CreateSource(handler);

        var from = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(-1);
        var to = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(1);

        // Act
        var transactions = await source.GetTransactionsAsync(from, to, CancellationToken.None);

        // Assert
        transactions.Should().HaveCount(1);
        var tx = transactions[0];
        tx.TransactionId.Should().Be("TX-001");
        tx.Amount.Should().Be(1500.00m);
        tx.Currency.Should().Be("CZK");
        tx.Description.Should().Be("THRESHOLD");
        tx.TransactionDate.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime);
    }

    [Fact]
    public async Task GetTransactionsAsync_Amount_ConvertedFromCentsToDecimal()
    {
        // Arrange
        var json = """
            {
              "data": [
                {
                  "id": "TX-002",
                  "time": 1744300800,
                  "amount": 150000,
                  "currency": "CZK",
                  "payment_type": "THRESHOLD"
                }
              ],
              "paging": {}
            }
            """;

        var handler = new StaticResponseHandler(HttpStatusCode.OK, json);
        var source = CreateSource(handler);

        var from = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(-1);
        var to = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(1);

        // Act
        var transactions = await source.GetTransactionsAsync(from, to, CancellationToken.None);

        // Assert
        transactions.Should().HaveCount(1);
        transactions[0].Amount.Should().Be(1500.00m);
    }

    [Fact]
    public async Task GetTransactionsAsync_Pagination_AllPagesCollected()
    {
        // Arrange — page 1 has paging.next, page 2 has none
        var page1Json = """
            {
              "data": [
                { "id": "TX-001", "time": 1744300800, "amount": 10000, "currency": "CZK", "payment_type": "THRESHOLD" }
              ],
              "paging": {
                "next": "https://graph.facebook.com/v21.0/act_123456789/transactions?after=cursor1&access_token=test-token"
              }
            }
            """;

        var page2Json = """
            {
              "data": [
                { "id": "TX-002", "time": 1744300800, "amount": 20000, "currency": "CZK", "payment_type": "THRESHOLD" }
              ],
              "paging": {
                "cursors": { "before": "cursor1", "after": "cursor2" }
              }
            }
            """;

        var handler = new SequentialResponseHandler(
            (HttpStatusCode.OK, page1Json),
            (HttpStatusCode.OK, page2Json));

        var source = CreateSource(handler);

        var from = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(-1);
        var to = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(1);

        // Act
        var transactions = await source.GetTransactionsAsync(from, to, CancellationToken.None);

        // Assert
        transactions.Should().HaveCount(2);
        transactions.Select(t => t.TransactionId).Should().Contain(["TX-001", "TX-002"]);
    }

    /// <summary>
    /// Regression test for #726: verifies that the account ID from MetaAdsSettings.AccountId
    /// is correctly used in the request URL. Before the fix, the property was named AdAccountId
    /// which did not match the configuration key "AccountId", causing the value to be null and
    /// producing malformed URLs → HTTP 400 from Facebook Graph API.
    /// </summary>
    [Fact]
    public async Task GetTransactionsAsync_UsesAccountIdFromSettings_InRequestUrl()
    {
        // Arrange
        const string expectedAccountId = "act_987654321";
        var capturedUrl = (string?)null;

        var json = """
            {
              "data": [],
              "paging": {}
            }
            """;

        var capturingHandler = new CapturingHandler(HttpStatusCode.OK, json, url => capturedUrl = url);

        var settings = new MetaAdsSettings
        {
            AccountId = expectedAccountId,
            AccessToken = "test-token",
            ApiVersion = "v21.0"
        };

        var httpClient = new HttpClient(capturingHandler)
        {
            BaseAddress = new Uri("https://graph.facebook.com/")
        };

        var source = new MetaAdsTransactionSource(
            httpClient,
            Options.Create(settings),
            NullLogger<MetaAdsTransactionSource>.Instance);

        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddDays(1);

        // Act
        await source.GetTransactionsAsync(from, to, CancellationToken.None);

        // Assert — URL must contain the AccountId value, not be empty/null
        capturedUrl.Should().NotBeNull();
        capturedUrl.Should().Contain(expectedAccountId,
            because: "MetaAdsSettings.AccountId must be bound from configuration and used in the Facebook Graph API URL");
    }

    [Fact]
    public async Task GetTransactionsAsync_RateLimitRetry_SucceedsOnSecondAttempt()
    {
        // Arrange — first call returns 429, second returns 200 with a transaction
        var successJson = """
            {
              "data": [
                { "id": "TX-001", "time": 1744300800, "amount": 10000, "currency": "CZK", "payment_type": "THRESHOLD" }
              ],
              "paging": {}
            }
            """;

        var handler = new SequentialResponseHandler(
            (HttpStatusCode.TooManyRequests, "{}"),
            (HttpStatusCode.OK, successJson));

        // Build source with a fast-retry pipeline (no delay) for test speed
        var settings = new MetaAdsSettings
        {
            AccountId = "act_123456789",
            AccessToken = "test-token",
            ApiVersion = "v21.0"
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.facebook.com/")
        };

        var testPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>(ex => ex.StatusCode == HttpStatusCode.TooManyRequests),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
            })
            .Build();

        var source = new MetaAdsTransactionSource(
            httpClient,
            Options.Create(settings),
            NullLogger<MetaAdsTransactionSource>.Instance,
            testPipeline);

        var from = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(-1);
        var to = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(1);

        // Act
        var transactions = await source.GetTransactionsAsync(from, to, CancellationToken.None);

        // Assert — should succeed after retry, not throw
        transactions.Should().HaveCount(1);
        transactions[0].TransactionId.Should().Be("TX-001");
    }

    [Fact]
    public async Task GetTransactionsAsync_InitialUrl_ContainsTimeRange()
    {
        // Arrange
        var capturedUrls = new List<string>();
        var responseBody = """{"data":[],"paging":{"cursors":{"before":"","after":""}}}""";
        var handler = new CapturingSequentialHandler(
            capturedUrls,
            (HttpStatusCode.OK, responseBody));

        var source = CreateSource(handler);

        var from = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2024, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        // Act
        await source.GetTransactionsAsync(from, to, CancellationToken.None);

        // Assert
        capturedUrls.Should().HaveCount(1);
        capturedUrls[0].Should().Contain("""time_range={"since":"2024-03-01","until":"2024-03-31"}""");
    }

    [Fact]
    public async Task GetTransactionsAsync_PaginationUrl_UsedVerbatim()
    {
        // Arrange
        var capturedUrls = new List<string>();
        var firstPageBody = """{"data":[{"id":"1","time":1709251200,"amount":100,"currency":"CZK","payment_type":"card"}],"paging":{"next":"https://graph.facebook.com/next-page?cursor=abc123"}}""";
        var secondPageBody = """{"data":[],"paging":{"cursors":{"before":"","after":""}}}""";
        var handler = new CapturingSequentialHandler(
            capturedUrls,
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
    }
}

/// <summary>Always returns the same response.</summary>
file sealed class StaticResponseHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

/// <summary>Returns a fixed response and captures the request URL for assertion.</summary>
file sealed class CapturingHandler(HttpStatusCode statusCode, string body, Action<string> onRequest) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        onRequest(request.RequestUri?.ToString() ?? string.Empty);
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

/// <summary>Returns responses in sequence; repeats the last one if exhausted.</summary>
file sealed class SequentialResponseHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode, string)> _responses;

    public SequentialResponseHandler(params (HttpStatusCode, string)[] responses)
    {
        _responses = new Queue<(HttpStatusCode, string)>(responses);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Dequeue until the last entry; keep the last for any additional calls.
        var (status, body) = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

/// <summary>Captures every request URL and returns responses in sequence; repeats the last on exhaustion.</summary>
file sealed class CapturingSequentialHandler : HttpMessageHandler
{
    private readonly List<string> _capturedUrls;
    private readonly Queue<(HttpStatusCode, string)> _responses;

    public CapturingSequentialHandler(List<string> capturedUrls, params (HttpStatusCode, string)[] responses)
    {
        _capturedUrls = capturedUrls;
        _responses = new Queue<(HttpStatusCode, string)>(responses);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _capturedUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
        var (status, body) = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
