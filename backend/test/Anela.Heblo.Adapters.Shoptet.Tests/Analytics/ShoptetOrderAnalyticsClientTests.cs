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
    public async Task An_http_timeout_is_retried_even_though_it_throws_TaskCanceledException()
    {
        // Arrange — HttpClient reports its own timeout as TaskCanceledException, which IS an
        // OperationCanceledException. Telling them apart by exception type would leave every
        // per-request timeout of a seven-hour backfill unretried, so the filter reads the
        // caller's token instead. The token here is live: this is a timeout, not a cancellation.
        var (client, handler) = CreateClient(
            _ => throw new TaskCanceledException("The request was canceled due to a timeout."),
            _ => Json(ShoptetOrderTestData.SimpleOrderJson));

        // Act
        var order = await client.GetOrderAsync("126020373", CancellationToken.None);

        // Assert
        order.Should().NotBeNull();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_retried()
    {
        // Arrange — the same exception type as the timeout above, but with a token that is already
        // cancelled by the time the failure is classified.
        using var cts = new CancellationTokenSource();
        var (client, handler) = CreateClient(_ =>
        {
            cts.Cancel();
            throw new TaskCanceledException("cancelled");
        });

        // Act
        var act = () => client.GetOrderAsync("126020373", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().HaveCount(1, "a cancelled call must not be retried");
    }

    [Fact]
    public async Task A_404_on_a_list_endpoint_throws_rather_than_reading_as_an_empty_window()
    {
        // Arrange — 404 is benign only on the detail endpoint. On a listing it must not degrade to
        // "this window held no orders": the backfill would persist an advanced cursor over a month
        // it never actually read, and nothing revisits it.
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        // Act
        var act = () => client.ListCodesByCreationTimeAsync(
            DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow, page: 1);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task A_404_on_the_change_log_throws_rather_than_reading_as_no_changes()
    {
        // Arrange — worse than the backfill case: an empty change log looks like a clean night, so
        // the watermark advances and a whole day of edits and deletions is lost for ever.
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        // Act
        var act = () => client.ListChangesAsync(
            DateTimeOffset.UtcNow.AddDays(-1), page: 1, itemsPerPage: 1000);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task An_unrecognised_list_envelope_throws_rather_than_reading_as_empty()
    {
        // Arrange — a 200 whose body is not the data-wrapped shape used to deserialise to null and
        // then be swallowed into an empty page.
        var (client, _) = CreateClient(_ => Json("{\"unexpected\":true}"));

        // Act
        var act = () => client.ListCodesByCreationTimeAsync(
            DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow, page: 1);

        // Assert
        await act.Should().ThrowAsync<ShoptetOrderSyncException>();
    }

    [Fact]
    public async Task A_404_on_the_detail_endpoint_is_still_treated_as_a_deleted_order()
    {
        // Arrange — the one place a 404 is expected: the order went away between the listing and
        // this call, which is normal on a multi-hour backfill.
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        // Act
        var order = await client.GetOrderAsync("126020373");

        // Assert
        order.Should().BeNull();
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
