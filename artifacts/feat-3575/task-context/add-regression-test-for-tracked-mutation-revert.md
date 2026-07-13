### task: add-regression-test-for-tracked-mutation-revert

**Goal:** Add integration-style test coverage using a real EF Core change tracker (InMemory provider) that
proves: (a) without the fix, a failed re-import corrupts the existing invoice row via a later invoice's
`SaveChangesAsync` in the same batch, and (b) with the fix applied, the row is left untouched while the
batch still correctly reports the failure and continues processing subsequent invoices. This is required
because the existing mocked `InvoiceImportServiceTests.cs` (all dependencies are `Mock<T>`, including
`IIssuedInvoiceRepository`) cannot observe EF Core change-tracker leakage — a mocked repository has no
change tracker to leak into.

**Files to touch:**
- New file: `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportRealChangeTrackerTests.cs`
  (new file, following the naming/location convention of the existing
  `InvoiceImportServiceTests.cs`/`InvoiceImportIntegrationTests.cs` in the same folder).

**Specific changes:**

Follow the `PackageRepositoryAddMissingTests.cs` pattern
(`backend/test/Anela.Heblo.Tests/Features/Packaging/PackageRepositoryAddMissingTests.cs`): construct
`ApplicationDbContext` directly with `UseInMemoryDatabase(Guid.NewGuid().ToString())` and a real
`IssuedInvoiceRepository` against it — no `WebApplicationFactory`/HTTP layer. Wire the real
`InvoiceImportService` against that real repository, with `IIssuedInvoiceSource`, `IIssuedInvoiceClient`,
`IIssuedInvoiceImportTransformation` (empty list is fine — no transformation is needed to trigger the
failure), and `IMapper` mocked via Moq (matching `InvoiceImportServiceTests.cs`'s existing mocking style
for those dependencies). `IssuedInvoiceRepository`'s constructor also needs `ILogger<IssuedInvoiceRepository>`
— use `Mock<ILogger<IssuedInvoiceRepository>>().Object` or a NullLogger.

Test class shape:
```csharp
public class InvoiceImportRealChangeTrackerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly IssuedInvoiceRepository _repository;
    private readonly Mock<IIssuedInvoiceSource> _mockSource;
    private readonly Mock<IIssuedInvoiceClient> _mockClient;
    private readonly Mock<IMapper> _mockMapper;
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

        _service = new InvoiceImportService(
            _mockSource.Object,
            _mockClient.Object,
            _repository,
            Array.Empty<IIssuedInvoiceImportTransformation>(),
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

        // Mapper mutates the tracked entity in place (mimicking AutoMapper's Map(src, dest) overload),
        // then the transformation-equivalent failure point (client.SaveAsync) throws for A only.
        _mockMapper.Setup(x => x.Map(detailA, It.IsAny<IssuedInvoice>()))
            .Callback<IssuedInvoiceDetail, IssuedInvoice>((src, dest) =>
            {
                dest.CustomerName = "MUTATED-SHOULD-NOT-PERSIST";
                dest.Price = 424242m;
            });
        _mockMapper.Setup(x => x.Map<IssuedInvoiceDetail, IssuedInvoice>(detailB))
            .Returns(new IssuedInvoice { Id = "INV-B", Currency = "CZK", ExtraProperties = "{}" });

        _mockClient.Setup(x => x.SaveAsync(detailA, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated downstream failure for A"));
        _mockClient.Setup(x => x.SaveAsync(detailB, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _service.ImportInvoicesAsync("test", query);

        // Assert — reporting behavior unchanged
        Assert.Contains("INV-A", result.Failed);
        Assert.Contains("INV-B", result.Succeeded);

        // Assert — A's row is byte-for-byte unchanged from its pre-import state
        _db.ChangeTracker.Clear();
        var persistedA = await _db.Set<IssuedInvoice>().AsNoTracking().SingleAsync(x => x.Id == "INV-A");
        Assert.Equal("Original Customer", persistedA.CustomerName);
        Assert.Equal(1000m, persistedA.Price);

        // Assert — B still imported and saved
        var persistedB = await _db.Set<IssuedInvoice>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == "INV-B");
        Assert.NotNull(persistedB);
    }
}
```

Notes for the implementer:
- `_mockClient.Setup(...).ThrowsAsync(...)` on `SaveAsync` triggers the *inner* catch in
  `ExecuteImportInvoice` today (lines 99-113), which calls `invoice.SyncFailed(...)` and then falls
  through to `UpdateAsync`/`SaveChangesAsync` — that path does **not** reach the outer catch and is
  explicitly out of scope for the revert (spec FR-2 scope boundary). To exercise the *outer* catch (the
  actual bug), the thrown exception must occur in a step that is **not** wrapped by the inner try — i.e.
  during `_mapper.Map(invoiceDetail, invoice)` itself, or inside the transformation loop. Prefer making the
  transformation pipeline throw: add one real
  `Mock<IIssuedInvoiceImportTransformation>` (instead of `Array.Empty<...>()`) whose
  `TransformAsync(detailA, ...)` throws `new InvalidOperationException(...)` and whose
  `TransformAsync(detailB, ...)` returns `detailB` unchanged. Adjust the arrange/act code above
  accordingly (this supersedes the `_mockClient.Setup(...).ThrowsAsync(...)` line for A — that line as
  drafted exercises the wrong catch block and must not be used to simulate the bug's trigger point).
- Verify this test fails on the pre-fix code (temporarily revert the `InvoiceImportService.cs` /
  `IssuedInvoiceRepository.cs` changes from the other task, or write this test first against
  unfixed code per TDD, and confirm `persistedA.CustomerName` comes back as
  `"MUTATED-SHOULD-NOT-PERSIST"` before the fix and `"Original Customer"` after).
- Required `using`s: `Anela.Heblo.Persistence`, `Anela.Heblo.Persistence.Invoices`,
  `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Logging`, plus the existing Invoices/Moq/Xunit
  usings already used in `InvoiceImportServiceTests.cs`.

**Verification:**
- New test fails against the pre-fix code and passes once `revert-tracked-mutation-on-existing-invoice-import-failure`
  is applied (confirm both states explicitly during development, per spec FR-4's acceptance criteria).
- `dotnet test --filter FullyQualifiedName~InvoiceImportRealChangeTrackerTests` passes.
- Full `dotnet build` + `dotnet test` for `Anela.Heblo.Tests` passes (no regressions in
  `InvoiceImportServiceTests.cs` or `InvoiceImportIntegrationTests.cs`).
- `dotnet format` clean.
