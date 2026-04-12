using Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.GridLayouts;

public class ResetGridLayoutHandlerTests
{
    private readonly Mock<IGridLayoutRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<ResetGridLayoutHandler>> _loggerMock = new();

    private ResetGridLayoutHandler CreateHandler() =>
        new(_repositoryMock.Object, _currentUserMock.Object, _loggerMock.Object);

    [Fact]
    public async Task Handle_CallsDeleteWithCorrectUserAndGrid()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock.Setup(x => x.DeleteAsync("user-1", "test-grid", default)).Returns(Task.CompletedTask);

        var handler = CreateHandler();
        await handler.Handle(new ResetGridLayoutRequest { GridKey = "test-grid" }, default);

        _repositoryMock.Verify(x => x.DeleteAsync("user-1", "test-grid", default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenDatabaseThrows_ReturnsDatabaseErrorAndLogsError()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock
            .Setup(x => x.DeleteAsync("user-1", "test-grid", default))
            .ThrowsAsync(new NpgsqlException("relation \"GridLayouts\" does not exist"));

        var handler = CreateHandler();
        var response = await handler.Handle(new ResetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DatabaseError, response.ErrorCode);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Database error resetting GridLayout")),
                It.IsAny<NpgsqlException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
