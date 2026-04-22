using System.Net;
using System.Text;
using Anela.Heblo.Adapters.MetaAds;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
            AdAccountId = "act_123456789",
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
        tx.Platform.Should().Be("MetaAds");
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
            AdAccountId = "act_123456789",
            AccessToken = "test-token",
            ApiVersion = "v21.0"
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.facebook.com/")
        };

        var source = new MetaAdsTransactionSource(
            httpClient,
            Options.Create(settings),
            NullLogger<MetaAdsTransactionSource>.Instance,
            MetaAdsTransactionSource.BuildTestPipeline());

        var from = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(-1);
        var to = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(1);

        // Act
        var transactions = await source.GetTransactionsAsync(from, to, CancellationToken.None);

        // Assert — should succeed after retry, not throw
        transactions.Should().HaveCount(1);
        transactions[0].TransactionId.Should().Be("TX-001");
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
