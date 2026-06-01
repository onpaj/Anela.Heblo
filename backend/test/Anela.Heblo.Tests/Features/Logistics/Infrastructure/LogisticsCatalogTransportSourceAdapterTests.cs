using System.Linq.Expressions;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Infrastructure;

public class LogisticsCatalogTransportSourceAdapterTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly LogisticsCatalogTransportSourceAdapter _adapter;

    public LogisticsCatalogTransportSourceAdapterTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _adapter = new LogisticsCatalogTransportSourceAdapter(_repositoryMock.Object);
    }

    [Fact]
    public async Task GetProductsInTransportAsync_UsesIsInTransportPredicate()
    {
        // Arrange
        Expression<Func<TransportBox, bool>>? capturedPredicate = null;

        _repositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<TransportBox, bool>>>(), true, It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<TransportBox, bool>>, bool, CancellationToken>((predicate, _, _) =>
            {
                capturedPredicate = predicate;
            })
            .ReturnsAsync(new List<TransportBox>());

        // Act
        await _adapter.GetProductsInTransportAsync(CancellationToken.None);

        // Assert
        capturedPredicate.Should().BeSameAs(TransportBox.IsInTransportPredicate);
    }

    [Fact]
    public async Task GetProductsInTransportAsync_PassesIncludeDetailsTrue()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<TransportBox, bool>>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox>());

        // Act
        await _adapter.GetProductsInTransportAsync(CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            x => x.FindAsync(
                It.IsAny<Expression<Func<TransportBox, bool>>>(),
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProductsInTransportAsync_AggregatesItemsByProductCode()
    {
        // Arrange
        var box1 = CreateBoxWithItems(new[]
        {
            ("PROD-001", "Product 1", 10.5),
            ("PROD-002", "Product 2", 5.0)
        });

        var box2 = CreateBoxWithItems(new[]
        {
            ("PROD-001", "Product 1", 3.5),
            ("PROD-003", "Product 3", 8.0)
        });

        _repositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<TransportBox, bool>>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box1, box2 });

        // Act
        var result = await _adapter.GetProductsInTransportAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result["PROD-001"].Should().Be(14); // 10.5 + 3.5 = 14
        result["PROD-002"].Should().Be(5);  // 5.0 = 5
        result["PROD-003"].Should().Be(8);  // 8.0 = 8
    }

    [Fact]
    public async Task GetProductsInTransportAsync_ReturnsEmptyDictionaryWhenNoBoxes()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<TransportBox, bool>>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox>());

        // Act
        var result = await _adapter.GetProductsInTransportAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductsInReserveAsync_UsesIsInReservePredicate()
    {
        // Arrange
        Expression<Func<TransportBox, bool>>? capturedPredicate = null;

        _repositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<TransportBox, bool>>>(), true, It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<TransportBox, bool>>, bool, CancellationToken>((predicate, _, _) =>
            {
                capturedPredicate = predicate;
            })
            .ReturnsAsync(new List<TransportBox>());

        // Act
        await _adapter.GetProductsInReserveAsync(CancellationToken.None);

        // Assert
        capturedPredicate.Should().BeSameAs(TransportBox.IsInReservePredicate);
    }

    [Fact]
    public async Task GetProductsInReserveAsync_AggregatesItemsByProductCode()
    {
        // Arrange
        var box1 = CreateBoxWithItems(new[]
        {
            ("PROD-001", "Product 1", 15.0),
            ("PROD-002", "Product 2", 20.0)
        });

        _repositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<TransportBox, bool>>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box1 });

        // Act
        var result = await _adapter.GetProductsInReserveAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result["PROD-001"].Should().Be(15);
        result["PROD-002"].Should().Be(20);
    }

    [Fact]
    public async Task GetProductsInQuarantineAsync_UsesIsInQuarantinePredicate()
    {
        // Arrange
        Expression<Func<TransportBox, bool>>? capturedPredicate = null;

        _repositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<TransportBox, bool>>>(), true, It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<TransportBox, bool>>, bool, CancellationToken>((predicate, _, _) =>
            {
                capturedPredicate = predicate;
            })
            .ReturnsAsync(new List<TransportBox>());

        // Act
        await _adapter.GetProductsInQuarantineAsync(CancellationToken.None);

        // Assert
        capturedPredicate.Should().BeSameAs(TransportBox.IsInQuarantinePredicate);
    }

    [Fact]
    public async Task GetProductsInQuarantineAsync_AggregatesItemsByProductCode()
    {
        // Arrange
        var box1 = CreateBoxWithItems(new[]
        {
            ("PROD-A", "Product A", 7.5),
            ("PROD-B", "Product B", 2.5)
        });

        var box2 = CreateBoxWithItems(new[]
        {
            ("PROD-A", "Product A", 12.0)
        });

        _repositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<TransportBox, bool>>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox> { box1, box2 });

        // Act
        var result = await _adapter.GetProductsInQuarantineAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result["PROD-A"].Should().Be(19); // 7.5 + 12.0 = 19
        result["PROD-B"].Should().Be(3);  // 2.5 = 3
    }

    [Fact]
    public async Task GetProductsInQuarantineAsync_ReturnsEmptyDictionaryWhenNoBoxes()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<TransportBox, bool>>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox>());

        // Act
        var result = await _adapter.GetProductsInQuarantineAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Helper method to create a TransportBox with items.
    /// Creates an Opened box and adds the specified items to it.
    /// </summary>
    private static TransportBox CreateBoxWithItems(params (string productCode, string productName, double amount)[] items)
    {
        var box = new TransportBox();

        // Use reflection to set State to Opened (required before AddItem)
        var stateProperty = typeof(TransportBox).GetProperty("State");
        stateProperty?.SetValue(box, TransportBoxState.Opened);

        // Set a box code (required for some box operations)
        var codeField = typeof(TransportBox).GetField("<Code>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        codeField?.SetValue(box, "B001");

        // Add items to the box
        foreach (var (productCode, productName, amount) in items)
        {
            box.AddItem(productCode, productName, amount, DateTime.UtcNow, "test");
        }

        return box;
    }
}
