using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.Ecomail;
using Anela.Heblo.Domain.Features.Ecomail;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Tests.Ecomail;

public class EcomailApiClientTests
{
    private const string CampaignsPage = """
    [
      {"id":264,"title":"Plet v lete_0726","subject":"Co slunce pleti bere","from_email":"info@newsletter.anela.cz",
       "sent_at":"2026-07-26 05:33:17","scheduled_at":"2026-07-26 07:30:00","recipients":12715,"status":3,
       "campaign_type":"ab","parent_id":null},
      {"id":266,"title":"Plet v lete_0726","subject":"Co slunce pleti bere","from_email":"info@newsletter.anela.cz",
       "sent_at":null,"scheduled_at":"2026-07-26 07:30:00","recipients":1271,"status":3,
       "campaign_type":"variation","parent_id":264}
    ]
    """;

    private const string CampaignStats = """
    {"stats":{"inject":12715,"delivery":12707,"delivery_rate":99.94,"open":3248,"total_open":4100,
      "open_rate":25.56,"click":49,"total_click":73,"click_rate":0.39,"bounce":8,"bounce_rate":0.06,
      "spam":0,"spam_rate":0,"unsub":33,"unsub_rate":0.26,"conversions":42,"conversions_value":72302}}
    """;

    private const string Pipelines = """
    [{"id":14720,"name":"Opusteny kosik_2025","list_id":1,
      "created_at":"2025-09-08 22:04:03","updated_at":"2026-04-03 20:04:04"}]
    """;

    private const string StatsDetail = """
    {"next_page_url":null,"total":403,"per_page":1,"subscribers":{"foo@bar.cz":{"open":2}}}
    """;

