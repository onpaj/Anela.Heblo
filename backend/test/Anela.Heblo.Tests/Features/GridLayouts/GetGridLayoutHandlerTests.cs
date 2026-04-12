using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.GridLayouts;

public class GetGridLayoutHandlerTests
{
    private readonly Mock<IGridLayoutRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<GetGridLayoutHandler>> _loggerMock = new();

    private GetGridLayoutHandler CreateHandler() =>
        new(_repositoryMock.Object, _currentUserMock.Object, _loggerMock.Object);

    [Fact]
    public async Task Handle_WhenNoSavedLayout_ReturnsNull()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock.Setup(x => x.GetAsync("user-1", "test-grid", default)).ReturnsAsync((GridLayout?)null);

        var handler = CreateHandler();
        var response = await handler.Handle(new GetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.Null(response.Layout);
    }

    [Fact]
    public async Task Handle_WhenSavedLayoutExists_ReturnsDeserializedDto()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));

        var payload = new { columns = new[] { new { id = "col1", order = 0, width = 120, hidden = false } } };
        var json = JsonSerializer.Serialize(payload);

        _repositoryMock.Setup(x => x.GetAsync("user-1", "test-grid", default)).ReturnsAsync(new GridLayout
        {
            UserId = "user-1",
            GridKey = "test-grid",
            LayoutJson = json,
            LastModified = DateTime.UtcNow
        });

        var handler = CreateHandler();
        var response = await handler.Handle(new GetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.NotNull(response.Layout);
        Assert.Single(response.Layout!.Columns);
        Assert.Equal("col1", response.Layout.Columns[0].Id);
        Assert.Equal(120, response.Layout.Columns[0].Width);
    }

    [Fact]
    public async Task Handle_WhenDatabaseThrows_ReturnsNullLayoutAndLogsError()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock
            .Setup(x => x.GetAsync("user-1", "test-grid", default))
            .ThrowsAsync(new NpgsqlException("relation \"GridLayouts\" does not exist"));

        var handler = CreateHandler();
        var response = await handler.Handle(new GetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.Null(response.Layout);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Database error reading GridLayout")),
                It.IsAny<NpgsqlException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
