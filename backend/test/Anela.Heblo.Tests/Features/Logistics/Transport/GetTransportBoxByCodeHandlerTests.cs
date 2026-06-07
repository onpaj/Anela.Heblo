using Anela.Heblo.Application.Features.Logistics;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxByCode;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class GetTransportBoxByCodeHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<ILogisticsCatalogSource> _catalogSourceMock;
    private readonly Mock<ILogger<GetTransportBoxByCodeHandler>> _loggerMock;
    private readonly GetTransportBoxByCodeHandler _handler;

    public GetTransportBoxByCodeHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _catalogSourceMock = new Mock<ILogisticsCatalogSource>();
        _loggerMock = new Mock<ILogger<GetTransportBoxByCodeHandler>>();

        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<TransportBoxMappingProfile>();
        }, NullLoggerFactory.Instance);
        var mapper = config.CreateMapper();

        _handler = new GetTransportBoxByCodeHandler(_loggerMock.Object, _repositoryMock.Object, _catalogSourceMock.Object, mapper);
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
    public async Task Handle_BoxInInvalidState_ReturnsSuccessResponseWithNotReceivable()
    {
        // Arrange
        var request = new GetTransportBoxByCodeRequest { BoxCode = "B001" };
        var box = CreateTestBoxWithItems(TransportBoxState.Opened, "B001");

        _repositoryMock
            .Setup(x => x.GetByCodeAsync("B001"))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(box.Id))
            .ReturnsAsync(box);

        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync("TEST-PRODUCT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogisticsCatalogItem
            {
                ProductCode = "TEST-PRODUCT",
                Image = "https://example.com/image.jpg",
                EshopStock = 10.5m
            });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.TransportBox.Should().NotBeNull();
        result.TransportBox!.Code.Should().Be("B001");
        result.TransportBox.State.Should().Be(TransportBoxState.Opened.ToString());
        result.TransportBox.IsReceivable.Should().BeFalse();
        result.TransportBox.Items.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(TransportBoxState.Reserve)]
    [InlineData(TransportBoxState.InTransit)]
    [InlineData(TransportBoxState.Quarantine)]
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

        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync("TEST-PRODUCT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogisticsCatalogItem
            {
                ProductCode = "TEST-PRODUCT",
                Image = "https://example.com/image.jpg",
                EshopStock = 10.5m
            });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.TransportBox.Should().NotBeNull();
        result.TransportBox!.Code.Should().Be("B001");
        result.TransportBox.State.Should().Be(state.ToString());
        result.TransportBox.IsReceivable.Should().BeTrue();
        result.TransportBox.Items.Should().HaveCount(1);
        result.TransportBox.Items[0].ImageUrl.Should().Be("https://example.com/image.jpg");
        result.TransportBox.Items[0].OnStock.Should().Be(10.5m);
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
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxNotFound);
        _repositoryMock.Verify(x => x.GetByCodeAsync("B001"), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidBox_CallsGetCatalogItemAsyncPerUniqueProductCode()
    {
        // Arrange
        var request = new GetTransportBoxByCodeRequest { BoxCode = "B001" };
        var box = CreateTestBoxWithItems(TransportBoxState.Reserve, "B001");

        _repositoryMock
            .Setup(x => x.GetByCodeAsync("B001"))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(box.Id))
            .ReturnsAsync(box);

        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync("TEST-PRODUCT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogisticsCatalogItem
            {
                ProductCode = "TEST-PRODUCT",
                Image = "https://example.com/image.jpg",
                EshopStock = 10.5m
            });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _catalogSourceMock.Verify(x => x.GetCatalogItemAsync("TEST-PRODUCT", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ItemMissingFromCatalog_LogsWarningAndLeavesItemUnpopulated()
    {
        // Arrange
        var request = new GetTransportBoxByCodeRequest { BoxCode = "B001" };
        var box = CreateTestBoxWithItems(TransportBoxState.Reserve, "B001");

        _repositoryMock
            .Setup(x => x.GetByCodeAsync("B001"))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(box.Id))
            .ReturnsAsync(box);

        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LogisticsCatalogItem?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TransportBox.Should().NotBeNull();
        result.TransportBox!.Items.Should().HaveCount(1);
        result.TransportBox.Items[0].ImageUrl.Should().BeNull();
        result.TransportBox.Items[0].OnStock.Should().Be(0);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("TEST-PRODUCT")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce());
    }

    private TransportBox CreateTestBox(TransportBoxState state, string code)
    {
        var box = new TransportBox();

        var stateProperty = typeof(TransportBox).GetProperty("State");
        stateProperty?.SetValue(box, state);

        var codeField = typeof(TransportBox).GetField("<Code>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        codeField?.SetValue(box, code);

        var idProperty = typeof(TransportBox).GetProperty("Id");
        idProperty?.SetValue(box, 1);

        return box;
    }

    private TransportBox CreateTestBoxWithItems(TransportBoxState state, string code)
    {
        var box = CreateTestBox(state, code);

        var itemsField = typeof(TransportBox).GetField("_items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (itemsField != null)
        {
            var items = (List<TransportBoxItem>)itemsField.GetValue(box)!;

            var itemType = typeof(TransportBoxItem);
            var item = Activator.CreateInstance(itemType,
                "TEST-PRODUCT",
                "Test Product",
                1.0,
                DateTime.Now,
                "TestUser",
                null,
                null,
                null);

            if (item != null)
            {
                items.Add((TransportBoxItem)item);
            }
        }

        return box;
    }
}
