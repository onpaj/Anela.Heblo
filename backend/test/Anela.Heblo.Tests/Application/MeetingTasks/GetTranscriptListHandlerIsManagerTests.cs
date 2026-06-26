using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.MeetingTasks;

public class GetTranscriptListHandlerIsManagerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repositoryMock;
    private readonly Mock<IMeetingAccessGuard> _guardMock;
    private readonly Mock<ICurrentUserService> _userServiceMock;
    private readonly GetTranscriptListHandler _handler;

    public GetTranscriptListHandlerIsManagerTests()
    {
        _repositoryMock = new Mock<IMeetingTranscriptRepository>();
        _guardMock = new Mock<IMeetingAccessGuard>();
        _userServiceMock = new Mock<ICurrentUserService>();
        _handler = new GetTranscriptListHandler(
            _repositoryMock.Object,
            _guardMock.Object,
            _userServiceMock.Object,
            new Mock<ILogger<GetTranscriptListHandler>>().Object);
    }

    [Fact]
    public async Task Handle_PassesIsManagerTrue_AndEmail_ToRepository_WhenManager()
    {
        _guardMock.Setup(x => x.IsManager()).Returns(true);
        _userServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, "Manager", "manager@test.com", true));
        _repositoryMock
            .Setup(x => x.GetListAsync(null, null, false, true, "manager@test.com", 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<MeetingTranscript>(), 0));

        var result = await _handler.Handle(new GetTranscriptListRequest { PageNumber = 1, PageSize = 20 }, default);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(
            x => x.GetListAsync(null, null, false, true, "manager@test.com", 1, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PassesIsManagerFalse_AndEmail_ToRepository_WhenNonManager()
    {
        _guardMock.Setup(x => x.IsManager()).Returns(false);
        _userServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, "User", "user@test.com", true));
        _repositoryMock
            .Setup(x => x.GetListAsync(null, null, false, false, "user@test.com", 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<MeetingTranscript>(), 0));

        var result = await _handler.Handle(new GetTranscriptListRequest { PageNumber = 1, PageSize = 20 }, default);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(
            x => x.GetListAsync(null, null, false, false, "user@test.com", 1, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
