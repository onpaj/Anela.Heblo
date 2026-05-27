using Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class UpdateProductCompositionOrderHandlerTests
{
    private readonly Mock<IManufactureClient> _manufactureClientMock = new();
    private readonly Mock<ILogger<UpdateProductCompositionOrderHandler>> _loggerMock = new();
    private readonly UpdateProductCompositionOrderHandler _handler;

    public UpdateProductCompositionOrderHandlerTests()
    {
        _handler = new UpdateProductCompositionOrderHandler(
            _manufactureClientMock.Object,
            _loggerMock.Object);
    }

    private static ManufactureTemplate BuildTemplate(params (int TemplateId, string Code)[] ingredients)
    {
        return new ManufactureTemplate
        {
            ProductCode = "PRD1",
            Ingredients = ingredients.Select(i => new Ingredient
            {
                TemplateId = i.TemplateId,
                ProductCode = i.Code,
                ProductName = i.Code
            }).ToList()
        };
    }

    [Fact]
    public async Task Handle_ValidOrder_CallsSetBomItemsOrderAsync_WithCorrectPairs()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"), (200, "MAT-B"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _manufactureClientMock
            .Setup(x => x.SetBomItemsOrderAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<(int, int)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1 },
                new() { IngredientProductCode = "MAT-B", SortOrder = 2 },
            }
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.UpdatedCount.Should().Be(2);
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int)>>(seq =>
                    seq.Any(t => t.Item1 == 100 && t.Item2 == 1) &&
                    seq.Any(t => t.Item1 == 200 && t.Item2 == 2)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TemplateNull_ReturnsZeroAndDoesNotCallSetOrder()
    {
        // Arrange
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate?)null);

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1 },
            }
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.UpdatedCount.Should().Be(0);
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAsync(It.IsAny<string>(), It.IsAny<IEnumerable<(int, int)>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_RequestItemCodeNotInTemplate_IsSkippedWithWarning()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A")); // Only MAT-A in BoM
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _manufactureClientMock
            .Setup(x => x.SetBomItemsOrderAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<(int, int)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1 },
                new() { IngredientProductCode = "MAT-GHOST", SortOrder = 2 }, // not in BoM
            }
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.UpdatedCount.Should().Be(1); // only MAT-A was mapped
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int)>>(seq =>
                    seq.Count() == 1 && seq.Single().Item1 == 100),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyOrder_CallsSetBomItemsOrderWithEmptySequence()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"), (200, "MAT-B"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _manufactureClientMock
            .Setup(x => x.SetBomItemsOrderAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<(int, int)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>()
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.UpdatedCount.Should().Be(0);
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int)>>(seq => !seq.Any()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
