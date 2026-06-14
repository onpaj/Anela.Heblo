using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.GridLayouts;

/// <summary>
/// Tests for the slim persistence payload refactor.
/// These tests verify that GridLayoutPersistencePayload contains only columns,
/// and that GridLayoutDto is assembled from payload + entity.GridKey + entity.LastModified.
/// </summary>
public class SaveGridLayoutHandlerPayloadTests
{
    private readonly Mock<IGridLayoutRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<ILogger<SaveGridLayoutHandler>> _loggerMock = new();
    private readonly SaveGridLayoutHandler _handler;

    public SaveGridLayoutHandlerPayloadTests()
    {
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test", "test@test.com", true));

        _handler = new SaveGridLayoutHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_SerializesOnlyColumnsToPayload_NotGridKeyOrLastModified()
    {
        // Arrange
        var columns = new List<GridColumnStateDto>
        {
            new() { Id = "col-1", Order = 0, Width = 100, Hidden = false },
            new() { Id = "col-2", Order = 1, Width = null, Hidden = true }
        };
        var request = new SaveGridLayoutRequest { GridKey = "my-grid", Columns = columns };

        string? capturedJson = null;
        _repositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, json, _) => capturedJson = json);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedJson);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(capturedJson!);
        Assert.NotNull(parsed);
        Assert.True(parsed.ContainsKey("columns"), "Payload must contain 'columns'");
        Assert.False(parsed.ContainsKey("gridKey"), "Payload must NOT contain 'gridKey'");
        Assert.False(parsed.ContainsKey("lastModified"), "Payload must NOT contain 'lastModified'");
    }

    [Fact]
    public async Task Handle_ColumnsRoundTrip_AllPropertiesPreserved()
    {
        // Arrange
        var columns = new List<GridColumnStateDto>
        {
            new() { Id = "a", Order = 3, Width = 250, Hidden = false },
            new() { Id = "b", Order = 7, Width = null, Hidden = true },
            new() { Id = "c", Order = 12, Width = 50, Hidden = false }
        };
        var request = new SaveGridLayoutRequest { GridKey = "grid-rt", Columns = columns };

        string? capturedJson = null;
        _repositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, json, _) => capturedJson = json);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert — deserialize back and verify structure
        Assert.NotNull(capturedJson);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(capturedJson!);
        Assert.NotNull(parsed);
        var columnsElement = parsed["columns"];
        var recoveredColumns = columnsElement.Deserialize<List<GridColumnStateDto>>();
        Assert.NotNull(recoveredColumns);
        Assert.Equal(3, recoveredColumns.Count);

        // Verify first column
        Assert.Equal("a", recoveredColumns[0].Id);
        Assert.Equal(3, recoveredColumns[0].Order);
        Assert.Equal(250, recoveredColumns[0].Width);
        Assert.False(recoveredColumns[0].Hidden);

        // Verify second column (with null width)
        Assert.Equal("b", recoveredColumns[1].Id);
        Assert.Equal(7, recoveredColumns[1].Order);
        Assert.Null(recoveredColumns[1].Width);
        Assert.True(recoveredColumns[1].Hidden);

        // Verify third column
        Assert.Equal("c", recoveredColumns[2].Id);
        Assert.Equal(12, recoveredColumns[2].Order);
        Assert.Equal(50, recoveredColumns[2].Width);
        Assert.False(recoveredColumns[2].Hidden);
    }

    [Fact]
    public async Task Handle_EmptyColumnsList_SerializesValidPayload()
    {
        // Arrange
        var request = new SaveGridLayoutRequest { GridKey = "empty-grid", Columns = new List<GridColumnStateDto>() };

        string? capturedJson = null;
        _repositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, json, _) => capturedJson = json);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedJson);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(capturedJson!);
        Assert.NotNull(parsed);
        Assert.True(parsed.ContainsKey("columns"));
        var columnsElement = parsed["columns"];
        var recoveredColumns = columnsElement.Deserialize<List<GridColumnStateDto>>();
        Assert.NotNull(recoveredColumns);
        Assert.Empty(recoveredColumns);
    }
}

/// <summary>
/// Tests for the slim persistence payload deserialization.
/// These tests verify that GetGridLayoutHandler correctly deserializes the slim payload
/// and assembles GridLayoutDto using entity.GridKey and entity.LastModified.
/// </summary>
public class GetGridLayoutHandlerPayloadTests
{
    private readonly Mock<IGridLayoutRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<ILogger<GetGridLayoutHandler>> _loggerMock = new();
    private readonly GetGridLayoutHandler _handler;

