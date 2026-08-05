using System.Net;
using System.Text;
using Anela.Heblo.Adapters.Logeto;
using Anela.Heblo.Domain.Features.Attendance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Logeto.Tests;

public class LogetoClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();

        public StubHandler(params HttpResponseMessage[] responses)
            => _responses = new Queue<HttpResponseMessage>(responses);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responses.Dequeue();
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static LogetoClient CreateClient(StubHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://acme.logeto.com") };
        var options = Options.Create(new LogetoOptions
        {
            AccountName = "acme",
            AccessKey = "test-key"
        });
        return new LogetoClient(httpClient, options, NullLogger<LogetoClient>.Instance);
    }

    [Fact]
    public async Task GetActivitiesAsync_SendsAccessKeyHeaderAndCorrectPath()
    {
        var handler = new StubHandler(Json("""{"ContinuationToken":null,"Items":[]}"""));
        var client = CreateClient(handler);

        await client.GetActivitiesAsync(CancellationToken.None);

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/v2/Activities");
        handler.Requests[0].Headers.GetValues("AccessKey").Should().ContainSingle()
            .Which.Should().Be("test-key");
    }

    [Fact]
    public async Task GetActivitiesAsync_DeserializesItems()
    {
        var guid = Guid.NewGuid();
        var handler = new StubHandler(Json($$"""
            {"ContinuationToken":null,"Items":[
              {"Guid":"{{guid}}","Name":"Oběd","Type":"Break","Inactive":false}
            ]}
            """));
        var client = CreateClient(handler);

        var activities = await client.GetActivitiesAsync(CancellationToken.None);

        activities.Should().HaveCount(1);
        activities[0].Guid.Should().Be(guid);
        activities[0].Name.Should().Be("Oběd");
        activities[0].Type.Should().Be(LogetoActivityTypes.Break);
    }

    [Fact]
    public async Task GetPeopleAsync_FollowsContinuationTokenAcrossPages()
    {
        var handler = new StubHandler(
            Json("""{"ContinuationToken":"page2","Items":[{"Guid":"11111111-1111-1111-1111-111111111111","Note":"integration","Inactive":false}]}"""),
            Json("""{"ContinuationToken":null,"Items":[{"Guid":"22222222-2222-2222-2222-222222222222","Note":null,"Inactive":false}]}"""));
        var client = CreateClient(handler);

        var people = await client.GetPeopleAsync(CancellationToken.None);

        people.Should().HaveCount(2);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].RequestUri!.Query.Should().Contain("ContinuationToken=page2");
    }

    [Fact]
    public async Task GetTimeTrackingAsync_PassesDateRangeAndRepeatsItOnNextPages()
    {
        var handler = new StubHandler(
            Json("""{"ContinuationToken":"t2","Items":[]}"""),
            Json("""{"ContinuationToken":null,"Items":[]}"""));
        var client = CreateClient(handler);

        await client.GetTimeTrackingAsync(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 4), CancellationToken.None);

        handler.Requests[0].RequestUri!.Query.Should().Contain("From=2026-08-01").And.Contain("To=2026-08-04");
        handler.Requests[1].RequestUri!.Query
            .Should().Contain("From=2026-08-01").And.Contain("To=2026-08-04").And.Contain("ContinuationToken=t2");
    }

    [Fact]
    public async Task CreateTimeEntryAsync_PostsMergeQueryAndPascalCaseBody()
    {
        var handler = new StubHandler(Json("""{"Guid":"33333333-3333-3333-3333-333333333333"}""", HttpStatusCode.Created));
        var client = CreateClient(handler);

        await client.CreateTimeEntryAsync(new LogetoCreateTimeEntryRequest
        {
            Person = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Activity = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Date = new DateOnly(2026, 8, 3),
            From = "2026-08-03T09:00:00Z",
            To = "2026-08-03T09:30:00Z",
            Billable = false,
            Description = "Automatická přestávka",
            ExternalKey = "autobreak-x-2026-08-03"
        }, merge: true, CancellationToken.None);

        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/v2/TimeTracking?merge=true");
        handler.RequestBodies[0].Should().Contain("\"Person\"").And.Contain("\"Billable\":false");
        handler.RequestBodies[0].Should().NotContain("\"Hours\"", "null members must be omitted");
    }

    [Fact]
    public async Task ErrorResponse_ThrowsLogetoApiExceptionWithApiMessage()
    {
        var handler = new StubHandler(Json(
            """{"Error":{"Code":"InvalidTime","Message":"Seconds must be zero"}}""",
            HttpStatusCode.BadRequest));
        var client = CreateClient(handler);

        var act = () => client.GetActivitiesAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<LogetoApiException>();
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.ApiErrorCode.Should().Be("InvalidTime");
        ex.Which.Message.Should().Contain("Seconds must be zero");
    }

    [Fact]
    public async Task RepeatedContinuationToken_StopsInsteadOfLoopingForever()
    {
        var handler = new StubHandler(
            Json("""{"ContinuationToken":"same","Items":[]}"""),
            Json("""{"ContinuationToken":"same","Items":[]}"""));
        var client = CreateClient(handler);

        var people = await client.GetPeopleAsync(CancellationToken.None);

        people.Should().BeEmpty();
        handler.Requests.Should().HaveCount(2);
    }
}
