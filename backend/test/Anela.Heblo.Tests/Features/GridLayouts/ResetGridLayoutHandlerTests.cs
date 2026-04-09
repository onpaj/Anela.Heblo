using Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.GridLayouts;

public class ResetGridLayoutHandlerTests
{
    private readonly Mock<IGridLayoutRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private ResetGridLayoutHandler CreateHandler() =>
        new(_repositoryMock.Object, _currentUserMock.Object);

    [Fact]
    public async Task Handle_CallsDeleteWithCorrectUserAndGrid()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock.Setup(x => x.DeleteAsync("user-1", "test-grid", default)).Returns(Task.CompletedTask);

        var handler = CreateHandler();
        await handler.Handle(new ResetGridLayoutRequest { GridKey = "test-grid" }, default);

        _repositoryMock.Verify(x => x.DeleteAsync("user-1", "test-grid", default), Times.Once);
    }
}
