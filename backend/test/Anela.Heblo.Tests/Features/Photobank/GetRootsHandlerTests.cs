using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetRoots;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class GetRootsHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repoMock = new();

    private GetRootsHandler CreateHandler() => new(_repoMock.Object);

    [Fact]
    public async Task Handle_ReturnsMappedRoots_WithAllFields()
    {
        // Arrange
        var lastIndexed = new DateTime(2026, 4, 24, 3, 0, 0, DateTimeKind.Utc);
        var roots = new List<PhotobankIndexRoot>
        {
            new()
            {
                Id = 1,
                SharePointPath = "/Fotky/Produkty",
                DisplayName = "Produkty",
                DriveId = "drive-abc",
                RootItemId = "item-xyz",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastIndexedAt = lastIndexed,
            }
        };
        _repoMock
            .Setup(r => r.GetRootsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roots);

        // Act
        var response = await CreateHandler().Handle(new GetRootsRequest(), CancellationToken.None);

        // Assert
        response.Roots.Should().HaveCount(1);
        var dto = response.Roots[0];
        dto.Id.Should().Be(1);
        dto.SharePointPath.Should().Be("/Fotky/Produkty");
        dto.DisplayName.Should().Be("Produkty");
        dto.DriveId.Should().Be("drive-abc");
        dto.RootItemId.Should().Be("item-xyz");
        dto.IsActive.Should().BeTrue();
        dto.LastIndexedAt.Should().Be(lastIndexed);
    }

    [Fact]
    public async Task Handle_MapsNullFields_WhenEntityHasNoOptionalValues()
    {
        // Arrange
        var roots = new List<PhotobankIndexRoot>
        {
            new()
            {
                Id = 2,
                SharePointPath = "/Fotky",
                DisplayName = null,
                DriveId = null,
                RootItemId = null,
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
                LastIndexedAt = null,
            }
        };
        _repoMock
            .Setup(r => r.GetRootsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roots);

        // Act
        var response = await CreateHandler().Handle(new GetRootsRequest(), CancellationToken.None);

        // Assert
        var dto = response.Roots[0];
        dto.DisplayName.Should().BeNull();
        dto.DriveId.Should().BeNull();
        dto.RootItemId.Should().BeNull();
        dto.LastIndexedAt.Should().BeNull();
        dto.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoRootsExist()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetRootsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PhotobankIndexRoot>());

        // Act
        var response = await CreateHandler().Handle(new GetRootsRequest(), CancellationToken.None);

        // Assert
        response.Roots.Should().BeEmpty();
    }
}
