# Remove Redundant SaveChangesAsync in InvoiceImportService Implementation Plan

**Goal:** Fix `InvoiceImportService.ExecuteImportInvoice` so a newly-imported invoice is persisted with exactly one `SaveChangesAsync` round trip instead of two, without changing behavior for re-imported (existing) invoices.

**Architecture:** `GetOrCreateAsync` stops calling `_repository.SaveChangesAsync(...)` internally and instead returns `(IssuedInvoice Invoice, bool IsNew)`. `ExecuteImportInvoice` uses `IsNew` to skip the now-unnecessary `_repository.UpdateAsync(...)` call for brand-new invoices (which are already tracked via `AddAsync`) and always calls `_repository.SaveChangesAsync(...)` exactly once at the end, for both new and existing invoices.

**Tech Stack:** .NET 8, MediatR-based backend, EF Core (`Anela.Heblo.Persistence.ApplicationDbContext`), xUnit + Moq for tests, EF Core InMemory provider for the new state-tracking regression test.

---

### task: fix-invoice-import-double-save

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs:81-138`
- Test: `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs`

This task changes the production code and mechanically updates the existing mocked-repository unit tests so they keep passing (and, per FR-2, so they explicitly assert the new call-count contract for a new invoice: `AddAsync` once, `UpdateAsync` never, `SaveChangesAsync` once).

- [ ] **Step 1: Write/adjust failing tests first (TDD red)**

Open `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs`.

**1a. Update `ImportInvoicesAsync_WithSuccessfulBatch_ReturnsSuccessResult`** (currently lines 53-87) to assert the new-invoice call-count contract from FR-2. Replace the existing `// Assert` block:

Old (lines 80-87):
```csharp
        // Assert
        Assert.Equal("test-request-123", result.RequestId);
        Assert.Single(result.Succeeded);
        Assert.Contains("INV-001", result.Succeeded);
        Assert.Empty(result.Failed);
        _mockInvoiceSource.Verify(x => x.CommitAsync(batch, It.IsAny<string>()), Times.Once);
        _mockInvoiceSource.Verify(x => x.FailAsync(batch, It.IsAny<string>()), Times.Never);
```

New:
```csharp
        // Assert
        Assert.Equal("test-request-123", result.RequestId);
        Assert.Single(result.Succeeded);
        Assert.Contains("INV-001", result.Succeeded);
        Assert.Empty(result.Failed);
        _mockInvoiceSource.Verify(x => x.CommitAsync(batch, It.IsAny<string>()), Times.Once);
        _mockInvoiceSource.Verify(x => x.FailAsync(batch, It.IsAny<string>()), Times.Never);

        // FR-2: new invoice -> AddAsync once, UpdateAsync never, SaveChangesAsync exactly once
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(x => x.UpdateAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
```

This assertion (`UpdateAsync` `Times.Never`) will fail against the current production code, which always calls `UpdateAsync` regardless of whether the invoice is new. This is the "red" state.

**1b. Fix `ImportInvoicesAsync_WithExternalServiceFailure_TracksSyncStatus`** (currently lines 128-166). This test creates a brand-new invoice (`GetByIdAsync` returns `null`) and currently verifies the failure state via `UpdateAsync`'s captured argument — that verification is invalid once `UpdateAsync` is no longer called for new invoices, because the mutation still happens on the object but `UpdateAsync` itself is skipped.

Replace the `// Assert` block, old (lines 155-166):
```csharp
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
```

New:
```csharp
        // Assert
        Assert.Equal("test-request-789", result.RequestId);
        Assert.Single(result.Succeeded); // Invoice is saved even if external sync fails
        Assert.Contains("INV-003", result.Succeeded);
        Assert.Empty(result.Failed);

        // Invoice is new: sync failure state is set directly on the tracked entity,
        // UpdateAsync must NOT be called (it is already tracked via AddAsync).
        Assert.Contains("ABRA Flexi API unavailable", invoice.ErrorMessage);
        _mockRepository.Verify(x => x.UpdateAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockInvoiceSource.Verify(x => x.CommitAsync(batch, It.IsAny<string>()), Times.Once); // Batch still commits
```

