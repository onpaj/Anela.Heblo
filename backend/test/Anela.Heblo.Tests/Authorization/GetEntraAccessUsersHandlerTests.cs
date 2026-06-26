using Anela.Heblo.Application.Features.Authorization.Contracts;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GetEntraAccessUsersHandlerTests
{
    private static GetEntraAccessUsersHandler NewHandler(IEntraAccessUserSource source)
        => new(source);

    [Fact]
    public async Task Handle_ReturnsEntraUsersOrderedByDisplayName()
    {
        var mock = new Mock<IEntraAccessUserSource>();
        mock.Setup(s => s.GetBaseMembersAsync(default))
            .ReturnsAsync(new List<EntraAccessUserRecord>
            {
                new("obj-2", "z@x.cz", "Zdenek Novak"),
                new("obj-1", "a@x.cz", "Anna Novak"),
            });

        var result = await NewHandler(mock.Object).Handle(new GetEntraAccessUsersRequest(), default);

        result.Success.Should().BeTrue();
        result.Users.Should().HaveCount(2);
        result.Users[0].DisplayName.Should().Be("Anna Novak");
        result.Users[0].EntraObjectId.Should().Be("obj-1");
        result.Users[1].DisplayName.Should().Be("Zdenek Novak");
    }

    [Fact]
    public async Task Handle_WhenSourceReturnsEmpty_ReturnsEmptyList()
    {
        var mock = new Mock<IEntraAccessUserSource>();
        mock.Setup(s => s.GetBaseMembersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntraAccessUserRecord>());

        var result = await NewHandler(mock.Object).Handle(new GetEntraAccessUsersRequest(), default);

        result.Success.Should().BeTrue();
        result.Users.Should().BeEmpty();
    }
}
