using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetMeetingUsers;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class GetMeetingUsersHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsAllDirectoryUsers()
    {
        // Arrange
        var directory = new Mock<IMeetingUserDirectory>();
        directory.Setup(d => d.GetAll()).Returns(new List<MeetingUser>
        {
            new("andrea@anela.cz", "Andrea Nováková", new[] { "Andy" }),
            new("petr@anela.cz", "Petr Svoboda", Array.Empty<string>()),
        });
        var handler = new GetMeetingUsersHandler(directory.Object);

        // Act
        var result = await handler.Handle(new GetMeetingUsersRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Users.Should().HaveCount(2);
        result.Users[0].Email.Should().Be("andrea@anela.cz");
        result.Users[0].DisplayName.Should().Be("Andrea Nováková");
        result.Users[0].Aliases.Should().ContainSingle().Which.Should().Be("Andy");
        result.Users[1].Email.Should().Be("petr@anela.cz");
        result.Users[1].Aliases.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenDirectoryIsEmpty()
    {
        // Arrange
        var directory = new Mock<IMeetingUserDirectory>();
        directory.Setup(d => d.GetAll()).Returns(new List<MeetingUser>());
        var handler = new GetMeetingUsersHandler(directory.Object);

        // Act
        var result = await handler.Handle(new GetMeetingUsersRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Users.Should().BeEmpty();
    }
}