    private static (EcomailApiClient client, List<HttpRequestMessage> requests) CreateClient(
        params (HttpStatusCode status, string body)[] responses)
    {
        var requests = new List<HttpRequestMessage>();
        var queue = new Queue<(HttpStatusCode, string)>(responses);
        var handler = new Mock<HttpMessageHandler>();

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage request, CancellationToken _) =>
            {
                requests.Add(request);
                var (status, body) = queue.Count > 0 ? queue.Dequeue() : (HttpStatusCode.OK, "[]");
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler.Object) { BaseAddress = new Uri("https://api2.ecomailapp.cz") });

        var options = Options.Create(new EcomailOptions { ApiKey = "test-key" });
        return (new EcomailApiClient(options, factory.Object, NullLogger<EcomailApiClient>.Instance), requests);
    }

    [Fact]
    public async Task sends_the_api_key_in_the_key_header()
    {
        var (client, requests) = CreateClient((HttpStatusCode.OK, "[]"));

        await client.GetPipelinesAsync();

        requests.Single().Headers.GetValues("key").Should().ContainSingle().Which.Should().Be("test-key");
    }

    [Fact]
    public async Task requests_campaigns_with_per_page_50_because_51_is_rejected()
    {
        var (client, requests) = CreateClient((HttpStatusCode.OK, "[]"));

        await client.GetCampaignsAsync();

        requests[0].RequestUri!.Query.Should().Contain("per_page=50");
    }

    [Fact]
    public async Task pages_campaigns_until_a_short_page_arrives()
    {
        var fullPage = "[" + string.Join(",", Enumerable.Range(1, 50).Select(i =>
            $$"""{"id":{{i}},"title":"t","subject":"s","status":3,"campaign_type":"email","recipients":1}""")) + "]";
        var (client, requests) = CreateClient(
            (HttpStatusCode.OK, fullPage),
            (HttpStatusCode.OK, CampaignsPage));   // 2 items — short, so paging stops

        var campaigns = await client.GetCampaignsAsync();

        campaigns.Should().HaveCount(52);
        requests.Should().HaveCount(2);
        requests[1].RequestUri!.Query.Should().Contain("page=2");
    }

    [Fact]
    public async Task campaign_paging_stops_at_the_page_cap_and_logs_a_warning()
    {
        // Every page is full (never short), so without the cap this would page forever.
        var fullPage = "[" + string.Join(",", Enumerable.Range(1, 50).Select(i =>
            $$"""{"id":{{i}},"title":"t","subject":"s","status":3,"campaign_type":"email","recipients":1}""")) + "]";

        var requests = new List<HttpRequestMessage>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage request, CancellationToken _) =>
            {
                requests.Add(request);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(fullPage, Encoding.UTF8, "application/json")
                });
            });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler.Object) { BaseAddress = new Uri("https://api2.ecomailapp.cz") });

        var mockLogger = new Mock<ILogger<EcomailApiClient>>();
        var options = Options.Create(new EcomailOptions { ApiKey = "test-key" });
        var client = new EcomailApiClient(options, factory.Object, mockLogger.Object);

        var campaigns = await client.GetCampaignsAsync();

        requests.Should().HaveCount(200, "paging must stop at the cap even though every page was full");
        campaigns.Should().HaveCount(200 * 50);
        mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("200")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "a runaway paging loop against a live account with no sandbox must be logged, not silently stopped");
    }

    [Fact]
    public async Task parses_campaign_type_parent_id_and_null_sent_at()
    {
        var (client, _) = CreateClient((HttpStatusCode.OK, CampaignsPage));

        var campaigns = await client.GetCampaignsAsync();

        var parent = campaigns.Single(c => c.Id == 264);
        parent.CampaignType.Should().Be("ab");
        parent.ParentId.Should().BeNull();
        parent.SentAt.Should().Be(new DateTime(2026, 7, 26, 5, 33, 17));
        parent.Recipients.Should().Be(12715);

        var variation = campaigns.Single(c => c.Id == 266);
        variation.CampaignType.Should().Be("variation");
        variation.ParentId.Should().Be(264);
        variation.SentAt.Should().BeNull("variations never carry sent_at");
    }

    [Fact]
    public async Task parses_stats_including_conversions_value()
    {
        var (client, _) = CreateClient((HttpStatusCode.OK, CampaignStats));

        var stats = await client.GetCampaignStatsAsync(264);

        stats.Should().NotBeNull();
        stats!.Inject.Should().Be(12715);
        stats.Open.Should().Be(3248);
        stats.Unsub.Should().Be(33);
        stats.Conversions.Should().Be(42);
        stats.ConversionsValue.Should().Be(72302m);
    }

    [Fact]
    public async Task parses_pipelines()
    {
        var (client, _) = CreateClient((HttpStatusCode.OK, Pipelines));

        var pipelines = await client.GetPipelinesAsync();

        pipelines.Should().ContainSingle();
        pipelines[0].Id.Should().Be(14720);
        pipelines[0].Name.Should().Be("Opusteny kosik_2025");
        pipelines[0].ListId.Should().Be(1);
    }

    [Fact]
    public async Task event_count_reads_total_and_requests_a_single_row()
    {
        var (client, requests) = CreateClient((HttpStatusCode.OK, StatsDetail));

        var count = await client.GetPipelineEventCountAsync(
            31762, "open", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        count.Should().Be(403);

        var query = requests.Single().RequestUri!.Query;
        query.Should().Contain("event=open");
        query.Should().Contain("from_date=2026-08-01");
        query.Should().Contain("to_date=2026-08-31");
        query.Should().Contain("per_page=1", "only .total is read, so never pull subscriber rows");
    }

    [Fact]
    public async Task event_count_returns_null_when_total_is_null()
    {
        // Ecomail answers an event it does not support with {"total":null} rather than an error
        // (CLUSTER-B-FINDINGS.md 8.5). Reading that as 0 would be indistinguishable from "nobody
        // did this", and the sync service locks a month on the strength of a zero.
        var (client, _) = CreateClient((HttpStatusCode.OK, """{"total":null}"""));

        var count = await client.GetPipelineEventCountAsync(
            1, "conversion", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        count.Should().BeNull("an unsupported event is unknown, not zero");
    }

    [Fact]
    public async Task a_not_found_on_the_campaign_listing_throws_instead_of_truncating_the_list()
    {
        // A 404 on a collection endpoint means the endpoint moved or lost scope. Swallowed, it
        // would look like an empty page, be read as the last page, and silently truncate the
        // campaign list into a green run.
        var (client, _) = CreateClient((HttpStatusCode.NotFound, """{"message":"Not Found!"}"""));

        var act = () => client.GetCampaignsAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task a_not_found_on_the_pipeline_listing_throws_instead_of_returning_empty()
    {
        // Swallowed, this would hand the sync service an empty pipeline list with no exception,
        // bypassing its fall-back to known ids and silently ending snapshot collection forever.
        var (client, _) = CreateClient((HttpStatusCode.NotFound, """{"message":"Not Found!"}"""));

        var act = () => client.GetPipelinesAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task a_transient_server_error_is_retried_rather_than_losing_the_item_for_the_run()
    {
        // Without this, one 502 from a proxy during a ~400-call backfill permanently drops that
        // month for the whole 6-hour cycle.
        var (client, requests) = CreateClient(
            (HttpStatusCode.BadGateway, "{}"),
            (HttpStatusCode.OK, StatsDetail));

        var count = await client.GetPipelineEventCountAsync(
            31762, "open", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        count.Should().Be(403);
        requests.Should().HaveCount(2, "the 502 should have been retried, not surfaced");
    }

    [Fact]
    public async Task stats_returns_null_when_the_campaign_is_gone()
    {
        var (client, _) = CreateClient((HttpStatusCode.NotFound, """{"message":"Not Found!"}"""));

        var stats = await client.GetCampaignStatsAsync(999999);

        stats.Should().BeNull("a deleted campaign must not fail the whole sync");
    }

    [Fact]
    public async Task a_forbidden_response_throws_instead_of_being_swallowed_as_empty()
    {
        // Unlike 404 (a deleted resource), 403 means a permission/scope problem. This API key is
        // already known to 403 on some endpoints — silently returning null here would let a scope
        // change turn into a green, empty run forever.
        var (client, _) = CreateClient((HttpStatusCode.Forbidden, """{"message":"Forbidden"}"""));

        var act = () => client.GetPipelinesAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task honours_ecomails_retry_after_header_instead_of_the_fixed_backoff()
    {
        // The pipeline's fixed exponential backoff starts at 2s; Retry-After here is far
        // shorter. If the client ignored the header (the bug this test guards against), the
        // call would take >= 2s because Polly would fall back to the fixed exponential delay.
        var (client, requests) = CreateClientWithRetryAfter(TimeSpan.FromMilliseconds(100));

        var stopwatch = Stopwatch.StartNew();
        var pipelines = await client.GetPipelinesAsync();
        stopwatch.Stop();

        pipelines.Should().NotBeNull();
        requests.Should().HaveCount(2, "the first call was throttled and the retry succeeded");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            "a Retry-After-derived delay should be used instead of the ~2s fixed exponential backoff");
    }

    private static (EcomailApiClient client, List<HttpRequestMessage> requests) CreateClientWithRetryAfter(
        TimeSpan retryAfterHeaderValue)
    {
        var requests = new List<HttpRequestMessage>();
        var callCount = 0;
        var handler = new Mock<HttpMessageHandler>();

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage request, CancellationToken _) =>
            {
                requests.Add(request);
                callCount++;

                if (callCount == 1)
                {
                    var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                    throttled.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfterHeaderValue);
                    return Task.FromResult(throttled);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
            });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler.Object) { BaseAddress = new Uri("https://api2.ecomailapp.cz") });

        var options = Options.Create(new EcomailOptions { ApiKey = "test-key" });
        return (new EcomailApiClient(options, factory.Object, NullLogger<EcomailApiClient>.Instance), requests);
    }
}

