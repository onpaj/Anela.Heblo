using System.Net;
using Anela.Heblo.Adapters.ShoptetApi.Analytics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Analytics;

public class ShoptetOrderAnalyticsClientTests
{
    private static (ShoptetOrderAnalyticsClient Client, RecordingHandler Handler) CreateClient(
        params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
    {
        var handler = new RecordingHandler(responses);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.myshoptet.com") };
        var options = Options.Create(new ShoptetOrdersSyncOptions { MaxRetryAttempts = 3 });

        return (new ShoptetOrderAnalyticsClient(
            http,
            new ShoptetApiThrottle(requestsPerSecond: 1000),
            options,
            NullLogger<ShoptetOrderAnalyticsClient>.Instance), handler);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Fact]
    public async Task Creation_time_filters_are_sent_as_ISO_8601_with_an_explicit_offset()
    {
        // Arrange — Shoptet returns 400 for "2026-09-21" or "2026-09-21T00:00:00"; it needs the
        // offset, and the "+" has to survive URL encoding.
        var (client, handler) = CreateClient(_ => Json("""{"data":{"orders":[],"paginator":{}}}"""));

        // Act
        await client.ListCodesByCreationTimeAsync(
            new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(2)),
            page: 1);

        // Assert
        var url = handler.Requests.Single().RequestUri!.ToString();
        url.Should().Contain("creationTimeFrom=2026-09-21T00%3A00%3A00%2B02%3A00");
        url.Should().Contain("creationTimeTo=2026-10-01T00%3A00%3A00%2B02%3A00");
        url.Should().Contain("itemsPerPage=50", "Shoptet caps /api/orders at 50 per page");
    }

    [Fact]
    public async Task A_deleted_order_answers_404_and_maps_to_null_rather_than_throwing()
    {
        // Arrange — an order can be deleted between the listing and the detail call.
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        // Act
        var (order, raw) = await client.GetOrderWithRawAsync("126020373");

        // Assert
        order.Should().BeNull();
        raw.Should().Be("{}");
    }

    [Fact]
    public async Task A_429_is_retried_after_a_back_off()
    {
        // Arrange
        var (client, handler) = CreateClient(
            _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            _ => Json(ShoptetOrderTestData.SimpleOrderJson));

        // Act
        var order = await client.GetOrderAsync("126020373");

        // Assert
        order.Should().NotBeNull();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_dropped_connection_is_retried()
    {
        // Arrange — a seven-hour backfill will hit these.
        var (client, handler) = CreateClient(
            _ => throw new HttpRequestException("connection reset"),
            _ => Json(ShoptetOrderTestData.SimpleOrderJson));

        // Act
        var order = await client.GetOrderAsync("126020373");

        // Assert
        order.Should().NotBeNull();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_retried()
    {
        // Arrange — an HttpClient timeout surfaces as TaskCanceledException, which is also what
        // real cancellation throws; only the caller's token tells them apart.
        var (client, handler) = CreateClient(_ => throw new TaskCanceledException("cancelled"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => client.GetOrderAsync("126020373", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task The_raw_response_body_is_returned_verbatim_for_raw_payload()
    {
        // Arrange
        var (client, _) = CreateClient(_ => Json(ShoptetOrderTestData.SimpleOrderJson));

        // Act
        var (_, raw) = await client.GetOrderWithRawAsync("126020373");

        // Assert
        raw.Should().Be(ShoptetOrderTestData.SimpleOrderJson);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;

        public List<HttpRequestMessage> Requests { get; } = new();

        public RecordingHandler(IEnumerable<Func<HttpRequestMessage, HttpResponseMessage>> responses)
            => _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var next = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return Task.FromResult(next(request));
        }
    }
}
