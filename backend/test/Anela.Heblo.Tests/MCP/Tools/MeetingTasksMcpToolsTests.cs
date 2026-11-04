using System.Text.Json;
using System.Text.Json.Nodes;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class MeetingTasksMcpToolsTests
{
    private static readonly string ReadRole = AccessRoles.For(Feature.Anela_Meetings, AccessLevel.Read);

    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly MeetingTasksMcpTools _tools;

    public MeetingTasksMcpToolsTests()
    {
        // Default: caller has read access. FORBIDDEN tests override this to false.
        _currentUserServiceMock.Setup(s => s.IsInRole(ReadRole)).Returns(true);
        _tools = new MeetingTasksMcpTools(
            _mediatorMock.Object,
            _currentUserServiceMock.Object,
            Mock.Of<ILogger<MeetingTasksMcpTools>>());
    }

    private static MeetingTranscriptDto BuildTranscript(Guid id) => new()
    {
        Id = id,
        PlaudRecordingId = "plaud-1",
        Subject = "Weekly sync",
        Summary = "We discussed the roadmap.",
        RawTranscript = "Alice: hello. Bob: hi.",
        Status = "Approved",
        TaskCount = 1,
        ApprovedTaskCount = 1,
        AccessLevel = "Public",
        Tasks = new List<ProposedTaskDto>
        {
            new() { Id = Guid.NewGuid(), Title = "Ship it", Description = "Ship the feature", Assignee = "Alice", Status = "Approved" }
        }
    };

    [Fact]
    public async Task ListMeetings_MapsParametersOntoRequest()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTranscriptListRequest>(), default))
            .ReturnsAsync(new GetTranscriptListResponse { Items = new(), TotalCount = 0, PageNumber = 2, PageSize = 10 });

        // Act
        var json = await _tools.ListMeetings(
            searchText: "roadmap",
            statusFilter: "Approved",
            searchInTranscript: true,
            pageNumber: 2,
            pageSize: 10);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetTranscriptListRequest>(req =>
                req.SearchText == "roadmap" &&
                req.StatusFilter == "Approved" &&
                req.SearchInTranscript == true &&
                req.PageNumber == 2 &&
                req.PageSize == 10),
            default), Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetTranscriptListResponse>(json, McpJsonOptions.Default);
        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized!.PageNumber);
    }

    private static readonly Guid ListedMeetingId = Guid.Parse("7f0c3a52-1111-4a6e-9c1d-000000000001");
    private const string ListedSummary = "A very long summary of the meeting.";

    private static MeetingTranscript BuildListedMeeting() => new()
    {
        Id = ListedMeetingId,
        PlaudRecordingId = "rec-listed",
        PlaudCreatedAt = new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc),
        Subject = "Weekly sync",
        Summary = ListedSummary,
        RawTranscript = "Alice: hello.",
        Status = MeetingTranscriptStatus.PartiallyApproved,
        ReceivedAt = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc),
        ReviewedAt = new DateTime(2026, 11, 3, 11, 0, 0, DateTimeKind.Utc),
        ReviewedByUser = "Manager",
        AccessLevel = MeetingAccessLevel.Public,
        Tasks = new List<ProposedTask>
        {
            new() { Id = Guid.NewGuid(), Title = "A", Description = "a", Assignee = "Alice", Status = ProposedTaskStatus.Approved },
            new() { Id = Guid.NewGuid(), Title = "B", Description = "b", Assignee = "Bob", Status = ProposedTaskStatus.Rejected },
            new() { Id = Guid.NewGuid(), Title = "C", Description = "c", Assignee = "Eve", Status = ProposedTaskStatus.Pending }
        }
    };

    /// <summary>Runs list requests through the real handler so tests observe the tool's real output.</summary>
    private Mock<IMeetingTranscriptRepository> RouteListRequestsThroughRealHandler(bool isManager, string email)
    {
        var repository = new Mock<IMeetingTranscriptRepository>();
        repository
            .Setup(r => r.GetListAsync(
                It.IsAny<MeetingTranscriptStatus?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<MeetingTranscript> { BuildListedMeeting() }, 41));

        var guard = new Mock<IMeetingAccessGuard>();
        guard.Setup(g => g.IsManager()).Returns(isManager);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.GetCurrentUser()).Returns(new CurrentUser(null, "Caller", email, true));

        var handler = new GetTranscriptListHandler(
            repository.Object,
            guard.Object,
            currentUser.Object,
            NullLogger<GetTranscriptListHandler>.Instance);

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTranscriptListRequest>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<GetTranscriptListResponse> request, CancellationToken ct) =>
                handler.Handle((GetTranscriptListRequest)request, ct));

        return repository;
    }

    [Fact]
    public async Task ListMeetings_ReturnsSummary_WhenIncludeSummaryIsNotPassed()
    {
        // Arrange
        RouteListRequestsThroughRealHandler(isManager: true, email: "manager@anela.cz");

        // Act
        var json = await _tools.ListMeetings();

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(ListedSummary, doc.RootElement.GetProperty("Items")[0].GetProperty("Summary").GetString());
    }

    [Fact]
    public async Task ListMeetings_SerializesSummaryAsExplicitNull_WhenIncludeSummaryIsFalse()
    {
        // Arrange
        RouteListRequestsThroughRealHandler(isManager: true, email: "manager@anela.cz");

        // Act
        var json = await _tools.ListMeetings(includeSummary: false);

        // Assert
        Assert.DoesNotContain(ListedSummary, json);
        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement.GetProperty("Items")[0];
        Assert.Equal(JsonValueKind.Null, item.GetProperty("Summary").ValueKind);
    }

    [Fact]
    public async Task ListMeetings_OutputDiffersOnlyInSummary_BetweenIncludeSummaryValues()
    {
        // Arrange
        RouteListRequestsThroughRealHandler(isManager: true, email: "manager@anela.cz");

        // Act
        var withSummary = JsonNode.Parse(await _tools.ListMeetings(includeSummary: true))!;
        var withoutSummary = JsonNode.Parse(await _tools.ListMeetings(includeSummary: false))!;

        // Assert
        Assert.Equal(ListedSummary, withSummary["Items"]![0]!["Summary"]!.GetValue<string>());
        Assert.Null(withoutSummary["Items"]![0]!["Summary"]);
        withSummary["Items"]![0]!.AsObject().Remove("Summary");
        withoutSummary["Items"]![0]!.AsObject().Remove("Summary");
        Assert.True(JsonNode.DeepEquals(withSummary, withoutSummary));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListMeetings_QueriesWithSameFiltersAndCallerVisibility_ForEitherIncludeSummaryValue(bool includeSummary)
    {
        // Arrange
        var repository = RouteListRequestsThroughRealHandler(isManager: false, email: "reader@anela.cz");

        // Act
        await _tools.ListMeetings(
            searchText: "roadmap",
            statusFilter: "Approved",
            searchInTranscript: true,
            pageNumber: 2,
            pageSize: 10,
            includeSummary: includeSummary);

        // Assert
        repository.Verify(r => r.GetListAsync(
            MeetingTranscriptStatus.Approved,
            "roadmap",
            true,
            false,
            "reader@anela.cz",
            2,
            10,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"includeSummary\":true}", true)]
    [InlineData("{\"includeSummary\":false}", false)]
    public async Task Sdk_tool_binding_maps_includeSummary_and_defaults_it_to_true(string argumentsJson, bool expected)
    {
        // Arrange
        GetTranscriptListRequest? sent = null;
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTranscriptListRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<GetTranscriptListResponse>, CancellationToken>((r, _) => sent = (GetTranscriptListRequest)r)
            .ReturnsAsync(new GetTranscriptListResponse());
        var tool = McpServerTool.Create(
            typeof(MeetingTasksMcpTools).GetMethod(nameof(MeetingTasksMcpTools.ListMeetings))!, _tools);
        var context = new RequestContext<CallToolRequestParams>(new Mock<McpServer>().Object, new JsonRpcRequest { Method = "tools/call" })
        {
            Params = new CallToolRequestParams
            {
                Name = tool.ProtocolTool.Name,
                Arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson)!
            }
        };

        // Act
        var result = await tool.InvokeAsync(context, CancellationToken.None);

        // Assert
        Assert.NotEqual(true, result.IsError);
        Assert.Equal(expected, sent!.IncludeSummary);
    }

    [Fact]
    public void Sdk_tool_schema_exposes_includeSummary_as_optional_with_a_description()
    {
        // Act
        var tool = McpServerTool.Create(
            typeof(MeetingTasksMcpTools).GetMethod(nameof(MeetingTasksMcpTools.ListMeetings))!, _tools);

        // Assert
        var schema = tool.ProtocolTool.InputSchema;
        var parameter = schema.GetProperty("properties").GetProperty("includeSummary");
        Assert.Equal("boolean", parameter.GetProperty("type").GetString());
        Assert.True(parameter.GetProperty("default").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(parameter.GetProperty("description").GetString()));
        if (schema.TryGetProperty("required", out var required))
        {
            Assert.DoesNotContain(required.EnumerateArray(), name => name.GetString() == "includeSummary");
        }
    }

    [Fact]
    public async Task GetMeetingSummary_SendsDetailRequest_AndOmitsTranscriptAndTasks()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTranscriptDetailRequest>(), default))
            .ReturnsAsync(new GetTranscriptDetailResponse { Transcript = BuildTranscript(id) });

        // Act
        var json = await _tools.GetMeetingSummary(id);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetTranscriptDetailRequest>(req => req.Id == id), default), Times.Once);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("We discussed the roadmap.", root.GetProperty("Summary").GetString());
        Assert.False(root.TryGetProperty("RawTranscript", out _));
        Assert.False(root.TryGetProperty("Tasks", out _));
    }

    [Fact]
    public async Task GetMeetingTranscript_ReturnsRawTranscriptOnly()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTranscriptDetailRequest>(), default))
            .ReturnsAsync(new GetTranscriptDetailResponse { Transcript = BuildTranscript(id) });

        // Act
        var json = await _tools.GetMeetingTranscript(id);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Alice: hello. Bob: hi.", root.GetProperty("RawTranscript").GetString());
        Assert.False(root.TryGetProperty("Summary", out _));
        Assert.False(root.TryGetProperty("Tasks", out _));
    }

    [Fact]
    public async Task GetMeetingTasks_ReturnsTaskList()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTranscriptDetailRequest>(), default))
            .ReturnsAsync(new GetTranscriptDetailResponse { Transcript = BuildTranscript(id) });

        // Act
        var json = await _tools.GetMeetingTasks(id);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tasks = root.GetProperty("Tasks");
        Assert.Equal(1, tasks.GetArrayLength());
        Assert.Equal("Ship it", tasks[0].GetProperty("Title").GetString());
        Assert.False(root.TryGetProperty("RawTranscript", out _));
    }

    [Theory]
    [InlineData("ListMeetings")]
    [InlineData("GetMeetingSummary")]
    [InlineData("GetMeetingTranscript")]
    [InlineData("GetMeetingTasks")]
    public async Task Tools_ThrowForbidden_AndSkipMediator_WhenUserLacksReadRole(string tool)
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.IsInRole(ReadRole)).Returns(false);

        // Act
        var exception = await Assert.ThrowsAsync<McpException>(() => tool switch
        {
            "ListMeetings" => _tools.ListMeetings(),
            "GetMeetingSummary" => _tools.GetMeetingSummary(Guid.NewGuid()),
            "GetMeetingTranscript" => _tools.GetMeetingTranscript(Guid.NewGuid()),
            _ => _tools.GetMeetingTasks(Guid.NewGuid())
        });

        // Assert
        Assert.Contains("FORBIDDEN", exception.Message);
        Assert.Contains(ReadRole, exception.Message);
        _mediatorMock.Verify(m => m.Send(It.IsAny<IRequest<GetTranscriptListResponse>>(), default), Times.Never);
        _mediatorMock.Verify(m => m.Send(It.IsAny<IRequest<GetTranscriptDetailResponse>>(), default), Times.Never);
    }

    [Fact]
    public async Task GetMeetingSummary_ThrowsMcpException_WhenHandlerReportsNotFound()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTranscriptDetailRequest>(), default))
            .ReturnsAsync(new GetTranscriptDetailResponse(ErrorCodes.ResourceNotFound));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(() => _tools.GetMeetingSummary(Guid.NewGuid()));
        Assert.Contains("ResourceNotFound", exception.Message);
    }
}
