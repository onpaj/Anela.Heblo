using Anela.Heblo.Application.Features.Authorization.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure;
using Anela.Heblo.Application.Features.UserManagement.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.UserManagement;

public class EntraAccessUserSourceAdapterTests
{
    [Fact]
    public async Task GetBaseMembersAsync_GraphAuthException_TranslatesToEntraAccessSourceAuthException()
    {
        var graphMock = new Mock<IGraphService>();
        graphMock
            .Setup(g => g.GetAppRoleMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GraphServiceAuthException("token acquisition failed", new Exception("inner")));

        var adapter = new EntraAccessUserSourceAdapter(graphMock.Object);

        await Assert.ThrowsAsync<EntraAccessSourceAuthException>(() => adapter.GetBaseMembersAsync(default));
    }

    [Fact]
    public async Task GetBaseMembersAsync_GraphServiceException_TranslatesToEntraAccessSourceException()
    {
        var graphMock = new Mock<IGraphService>();
        graphMock
            .Setup(g => g.GetAppRoleMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GraphServiceException("OData error", new Exception("inner")));

        var adapter = new EntraAccessUserSourceAdapter(graphMock.Object);

        await Assert.ThrowsAsync<EntraAccessSourceException>(() => adapter.GetBaseMembersAsync(default));
    }

    [Fact]
    public async Task GetBaseMembersAsync_UnauthorizedAccessException_PropagatesUnhandled()
    {
        var graphMock = new Mock<IGraphService>();
        graphMock
            .Setup(g => g.GetAppRoleMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("forbidden"));

        var adapter = new EntraAccessUserSourceAdapter(graphMock.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.GetBaseMembersAsync(default));
    }

    [Fact]
    public async Task GetBaseMembersAsync_Success_MapsUserDtoToEntraAccessUserRecord()
    {
        var graphMock = new Mock<IGraphService>();
        graphMock
            .Setup(g => g.GetAppRoleMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto>
            {
                new() { Id = "u1", DisplayName = "Alice", Email = "alice@example.com" }
            });

        var adapter = new EntraAccessUserSourceAdapter(graphMock.Object);

        var result = await adapter.GetBaseMembersAsync(default);

        result.Should().ContainSingle();
        result[0].Id.Should().Be("u1");
        result[0].DisplayName.Should().Be("Alice");
        result[0].Email.Should().Be("alice@example.com");
    }
}
