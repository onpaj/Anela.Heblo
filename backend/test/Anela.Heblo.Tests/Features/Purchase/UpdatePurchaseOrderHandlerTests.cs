using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public sealed class UpdatePurchaseOrderHandlerTests
{
    private readonly Mock<ILogger<UpdatePurchaseOrderHandler>> _loggerMock;
    private readonly Mock<IPurchaseOrderRepository> _repositoryMock;
    private readonly Mock<IMaterialCatalogService> _materialCatalogMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ISupplierRepository> _supplierRepositoryMock;
    private readonly UpdatePurchaseOrderHandler _handler;

    private const long ValidSupplierId = 1;
    private const string ValidSupplierName = "Test Supplier";
    private const string ValidMaterialId = "MAT001";
    private const string ValidMaterialName = "Test Material";
    private const string NewMaterialId = "MAT002";
    private const string NewMaterialName = "New Material";

    public UpdatePurchaseOrderHandlerTests()
    {
        _loggerMock = new Mock<ILogger<UpdatePurchaseOrderHandler>>();
        _repositoryMock = new Mock<IPurchaseOrderRepository>();
        _materialCatalogMock = new Mock<IMaterialCatalogService>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _supplierRepositoryMock = new Mock<ISupplierRepository>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", "Test User", "test@example.com", true));

        _supplierRepositoryMock
            .Setup(x => x.GetByIdAsync(ValidSupplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Supplier { Id = ValidSupplierId, Name = ValidSupplierName, Code = "SUP001" });

        _handler = new UpdatePurchaseOrderHandler(
            _loggerMock.Object,
            _repositoryMock.Object,
            _materialCatalogMock.Object,
            _currentUserServiceMock.Object,
            _supplierRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WithUpdateAndAddLines_BatchesCatalogLookupOnce()
    {
        // Arrange
        var existingOrder = new PurchaseOrder(
            "PO-2024-001",
            ValidSupplierId,
            ValidSupplierName,
            DateTime.UtcNow,
            null,
            null,
            "Test notes",
            "system");

        existingOrder.AddLine(ValidMaterialId, ValidMaterialName, 10, 25.50m, "Line notes");
        var existingLineId = existingOrder.Lines.First().Id;

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(existingOrder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Handler calls GetByIdsAsync once with all material IDs before processing lines
        _materialCatalogMock
            .Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken ct) =>
                (IReadOnlyDictionary<string, MaterialInfo>)ids.ToDictionary(
                    id => id,
                    id => new MaterialInfo { ProductCode = id, ProductName = $"Material {id}" }));

        var request = new UpdatePurchaseOrderRequest
        {
            Id = existingOrder.Id,
            SupplierId = ValidSupplierId,
            ExpectedDeliveryDate = DateTime.UtcNow.AddDays(30),
            ContactVia = null,
            Notes = "Updated notes",
            OrderNumber = null,
            Lines = new List<UpdatePurchaseOrderLineRequest>
            {
                new() { Id = existingLineId, MaterialId = ValidMaterialId, Name = ValidMaterialName, Quantity = 15, UnitPrice = 30.00m, Notes = "Updated line" },
                new() { Id = null, MaterialId = NewMaterialId, Name = NewMaterialName, Quantity = 5, UnitPrice = 50.00m, Notes = "New line" }
            }
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Lines.Should().HaveCount(2);
        response.Lines.Should().OnlyContain(line => !string.IsNullOrEmpty(line.MaterialName));

        // Catalog must be queried exactly once (batch) — not per-line, and not during response mapping
        _materialCatalogMock.Verify(
            x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "Catalog should be queried once as a batch before line processing, not per-line or during response mapping");
    }
}
