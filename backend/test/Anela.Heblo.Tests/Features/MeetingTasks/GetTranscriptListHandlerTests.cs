using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class GetTranscriptListHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repositoryMock;
    private readonly Mock<IMeetingAccessGuard> _guardMock;
    private readonly Mock<ICurrentUserService> _userServiceMock;
    private readonly GetTranscriptListHandler _handler;

    public GetTranscriptListHandlerTests()
    {
        _repositoryMock = new Mock<IMeetingTranscriptRepository>();
        _guardMock = new Mock<IMeetingAccessGuard>();
        _guardMock.Setup(x => x.IsManager()).Returns(true);
        _userServiceMock = new Mock<ICurrentUserService>();
        _userServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, "Manager", null, true));
        _handler = new GetTranscriptListHandler(
            _repositoryMock.Object,
            _guardMock.Object,
            _userServiceMock.Object,
            NullLogger<GetTranscriptListHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ReturnsPagedResults()
    {
        // Arrange
        var transcriptId = Guid.NewGuid();
        var transcript = new MeetingTranscript
        {
            Id = transcriptId,
            PlaudRecordingId = "rec-001",
            PlaudCreatedAt = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc),
            Subject = "Sprint Planning",
            Summary = "Plan the sprint",
            RawTranscript = "",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = new DateTime(2026, 5, 1, 9, 30, 0, DateTimeKind.Utc),
            Tasks = new List<ProposedTask>
            {
                new ProposedTask
                {
                    Id = Guid.NewGuid(),
                    MeetingTranscriptId = transcriptId,
                    Title = "Write spec",
                    Description = "Draft the spec",
                    Assignee = "ondra",
                    Status = ProposedTaskStatus.Pending,
                    IsManuallyAdded = false
                }
            }
        };

        _repositoryMock
            .Setup(r => r.GetListAsync(
                It.IsAny<MeetingTranscriptStatus?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<MeetingTranscript> { transcript }, 1));

        var request = new GetTranscriptListRequest
        {
            PageNumber = 1,
            PageSize = 20
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalCount.Should().Be(1);
        result.Items.Should().HaveCount(1);

        var item = result.Items[0];
        item.Id.Should().Be(transcriptId);
        item.PlaudRecordingId.Should().Be("rec-001");
        item.Subject.Should().Be("Sprint Planning");
        item.Status.Should().Be("PendingReview");
        item.TaskCount.Should().Be(1);
        item.ApprovedTaskCount.Should().Be(0);
        item.RejectedTaskCount.Should().Be(0);
        item.Tasks.Should().BeEmpty();

        _repositoryMock.Verify(
            r => r.GetListAsync(null, null, false, true, null, 1, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
