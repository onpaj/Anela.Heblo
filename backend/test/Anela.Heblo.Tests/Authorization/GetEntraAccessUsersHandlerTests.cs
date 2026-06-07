using Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GetEntraAccessUsersHandlerTests
{
    private static GetEntraAccessUsersHandler NewHandler(IGraphService graphService)
        => new(graphService);

    [Fact]
    public async Task Handle_ReturnsEntraUsersOrderedByDisplayName()
    {
        var mock = new Mock<IGraphService>();
        mock.Setup(g => g.GetAppRoleMembersAsync("heblo_user", default))
            .ReturnsAsync(new List<UserDto>
            {
                new() { Id = "obj-2", DisplayName = "Zdenek Novak", Email = "z@x.cz" },
                new() { Id = "obj-1", DisplayName = "Anna Novak", Email = "a@x.cz" },
            });

        var result = await NewHandler(mock.Object).Handle(new GetEntraAccessUsersRequest(), default);

        result.Success.Should().BeTrue();
        result.Users.Should().HaveCount(2);
        result.Users[0].DisplayName.Should().Be("Anna Novak");
        result.Users[0].EntraObjectId.Should().Be("obj-1");
        result.Users[1].DisplayName.Should().Be("Zdenek Novak");
    }

    [Fact]
    public async Task Handle_WhenGraphReturnsEmpty_ReturnsEmptyList()
    {
        var mock = new Mock<IGraphService>();
        mock.Setup(g => g.GetAppRoleMembersAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new List<UserDto>());

        var result = await NewHandler(mock.Object).Handle(new GetEntraAccessUsersRequest(), default);

        result.Success.Should().BeTrue();
        result.Users.Should().BeEmpty();
    }
}