public class EcomailNullableDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new EcomailNullableDateTimeConverter() },
    };

    [Fact]
    public void returns_null_for_a_json_null_token()
    {
        JsonSerializer.Deserialize<DateTime?>("null", Options).Should().BeNull();
    }

    [Fact]
    public void parses_ecomails_space_separated_format()
    {
        var result = JsonSerializer.Deserialize<DateTime?>("\"2026-07-26 05:33:17\"", Options);

        result.Should().Be(new DateTime(2026, 7, 26, 5, 33, 17));
        result!.Value.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    [Fact]
    public void falls_back_to_parsing_an_iso_8601_form()
    {
        var result = JsonSerializer.Deserialize<DateTime?>("\"2026-07-26T05:33:17\"", Options);

        result.Should().Be(new DateTime(2026, 7, 26, 5, 33, 17));
        result!.Value.Kind.Should().Be(DateTimeKind.Unspecified, "these columns are timestamp without time zone");
    }

    [Fact]
    public void throws_a_json_exception_for_unparseable_input_instead_of_silently_returning_null()
    {
        var act = () => JsonSerializer.Deserialize<DateTime?>("\"not-a-timestamp\"", Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void normalises_a_utc_designated_iso_8601_form_to_unspecified_kind()
    {
        // Bare DateTime.TryParse turns a "Z"-suffixed string into Kind=Utc on its own — this
        // case only passes because of the explicit SpecifyKind normalisation in tier 3.
        // These columns are `timestamp without time zone`; a Kind=Utc value reaching one
        // throws at runtime (see the FixMarketingPerformanceTimestampKind migration).
        var result = JsonSerializer.Deserialize<DateTime?>("\"2026-07-26T05:33:17Z\"", Options);

        result!.Value.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    [Fact]
    public void normalises_an_offset_iso_8601_form_to_unspecified_kind()
    {
        // Bare DateTime.TryParse turns an offset-suffixed string into Kind=Local on its own —
        // this case only passes because of the explicit SpecifyKind normalisation in tier 3.
        var result = JsonSerializer.Deserialize<DateTime?>("\"2026-07-26T05:33:17+02:00\"", Options);

        result!.Value.Kind.Should().Be(DateTimeKind.Unspecified);
    }
}
