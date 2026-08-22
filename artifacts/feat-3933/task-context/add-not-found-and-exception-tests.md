### task: add-not-found-and-exception-tests


**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` (append two more `[Fact]` methods after `Handle_WithDetailsFalse_CallsGetByIdAsync`, before the closing `}` of the class)

This task covers FR-3 (invoice not found → `ResourceNotFound`, mapper never invoked) and FR-4 (repository throws → caught, `ErrorCodes.Exception`, no rethrow).

- [ ] **Step 1: Write the failing test for invoice-not-found**

Insert this new method directly after `Handle_WithDetailsFalse_CallsGetByIdAsync`:

```csharp
    [Fact]
    public async Task Handle_InvoiceNotFound_ReturnsResourceNotFoundError()
    {
        // Arrange
        var request = new GetIssuedInvoiceDetailRequest
        {
            InvoiceId = "INV-TEST-003",
            WithDetails = false
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync("INV-TEST-003", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IssuedInvoice?)null);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        response.Invoice.Should().BeNull();
        response.Params.Should().ContainKey("ErrorMessage").WhoseValue.Should().Be("Faktura nebyla nalezena");
        _mapperMock.Verify(m => m.Map<IssuedInvoiceDetailDto>(It.IsAny<object>()), Times.Never);
    }
```

- [ ] **Step 2: Write the failing test for the outer exception handler**

Insert this new method directly after `Handle_InvoiceNotFound_ReturnsResourceNotFoundError`:

```csharp
    [Fact]
    public async Task Handle_RepositoryThrows_ReturnsExceptionError()
    {
        // Arrange
        var request = new GetIssuedInvoiceDetailRequest
        {
            InvoiceId = "INV-TEST-004",
            WithDetails = false
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync("INV-TEST-004", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated failure"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.Exception);
        response.Invoice.Should().BeNull();
        response.Params.Should().ContainKey("ErrorMessage").WhoseValue.Should().Be("Chyba při načítání detailu faktury");
    }
```

- [ ] **Step 3: Run both new tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceDetailHandlerTests.Handle_InvoiceNotFound_ReturnsResourceNotFoundError|FullyQualifiedName~GetIssuedInvoiceDetailHandlerTests.Handle_RepositoryThrows_ReturnsExceptionError"`

Expected: `Passed! - Failed: 0, Passed: 2`.

- [ ] **Step 4: Run the full test class to confirm nothing regressed**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceDetailHandlerTests"`

Expected: `Passed! - Failed: 0, Passed: 7` (3 validation-theory cases + 2 dispatch tests + not-found + exception).

- [ ] **Step 5: Build the whole solution and run formatting check**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: `Build succeeded. 0 Error(s)`.

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: exits with code 0 (no formatting violations). If it reports violations in the modified file, run `dotnet format backend/Anela.Heblo.sln` (without `--verify-no-changes`) to auto-fix, then re-run the verify command and re-run the test suite from Step 4 before committing.

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs
git commit -m "test: cover not-found and exception branches in GetIssuedInvoiceDetailHandler"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (null InvoiceId guard) → `extend-null-invoice-id-validation-test`.
- FR-2 (`WithDetails` dispatch, both directions) → `add-with-details-dispatch-tests`.
- FR-3 (not-found → `ResourceNotFound`, mapper never invoked) → `add-not-found-and-exception-tests` Step 1.
- FR-4 (repository throws → caught, `ErrorCodes.Exception`, no rethrow) → `add-not-found-and-exception-tests` Step 2.
- NFR-1 (fast, no I/O) — satisfied structurally: all tests use Moq doubles only, no real repository/database.
- NFR-2 (no real data) — satisfied: all `InvoiceId` values used are synthetic (`INV-TEST-00N`), matching the spec's suggested pattern.

**Placeholder scan:** No `TBD`/`TODO`/"add appropriate handling" phrases; every step has complete, concrete C# code with exact expected values (`"Faktura nebyla nalezena"`, `"Chyba při načítání detailu faktury"`, `ErrorCodes.ResourceNotFound`, `ErrorCodes.Exception`) taken directly from the handler source read during planning.

**Type consistency:** `GetIssuedInvoiceDetailRequest.InvoiceId`/`WithDetails`, `IssuedInvoice.Id`/`SyncHistoryCount`, `IssuedInvoiceDetailDto.Id`, `GetIssuedInvoiceDetailResponse.Invoice`/`Success`/`ErrorCode`/`Params` (inherited from `BaseResponse`), and `IIssuedInvoiceRepository.GetByIdAsync(string, CancellationToken)` / `GetByIdWithSyncHistoryAsync(string, CancellationToken)` all match the actual source read from `GetIssuedInvoiceDetailHandler.cs`, `GetIssuedInvoiceDetailRequest.cs`, `GetIssuedInvoiceDetailResponse.cs`, `IssuedInvoice.cs`, `IIssuedInvoiceRepository.cs`, `IssuedInvoiceDto.cs`, and `IssuedInvoiceDetailDto.cs` prior to writing this plan. No new production code, interfaces, or method signatures are introduced.