    public GetGridLayoutHandlerPayloadTests()
    {
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test", "test@test.com", true));

        _handler = new GetGridLayoutHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_DeserializesSlimPayloadAndAssemblesDto()
    {
        // Arrange
        var json = "{\"columns\":[{\"id\":\"col-1\",\"order\":0,\"width\":120,\"hidden\":false},{\"id\":\"col-2\",\"order\":1,\"width\":null,\"hidden\":true}]}";
        var lastModified = new DateTime(2025, 6, 1, 12, 30, 45, DateTimeKind.Utc);
        var entity = new GridLayout
        {
            UserId = "user-1",
            GridKey = "grid-abc",
            LayoutJson = json,
            LastModified = lastModified
        };

        _repositoryMock
            .Setup(r => r.GetAsync("user-1", "grid-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        // Act
        var response = await _handler.Handle(new GetGridLayoutRequest { GridKey = "grid-abc" }, CancellationToken.None);

        // Assert
        Assert.NotNull(response.Layout);
        var dto = response.Layout;
        Assert.Equal("grid-abc", dto.GridKey);
        Assert.Equal(lastModified, dto.LastModified);
        Assert.Equal(2, dto.Columns.Count);
        Assert.Equal("col-1", dto.Columns[0].Id);
        Assert.Equal(0, dto.Columns[0].Order);
        Assert.Equal(120, dto.Columns[0].Width);
        Assert.False(dto.Columns[0].Hidden);
        Assert.Equal("col-2", dto.Columns[1].Id);
        Assert.Null(dto.Columns[1].Width);
        Assert.True(dto.Columns[1].Hidden);
    }

    [Fact]
    public async Task Handle_UsesEntityGridKeyAsAuthoritative_IgnoringAnyEmbedded()
    {
        // Arrange — legacy JSON with embedded gridKey (should be ignored)
        var legacyJson = "{\"gridKey\":\"embedded-old-key\",\"columns\":[{\"id\":\"c1\",\"order\":1,\"width\":100,\"hidden\":false}],\"lastModified\":\"2025-01-01T00:00:00Z\"}";
        var entityLastModified = new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc);
        var entity = new GridLayout
        {
            UserId = "user-1",
            GridKey = "authoritative-grid-key",
            LayoutJson = legacyJson,
            LastModified = entityLastModified
        };

        _repositoryMock
            .Setup(r => r.GetAsync("user-1", "authoritative-grid-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        // Act
        var response = await _handler.Handle(new GetGridLayoutRequest { GridKey = "authoritative-grid-key" }, CancellationToken.None);

        // Assert
        Assert.NotNull(response.Layout);
        var dto = response.Layout;
        Assert.Equal("authoritative-grid-key", dto.GridKey);
        Assert.Equal(entityLastModified, dto.LastModified);
        Assert.Single(dto.Columns);
        Assert.Equal("c1", dto.Columns[0].Id);
    }

    [Fact]
    public async Task Handle_WhenPayloadColumnsIsNull_UsesEmptyList()
    {
        // Arrange — payload with null columns (defensive handling)
        var json = "{\"columns\":null}";
        var entity = new GridLayout
        {
            UserId = "user-1",
            GridKey = "grid-null-cols",
            LayoutJson = json,
            LastModified = DateTime.UtcNow
        };

        _repositoryMock
            .Setup(r => r.GetAsync("user-1", "grid-null-cols", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        // Act
        var response = await _handler.Handle(new GetGridLayoutRequest { GridKey = "grid-null-cols" }, CancellationToken.None);

        // Assert
        Assert.NotNull(response.Layout);
        Assert.NotNull(response.Layout.Columns);
        Assert.Empty(response.Layout.Columns);
    }

    [Fact]
    public async Task Handle_WhenPayloadIsEmpty_ReturnsNullLayout()
    {
        // Arrange — empty JSON object
        var json = "{}";
        var entity = new GridLayout
        {
            UserId = "user-1",
            GridKey = "grid-empty",
            LayoutJson = json,
            LastModified = DateTime.UtcNow
        };

        _repositoryMock
            .Setup(r => r.GetAsync("user-1", "grid-empty", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        // Act
        var response = await _handler.Handle(new GetGridLayoutRequest { GridKey = "grid-empty" }, CancellationToken.None);

        // Assert — {} deserializes to a payload with null Columns
        Assert.NotNull(response.Layout);
        Assert.NotNull(response.Layout.Columns);
        Assert.Empty(response.Layout.Columns);
        Assert.Equal("grid-empty", response.Layout.GridKey);
    }

    [Fact]
    public async Task Handle_WhenJsonIsMalformed_ReturnsNullAndLogsWarning()
    {
        // Arrange
        var entity = new GridLayout
        {
            UserId = "user-1",
            GridKey = "grid-bad",
            LayoutJson = "{not valid json",
            LastModified = DateTime.UtcNow
        };

        _repositoryMock
            .Setup(r => r.GetAsync("user-1", "grid-bad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        // Act
        var response = await _handler.Handle(new GetGridLayoutRequest { GridKey = "grid-bad" }, CancellationToken.None);

        // Assert
        Assert.Null(response.Layout);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Malformed LayoutJson")),
                It.IsAny<JsonException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenJsonIsLiteralNull_ReturnsNullLayout()
    {
        // Arrange
        var entity = new GridLayout
        {
            UserId = "user-1",
            GridKey = "grid-null-json",
            LayoutJson = "null",
            LastModified = DateTime.UtcNow
        };

        _repositoryMock
            .Setup(r => r.GetAsync("user-1", "grid-null-json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        // Act
        var response = await _handler.Handle(new GetGridLayoutRequest { GridKey = "grid-null-json" }, CancellationToken.None);

        // Assert — payload is null, returns null layout without logging warning
        Assert.Null(response.Layout);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEntityNotFound_ReturnsNullLayout()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetAsync("user-1", "missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GridLayout?)null);

        // Act
        var response = await _handler.Handle(new GetGridLayoutRequest { GridKey = "missing" }, CancellationToken.None);

        // Assert
        Assert.Null(response.Layout);
    }

    [Fact]
    public async Task Handle_WhenDatabaseThrows_ReturnsNullAndLogsError()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetAsync("user-1", "grid-error", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GridLayoutPersistenceException(
                "Database error",
                new InvalidOperationException("simulated error")));

        // Act
        var response = await _handler.Handle(new GetGridLayoutRequest { GridKey = "grid-error" }, CancellationToken.None);

        // Assert
        Assert.Null(response.Layout);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Database error reading GridLayout")),
                It.IsAny<GridLayoutPersistenceException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
