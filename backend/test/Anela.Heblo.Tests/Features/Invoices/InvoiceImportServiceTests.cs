using System.ComponentModel;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class InvoiceImportServiceTests
{
    private readonly Mock<IIssuedInvoiceSource> _mockInvoiceSource;
    private readonly Mock<IIssuedInvoiceClient> _mockInvoiceClient;
    private readonly Mock<IIssuedInvoiceRepository> _mockRepository;
    private readonly Mock<IMapper> _mockMapper;
    private readonly Mock<ILogger<InvoiceImportService>> _mockLogger;
    private readonly List<Mock<IIssuedInvoiceImportTransformation>> _mockTransformations;
    private readonly InvoiceImportService _service;

    public InvoiceImportServiceTests()
    {
        _mockInvoiceSource = new Mock<IIssuedInvoiceSource>();
        _mockInvoiceClient = new Mock<IIssuedInvoiceClient>();
        _mockRepository = new Mock<IIssuedInvoiceRepository>();
        _mockMapper = new Mock<IMapper>();
        _mockLogger = new Mock<ILogger<InvoiceImportService>>();
        _mockTransformations = new List<Mock<IIssuedInvoiceImportTransformation>>();

        _service = new InvoiceImportService(
            _mockInvoiceSource.Object,
            _mockInvoiceClient.Object,
            _mockRepository.Object,
            _mockTransformations.Select(t => t.Object),
            _mockMapper.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void InvoiceImportService_HasCorrectDisplayNameAttribute()
    {
        // Arrange & Act
        var method = typeof(InvoiceImportService).GetMethod(nameof(InvoiceImportService.ImportInvoicesAsync));
        var attribute = method?.GetCustomAttributes(typeof(DisplayNameAttribute), false).FirstOrDefault() as DisplayNameAttribute;

        // Assert
        Assert.NotNull(attribute);
        Assert.Equal("Import faktur: {0}", attribute.DisplayName);
    }

    [Fact]
    public async Task ImportInvoicesAsync_WithSuccessfulBatch_ReturnsSuccessResult()
    {
        // Arrange
        var query = new IssuedInvoiceSourceQuery { RequestId = "test-request-123" };
        var invoiceDetail = CreateTestInvoiceDetail("INV-001");
        var batch = CreateTestBatch("batch-1", invoiceDetail);
        var invoice = CreateTestIssuedInvoice("INV-001");

        _mockInvoiceSource.Setup(x => x.GetAllAsync(query))
            .ReturnsAsync(new List<IssuedInvoiceDetailBatch> { batch });
        _mockRepository.Setup(x => x.GetByIdAsync("INV-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IssuedInvoice?)null);
        _mockMapper.Setup(x => x.Map<IssuedInvoiceDetail, IssuedInvoice>(invoiceDetail))
            .Returns(invoice);
        _mockInvoiceClient.Setup(x => x.SaveAsync(invoiceDetail, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository.Setup(x => x.AddAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IssuedInvoice i, CancellationToken c) => i);
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ImportInvoicesAsync("test-description", query);

        // Assert
        Assert.Equal("test-request-123", result.RequestId);
        Assert.Single(result.Succeeded);
        Assert.Contains("INV-001", result.Succeeded);
        Assert.Empty(result.Failed);
        _mockInvoiceSource.Verify(x => x.CommitAsync(batch, It.IsAny<string>()), Times.Once);
        _mockInvoiceSource.Verify(x => x.FailAsync(batch, It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ImportInvoicesAsync_WithPartialFailure_TracksFailedInvoices()
    {
        // Arrange
        var query = new IssuedInvoiceSourceQuery { RequestId = "test-request-456" };
        var successInvoice = CreateTestInvoiceDetail("INV-001");
        var failInvoice = CreateTestInvoiceDetail("INV-002");
        var batch = CreateTestBatch("batch-1", successInvoice, failInvoice);
        var invoice1 = CreateTestIssuedInvoice("INV-001");
        var invoice2 = CreateTestIssuedInvoice("INV-002");

        _mockInvoiceSource.Setup(x => x.GetAllAsync(query))
            .ReturnsAsync(new List<IssuedInvoiceDetailBatch> { batch });

        // Setup successful invoice
        _mockRepository.Setup(x => x.GetByIdAsync("INV-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IssuedInvoice?)null);
        _mockMapper.Setup(x => x.Map<IssuedInvoiceDetail, IssuedInvoice>(successInvoice))
            .Returns(invoice1);
        _mockInvoiceClient.Setup(x => x.SaveAsync(successInvoice, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Setup failed invoice - repository throws exception
        _mockRepository.Setup(x => x.GetByIdAsync("INV-002", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        // Act
        var result = await _service.ImportInvoicesAsync("test-description", query);

        // Assert
        Assert.Equal("test-request-456", result.RequestId);
        Assert.Single(result.Succeeded);
        Assert.Contains("INV-001", result.Succeeded);
        Assert.Single(result.Failed);
        Assert.Contains("INV-002", result.Failed);
        _mockInvoiceSource.Verify(x => x.FailAsync(batch, It.IsAny<string>()), Times.Once);
        _mockInvoiceSource.Verify(x => x.CommitAsync(batch, It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ImportInvoicesAsync_WithExternalServiceFailure_TracksSyncStatus()
    {
        // Arrange
        var query = new IssuedInvoiceSourceQuery { RequestId = "test-request-789" };
        var invoiceDetail = CreateTestInvoiceDetail("INV-003");
        var batch = CreateTestBatch("batch-1", invoiceDetail);
        var invoice = CreateTestIssuedInvoice("INV-003");

        _mockInvoiceSource.Setup(x => x.GetAllAsync(query))
            .ReturnsAsync(new List<IssuedInvoiceDetailBatch> { batch });
        _mockRepository.Setup(x => x.GetByIdAsync("INV-003", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IssuedInvoice?)null);
        _mockMapper.Setup(x => x.Map<IssuedInvoiceDetail, IssuedInvoice>(invoiceDetail))
            .Returns(invoice);
        _mockInvoiceClient.Setup(x => x.SaveAsync(invoiceDetail, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("ABRA Flexi API unavailable"));
        _mockRepository.Setup(x => x.AddAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IssuedInvoice i, CancellationToken c) => i);
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ImportInvoicesAsync("test-description", query);

        // Assert
        Assert.Equal("test-request-789", result.RequestId);
        Assert.Single(result.Succeeded); // Invoice is saved even if external sync fails
        Assert.Contains("INV-003", result.Succeeded);
        Assert.Empty(result.Failed);

        // Verify invoice sync status was updated with failure
        _mockRepository.Verify(x => x.UpdateAsync(It.Is<IssuedInvoice>(i =>
            i.Id == "INV-003" && i.ErrorMessage!.Contains("ABRA Flexi API unavailable")),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockInvoiceSource.Verify(x => x.CommitAsync(batch, It.IsAny<string>()), Times.Once); // Batch still commits
    }

    [Fact]
    public async Task ImportInvoicesAsync_WithTransformations_AppliesAllTransformations()
    {
        // Arrange
        var query = new IssuedInvoiceSourceQuery { RequestId = "test-request-transforms" };
        var originalInvoice = CreateTestInvoiceDetail("INV-004");
        var transformedInvoice1 = CreateTestInvoiceDetail("INV-004-T1");
        var transformedInvoice2 = CreateTestInvoiceDetail("INV-004-T2");
        var batch = CreateTestBatch("batch-1", originalInvoice);
        var invoice = CreateTestIssuedInvoice("INV-004");

        var transformation1 = new Mock<IIssuedInvoiceImportTransformation>();
        var transformation2 = new Mock<IIssuedInvoiceImportTransformation>();
        _mockTransformations.AddRange(new[] { transformation1, transformation2 });

        var serviceWithTransformations = new InvoiceImportService(
            _mockInvoiceSource.Object,
            _mockInvoiceClient.Object,
            _mockRepository.Object,
            new[] { transformation1.Object, transformation2.Object },
            _mockMapper.Object,
            _mockLogger.Object);

        _mockInvoiceSource.Setup(x => x.GetAllAsync(query))
            .ReturnsAsync(new List<IssuedInvoiceDetailBatch> { batch });
        _mockRepository.Setup(x => x.GetByIdAsync("INV-004", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IssuedInvoice?)null);
        _mockMapper.Setup(x => x.Map<IssuedInvoiceDetail, IssuedInvoice>(originalInvoice))
            .Returns(invoice);

        transformation1.Setup(x => x.TransformAsync(originalInvoice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transformedInvoice1);
        transformation2.Setup(x => x.TransformAsync(transformedInvoice1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transformedInvoice2);

        _mockInvoiceClient.Setup(x => x.SaveAsync(transformedInvoice2, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository.Setup(x => x.AddAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IssuedInvoice i, CancellationToken c) => i);
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await serviceWithTransformations.ImportInvoicesAsync("test-description", query);

        // Assert
        transformation1.Verify(x => x.TransformAsync(originalInvoice, It.IsAny<CancellationToken>()), Times.Once);
        transformation2.Verify(x => x.TransformAsync(transformedInvoice1, It.IsAny<CancellationToken>()), Times.Once);
        _mockInvoiceClient.Verify(x => x.SaveAsync(transformedInvoice2, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(result.Succeeded);
    }

    [Fact]
    public async Task ImportInvoicesAsync_WithExistingInvoice_UpdatesExisting()
    {
        // Arrange
        var query = new IssuedInvoiceSourceQuery { RequestId = "test-existing" };
        var invoiceDetail = CreateTestInvoiceDetail("INV-005");
        var batch = CreateTestBatch("batch-1", invoiceDetail);
        var existingInvoice = CreateTestIssuedInvoice("INV-005");

        _mockInvoiceSource.Setup(x => x.GetAllAsync(query))
            .ReturnsAsync(new List<IssuedInvoiceDetailBatch> { batch });
        _mockRepository.Setup(x => x.GetByIdAsync("INV-005", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingInvoice); // Invoice already exists
        _mockInvoiceClient.Setup(x => x.SaveAsync(invoiceDetail, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ImportInvoicesAsync("test-description", query);

        // Assert
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepository.Verify(x => x.UpdateAsync(existingInvoice, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(result.Succeeded);
        Assert.Contains("INV-005", result.Succeeded);
    }

    [Fact]
    public async Task ImportInvoicesAsync_WithMultipleBatches_ProcessesSequentially()
    {
        // Arrange
        var query = new IssuedInvoiceSourceQuery { RequestId = "test-multiple-batches" };
        var invoice1 = CreateTestInvoiceDetail("INV-BATCH1");
        var invoice2 = CreateTestInvoiceDetail("INV-BATCH2");
        var batch1 = CreateTestBatch("batch-1", invoice1);
        var batch2 = CreateTestBatch("batch-2", invoice2);
        var domainInvoice1 = CreateTestIssuedInvoice("INV-BATCH1");
        var domainInvoice2 = CreateTestIssuedInvoice("INV-BATCH2");

        _mockInvoiceSource.Setup(x => x.GetAllAsync(query))
            .ReturnsAsync(new List<IssuedInvoiceDetailBatch> { batch1, batch2 });
        _mockRepository.Setup(x => x.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IssuedInvoice?)null);
        _mockMapper.Setup(x => x.Map<IssuedInvoiceDetail, IssuedInvoice>(invoice1))
            .Returns(domainInvoice1);
        _mockMapper.Setup(x => x.Map<IssuedInvoiceDetail, IssuedInvoice>(invoice2))
            .Returns(domainInvoice2);
        _mockInvoiceClient.Setup(x => x.SaveAsync(It.IsAny<IssuedInvoiceDetail>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository.Setup(x => x.AddAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IssuedInvoice i, CancellationToken c) => i);
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ImportInvoicesAsync("test-description", query);

        // Assert
        Assert.Equal(2, result.Succeeded.Count);
        Assert.Contains("INV-BATCH1", result.Succeeded);
        Assert.Contains("INV-BATCH2", result.Succeeded);
        _mockInvoiceSource.Verify(x => x.CommitAsync(batch1, It.IsAny<string>()), Times.Once);
        _mockInvoiceSource.Verify(x => x.CommitAsync(batch2, It.IsAny<string>()), Times.Once);
    }

    private static IssuedInvoiceDetail CreateTestInvoiceDetail(string code)
    {
        return new IssuedInvoiceDetail
        {
            Code = code,
            Price = new InvoicePrice
            {
                WithVat = 1000,
                CurrencyCode = "CZK"
            }
        };
    }

    private static IssuedInvoiceDetailBatch CreateTestBatch(string batchId, params IssuedInvoiceDetail[] invoices)
    {
        return new IssuedInvoiceDetailBatch
        {
            BatchId = batchId,
            Invoices = invoices.ToList()
        };
    }

    private static IssuedInvoice CreateTestIssuedInvoice(string code)
    {
        return new IssuedInvoice
        {
            Id = code,
            InvoiceDate = DateTime.Today,
            DueDate = DateTime.Today.AddDays(30),
            TaxDate = DateTime.Today,
            Price = 1000,
            Currency = "CZK",
            ExtraProperties = "{}",
            CreationTime = DateTime.UtcNow
        };
    }
}