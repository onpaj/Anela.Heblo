using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Invoices;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

/// <summary>
/// Regression coverage for the ExecuteImportInvoice "save new invoice twice" bug.
/// Uses a real EF Core change tracker (InMemory provider) via IssuedInvoiceRepository +
/// ApplicationDbContext instead of a mocked repository, because a mocked repository cannot
/// detect the class of bug where UpdateAsync is called on an entity that was just AddAsync'd
/// but never saved (EF would flip it from Added to Modified and try to UPDATE a row that
/// does not exist yet).
/// </summary>
public class InvoiceImportServiceStateTrackingTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IssuedInvoiceRepository _repository;
    private readonly Mock<IIssuedInvoiceSource> _mockInvoiceSource;
    private readonly Mock<IIssuedInvoiceClient> _mockInvoiceClient;
    private readonly Mock<IMapper> _mockMapper;
    private readonly Mock<ILogger<InvoiceImportService>> _mockLogger;
    private readonly InvoiceImportService _service;

    public InvoiceImportServiceStateTrackingTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"InvoiceImportStateTrackingTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new IssuedInvoiceRepository(_context, new Mock<ILogger<IssuedInvoiceRepository>>().Object);

        _mockInvoiceSource = new Mock<IIssuedInvoiceSource>();
        _mockInvoiceClient = new Mock<IIssuedInvoiceClient>();
        _mockMapper = new Mock<IMapper>();
        _mockLogger = new Mock<ILogger<InvoiceImportService>>();

        _service = new InvoiceImportService(
            _mockInvoiceSource.Object,
            _mockInvoiceClient.Object,
            _repository,
            Array.Empty<IIssuedInvoiceImportTransformation>(),
            _mockMapper.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task ImportInvoicesAsync_WithNewInvoice_PersistsWithSingleSaveChangesCall()
    {
        // Arrange
        var query = new IssuedInvoiceSourceQuery { RequestId = "state-tracking-new" };
        var invoiceDetail = new IssuedInvoiceDetail
        {
            Code = "INV-STATE-001",
            Price = new InvoicePrice { WithVat = 1000, CurrencyCode = "CZK" }
        };
        var batch = new IssuedInvoiceDetailBatch { BatchId = "batch-1", Invoices = new List<IssuedInvoiceDetail> { invoiceDetail } };

        var mappedInvoice = new IssuedInvoice
        {
            Id = "INV-STATE-001",
            InvoiceDate = DateTime.Today,
            DueDate = DateTime.Today.AddDays(30),
            TaxDate = DateTime.Today,
            Price = 1000,
            Currency = "CZK",
            ExtraProperties = "{}"
        };

        _mockInvoiceSource.Setup(x => x.GetAllAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IssuedInvoiceDetailBatch> { batch });
        _mockMapper.Setup(x => x.Map<IssuedInvoiceDetail, IssuedInvoice>(invoiceDetail))
            .Returns(mappedInvoice);
        _mockInvoiceClient.Setup(x => x.SaveAsync(invoiceDetail, It.IsAny<CancellationToken>()))
            .ReturnsAsync("raw-adapter-response");

        // Act
        var result = await _service.ImportInvoicesAsync("test-description", query);

        // Assert — exactly one persistence flush occurred: if production code regressed to
        // calling UpdateAsync on the still-unsaved (Added-tracked) entity, EF's InMemory
        // provider would throw DbUpdateConcurrencyException (0 rows affected on "update"),
        // which ExecuteImportInvoice's catch block would turn into a Failed entry here.
        Assert.Single(result.Succeeded);
        Assert.Contains("INV-STATE-001", result.Succeeded);
        Assert.Empty(result.Failed);

        var saved = await _context.IssuedInvoices
            .AsNoTracking()
            .SingleAsync(x => x.Id == "INV-STATE-001");

        // Synced fields populated by the successful ERP sync
        Assert.True(saved.IsSynced);
        Assert.NotNull(saved.LastSyncTime);

        // Audit fields set by IssuedInvoiceRepository.AddAsync
        Assert.True(saved.CreationTime > DateTime.MinValue);
        Assert.NotNull(saved.ConcurrencyStamp);
        Assert.NotEmpty(saved.ConcurrencyStamp);

        // UpdateAsync must have been skipped for a new invoice: LastModificationTime is only
        // ever set by IssuedInvoiceRepository.UpdateAsync, so it must remain null.
        Assert.Null(saved.LastModificationTime);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
