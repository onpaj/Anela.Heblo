using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateBoMIngredientAmount;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.UseCases;

public class UpdateBoMIngredientAmountHandlerTests
{
    private readonly Mock<IManufactureClient> _manufactureClientMock;
    private readonly Mock<IManufactureErrorTransformer> _errorTransformerMock;
    private readonly Mock<ILogger<UpdateBoMIngredientAmountHandler>> _loggerMock;
    private readonly UpdateBoMIngredientAmountHandler _handler;

    private const string ProductCode = "PROD001";
    private const string IngredientCode = "ING001";
    private const double NewAmount = 12.5;

    public UpdateBoMIngredientAmountHandlerTests()
    {
        _manufactureClientMock = new Mock<IManufactureClient>();
        _errorTransformerMock = new Mock<IManufactureErrorTransformer>();
        _loggerMock = new Mock<ILogger<UpdateBoMIngredientAmountHandler>>();

        _handler = new UpdateBoMIngredientAmountHandler(
            _manufactureClientMock.Object,
            _errorTransformerMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenClientSucceeds_ReturnsSuccess()
    {
        // Arrange
        _manufactureClientMock
            .Setup(x => x.UpdateBoMIngredientAmountAsync(
                ProductCode, IngredientCode, NewAmount, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateBoMIngredientAmountRequest
        {
            ProductCode = ProductCode,
            IngredientCode = IngredientCode,
            NewAmount = NewAmount
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.UserMessage.Should().BeNull();

        _manufactureClientMock.Verify(
            x => x.UpdateBoMIngredientAmountAsync(
                ProductCode, IngredientCode, NewAmount, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenClientThrows_ReturnsErrorResponseWithUserMessage()
    {
        // Arrange
        var exception = new InvalidOperationException("ERP connection failed");
        const string transformedMessage = "Chyba při aktualizaci kusovníku";

        _manufactureClientMock
            .Setup(x => x.UpdateBoMIngredientAmountAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        _errorTransformerMock
            .Setup(x => x.Transform(exception))
            .Returns(transformedMessage);

        var request = new UpdateBoMIngredientAmountRequest
        {
            ProductCode = ProductCode,
            IngredientCode = IngredientCode,
            NewAmount = NewAmount
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.UserMessage.Should().Be(transformedMessage);

        _errorTransformerMock.Verify(x => x.Transform(exception), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCancelled_PropagatesOperationCanceledException()
    {
        // Arrange
        _manufactureClientMock
            .Setup(x => x.UpdateBoMIngredientAmountAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var request = new UpdateBoMIngredientAmountRequest
        {
            ProductCode = ProductCode,
            IngredientCode = IngredientCode,
            NewAmount = NewAmount
        };

        // Act
        var act = async () => await _handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();

        _errorTransformerMock.Verify(x => x.Transform(It.IsAny<Exception>()), Times.Never);
    }
}
