using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class DataQualityStockOperationQueryAdapterTests
{
    private readonly Mock<IStockUpOperationRepository> _repository = new();

    private DataQualityStockOperationQueryAdapter CreateAdapter() => new(_repository.Object);

    private static StockUpOperation CreateOperation(
        string documentNumber,
        string productCode,
        int amount,
        StockUpOperationState state,
        DateTime createdAt,
        string? errorMessage = null)
    {
        var op = new StockUpOperation(documentNumber, productCode, amount, StockUpSourceType.TransportBox, 1);
        // Set CreatedAt explicitly
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(op, createdAt);

        // Transition to desired state using domain methods
        if (state != StockUpOperationState.Pending)
        {
            if (state == StockUpOperationState.Submitted)
            {
                op.MarkAsSubmitted(createdAt);
            }
            else if (state == StockUpOperationState.Completed)
            {
                op.MarkAsSubmitted(createdAt);
                op.MarkAsCompleted(createdAt);
            }
            else if (state == StockUpOperationState.Failed)
            {
                op.MarkAsFailed(createdAt, errorMessage ?? "Unknown error");
            }
        }

        return op;
    }

    [Fact]
    public async Task GetByCreatedDateRangeAsync_ProjectsAllRequiredFields()
    {
        // Arrange
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc);
        var op = CreateOperation("DOC-1", "P-001", 5, StockUpOperationState.Failed,
            new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc), errorMessage: "boom");
        _repository.Setup(r => r.GetAll()).Returns(new[] { op }.AsQueryable());

        // Act
        var result = await CreateAdapter().GetByCreatedDateRangeAsync(from, to, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        var snapshot = result[0];
        snapshot.ProductCode.Should().Be("P-001");
        snapshot.Amount.Should().Be(5);
        snapshot.DocumentNumber.Should().Be("DOC-1");
        snapshot.State.Should().Be(StockOperationStateSnapshot.Failed);
        snapshot.CreatedAtUtc.Should().Be(new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc));
        snapshot.ErrorMessage.Should().Contain("boom");
    }

    [Fact]
    public async Task GetByCreatedDateRangeAsync_ProjectsNullErrorMessage()
    {
        var op = CreateOperation("DOC-2", "P-002", 1, StockUpOperationState.Pending,
            new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc));
        _repository.Setup(r => r.GetAll()).Returns(new[] { op }.AsQueryable());

        var result = await CreateAdapter().GetByCreatedDateRangeAsync(
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
            CancellationToken.None);

        result.Should().ContainSingle();
        result[0].ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task GetByCreatedDateRangeAsync_FiltersOutsideDateWindow()
    {
        var inside = CreateOperation("DOC-IN", "P-IN", 1, StockUpOperationState.Completed,
            new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc));
        var beforeWindow = CreateOperation("DOC-BEFORE", "P-BEFORE", 1, StockUpOperationState.Completed,
            new DateTime(2026, 5, 30, 10, 0, 0, DateTimeKind.Utc));
        var afterWindow = CreateOperation("DOC-AFTER", "P-AFTER", 1, StockUpOperationState.Completed,
            new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        _repository.Setup(r => r.GetAll())
            .Returns(new[] { inside, beforeWindow, afterWindow }.AsQueryable());

        var result = await CreateAdapter().GetByCreatedDateRangeAsync(
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
            CancellationToken.None);

        result.Should().ContainSingle()
            .Which.ProductCode.Should().Be("P-IN");
    }

    [Theory]
    [InlineData(StockUpOperationState.Pending, StockOperationStateSnapshot.Pending)]
    [InlineData(StockUpOperationState.Submitted, StockOperationStateSnapshot.Submitted)]
    [InlineData(StockUpOperationState.Completed, StockOperationStateSnapshot.Completed)]
    [InlineData(StockUpOperationState.Failed, StockOperationStateSnapshot.Failed)]
    public async Task GetByCreatedDateRangeAsync_MapsStateOneToOne(
        StockUpOperationState catalogState,
        StockOperationStateSnapshot expected)
    {
        var op = CreateOperation("DOC-1", "P-1", 1, catalogState,
            new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc));
        _repository.Setup(r => r.GetAll()).Returns(new[] { op }.AsQueryable());

        var result = await CreateAdapter().GetByCreatedDateRangeAsync(
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
            CancellationToken.None);

        result.Should().ContainSingle().Which.State.Should().Be(expected);
    }

    [Fact]
    public async Task GetByCreatedDateRangeAsync_HandlesEveryCatalogStateMember_WithoutThrowing()
    {
        foreach (var state in Enum.GetValues<StockUpOperationState>())
        {
            _repository.Reset();
            var op = CreateOperation("DOC", "P", 1, state,
                new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc));
            _repository.Setup(r => r.GetAll()).Returns(new[] { op }.AsQueryable());

            var act = () => CreateAdapter().GetByCreatedDateRangeAsync(
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
                CancellationToken.None);

            await act.Should().NotThrowAsync($"adapter must map Catalog state {state}");
        }
    }

    [Fact]
    public async Task GetByCreatedDateRangeAsync_WhenEmpty_ReturnsEmptyList()
    {
        _repository.Setup(r => r.GetAll()).Returns(Array.Empty<StockUpOperation>().AsQueryable());

        var result = await CreateAdapter().GetByCreatedDateRangeAsync(
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
            CancellationToken.None);

        result.Should().BeEmpty();
    }
}
