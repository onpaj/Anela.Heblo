using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufactureOrder;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class CreateManufactureOrderHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IProductNameFormatter> _productNameFormatterMock;
    private readonly CreateManufactureOrderHandler _handler;

    private const string ValidProductCode = "SEMI001";
    private const string ValidProductName = "Test Semi Product";
    private const double ValidOriginalBatchSize = 1000.0;
    private const double ValidNewBatchSize = 1500.0;
    private const double ValidScaleFactor = 1.5;
    private const string ValidResponsiblePerson = "John Doe";
    private const string GeneratedOrderNumber = "MO-2024-001";

    public CreateManufactureOrderHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _productNameFormatterMock = new Mock<IProductNameFormatter>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", "Test User", "test@example.com", true));

        _productNameFormatterMock.Setup(s => s.ShortProductName(It.IsAny<string>())).Returns<string>(_ => ValidProductName);
        _handler = new CreateManufactureOrderHandler(
            _repositoryMock.Object,
            _productNameFormatterMock.Object,
            _catalogRepositoryMock.Object,
            _currentUserServiceMock.Object,
            TimeProvider.System);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ShouldCreateManufactureOrderAndReturnResponse()
    {
        var request = CreateValidRequest();

        // Mock catalog repository to return a valid catalog item
        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateValidCatalogItem());

        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                order.Id = 1;
                return order;
            });

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Id.Should().Be(1);
        result.OrderNumber.Should().Be(GeneratedOrderNumber);
    }

    [Fact]
    public async Task Handle_ShouldCreateOrderWithCorrectBasicProperties()
    {
        var request = CreateValidRequest();
        ManufactureOrder? capturedOrder = null;

        // Mock catalog repository to return a valid catalog item
        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateValidCatalogItem());

        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                capturedOrder = order;
                order.Id = 1;
                return order;
            });

        await _handler.Handle(request, CancellationToken.None);

        capturedOrder.Should().NotBeNull();
        capturedOrder!.OrderNumber.Should().Be(GeneratedOrderNumber);
        capturedOrder.CreatedByUser.Should().Be("Test User");
        capturedOrder.ResponsiblePerson.Should().Be(ValidResponsiblePerson);
        capturedOrder.SemiProductPlannedDate.Should().Be(request.SemiProductPlannedDate);
        capturedOrder.ProductPlannedDate.Should().Be(request.ProductPlannedDate);
        capturedOrder.State.Should().Be(ManufactureOrderState.Draft);
        capturedOrder.StateChangedByUser.Should().Be("Test User");
    }

    [Fact]
    public async Task Handle_ShouldCreateSemiProductWithCorrectProperties()
    {
        var request = CreateValidRequest();
        ManufactureOrder? capturedOrder = null;

        // Mock catalog repository to return a valid catalog item
        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateValidCatalogItem());

        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                capturedOrder = order;
                order.Id = 1;
                return order;
            });

        await _handler.Handle(request, CancellationToken.None);

        capturedOrder.Should().NotBeNull();
        capturedOrder!.SemiProduct.Should().NotBeNull();
        capturedOrder.SemiProduct!.ProductCode.Should().Be(ValidProductCode);
        capturedOrder.SemiProduct.ProductName.Should().Be(ValidProductName);
        capturedOrder.SemiProduct.PlannedQuantity.Should().Be((decimal)ValidNewBatchSize);
        capturedOrder.SemiProduct.ActualQuantity.Should().Be((decimal)ValidNewBatchSize);
        capturedOrder.SemiProduct.BatchMultiplier.Should().Be((decimal)ValidScaleFactor);
    }

    [Theory]
    [InlineData(1.5, 1.5)]
    [InlineData(2.0, 2.0)]
    [InlineData(0.75, 0.75)]
    [InlineData(3.333, 3.333)]
    public async Task Handle_ShouldSetCorrectBatchMultiplierFromScaleFactor(double scaleFactor, double expectedBatchMultiplier)
    {
        var request = CreateValidRequest();
        request.ScaleFactor = scaleFactor;
        ManufactureOrder? capturedOrder = null;

        // Mock catalog repository to return a valid catalog item
        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateValidCatalogItem());

        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                capturedOrder = order;
                order.Id = 1;
                return order;
            });

        await _handler.Handle(request, CancellationToken.None);

        capturedOrder.Should().NotBeNull();
        capturedOrder!.SemiProduct.Should().NotBeNull();
        capturedOrder.SemiProduct!.BatchMultiplier.Should().Be((decimal)expectedBatchMultiplier);
    }

    [Fact]
    public async Task Handle_WithMultipleProducts_ShouldCreateAllProducts()
    {
        var request = CreateValidRequest();
        request.Products.Add(new CreateManufactureOrderProductRequest
        {
            ProductCode = "PROD002",
            ProductName = "Second Product",
            PlannedQuantity = 50.0
        });

        ManufactureOrder? capturedOrder = null;

        // Mock catalog repository to return a valid catalog item
        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateValidCatalogItem());

        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                capturedOrder = order;
                order.Id = 1;
                return order;
            });

        await _handler.Handle(request, CancellationToken.None);

        capturedOrder.Should().NotBeNull();
        capturedOrder!.Products.Should().HaveCount(2);

        var firstProduct = capturedOrder.Products.First(p => p.ProductCode == "PROD001");
        firstProduct.ProductName.Should().Be("Final Product 1");
        firstProduct.PlannedQuantity.Should().Be(100.0m);
        firstProduct.SemiProductCode.Should().Be(ValidProductCode);

        var secondProduct = capturedOrder.Products.First(p => p.ProductCode == "PROD002");
        secondProduct.ProductName.Should().Be("Second Product");
        secondProduct.PlannedQuantity.Should().Be(50.0m);
        secondProduct.SemiProductCode.Should().Be(ValidProductCode);
    }

    [Fact]
    public async Task Handle_ShouldOnlyCreateProductsWithPositiveQuantity()
    {
        var request = CreateValidRequest();
        request.Products.Add(new CreateManufactureOrderProductRequest
        {
            ProductCode = "PROD002",
            ProductName = "Zero Quantity Product",
            PlannedQuantity = 0.0
        });

        ManufactureOrder? capturedOrder = null;

        // Mock catalog repository to return a valid catalog item
        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateValidCatalogItem());

        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                capturedOrder = order;
                order.Id = 1;
                return order;
            });

        await _handler.Handle(request, CancellationToken.None);

        capturedOrder.Should().NotBeNull();
        capturedOrder!.Products.Should().HaveCount(1);
        capturedOrder.Products.First().ProductCode.Should().Be("PROD001");
    }



    [Fact]
    public async Task Handle_ShouldCallGenerateOrderNumber()
    {
        var request = CreateValidRequest();

        // Mock catalog repository to return a valid catalog item
        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateValidCatalogItem());

        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                order.Id = 1;
                return order;
            });

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(
            x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldAddOrderToRepository()
    {
        var request = CreateValidRequest();

        // Mock catalog repository to return a valid catalog item
        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateValidCatalogItem());

        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                order.Id = 1;
                return order;
            });

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(
            x => x.AddOrderAsync(It.Is<ManufactureOrder>(o =>
                o.OrderNumber == GeneratedOrderNumber &&
                o.ResponsiblePerson == ValidResponsiblePerson &&
                o.State == ManufactureOrderState.Draft &&
                o.SemiProduct != null &&
                o.SemiProduct.ProductCode == ValidProductCode),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithEmptyProductsList_ShouldCreateOrderWithNoProducts()
    {
        var request = CreateValidRequest();
        request.Products = new List<CreateManufactureOrderProductRequest>();

        ManufactureOrder? capturedOrder = null;

        // Mock catalog repository to return a valid catalog item
        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateValidCatalogItem());

        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                capturedOrder = order;
                order.Id = 1;
                return order;
            });

        await _handler.Handle(request, CancellationToken.None);

        capturedOrder.Should().NotBeNull();
        capturedOrder!.Products.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenRepositoryThrows_ShouldPropagateException()
    {
        var request = CreateValidRequest();

        // Mock catalog repository to return a valid catalog item
        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateValidCatalogItem());

        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        var action = async () => await _handler.Handle(request, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database error");
    }

    [Fact]
    public async Task Handle_WhenOrderNumberGeneratorThrows_ShouldPropagateException()
    {
        var request = CreateValidRequest();

        // Mock catalog repository to return a valid catalog item
        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateValidCatalogItem());

        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Order number generation failed"));

        var action = async () => await _handler.Handle(request, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Order number generation failed");
    }

    private static CreateManufactureOrderRequest CreateValidRequest()
    {
        return new CreateManufactureOrderRequest
        {
            ProductCode = ValidProductCode,
            ProductName = ValidProductName,
            OriginalBatchSize = ValidOriginalBatchSize,
            NewBatchSize = ValidNewBatchSize,
            ScaleFactor = ValidScaleFactor,
            SemiProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            ProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14)),
            ResponsiblePerson = ValidResponsiblePerson,
            Products = new List<CreateManufactureOrderProductRequest>
            {
                new()
                {
                    ProductCode = "PROD001",
                    ProductName = "Final Product 1",
                    PlannedQuantity = 100.0
                }
            }
        };
    }

    private static CatalogAggregate CreateValidCatalogItem()
    {
        return new CatalogAggregate
        {
            ProductCode = ValidProductCode,
            ProductName = ValidProductName,
            Properties = new CatalogProperties
            {
                ExpirationMonths = 12
            }
        };
    }
}