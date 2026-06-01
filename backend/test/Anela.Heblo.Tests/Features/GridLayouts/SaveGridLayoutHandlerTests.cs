using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.GridLayouts;

public class SaveGridLayoutHandlerTests
{
    private readonly Mock<IGridLayoutRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<SaveGridLayoutHandler>> _loggerMock = new();

    private SaveGridLayoutHandler CreateHandler() =>
        new(_repositoryMock.Object, _currentUserMock.Object, _loggerMock.Object);

    [Fact]
    public async Task Handle_CallsUpsertWithSerializedColumns()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));

        string? capturedJson = null;
        _repositoryMock
            .Setup(x => x.UpsertAsync("user-1", "test-grid", It.IsAny<string>(), default))
            .Callback<string, string, string, CancellationToken>((_, _, json, _) => capturedJson = json)
            .Returns(Task.CompletedTask);

        var request = new SaveGridLayoutRequest
        {
            GridKey = "test-grid",
            Columns = new List<GridColumnStateDto>
            {
                new() { Id = "col1", Order = 0, Width = 150, Hidden = false },
                new() { Id = "col2", Order = 1, Width = null, Hidden = true }
            }
        };

        var handler = CreateHandler();
        await handler.Handle(request, default);

        _repositoryMock.Verify(x => x.UpsertAsync("user-1", "test-grid", It.IsAny<string>(), default), Times.Once);
        Assert.NotNull(capturedJson);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(capturedJson!);
        Assert.True(parsed!.ContainsKey("columns"));
    }

    [Fact]
    public async Task Handle_WhenDatabaseThrows_ReturnsDatabaseErrorAndLogsError()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock
            .Setup(x => x.UpsertAsync("user-1", "test-grid", It.IsAny<string>(), default))
            .ThrowsAsync(new NpgsqlException("relation \"GridLayouts\" does not exist"));

        var request = new SaveGridLayoutRequest { GridKey = "test-grid", Columns = new List<GridColumnStateDto>() };

        var handler = CreateHandler();
        var response = await handler.Handle(request, default);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DatabaseError, response.ErrorCode);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Database error saving GridLayout")),
                It.IsAny<NpgsqlException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
