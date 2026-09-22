using System.Net;
using System.Text;
using Anela.Heblo.Adapters.Ecomail;
using Anela.Heblo.Domain.Features.Ecomail;
using FluentAssertions;
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
    public async Task event_count_returns_zero_when_total_is_null()
    {
        // Ecomail answers an unknown event name with {"total":null} rather than an error.
        var (client, _) = CreateClient((HttpStatusCode.OK, """{"total":null}"""));

        var count = await client.GetPipelineEventCountAsync(
            1, "conversion", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        count.Should().Be(0);
    }

    [Fact]
    public async Task stats_returns_null_when_the_campaign_is_gone()
    {
        var (client, _) = CreateClient((HttpStatusCode.NotFound, """{"message":"Not Found!"}"""));

        var stats = await client.GetCampaignStatsAsync(999999);

        stats.Should().BeNull("a deleted campaign must not fail the whole sync");
    }
}
