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

    private void SetupSetBomItemsOrderAndPhaseAsync()
    {
        _manufactureClientMock
            .Setup(x => x.SetBomItemsOrderAndPhaseAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<(int, int, string?)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Handle_ValidOrder_CallsSetBomItemsOrderAndPhaseAsync_WithCorrectPairs()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"), (200, "MAT-B"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        SetupSetBomItemsOrderAndPhaseAsync();

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
            x => x.SetBomItemsOrderAndPhaseAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int, string?)>>(seq =>
                    seq.Any(t => t.Item1 == 100 && t.Item2 == 1) &&
                    seq.Any(t => t.Item1 == 200 && t.Item2 == 2)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TemplateNull_ReturnsZeroAndDoesNotCallSetBomItemsOrderAndPhase()
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
            x => x.SetBomItemsOrderAndPhaseAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<(int, int, string?)>>(),
                It.IsAny<CancellationToken>()),
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
        SetupSetBomItemsOrderAndPhaseAsync();

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
        response.UpdatedCount.Should().Be(1);
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAndPhaseAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int, string?)>>(seq =>
                    seq.Count() == 1 && seq.Single().Item1 == 100),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyOrder_SkipsSetBomItemsOrderAndPhaseAndReturnsZero()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"), (200, "MAT-B"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

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
            x => x.SetBomItemsOrderAndPhaseAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<(int, int, string?)>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_PhaseLabelLowercase_IsNormalizedToUppercase()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"), (200, "MAT-B"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        SetupSetBomItemsOrderAndPhaseAsync();

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1, PhaseLabel = "a" }, // lowercase
                new() { IngredientProductCode = "MAT-B", SortOrder = 2, PhaseLabel = "A" }, // already uppercase
            }
        };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert: both normalised to "A"
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAndPhaseAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int, string?)>>(seq =>
                    seq.Any(t => t.Item1 == 100 && t.Item3 == "A") &&
                    seq.Any(t => t.Item1 == 200 && t.Item3 == "A")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidPhaseLabel_MultipleChars_NormalizesToNull()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        SetupSetBomItemsOrderAndPhaseAsync();

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1, PhaseLabel = "AB" }, // 2 chars — invalid
            }
        };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert: "AB" normalized to null
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAndPhaseAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int, string?)>>(seq =>
                    seq.Single().Item3 == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidPhaseLabel_NonLetterChar_NormalizesToNull()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        SetupSetBomItemsOrderAndPhaseAsync();

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1, PhaseLabel = "1" }, // digit — invalid
            }
        };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert: "1" normalized to null
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAndPhaseAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int, string?)>>(seq =>
                    seq.Single().Item3 == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NullPhaseLabel_ForwardedAsNull()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        SetupSetBomItemsOrderAndPhaseAsync();

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1, PhaseLabel = null },
            }
        };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAndPhaseAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int, string?)>>(seq =>
                    seq.Single().Item3 == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
