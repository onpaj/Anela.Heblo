using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.UseCases.CreatePurchaseOrder;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class CreatePurchaseOrderHandlerTests
{
    private readonly Mock<ILogger<CreatePurchaseOrderHandler>> _loggerMock;
    private readonly Mock<IPurchaseOrderRepository> _repositoryMock;
    private readonly Mock<IPurchaseOrderNumberGenerator> _orderNumberGeneratorMock;
    private readonly Mock<IMaterialCatalogService> _materialCatalogMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ISupplierRepository> _supplierRepositoryMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly CreatePurchaseOrderHandler _handler;

    private static readonly DateTimeOffset FixedNow = new(2024, 8, 2, 14, 30, 22, TimeSpan.Zero);

    private const long ValidSupplierId = 1;
    private const string ValidSupplierName = "Test Supplier";
    private const string ValidOrderDate = "2024-08-02";
    private const string ValidExpectedDeliveryDate = "2024-08-16";
    private const string ValidNotes = "Test purchase order";
    private const string ValidMaterialId = "MAT001";
    private const string ValidCode = "CODE001";
    private const string ValidName = "Test Material";
    private const string GeneratedOrderNumber = "PO-2024-001";

    public CreatePurchaseOrderHandlerTests()
    {
        _loggerMock = new Mock<ILogger<CreatePurchaseOrderHandler>>();
        _repositoryMock = new Mock<IPurchaseOrderRepository>();
        _orderNumberGeneratorMock = new Mock<IPurchaseOrderNumberGenerator>();
        _materialCatalogMock = new Mock<IMaterialCatalogService>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _supplierRepositoryMock = new Mock<ISupplierRepository>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(FixedNow);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", "Test User", "test@example.com", true));

        _supplierRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidSupplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Supplier { Id = ValidSupplierId, Name = ValidSupplierName, Code = "SUP001" });

        _repositoryMock
            .Setup(x => x.OrderNumberExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _handler = new CreatePurchaseOrderHandler(
            _loggerMock.Object,
            _repositoryMock.Object,
            _orderNumberGeneratorMock.Object,
            _materialCatalogMock.Object,
            _currentUserServiceMock.Object,
            _supplierRepositoryMock.Object,
            _timeProviderMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ShouldCreatePurchaseOrderAndReturnResponse()
    {
        var request = CreateValidRequest();
        _orderNumberGeneratorMock
            .Setup(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), It.IsAny<DateTimeOffset>(), It.IsAny<int>()))
            .Returns(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder po, CancellationToken ct) => po);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.OrderNumber.Should().Be(GeneratedOrderNumber);
        result.SupplierName.Should().Be(ValidSupplierName);
        result.OrderDate.Should().Be(DateTime.Parse(ValidOrderDate));
        result.ExpectedDeliveryDate.Should().Be(DateTime.Parse(ValidExpectedDeliveryDate));
        result.Status.Should().Be("Draft");
        result.Notes.Should().Be(ValidNotes);
        result.TotalAmount.Should().Be(255.00m);
        result.Lines.Should().HaveCount(1);
        result.CreatedBy.Should().Be("Test User");

        var line = result.Lines.First();
        line.MaterialId.Should().Be(ValidMaterialId);
        line.MaterialName.Should().Be(ValidName);
        line.Quantity.Should().Be(10);
        line.UnitPrice.Should().Be(25.50m);
        line.LineTotal.Should().Be(255.00m);
        line.Notes.Should().Be("Line notes");
    }

    [Fact]
    public async Task Handle_WithMultipleLines_ShouldCreateOrderWithAllLines()
    {
        var request = new CreatePurchaseOrderRequest
        {
            SupplierId = ValidSupplierId,
            OrderDate = ValidOrderDate,
            ExpectedDeliveryDate = ValidExpectedDeliveryDate,
            Notes = ValidNotes,
            OrderNumber = null, // OrderNumber - let system generate
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { MaterialId = ValidMaterialId, Name = ValidName, Quantity = 10, UnitPrice = 25.50m, Notes = "Line 1" },
                new() { MaterialId = "MAT002", Name = "Test Material 2", Quantity = 5, UnitPrice = 100.00m, Notes = "Line 2" },
                new() { MaterialId = "MAT003", Name = "Test Material 3", Quantity = 2, UnitPrice = 75.25m, Notes = "Line 3" }
            }
        };

        _orderNumberGeneratorMock
            .Setup(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), It.IsAny<DateTimeOffset>(), It.IsAny<int>()))
            .Returns(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder po, CancellationToken ct) => po);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Lines.Should().HaveCount(3);
        result.TotalAmount.Should().Be(905.50m);
    }

    [Fact]
    public async Task Handle_WithEmptyLines_ShouldCreateOrderWithZeroTotal()
    {
        var request = new CreatePurchaseOrderRequest
        {
            SupplierId = ValidSupplierId,
            OrderDate = ValidOrderDate,
            ExpectedDeliveryDate = ValidExpectedDeliveryDate,
            Notes = ValidNotes,
            OrderNumber = null, // OrderNumber - let system generate
            Lines = new List<CreatePurchaseOrderLineRequest>()
        };

        _orderNumberGeneratorMock
            .Setup(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), It.IsAny<DateTimeOffset>(), It.IsAny<int>()))
            .Returns(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder po, CancellationToken ct) => po);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Lines.Should().BeEmpty();
        result.TotalAmount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ShouldCallOrderNumberGenerator()
    {
        var request = CreateValidRequest();
        _orderNumberGeneratorMock
            .Setup(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), It.IsAny<DateTimeOffset>(), It.IsAny<int>()))
            .Returns(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder po, CancellationToken ct) => po);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _handler.Handle(request, CancellationToken.None);

        _orderNumberGeneratorMock.Verify(
            x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), It.IsAny<DateTimeOffset>(), 1),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldAddOrderToRepositoryAndSaveChanges()
    {
        var request = CreateValidRequest();
        _orderNumberGeneratorMock
            .Setup(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), It.IsAny<DateTimeOffset>(), It.IsAny<int>()))
            .Returns(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder po, CancellationToken ct) => po);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(
            x => x.AddAsync(It.Is<PurchaseOrder>(po =>
                po.OrderNumber == GeneratedOrderNumber &&
                po.SupplierName == ValidSupplierName &&
                po.OrderDate == DateTime.Parse(ValidOrderDate) &&
                po.Lines.Count == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _repositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldLogInformationMessages()
    {
        var request = CreateValidRequest();
        _orderNumberGeneratorMock
            .Setup(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), It.IsAny<DateTimeOffset>(), It.IsAny<int>()))
            .Returns(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder po, CancellationToken ct) => po);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _handler.Handle(request, CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Creating new purchase order for supplier")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Purchase order") && v.ToString()!.Contains("created successfully")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenRepositoryThrows_ShouldPropagateException()
    {
        var request = CreateValidRequest();
        _orderNumberGeneratorMock
            .Setup(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), It.IsAny<DateTimeOffset>(), It.IsAny<int>()))
            .Returns(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        var action = async () => await _handler.Handle(request, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database error");
    }

    [Fact]
    public async Task Handle_WhenGeneratedCandidateCollides_ShouldRetryWithNextAttempt()
    {
        var request = CreateValidRequest();
        var firstCandidate = "PO20240802-143022";
        var secondCandidate = "PO20240802-143022-2";

        _orderNumberGeneratorMock
            .Setup(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), FixedNow, 1))
            .Returns(firstCandidate);
        _orderNumberGeneratorMock
            .Setup(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), FixedNow, 2))
            .Returns(secondCandidate);

        _repositoryMock
            .Setup(x => x.OrderNumberExistsAsync(firstCandidate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repositoryMock
            .Setup(x => x.OrderNumberExistsAsync(secondCandidate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder po, CancellationToken ct) => po);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.OrderNumber.Should().Be(secondCandidate);
        result.Success.Should().BeTrue();
        _orderNumberGeneratorMock.Verify(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), FixedNow, 1), Times.Once);
        _orderNumberGeneratorMock.Verify(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), FixedNow, 2), Times.Once);
        _orderNumberGeneratorMock.Verify(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), FixedNow, 3), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAllCandidatesCollide_ShouldReturnPurchaseOrderNumberGenerationFailed()
    {
        var request = CreateValidRequest();

        _orderNumberGeneratorMock
            .Setup(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), It.IsAny<DateTimeOffset>(), It.IsAny<int>()))
            .Returns((DateTime orderDate, DateTimeOffset now, int attempt) => $"PO-collision-{attempt}");

        _repositoryMock
            .Setup(x => x.OrderNumberExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PurchaseOrderNumberGenerationFailed);

        _repositoryMock.Verify(x => x.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithExplicitOrderNumber_ShouldSkipGeneratorAndExistenceCheck()
    {
        var request = CreateValidRequest();
        request.OrderNumber = "PO-CLIENT-SUPPLIED";

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder po, CancellationToken ct) => po);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.OrderNumber.Should().Be("PO-CLIENT-SUPPLIED");
        _orderNumberGeneratorMock.Verify(
            x => x.GenerateCandidate(It.IsAny<DateTime>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>()),
            Times.Never);
        _repositoryMock.Verify(
            x => x.OrderNumberExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithContactVia_ShouldCreatePurchaseOrderWithContactVia()
    {
        var request = CreateValidRequest();
        request.ContactVia = ContactVia.Email;

        _orderNumberGeneratorMock
            .Setup(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), It.IsAny<DateTimeOffset>(), It.IsAny<int>()))
            .Returns(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder po, CancellationToken ct) => po);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.ContactVia.Should().Be(ContactVia.Email);
    }

    [Fact]
    public async Task Handle_WithoutContactVia_ShouldCreatePurchaseOrderWithNullContactVia()
    {
        var request = CreateValidRequest();
        request.ContactVia = null;

        _orderNumberGeneratorMock
            .Setup(x => x.GenerateCandidate(DateTime.Parse(ValidOrderDate), It.IsAny<DateTimeOffset>(), It.IsAny<int>()))
            .Returns(GeneratedOrderNumber);

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder po, CancellationToken ct) => po);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.ContactVia.Should().BeNull();
    }

    private static CreatePurchaseOrderRequest CreateValidRequest()
    {
        return new CreatePurchaseOrderRequest
        {
            SupplierId = ValidSupplierId,
            OrderDate = ValidOrderDate,
            ExpectedDeliveryDate = ValidExpectedDeliveryDate,
            Notes = ValidNotes,
            OrderNumber = null, // OrderNumber - let system generate
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { MaterialId = ValidMaterialId, Name = ValidName, Quantity = 10, UnitPrice = 25.50m, Notes = "Line notes" }
            }
        };
    }
}