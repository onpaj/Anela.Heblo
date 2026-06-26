using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
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

        var deserialized = JsonSerializer.Deserialize<GetTranscriptListResponse>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized!.PageNumber);
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
