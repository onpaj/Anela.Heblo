using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.GridLayouts;

public class SaveGridLayoutHandlerTests
{
    private readonly Mock<IGridLayoutRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private SaveGridLayoutHandler CreateHandler() =>
        new(_repositoryMock.Object, _currentUserMock.Object);

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
}
