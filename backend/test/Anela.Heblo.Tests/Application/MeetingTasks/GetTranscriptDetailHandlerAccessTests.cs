using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.MeetingTasks;

public class GetTranscriptDetailHandlerAccessTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repositoryMock;
    private readonly Mock<IMeetingAccessGuard> _guardMock;
    private readonly GetTranscriptDetailHandler _handler;

    public GetTranscriptDetailHandlerAccessTests()
    {
        _repositoryMock = new Mock<IMeetingTranscriptRepository>();
        _guardMock = new Mock<IMeetingAccessGuard>();
        _handler = new GetTranscriptDetailHandler(
            _repositoryMock.Object,
            _guardMock.Object,
            new Mock<ILogger<GetTranscriptDetailHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenTranscriptIsNull()
    {
        var id = Guid.NewGuid();
        _repositoryMock.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        var result = await _handler.Handle(new GetTranscriptDetailRequest { Id = id }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenAccessDenied()
    {
        var id = Guid.NewGuid();
        var transcript = MakeTranscript(id, MeetingAccessLevel.Private);
        _repositoryMock.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        _guardMock.Setup(x => x.CanAccess(transcript)).Returns(false);

        var result = await _handler.Handle(new GetTranscriptDetailRequest { Id = id }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsTranscript_WhenAccessAllowed()
    {
        var id = Guid.NewGuid();
        var transcript = MakeTranscript(id, MeetingAccessLevel.Public);
        _repositoryMock.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        _guardMock.Setup(x => x.CanAccess(transcript)).Returns(true);

        var result = await _handler.Handle(new GetTranscriptDetailRequest { Id = id }, default);

        result.Success.Should().BeTrue();
        result.Transcript.Should().NotBeNull();
        result.Transcript!.Id.Should().Be(id);
    }

    private static MeetingTranscript MakeTranscript(Guid id, MeetingAccessLevel level) => new()
    {
        Id = id,
        PlaudRecordingId = "test",
        PlaudCreatedAt = DateTime.UtcNow,
        Subject = "Test",
        Summary = "Test",
        RawTranscript = "Test",
        Status = MeetingTranscriptStatus.PendingReview,
        ReceivedAt = DateTime.UtcNow,
        AccessLevel = level,
        Tasks = [],
        AccessGrants = []
    };
}
