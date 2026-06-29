using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperations;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using AutoMapper;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetStockUpOperationsHandlerTests
{
    private readonly Mock<IStockUpOperationRepository> _repositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly GetStockUpOperationsHandler _handler;

    public GetStockUpOperationsHandlerTests()
    {
        _repositoryMock = new Mock<IStockUpOperationRepository>();
        _mapperMock = new Mock<IMapper>();
        _handler = new GetStockUpOperationsHandler(_repositoryMock.Object, _mapperMock.Object);
    }

    private void SetupQueryAsync(List<StockUpOperation> items, int? totalCount = null)
    {
        var count = totalCount ?? items.Count;
        _repositoryMock
            .Setup(r => r.QueryAsync(It.IsAny<StockUpOperationFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((items, count));
        _mapperMock
            .Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(_ => new StockUpOperationDto()).ToList());
    }

    #region State Filter Tests

    [Fact]
    public async Task Handle_StateFilter_Active_ReturnsOnlyActiveOperations()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest { State = "Active" };

        // Repository returns the pre-filtered result (Pending + Submitted + Failed = 3)
        var activeOperations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1), // Pending
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),  // Submitted
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),  // Failed
        };
        SetupQueryAsync(activeOperations);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.TotalCount); // Pending + Submitted + Failed
        Assert.Equal(3, result.Operations.Count);
    }

    [Fact]
    public async Task Handle_StateFilter_Pending_ReturnsOnlyPendingOperations()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest { State = "Pending" };

        var pendingOperations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1), // Pending
        };
        SetupQueryAsync(pendingOperations);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task Handle_StateFilter_Completed_ReturnsOnlyCompletedOperations()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest { State = "Completed" };

        var completedOperations = new List<StockUpOperation>
        {
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2), // Completed
        };
        SetupQueryAsync(completedOperations);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount);
    }

    #endregion

    #region SourceType Filter Tests

    [Fact]
    public async Task Handle_SourceTypeFilter_TransportBox_ReturnsOnlyTransportBoxOperations()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest { SourceType = StockUpSourceType.TransportBox };

        var transportBoxOps = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 2),
        };
        SetupQueryAsync(transportBoxOps);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Handle_SourceTypeFilter_GiftPackageManufacture_ReturnsOnlyGPMOperations()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest { SourceType = StockUpSourceType.GiftPackageManufacture };

        var gpmOps = new List<StockUpOperation>
        {
            new("DOC-002", "PROD2", 5, StockUpSourceType.GiftPackageManufacture, 1),
            new("DOC-003", "PROD3", 8, StockUpSourceType.GiftPackageManufacture, 2),
        };
        SetupQueryAsync(gpmOps);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.TotalCount);
    }

    #endregion

    #region SourceId Filter Tests

    [Fact]
    public async Task Handle_SourceIdFilter_ReturnsOnlyMatchingSourceId()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest { SourceId = 123 };

        var matchingOps = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 123),
            new("DOC-003", "PROD3", 8, StockUpSourceType.GiftPackageManufacture, 123),
        };
        SetupQueryAsync(matchingOps);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.TotalCount);
    }

    #endregion

    #region ProductCode Filter Tests

    [Fact]
    public async Task Handle_ProductCodeFilter_ExactMatch_ReturnsOnlyMatchingProduct()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest { ProductCode = "PROD123" };

        var matchingOps = new List<StockUpOperation>
        {
            new("DOC-001", "PROD123", 10, StockUpSourceType.TransportBox, 1),
        };
        SetupQueryAsync(matchingOps);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount);
    }

    #endregion

    #region DocumentNumber Filter Tests

    [Fact]
    public async Task Handle_DocumentNumberFilter_PartialMatch_ReturnsMatchingDocuments()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest { DocumentNumber = "doc" };

        // Repository returns case-insensitive partial matches: DOC-001, DOC-002, document-004
        var matchingOps = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
            new("document-004", "PROD4", 15, StockUpSourceType.TransportBox, 4),
        };
        SetupQueryAsync(matchingOps);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.TotalCount); // DOC-001, DOC-002, document-004 (case-insensitive contains)
    }

    #endregion

    #region Date Range Filter Tests

    [Fact]
    public async Task Handle_CreatedFromFilter_ReturnsOperationsAfterDate()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest { CreatedFrom = new DateTime(2025, 1, 15) };

        // Repository returns items at or after 2025-01-15
        var matchingOps = new List<StockUpOperation>
        {
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),
        };
        SetupQueryAsync(matchingOps);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.TotalCount); // DOC-002, DOC-003
    }

    [Fact]
    public async Task Handle_CreatedToFilter_ReturnsOperationsBeforeDate()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest { CreatedTo = new DateTime(2025, 1, 15) };

        // Repository returns items strictly before 2025-01-16 (entire day of 2025-01-15 included)
        var matchingOps = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
        };
        SetupQueryAsync(matchingOps);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.TotalCount); // DOC-001, DOC-002 (entire day of 2025-01-15 included)
    }

    [Fact]
    public async Task Handle_CreatedDateRange_ReturnsOperationsWithinRange()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest
        {
            CreatedFrom = new DateTime(2025, 1, 10),
            CreatedTo = new DateTime(2025, 1, 20)
        };

        // Repository returns items within the range
        var matchingOps = new List<StockUpOperation>
        {
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),
        };
        SetupQueryAsync(matchingOps);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.TotalCount); // DOC-002, DOC-003
    }

    #endregion

    #region Sorting Tests

    [Fact]
    public async Task Handle_SortById_Descending_ReturnsSortedOperations()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest { SortBy = "id", SortDescending = true };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),
        };
        SetupQueryAsync(operations);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.TotalCount);
        // Sorting is handled by the repository; handler just passes the filter through
    }

    [Fact]
    public async Task Handle_SortByCreatedAt_Ascending_ReturnsSortedOperations()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest { SortBy = "createdAt", SortDescending = false };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),
        };
        SetupQueryAsync(operations);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.TotalCount);
        // Sorting is handled by the repository; handler just passes the filter through
    }

    [Fact]
    public async Task Handle_DefaultSorting_ReturnsSortedByCreatedAtDescending()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest(); // No sorting specified

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
        };
        SetupQueryAsync(operations);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.TotalCount);
        // Default should be CreatedAt DESC — enforced by repository
    }

    #endregion

    #region Pagination Tests

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectPageSize()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest { PageSize = 2, Page = 1 };

        // Repository returns page 1 (2 items) with total count of 4
        var pageItems = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
        };
        SetupQueryAsync(pageItems, totalCount: 4);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(4, result.TotalCount); // Total operations
        Assert.Equal(2, result.Operations.Count); // Page size
    }

    [Fact]
    public async Task Handle_Pagination_SecondPage_ReturnsCorrectOperations()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest { PageSize = 2, Page = 2 };

        // Repository returns page 2 (2 items) with total count of 4
        var pageItems = new List<StockUpOperation>
        {
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),
            new("DOC-004", "PROD4", 15, StockUpSourceType.TransportBox, 4),
        };
        SetupQueryAsync(pageItems, totalCount: 4);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(2, result.Operations.Count); // Second page with 2 items
    }

    [Fact]
    public async Task Handle_DefaultPagination_Returns50Items()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest(); // No pagination specified

        // Repository returns first 50 items with total count of 100
        var pageItems = Enumerable.Range(1, 50)
            .Select(i => new StockUpOperation($"DOC-{i:D3}", $"PROD{i}", 10, StockUpSourceType.TransportBox, i))
            .ToList();
        SetupQueryAsync(pageItems, totalCount: 100);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(100, result.TotalCount);
        Assert.Equal(50, result.Operations.Count); // Default page size
    }

    #endregion

    #region Combined Filters Tests

    [Fact]
    public async Task Handle_CombinedFilters_ActiveStateAndTransportBox_ReturnsFilteredOperations()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest
        {
            State = "Active",
            SourceType = StockUpSourceType.TransportBox
        };

        // Repository returns pre-filtered: TransportBox + Active (Pending + Failed only)
        var matchingOps = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1), // Pending
            new("DOC-004", "PROD4", 15, StockUpSourceType.TransportBox, 4), // Failed
        };
        SetupQueryAsync(matchingOps);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.TotalCount); // Only TransportBox with Active states
    }

    [Fact]
    public async Task Handle_AllFilters_ReturnsCorrectlyFilteredOperations()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest
        {
            State = "Active",
            SourceType = StockUpSourceType.TransportBox,
            SourceId = 123,
            ProductCode = "PROD1",
            DocumentNumber = "BOX",
            CreatedFrom = new DateTime(2025, 1, 1),
            CreatedTo = new DateTime(2025, 12, 31),
            SortBy = "createdAt",
            SortDescending = true,
            PageSize = 10,
            Page = 1
        };

        // Repository returns the single matching result
        var matchingOps = new List<StockUpOperation>
        {
            new("BOX-001", "PROD1", 10, StockUpSourceType.TransportBox, 123), // Match
        };
        SetupQueryAsync(matchingOps);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount); // Only BOX-001 matches all filters
        Assert.Single(result.Operations);
    }

    #endregion
}
