using System.Net;
using System.Text;
using Anela.Heblo.Application.Features.MeetingTasks;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class GraphTodoServiceTests
{
    private static (GraphTodoService Service, RecordingHandler Handler) CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string todoListName = "Meeting Actions")
    {
        var tokenAcquisition = new Mock<ITokenAcquisition>();
        tokenAcquisition
            .Setup(t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null))
            .ReturnsAsync("fake-token");

        var recordingHandler = new RecordingHandler(handler);
        var httpClient = new HttpClient(recordingHandler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("MicrosoftGraph")).Returns(httpClient);

        var options = Options.Create(new MeetingTasksOptions { TodoListName = todoListName });

        var service = new GraphTodoService(
            tokenAcquisition.Object,
            factory.Object,
            options,
            NullLogger<GraphTodoService>.Instance);

        return (service, recordingHandler);
    }

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

    [Fact]
    public async Task ResolveUserIdByEmailAsync_MultipleMatches_ReturnsFirst()
    {
        var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"value":[{"id":"a","displayName":"John"},{"id":"b","displayName":"John"}]}""",
                Encoding.UTF8, "application/json")
        });

        var result = await service.ResolveUserIdByEmailAsync("john@anela.cz");

        result.Should().Be("a");
    }

    [Fact]
    public async Task ResolveUserIdByEmailAsync_HttpFailure_ReturnsNull()
    {
        var (service, _) = CreateService(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await service.ResolveUserIdByEmailAsync("anyone@anela.cz");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveUserIdByEmailAsync_ExceptionInTransport_ReturnsNull()
    {
        var (service, _) = CreateService(_ => throw new HttpRequestException("boom"));

        var result = await service.ResolveUserIdByEmailAsync("anyone@anela.cz");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveUserIdByEmailAsync_EmailWithSingleQuote_DoublesQuoteBeforeEscape()
    {
        string? capturedUrl = null;
        var (service, _) = CreateService(req =>
        {
            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"value":[{"id":"x","displayName":"O'Brien"}]}""",
                    Encoding.UTF8, "application/json")
            };
        });

        var result = await service.ResolveUserIdByEmailAsync("o'brien@anela.cz");

        result.Should().Be("x");
        // OData v4: single quote doubled then percent-encoded. %27 = single quote.
        capturedUrl.Should().Contain("mail eq %27o%27%27brien%40anela.cz%27");
    }

    [Fact]
    public async Task ResolveUserIdByEmailAsync_UsesAppToken_AndCallsGraphUsersEndpoint()
    {
        HttpRequestMessage? captured = null;
        var (service, _) = CreateService(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"value":[]}""", Encoding.UTF8, "application/json")
            };
        });

        await service.ResolveUserIdByEmailAsync("anyone@anela.cz");

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.AbsoluteUri.Should().StartWith("https://graph.microsoft.com/v1.0/users?");
        captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization!.Parameter.Should().Be("fake-token");
    }

    [Fact]
    public async Task CreateTodoTaskAsync_ExistingList_PostsTaskAndReturnsId()
    {
        var calls = new Queue<HttpResponseMessage>();
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"value":[{"id":"list-1","displayName":"Meeting Actions"}]}""",
                Encoding.UTF8, "application/json")
        });
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"task-42","title":"Write spec"}""", Encoding.UTF8, "application/json")
        });

        var (service, handler) = CreateService(_ => calls.Dequeue());

        var result = await service.CreateTodoTaskAsync(
            userId: "user-1",
            title: "Write spec",
            description: "Draft RFC",
            dueDate: new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc));

        result.Success.Should().BeTrue();
        result.ExternalTaskId.Should().Be("task-42");
        result.Error.Should().BeNull();

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.AbsoluteUri.Should()
            .Be("https://graph.microsoft.com/v1.0/users/user-1/todo/lists");
        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].RequestUri!.AbsoluteUri.Should()
            .Be("https://graph.microsoft.com/v1.0/users/user-1/todo/lists/list-1/tasks");
        handler.RequestBodies[1].Should().Contain("\"title\":\"Write spec\"");
        handler.RequestBodies[1].Should().Contain("\"contentType\":\"text\"");
        handler.RequestBodies[1].Should().Contain("\"content\":\"Draft RFC\"");
        handler.RequestBodies[1].Should().Contain("\"dueDateTime\"");
        handler.RequestBodies[1].Should().Contain("\"timeZone\":\"UTC\"");
    }

    [Fact]
    public async Task CreateTodoTaskAsync_MissingList_CreatesListThenTask()
    {
        var calls = new Queue<HttpResponseMessage>();
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"value":[]}""", Encoding.UTF8, "application/json")
        });
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(
                """{"id":"list-new","displayName":"Meeting Actions"}""",
                Encoding.UTF8, "application/json")
        });
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"task-99","title":"Do X"}""", Encoding.UTF8, "application/json")
        });

        var (service, handler) = CreateService(_ => calls.Dequeue());

        var result = await service.CreateTodoTaskAsync(
            userId: "user-2",
            title: "Do X",
            description: "",
            dueDate: null);

        result.Success.Should().BeTrue();
        result.ExternalTaskId.Should().Be("task-99");

        handler.Requests.Should().HaveCount(3);
        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].RequestUri!.AbsoluteUri.Should()
            .Be("https://graph.microsoft.com/v1.0/users/user-2/todo/lists");
        handler.RequestBodies[1].Should().Contain("\"displayName\":\"Meeting Actions\"");

        handler.Requests[2].RequestUri!.AbsoluteUri.Should()
            .Be("https://graph.microsoft.com/v1.0/users/user-2/todo/lists/list-new/tasks");
        handler.RequestBodies[2].Should().NotContain("dueDateTime");
    }

    [Fact]
    public async Task CreateTodoTaskAsync_ListLookupCaseInsensitive_MatchesByDisplayName()
    {
        var calls = new Queue<HttpResponseMessage>();
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"value":[{"id":"list-1","displayName":"meeting ACTIONS"}]}""",
                Encoding.UTF8, "application/json")
        });
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"task-1","title":"t"}""", Encoding.UTF8, "application/json")
        });

        var (service, _) = CreateService(_ => calls.Dequeue());

        var result = await service.CreateTodoTaskAsync("u", "t", "d", null);

        result.Success.Should().BeTrue();
        result.ExternalTaskId.Should().Be("task-1");
    }

    [Fact]
    public async Task CreateTodoTaskAsync_GraphReturnsError_ReturnsFailureResultWithMessage()
    {
        var calls = new Queue<HttpResponseMessage>();
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"value":[{"id":"list-1","displayName":"Meeting Actions"}]}""",
                Encoding.UTF8, "application/json")
        });
        calls.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("graph went boom", Encoding.UTF8, "text/plain")
        });

        var (service, _) = CreateService(_ => calls.Dequeue());

        var result = await service.CreateTodoTaskAsync("u", "t", "d", null);

        result.Success.Should().BeFalse();
        result.ExternalTaskId.Should().BeNull();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateTodoTaskAsync_TransportException_ReturnsFailureResultWithMessage()
    {
        var (service, _) = CreateService(_ => throw new HttpRequestException("network down"));

        var result = await service.CreateTodoTaskAsync("u", "t", "d", null);

        result.Success.Should().BeFalse();
        result.ExternalTaskId.Should().BeNull();
        result.Error.Should().Be("network down");
    }

    [Fact]
    public async Task CreateTodoTaskAsync_TodoListLookup401_ErrorIncludesStatusCodeAndBody()
    {
        // GET /todo/lists returns 401 with a Graph error body.
        // After the fix the error must carry both the status code AND a snippet of the body.
        var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """{"error":{"code":"InvalidAuthenticationToken","message":"Access token is invalid."}}""",
                Encoding.UTF8, "application/json")
        });

        var result = await service.CreateTodoTaskAsync("user-1", "T", "desc", null);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("401");
        result.Error.Should().Contain("InvalidAuthenticationToken");
    }

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
            RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responder(request);
        }
    }
}