Note: `invoice` is the local variable already declared at the top of the test method (`var invoice = CreateTestIssuedInvoice("INV-003");`), and it is the same object instance returned by the `AddAsync` mock setup, so it reflects the mutations made by `SyncFailed(...)` inside `ExecuteImportInvoice`.

**1c. Add a symmetric assertion to `ImportInvoicesAsync_WithExistingInvoice_UpdatesExisting`** (currently lines 263-291) to lock in the "existing invoice" half of FR-2 (`UpdateAsync` once, `SaveChangesAsync` once — unchanged from today). Replace the `// Assert` block, old (lines 286-291):
```csharp
        // Assert
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepository.Verify(x => x.UpdateAsync(existingInvoice, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(result.Succeeded);
        Assert.Contains("INV-005", result.Succeeded);
```

New:
```csharp
        // Assert
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<IssuedInvoice>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepository.Verify(x => x.UpdateAsync(existingInvoice, It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(result.Succeeded);
        Assert.Contains("INV-005", result.Succeeded);
```

Save the file.

- [ ] **Step 2: Verify the tests fail (red)**

```bash
cd /home/user/worktrees/feature-3566-Arch-Review-Invoices-Executeimportinvoice-Saves-Ne/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportServiceTests" --logger "console;verbosity=normal"
```

Expect `ImportInvoicesAsync_WithSuccessfulBatch_ReturnsSuccessResult` and `ImportInvoicesAsync_WithExternalServiceFailure_TracksSyncStatus` to FAIL (the new `Times.Never` verification on `UpdateAsync` does not hold against current production code, which always calls `UpdateAsync`). All other tests in the file continue to pass.

- [ ] **Step 3: Implement the production fix (green)**

Open `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs`.

Replace `ExecuteImportInvoice` (current lines 81-125):
```csharp
    private async Task<IssuedInvoice> ExecuteImportInvoice(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Importing invoice: {InvoiceNumber}", invoiceDetail.Code);

            var invoice = await GetOrCreateAsync(invoiceDetail.Code, () => _mapper.Map<IssuedInvoiceDetail, IssuedInvoice>(invoiceDetail), cancellationToken);

            // Always refresh core data fields from source (handles re-imports where data may have changed or was missing)
            _mapper.Map(invoiceDetail, invoice);

            // Apply transformations to domain model
            var transformedInvoice = invoiceDetail;
            foreach (var transformation in _importTransformations)
            {
                transformedInvoice = await transformation.TransformAsync(transformedInvoice, cancellationToken);
            }

            try
            {
                // Send to external system via abstraction
                var adapterResponse = await _issuedInvoiceClient.SaveAsync(transformedInvoice, cancellationToken);
                invoice.SyncSucceeded(transformedInvoice, adapterResponse);
                _logger.LogInformation(
                    "Successfully imported invoice: {InvoiceNumber}: {InvoiceValue} ({Currency})",
                    invoiceDetail.Code, invoiceDetail.Price.WithVat, invoiceDetail.Price.CurrencyCode);
            }
            catch (Exception ex)
            {
                var adapterResponse = (ex as IssuedInvoiceClientException)?.RawAdapterResponse;
                _logger.LogError(ex, "FlexiBee rejected invoice {InvoiceCode}: {Error}", transformedInvoice.Code, ex.Message);
                invoice.SyncFailed(transformedInvoice, ex.Message, adapterResponse);
            }

            await _repository.UpdateAsync(invoice, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            return invoice;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while importing invoice: {InvoiceNumber}", invoiceDetail.Code);
            throw;
        }
    }

    private async Task<IssuedInvoice> GetOrCreateAsync(string key, Func<IssuedInvoice> factory, CancellationToken cancellationToken = default)
    {
        var found = await _repository.GetByIdAsync(key, cancellationToken);
        if (found == null)
        {
            found = factory();
            await _repository.AddAsync(found, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }

        return found;
    }
```

