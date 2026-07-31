using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetDepartments;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Tests.Features.UserManagement;

public class GetDepartmentsHandlerTests
{
    private readonly Mock<IDepartmentQueryService> _mockDepartmentQueryService;
    private readonly Mock<ILogger<GetDepartmentsHandler>> _mockLogger;
    private readonly GetDepartmentsHandler _handler;

    public GetDepartmentsHandlerTests()
    {
        _mockDepartmentQueryService = new Mock<IDepartmentQueryService>();
        _mockLogger = new Mock<ILogger<GetDepartmentsHandler>>();
        _handler = new GetDepartmentsHandler(_mockDepartmentQueryService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WithDepartments_ReturnsSuccessfulResponse()
    {
        // Arrange
        var expectedDepartments = new List<DepartmentDto>
        {
            new() { Id = "1", Name = "Sales" },
            new() { Id = "2", Name = "Warehouse" }
        };

        _mockDepartmentQueryService
            .Setup(x => x.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDepartments);

        // Act
        var result = await _handler.Handle(new GetDepartmentsRequest(), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Departments.Count);
        Assert.Equal("Sales", result.Departments[0].Name);
        Assert.Equal("2", result.Departments[1].Id);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task Handle_WhenNoDepartments_ReturnsSuccessWithEmptyList()
    {
        // Arrange
        _mockDepartmentQueryService
            .Setup(x => x.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DepartmentDto>());

        // Act
        var result = await _handler.Handle(new GetDepartmentsRequest(), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Departments);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task Handle_WhenQueryServiceThrows_ReturnsFailureResponse()
    {
        // Arrange
        _mockDepartmentQueryService
            .Setup(x => x.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Flexi API error"));

        // Act
        var result = await _handler.Handle(new GetDepartmentsRequest(), CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InternalServerError, result.ErrorCode);
        Assert.Empty(result.Departments);
    }
}
