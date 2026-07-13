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