With:
```csharp
    private async Task<IssuedInvoice> ExecuteImportInvoice(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Importing invoice: {InvoiceNumber}", invoiceDetail.Code);

            var (invoice, isNew) = await GetOrCreateAsync(invoiceDetail.Code, () => _mapper.Map<IssuedInvoiceDetail, IssuedInvoice>(invoiceDetail), cancellationToken);

            // Always refresh core data fields from source (handles re-imports where data may have changed or was missing)
            _mapper.Map(invoiceDetail, invoice);

            // Apply transformations to domain model
            var transformedInvoice = invoiceDetail;
            foreach (var transformation in _importTransformations)
            {
                transformedInvoice = await transformation.TransformAsync(transformedInvoice, cancellationToken);
            }

            try
            {
                // Send to external system via abstraction
                var adapterResponse = await _issuedInvoiceClient.SaveAsync(transformedInvoice, cancellationToken);
                invoice.SyncSucceeded(transformedInvoice, adapterResponse);
                _logger.LogInformation(
                    "Successfully imported invoice: {InvoiceNumber}: {InvoiceValue} ({Currency})",
                    invoiceDetail.Code, invoiceDetail.Price.WithVat, invoiceDetail.Price.CurrencyCode);
            }
            catch (Exception ex)
            {
                var adapterResponse = (ex as IssuedInvoiceClientException)?.RawAdapterResponse;
                _logger.LogError(ex, "FlexiBee rejected invoice {InvoiceCode}: {Error}", transformedInvoice.Code, ex.Message);
                invoice.SyncFailed(transformedInvoice, ex.Message, adapterResponse);
            }

            // New invoices are already tracked via AddAsync inside GetOrCreateAsync — calling
            // UpdateAsync on them would mark an unsaved entity as Modified instead of Added.
            if (!isNew)
            {
                await _repository.UpdateAsync(invoice, cancellationToken);
            }

            await _repository.SaveChangesAsync(cancellationToken);

            return invoice;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while importing invoice: {InvoiceNumber}", invoiceDetail.Code);
            throw;
        }
    }

    private async Task<(IssuedInvoice Invoice, bool IsNew)> GetOrCreateAsync(string key, Func<IssuedInvoice> factory, CancellationToken cancellationToken = default)
    {
        var found = await _repository.GetByIdAsync(key, cancellationToken);
        if (found == null)
        {
            found = factory();
            await _repository.AddAsync(found, cancellationToken);
            return (found, true);
        }

        return (found, false);
    }
```

Save the file.

- [ ] **Step 4: Verify the tests pass (green)**

```bash
cd /home/user/worktrees/feature-3566-Arch-Review-Invoices-Executeimportinvoice-Saves-Ne/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportServiceTests" --logger "console;verbosity=normal"
```

Expect all tests in `InvoiceImportServiceTests` to pass, including the two updated in Step 1.

- [ ] **Step 5: Build and format check**

```bash
cd /home/user/worktrees/feature-3566-Arch-Review-Invoices-Executeimportinvoice-Saves-Ne/backend
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
```

If `dotnet format` reports changes, run `dotnet format Anela.Heblo.sln` and re-verify.

- [ ] **Step 6: Commit**
```bash
cd /home/user/worktrees/feature-3566-Arch-Review-Invoices-Executeimportinvoice-Saves-Ne
git add backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs
git commit -m "Fix InvoiceImportService double-save for newly imported invoices

GetOrCreateAsync no longer calls SaveChangesAsync internally and returns
(Invoice, IsNew) instead. ExecuteImportInvoice skips the redundant
UpdateAsync call for new invoices (already tracked via AddAsync) and
saves exactly once, cutting SaveChangesAsync round trips for new
invoices from 2 to 1."
```

---

### task: add-invoice-import-state-tracking-regression-test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceStateTrackingTests.cs`

