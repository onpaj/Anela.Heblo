using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Anela.Heblo.Application.Common.Graph;
using Anela.Heblo.Application.Features.MeetingTasks;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class GraphPlannerServiceTests
{
    private static (GraphPlannerService Service, RecordingHandler Handler) CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string planId = "plan-123",
        string? bucketId = null)
    {
        var tokenAcquisition = new Mock<ITokenAcquisition>();
        tokenAcquisition
            .Setup(t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null))
            .ReturnsAsync("fake-token");

        var recordingHandler = new RecordingHandler(handler);
        var httpClient = new HttpClient(recordingHandler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("MicrosoftGraph")).Returns(httpClient);

        var options = Options.Create(new MeetingTasksOptions
        {
            PlannerPlanId = planId,
            PlannerBucketId = bucketId
        });

        var service = new GraphPlannerService(
            tokenAcquisition.Object,
            factory.Object,
            options,
            NullLogger<GraphPlannerService>.Instance);

        return (service, recordingHandler);
    }

    // ─── ResolveUserIdByEmailAsync ────────────────────────────────────────────

    [Fact]
    public async Task ResolveUserIdByEmailAsync_SingleMatch_ReturnsUserId()
    {
        var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"value":[{"id":"user-123","displayName":"Ondra Pajgrt"}]}""",
                Encoding.UTF8, "application/json")
        });

        var result = await service.ResolveUserIdByEmailAsync("ondra@anela.cz");

        result.Should().Be("user-123");
    }

    [Fact]
    public async Task ResolveUserIdByEmailAsync_NoMatch_ReturnsNull()
    {
        var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"value":[]}""", Encoding.UTF8, "application/json")
        });

        var result = await service.ResolveUserIdByEmailAsync("nobody@anela.cz");

        result.Should().BeNull();
    }

    // ─── ExportTaskAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExportTaskAsync_Basic_PostsPlannerTaskAndReturnsId()
    {
        var (service, handler) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"planner-task-1"}""", Encoding.UTF8, "application/json")
        });

        var result = await service.ExportTaskAsync("user-abc", "Write spec", "", null);

        result.Success.Should().BeTrue();
        result.ExternalTaskId.Should().Be("planner-task-1");
        result.Error.Should().BeNull();

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.AbsoluteUri.Should()
            .Be("https://graph.microsoft.com/v1.0/planner/tasks");

        handler.RequestBodies[0].Should().Contain("\"planId\":\"plan-123\"");
        handler.RequestBodies[0].Should().Contain("\"title\":\"Write spec\"");
        handler.RequestBodies[0].Should().Contain("\"user-abc\"");
        handler.RequestBodies[0].Should().Contain("\"#microsoft.graph.plannerAssignment\"");
        handler.RequestBodies[0].Should().Contain("\" !\"");
    }

    [Fact]
    public async Task ExportTaskAsync_WithDescription_PatchesDetailsAfterCreate()
    {
        var calls = new Queue<HttpResponseMessage>();

        // 1. POST /planner/tasks
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"t1"}""", Encoding.UTF8, "application/json")
        });

        // 2. GET /planner/tasks/t1/details
        var detailsGet = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"description":""}""", Encoding.UTF8, "application/json")
        };
        detailsGet.Headers.ETag = new EntityTagHeaderValue("\"etag-abc\"");
        calls.Enqueue(detailsGet);

        // 3. PATCH /planner/tasks/t1/details
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

        var (service, handler) = CreateService(_ => calls.Dequeue());

        var result = await service.ExportTaskAsync("user-abc", "Write spec", "Some description", null);

        result.Success.Should().BeTrue();
        result.ExternalTaskId.Should().Be("t1");

        handler.Requests.Should().HaveCount(3);

        // GET details
        handler.Requests[1].Method.Should().Be(HttpMethod.Get);
        handler.Requests[1].RequestUri!.AbsoluteUri.Should()
            .Be("https://graph.microsoft.com/v1.0/planner/tasks/t1/details");

        // PATCH details
        handler.Requests[2].Method.Should().Be(HttpMethod.Patch);
        handler.Requests[2].RequestUri!.AbsoluteUri.Should()
            .Be("https://graph.microsoft.com/v1.0/planner/tasks/t1/details");
        handler.Requests[2].Headers.TryGetValues("If-Match", out var ifMatchValues).Should().BeTrue();
        ifMatchValues!.Should().ContainSingle().Which.Should().Be("\"etag-abc\"");
        handler.RequestBodies[2].Should().Contain("\"description\":\"Some description\"");
    }

    [Fact]
    public async Task ExportTaskAsync_EmptyDescription_SkipsDetailsPatch()
    {
        var (service, handler) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"t1"}""", Encoding.UTF8, "application/json")
        });

        var result = await service.ExportTaskAsync("user-abc", "Title", "", null);

        result.Success.Should().BeTrue();
        handler.Requests.Should().HaveCount(1, "empty description must not trigger GET/PATCH details");
    }

    [Fact]
    public async Task ExportTaskAsync_WithBucketId_IncludesBucketIdInPostBody()
    {
        var (service, handler) = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":"t1"}""", Encoding.UTF8, "application/json")
            },
            bucketId: "bucket-99");

        await service.ExportTaskAsync("user-abc", "Title", "", null);

        handler.RequestBodies[0].Should().Contain("\"bucketId\":\"bucket-99\"");
    }

    [Fact]
    public async Task ExportTaskAsync_NoBucketId_OmitsBucketIdFromPostBody()
    {
        var (service, handler) = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":"t1"}""", Encoding.UTF8, "application/json")
            });

        await service.ExportTaskAsync("user-abc", "Title", "", null);

        handler.RequestBodies[0].Should().NotContain("bucketId");
    }

    [Fact]
    public async Task ExportTaskAsync_WithDueDate_IncludesDueDateAsIso8601String()
    {
        var (service, handler) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"t1"}""", Encoding.UTF8, "application/json")
        });

        var due = new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc);
        await service.ExportTaskAsync("user-abc", "Title", "", due);

        var expected = due.ToString("o");
        handler.RequestBodies[0].Should().Contain(expected,
            "Planner dueDateTime is a plain ISO-8601 string, not a To Do date-time object");
        // Must NOT use the To Do object form
        handler.RequestBodies[0].Should().NotContain("\"timeZone\"");
    }

    [Fact]
    public async Task ExportTaskAsync_PlannerPostReturns401_ErrorContainsStatusCodeAndBody()
    {
        var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """{"error":{"code":"InvalidAuthenticationToken","message":"Access token is invalid."}}""",
                Encoding.UTF8, "application/json")
        });

        var result = await service.ExportTaskAsync("user-abc", "Title", "", null);

        result.Success.Should().BeFalse();
        result.ExternalTaskId.Should().BeNull();
        result.Error.Should().Contain("401");
        result.Error.Should().Contain("InvalidAuthenticationToken");
    }

    [Fact]
    public async Task ExportTaskAsync_TransportException_ReturnsFailureWithMessage()
    {
        var (service, _) = CreateService(_ => throw new HttpRequestException("network down"));

        var result = await service.ExportTaskAsync("user-abc", "Title", "", null);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("network down");
    }

    // ─── Recording infrastructure ─────────────────────────────────────────────

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> RequestBodies { get; } = new();

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responder(request);
        }
    }
}
