using Anela.Heblo.Adapters.Flexi.Accounting.Departments;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Accounting.Departments;

public class FlexiDepartmentQueryServiceTests
{
    private readonly Mock<IDepartmentClient> _departmentClientMock = new();
    private readonly FlexiDepartmentQueryService _service;

    public FlexiDepartmentQueryServiceTests()
    {
        _service = new FlexiDepartmentQueryService(_departmentClientMock.Object);
    }

    [Fact]
    public async Task GetDepartmentsAsync_MapsDomainDepartmentsToDtos()
    {
        // Arrange
        _departmentClientMock
            .Setup(x => x.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Department>
            {
                new() { Id = "1", Name = "Sales" },
                new() { Id = "2", Name = "Warehouse" }
            });

        // Act
        var result = await _service.GetDepartmentsAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result[0].Id.Should().Be("1");
        result[0].Name.Should().Be("Sales");
        result[1].Id.Should().Be("2");
        result[1].Name.Should().Be("Warehouse");
    }

    [Fact]
    public async Task GetDepartmentsAsync_WhenClientReturnsNoDepartments_ReturnsEmptyList()
    {
        // Arrange
        _departmentClientMock
            .Setup(x => x.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Department>());

        // Act
        var result = await _service.GetDepartmentsAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