This task adds an EF-Core-backed regression test that exercises `InvoiceImportService` against a real `IssuedInvoiceRepository` + `ApplicationDbContext` (`UseInMemoryDatabase`), per FR-2/NFR-2: mocked-repository tests cannot detect the class of bug where `UpdateAsync` is (incorrectly) called on a brand-new, not-yet-saved entity — only a real EF Core change tracker can. This is intentionally a *new* file, separate from `InvoiceImportServiceTests.cs` (which stays fully mocked) and separate from `InvoiceImportIntegrationTests.cs` (which mocks `IInvoiceImportService` entirely and never touches EF Core).

This is a pure test addition — no production code changes in this task (the production fix already lands in `fix-invoice-import-double-save`). The "TDD" angle here is: this test is written against the already-fixed code, and Step 2 proves it would have caught the bug by temporarily re-introducing it.

- [ ] **Step 1: Write the new test file**

Create `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceStateTrackingTests.cs`:

```csharp
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
```

- [ ] **Step 2: Confirm the test would have caught the original bug (sanity check)**

Temporarily revert the fix locally (do not commit this) to confirm the new test fails against the pre-fix behavior, proving it exercises the right code path. In `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs`, temporarily change:
```csharp
            if (!isNew)
            {
                await _repository.UpdateAsync(invoice, cancellationToken);
            }
```
to:
```csharp
            await _repository.UpdateAsync(invoice, cancellationToken);
```

Run:
```bash
cd /home/user/worktrees/feature-3566-Arch-Review-Invoices-Executeimportinvoice-Saves-Ne/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportServiceStateTrackingTests" --logger "console;verbosity=normal"
```

Expect `ImportInvoicesAsync_WithNewInvoice_PersistsWithSingleSaveChangesCall` to FAIL (either `Assert.Empty(result.Failed)` fails because the invoice landed in `Failed` due to a swallowed `DbUpdateConcurrencyException`, or `Assert.Null(saved.LastModificationTime)` fails because `UpdateAsync` ran and set it). Then revert the temporary change back to the `if (!isNew)` guard — the file must end up identical to the state produced by the `fix-invoice-import-double-save` task.

```bash
cd /home/user/worktrees/feature-3566-Arch-Review-Invoices-Executeimportinvoice-Saves-Ne
git diff backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs
```

Confirm this shows no diff (file matches the committed fix) before continuing.

- [ ] **Step 3: Verify the new test passes against the fixed code (green)**

```bash
cd /home/user/worktrees/feature-3566-Arch-Review-Invoices-Executeimportinvoice-Saves-Ne/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportServiceStateTrackingTests" --logger "console;verbosity=normal"
```

Expect `ImportInvoicesAsync_WithNewInvoice_PersistsWithSingleSaveChangesCall` to PASS.

- [ ] **Step 4: Run the full Invoices test slice plus a full build**

```bash
cd /home/user/worktrees/feature-3566-Arch-Review-Invoices-Executeimportinvoice-Saves-Ne/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Invoices" --logger "console;verbosity=normal"
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
```

All tests under the `Invoices` namespace (`InvoiceImportServiceTests`, `IssuedInvoiceRepositoryTests`, `InvoiceImportServiceStateTrackingTests`, and any others already in that folder) must pass, the build must succeed, and `dotnet format` must report no changes needed. If `dotnet format` reports changes, run `dotnet format Anela.Heblo.sln` and re-verify.

- [ ] **Step 5: Commit**
```bash
cd /home/user/worktrees/feature-3566-Arch-Review-Invoices-Executeimportinvoice-Saves-Ne
git add backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceStateTrackingTests.cs
git commit -m "Add EF Core regression test for InvoiceImportService new-invoice save path

Exercises InvoiceImportService against a real IssuedInvoiceRepository +
EF Core InMemory ApplicationDbContext, since mocked-repository tests
cannot detect an UpdateAsync call being (re-)introduced on a
not-yet-saved, Added-tracked invoice."
```

---

## Self-Review

