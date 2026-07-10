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
/// Regression coverage for the tracked-mutation revert fix (commit bea1c60d): a failed re-import of an
/// existing invoice must not leak its in-memory mutation into the shared DbContext's change tracker,
/// where a later invoice's SaveChangesAsync within the same batch could otherwise flush it.
/// Uses a real ApplicationDbContext (InMemory provider) and a real IssuedInvoiceRepository so the EF Core
/// change tracker is genuinely exercised — a fully-mocked IIssuedInvoiceRepository (as in
/// InvoiceImportServiceTests) has no change tracker to leak into and cannot observe this bug.
/// </summary>
public class InvoiceImportRealChangeTrackerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly IssuedInvoiceRepository _repository;
    private readonly Mock<IIssuedInvoiceSource> _mockSource;
    private readonly Mock<IIssuedInvoiceClient> _mockClient;
    private readonly Mock<IMapper> _mockMapper;
    private readonly Mock<IIssuedInvoiceImportTransformation> _mockTransformation;
    private readonly InvoiceImportService _service;

    public InvoiceImportRealChangeTrackerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"InvoiceImport_{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
        _repository = new IssuedInvoiceRepository(_db, Mock.Of<ILogger<IssuedInvoiceRepository>>());
        _mockSource = new Mock<IIssuedInvoiceSource>();
        _mockClient = new Mock<IIssuedInvoiceClient>();
        _mockMapper = new Mock<IMapper>();
        _mockTransformation = new Mock<IIssuedInvoiceImportTransformation>();

        _service = new InvoiceImportService(
            _mockSource.Object,
            _mockClient.Object,
            _repository,
            new[] { _mockTransformation.Object },
            _mockMapper.Object,
            Mock.Of<ILogger<InvoiceImportService>>());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ImportInvoicesAsync_WhenReImportOfExistingInvoiceFailsMidPipeline_DoesNotPersistPartialMutationAndContinuesBatch()
    {
        // Arrange — seed invoice A as a prior successful import
        var original = new IssuedInvoice
        {
            Id = "INV-A",
            InvoiceDate = new DateTime(2026, 1, 1),
            DueDate = new DateTime(2026, 1, 31),
            TaxDate = new DateTime(2026, 1, 1),
            Price = 1000m,
            Currency = "CZK",
            CustomerName = "Original Customer",
            ExtraProperties = "{}",
            CreationTime = DateTime.UtcNow,
        };
        _db.Set<IssuedInvoice>().Add(original);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear(); // simulate a fresh read on the next GetByIdAsync, as a new batch would

        var detailA = new IssuedInvoiceDetail { Code = "INV-A", Price = new InvoicePrice { WithVat = 9999, CurrencyCode = "CZK" } };
        var detailB = new IssuedInvoiceDetail { Code = "INV-B", Price = new InvoicePrice { WithVat = 500, CurrencyCode = "CZK" } };
        var batch = new IssuedInvoiceDetailBatch { BatchId = "batch-1", Invoices = new List<IssuedInvoiceDetail> { detailA, detailB } };
        var query = new IssuedInvoiceSourceQuery { RequestId = "test-revert" };

        _mockSource.Setup(x => x.GetAllAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IssuedInvoiceDetailBatch> { batch });

        // Mapper mutates the tracked entity in place (mimicking AutoMapper's Map(src, dest) overload) for
        // both invoices. For A, this mutation must never reach the DB because the transformation pipeline
        // below throws for A right after this — before A's own SaveChangesAsync runs.
        _mockMapper.Setup(x => x.Map(detailA, It.IsAny<IssuedInvoice>()))
            .Callback<IssuedInvoiceDetail, IssuedInvoice>((src, dest) =>
            {
                dest.CustomerName = "MUTATED-SHOULD-NOT-PERSIST";
                dest.Price = 424242m;
            });
        _mockMapper.Setup(x => x.Map<IssuedInvoiceDetail, IssuedInvoice>(detailB))
            .Returns(new IssuedInvoice { Id = "INV-B", Currency = "CZK", ExtraProperties = "{}" });

        // The transformation pipeline — not the client — is the failure point that reaches the outer
        // catch (the one that must trigger RevertTrackedChangesAsync). A's client.SaveAsync must never be
        // reached at all, since the transformation throws before that point in ExecuteImportInvoice.
        _mockTransformation.Setup(x => x.TransformAsync(detailA, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated transformation failure for A"));
        _mockTransformation.Setup(x => x.TransformAsync(detailB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detailB);

        _mockClient.Setup(x => x.SaveAsync(detailB, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _service.ImportInvoicesAsync("test", query);

        // Assert — reporting behavior unchanged
        Assert.Contains("INV-A", result.Failed);
        Assert.Contains("INV-B", result.Succeeded);

        // Assert — A's row is unchanged from its pre-import state (proving the tracked mutation from
        // A's failed re-import was reverted and never flushed by B's later SaveChangesAsync)
        _db.ChangeTracker.Clear();
        var persistedA = await _db.Set<IssuedInvoice>().AsNoTracking().SingleAsync(x => x.Id == "INV-A");
        Assert.Equal("Original Customer", persistedA.CustomerName);
        Assert.Equal(1000m, persistedA.Price);

        // Assert — B still imported and saved
        var persistedB = await _db.Set<IssuedInvoice>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == "INV-B");
        Assert.NotNull(persistedB);

        _mockClient.Verify(x => x.SaveAsync(detailA, It.IsAny<CancellationToken>()), Times.Never);
    }
}
