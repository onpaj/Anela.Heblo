using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxByCode;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class GetTransportBoxByCodeHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ILogger<GetTransportBoxByCodeHandler>> _loggerMock;
    private readonly GetTransportBoxByCodeHandler _handler;

    public GetTransportBoxByCodeHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _loggerMock = new Mock<ILogger<GetTransportBoxByCodeHandler>>();
        _handler = new GetTransportBoxByCodeHandler(_loggerMock.Object, _repositoryMock.Object, _catalogRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_EmptyBoxCode_ReturnsFailureResponse()
    {
        // Arrange
        var request = new GetTransportBoxByCodeRequest { BoxCode = "" };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
        result.TransportBox.Should().BeNull();
    }

    [Fact]
    public async Task Handle_BoxNotFound_ReturnsFailureResponse()
    {
        // Arrange
        var request = new GetTransportBoxByCodeRequest { BoxCode = "B999" };

        _repositoryMock
            .Setup(x => x.GetByCodeAsync("B999"))
            .ReturnsAsync((TransportBox?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxNotFound);
        result.TransportBox.Should().BeNull();
    }

    [Fact]
    public async Task Handle_BoxInInvalidState_ReturnsFailureResponse()
    {
        // Arrange
        var request = new GetTransportBoxByCodeRequest { BoxCode = "B001" };
        var box = CreateTestBox(TransportBoxState.Opened, "B001");

        _repositoryMock
            .Setup(x => x.GetByCodeAsync("B001"))
            .ReturnsAsync(box);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxStateChangeError);
        result.TransportBox.Should().BeNull();
    }

    [Theory]
    [InlineData(TransportBoxState.Reserve)]
    [InlineData(TransportBoxState.InTransit)]
    public async Task Handle_BoxInValidState_ReturnsSuccessResponse(TransportBoxState state)
    {
        // Arrange
        var request = new GetTransportBoxByCodeRequest { BoxCode = "B001" };
        var box = CreateTestBoxWithItems(state, "B001");

        _repositoryMock
            .Setup(x => x.GetByCodeAsync("B001"))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(box.Id))
            .ReturnsAsync(box);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.TransportBox.Should().NotBeNull();
        result.TransportBox!.Code.Should().Be("B001");
        result.TransportBox.State.Should().Be(state.ToString());
        result.TransportBox.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_FailedToLoadDetailedBox_ReturnsFailureResponse()
    {
        // Arrange
        var request = new GetTransportBoxByCodeRequest { BoxCode = "B001" };
        var box = CreateTestBox(TransportBoxState.Reserve, "B001");

        _repositoryMock
            .Setup(x => x.GetByCodeAsync("B001"))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(box.Id))
            .ReturnsAsync((TransportBox?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.DatabaseError);
        result.TransportBox.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TrimsAndUppercasesBoxCode()
    {
        // Arrange
        var request = new GetTransportBoxByCodeRequest { BoxCode = " b001 " };

        _repositoryMock
            .Setup(x => x.GetByCodeAsync("B001"))
            .ReturnsAsync((TransportBox?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert - The important part is that we called GetByCodeAsync with uppercase "B001"
        result.Success.Should().BeFalse(); // Box not found is expected
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxNotFound);
        _repositoryMock.Verify(x => x.GetByCodeAsync("B001"), Times.Once);
    }

    private TransportBox CreateTestBox(TransportBoxState state, string code)
    {
        var box = new TransportBox();

        // Set state and code using reflection
        var stateProperty = typeof(TransportBox).GetProperty("State");
        stateProperty?.SetValue(box, state);

        var codeField = typeof(TransportBox).GetField("<Code>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        codeField?.SetValue(box, code);

        // Set Id for testing
        var idProperty = typeof(TransportBox).GetProperty("Id");
        idProperty?.SetValue(box, 1);

        return box;
    }

    private TransportBox CreateTestBoxWithItems(TransportBoxState state, string code)
    {
        var box = CreateTestBox(state, code);

        // Add a test item using reflection
        var itemsField = typeof(TransportBox).GetField("_items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (itemsField != null)
        {
            var items = (List<TransportBoxItem>)itemsField.GetValue(box)!;

            // Create a test item
            var itemType = typeof(TransportBoxItem);
            var item = Activator.CreateInstance(itemType,
                "TEST-PRODUCT",
                "Test Product",
                1.0,
                DateTime.Now,
                "TestUser");

            if (item != null)
            {
                items.Add((TransportBoxItem)item);
            }
        }

        return box;
    }
}