**Spec coverage:**
- FR-1 (no `SaveChangesAsync` inside `GetOrCreateAsync`; `AddAsync` still called exactly once for new invoices) — covered by the production fix in `fix-invoice-import-double-save` Step 3, and asserted via `_mockRepository.Verify(x => x.AddAsync(...), Times.Once)` / `SaveChangesAsync(...), Times.Once` in the updated `ImportInvoicesAsync_WithSuccessfulBatch_ReturnsSuccessResult` test.
- FR-2 (no `UpdateAsync` on new invoice; exact call counts for both new and existing paths; must be covered by a real EF Core change tracker test) — covered by the mocked-test updates (`Times.Never` on `UpdateAsync` for new invoices, `Times.Once` for existing invoices) AND by the new `InvoiceImportServiceStateTrackingTests.cs`, which asserts `LastModificationTime == null` (proof `UpdateAsync` never ran) against a real `ApplicationDbContext`.
- FR-3 (crash-safety: no partial row unless the single `SaveChangesAsync` succeeds) — preserved structurally: `AddAsync` only stages the row in the change tracker: nothing is written to the database until the single `SaveChangesAsync` call at the end of `ExecuteImportInvoice`, after the ERP sync outcome (success or failure) has already been recorded on the in-memory entity via `SyncSucceeded`/`SyncFailed`.
- FR-4 (no behavior change for existing/re-import invoices) — `ImportInvoicesAsync_WithExistingInvoice_UpdatesExisting` and `ImportInvoicesAsync_WithExistingInvoice_RefreshesCoreDataFromSource` are left with only an additive `SaveChangesAsync` `Times.Once` assertion in the former; neither test's existing assertions were removed or weakened.
- NFR-1 (2N → N `SaveChangesAsync` calls) — structural result of removing the inner `SaveChangesAsync` from `GetOrCreateAsync`; implicitly covered by the `Times.Once` assertions (previously would have required `Times.Once` on the outer call plus an extra inner call the old code made, i.e. 2 calls total for a new invoice).
- NFR-2 (no field regression, validated against real persistence) — covered by `InvoiceImportServiceStateTrackingTests`, which reads back the persisted row via a fresh `AsNoTracking()` query and checks `IsSynced`, `LastSyncTime`, `CreationTime`, `ConcurrencyStamp`.
- NFR-3 (no interface/contract change; `GetOrCreateAsync` is private) — confirmed: `GetOrCreateAsync` remains `private`, only its return type changes from `Task<IssuedInvoice>` to `Task<(IssuedInvoice Invoice, bool IsNew)>`; `IInvoiceImportService`'s public surface (`ImportInvoicesAsync`) is untouched.
- Out of scope items (redundant `_mapper.Map(invoiceDetail, invoice)` call, transactional boundaries, other repositories, marketing invoices) — none touched by either task.

**Placeholder scan:** No "TBD", "similar to Task N", or omitted code blocks — every step includes the exact before/after code or exact shell commands.

**Type consistency:** `GetOrCreateAsync`'s new signature `Task<(IssuedInvoice Invoice, bool IsNew)>` matches the design doc exactly; the deconstruction `var (invoice, isNew) = await GetOrCreateAsync(...)` at the call site matches the named tuple members. `IssuedInvoiceRepository.AddAsync`/`UpdateAsync` signatures (confirmed from `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs:75-89`) are unchanged and match how both the production code and the new test invoke them. `ApplicationDbContext`'s constructor (`ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)`) and the `UseInMemoryDatabase` test setup pattern in the new test file mirror the existing, working pattern in `IssuedInvoiceRepositoryTests.cs` exactly.

**Task boundary check:** Production fix + mechanical mock-test updates are kept in one task (`fix-invoice-import-double-save`) since they are one indivisible code change verified by the same TDD red/green loop against the existing mocked test file. The new EF-Core-backed regression test is a separate task (`add-invoice-import-state-tracking-regression-test`) per the instructions, since it is a genuinely different kind of test (new file, real persistence layer, different regression it guards against) rather than an artificial split of a one-file change.
