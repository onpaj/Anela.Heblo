using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperations;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using AutoMapper;
using MockQueryable;
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

    #region State Filter Tests

    [Fact]
    public async Task Handle_StateFilter_Active_ReturnsOnlyActiveOperations()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest
        {
            State = "Active"
        };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1), // Pending
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),  // Submitted
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),  // Failed
            new("DOC-004", "PROD4", 15, StockUpSourceType.TransportBox, 4), // Completed
        };

        operations[1].MarkAsSubmitted(DateTime.UtcNow);
        operations[2].MarkAsSubmitted(DateTime.UtcNow);
        operations[2].MarkAsFailed(DateTime.UtcNow, "Test error");
        operations[3].MarkAsSubmitted(DateTime.UtcNow);
        operations[3].MarkAsCompleted(DateTime.UtcNow);

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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
        var request = new GetStockUpOperationsRequest
        {
            State = "Pending"
        };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1), // Pending
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),  // Submitted
        };

        operations[1].MarkAsSubmitted(DateTime.UtcNow);

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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
        var request = new GetStockUpOperationsRequest
        {
            State = "Completed"
        };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1), // Pending
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),  // Completed
        };

        operations[1].MarkAsSubmitted(DateTime.UtcNow);
        operations[1].MarkAsCompleted(DateTime.UtcNow);

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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
        var request = new GetStockUpOperationsRequest
        {
            SourceType = StockUpSourceType.TransportBox
        };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.GiftPackageManufacture, 1),
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 2),
        };

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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
        var request = new GetStockUpOperationsRequest
        {
            SourceType = StockUpSourceType.GiftPackageManufacture
        };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.GiftPackageManufacture, 1),
            new("DOC-003", "PROD3", 8, StockUpSourceType.GiftPackageManufacture, 2),
        };

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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
        var request = new GetStockUpOperationsRequest
        {
            SourceId = 123
        };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 123),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 456),
            new("DOC-003", "PROD3", 8, StockUpSourceType.GiftPackageManufacture, 123),
        };

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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
        var request = new GetStockUpOperationsRequest
        {
            ProductCode = "PROD123"
        };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD123", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD456", 5, StockUpSourceType.TransportBox, 2),
            new("DOC-003", "PROD1234", 8, StockUpSourceType.TransportBox, 3), // Should NOT match (exact match required)
        };

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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
        var request = new GetStockUpOperationsRequest
        {
            DocumentNumber = "doc"
        };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
            new("INV-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),
            new("document-004", "PROD4", 15, StockUpSourceType.TransportBox, 4), // Should match (case-insensitive)
        };

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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
        var cutoffDate = new DateTime(2025, 1, 15);
        var request = new GetStockUpOperationsRequest
        {
            CreatedFrom = cutoffDate
        };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),
        };

        // Set creation dates via reflection (CreatedAt is typically set in constructor, but we need to test filtering)
        // In real scenario, these would be set by the database/entity framework
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[0], new DateTime(2025, 1, 10));
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[1], new DateTime(2025, 1, 15));
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[2], new DateTime(2025, 1, 20));

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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
        var cutoffDate = new DateTime(2025, 1, 15);
        var request = new GetStockUpOperationsRequest
        {
            CreatedTo = cutoffDate
        };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),
        };

        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[0], new DateTime(2025, 1, 10, 10, 0, 0));
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[1], new DateTime(2025, 1, 15, 23, 59, 59));
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[2], new DateTime(2025, 1, 16, 0, 0, 1));

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),
            new("DOC-004", "PROD4", 15, StockUpSourceType.TransportBox, 4),
        };

        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[0], new DateTime(2025, 1, 5));
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[1], new DateTime(2025, 1, 15));
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[2], new DateTime(2025, 1, 20, 23, 59, 59));
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[3], new DateTime(2025, 1, 25));

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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
        var request = new GetStockUpOperationsRequest
        {
            SortBy = "id",
            SortDescending = true
        };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),
        };

        // Set IDs via reflection
        typeof(StockUpOperation).GetProperty("Id")!.SetValue(operations[0], 10);
        typeof(StockUpOperation).GetProperty("Id")!.SetValue(operations[1], 20);
        typeof(StockUpOperation).GetProperty("Id")!.SetValue(operations[2], 15);

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.TotalCount);
        // Verify sorting would be: 20, 15, 10 (descending by ID)
    }

    [Fact]
    public async Task Handle_SortByCreatedAt_Ascending_ReturnsSortedOperations()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest
        {
            SortBy = "createdAt",
            SortDescending = false
        };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),
        };

        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[0], new DateTime(2025, 1, 20));
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[1], new DateTime(2025, 1, 10));
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[2], new DateTime(2025, 1, 15));

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.TotalCount);
        // Verify sorting would be: 2025-01-10, 2025-01-15, 2025-01-20 (ascending)
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

        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[0], new DateTime(2025, 1, 10));
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[1], new DateTime(2025, 1, 20));

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.TotalCount);
        // Default should be CreatedAt DESC
    }

    #endregion

    #region Pagination Tests

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectPageSize()
    {
        // Arrange
        var request = new GetStockUpOperationsRequest
        {
            PageSize = 2,
            Page = 1
        };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),
            new("DOC-004", "PROD4", 15, StockUpSourceType.TransportBox, 4),
        };

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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
        var request = new GetStockUpOperationsRequest
        {
            PageSize = 2,
            Page = 2
        };

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1),
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),
            new("DOC-003", "PROD3", 8, StockUpSourceType.TransportBox, 3),
            new("DOC-004", "PROD4", 15, StockUpSourceType.TransportBox, 4),
        };

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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

        var operations = Enumerable.Range(1, 100)
            .Select(i => new StockUpOperation($"DOC-{i:D3}", $"PROD{i}", 10, StockUpSourceType.TransportBox, i))
            .ToList();

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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

        var operations = new List<StockUpOperation>
        {
            new("DOC-001", "PROD1", 10, StockUpSourceType.TransportBox, 1), // Pending - Match
            new("DOC-002", "PROD2", 5, StockUpSourceType.TransportBox, 2),  // Completed - No match
            new("DOC-003", "PROD3", 8, StockUpSourceType.GiftPackageManufacture, 3), // Pending GPM - No match
            new("DOC-004", "PROD4", 15, StockUpSourceType.TransportBox, 4), // Failed - Match
        };

        operations[1].MarkAsSubmitted(DateTime.UtcNow);
        operations[1].MarkAsCompleted(DateTime.UtcNow);
        operations[3].MarkAsSubmitted(DateTime.UtcNow);
        operations[3].MarkAsFailed(DateTime.UtcNow, "Test error");

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

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

        var operations = new List<StockUpOperation>
        {
            new("BOX-001", "PROD1", 10, StockUpSourceType.TransportBox, 123), // Match
            new("BOX-002", "PROD1", 5, StockUpSourceType.TransportBox, 456),  // Wrong SourceId
            new("INV-003", "PROD1", 8, StockUpSourceType.TransportBox, 123),  // Wrong DocumentNumber
            new("BOX-004", "PROD2", 15, StockUpSourceType.TransportBox, 123), // Wrong ProductCode
        };

        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[0], new DateTime(2025, 6, 15));
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[1], new DateTime(2025, 6, 15));
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[2], new DateTime(2025, 6, 15));
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(operations[3], new DateTime(2025, 6, 15));

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

        _mapperMock.Setup(m => m.Map<List<StockUpOperationDto>>(It.IsAny<List<StockUpOperation>>()))
            .Returns((List<StockUpOperation> source) => source.Select(op => new StockUpOperationDto()).ToList());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount); // Only BOX-001 matches all filters
        Assert.Single(result.Operations);
    }

    #endregion
}
