### task: add-with-details-dispatch-tests


**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` (append two new `[Fact]` methods after `Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError`, before the closing `}` of the class)

This task covers FR-2: `WithDetails == true` must dispatch to `GetByIdWithSyncHistoryAsync` (and never call `GetByIdAsync`); `WithDetails == false` must dispatch to `GetByIdAsync` (and never call `GetByIdWithSyncHistoryAsync`). Both cases assert a successful, mapped response.

- [ ] **Step 1: Write the failing test for `WithDetails = true`**

Insert this new method into the class, immediately after the existing `Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError` method (i.e. before the class's closing `}`):

```csharp
    [Fact]
    public async Task Handle_WithDetailsTrue_CallsGetByIdWithSyncHistoryAsync()
    {
        // Arrange
        var request = new GetIssuedInvoiceDetailRequest
        {
            InvoiceId = "INV-TEST-001",
            WithDetails = true
        };
        var invoice = new IssuedInvoice
        {
            Id = "INV-TEST-001",
            SyncHistoryCount = 2
        };
        var mappedDto = new IssuedInvoiceDetailDto { Id = "INV-TEST-001" };

        _repositoryMock
            .Setup(r => r.GetByIdWithSyncHistoryAsync("INV-TEST-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _mapperMock
            .Setup(m => m.Map<IssuedInvoiceDetailDto>(invoice))
            .Returns(mappedDto);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Invoice.Should().Be(mappedDto);
        _repositoryMock.Verify(r => r.GetByIdWithSyncHistoryAsync("INV-TEST-001", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 2: Run the test to verify it fails (compile error expected, since the method doesn't exist yet before this edit — verify it fails to find the method, or just proceed since this is a pure addition)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceDetailHandlerTests.Handle_WithDetailsTrue_CallsGetByIdWithSyncHistoryAsync"`

Expected before the code above is added: `No test matches the given testcase filter`. After adding the code above (this step is really "add then run"), it should compile and PASS immediately since the handler already implements this dispatch — there is no production code to change. Confirm PASS: `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 3: Write the failing test for `WithDetails = false`**

Insert this new method directly after `Handle_WithDetailsTrue_CallsGetByIdWithSyncHistoryAsync`:

```csharp
    [Fact]
    public async Task Handle_WithDetailsFalse_CallsGetByIdAsync()
    {
        // Arrange
        var request = new GetIssuedInvoiceDetailRequest
        {
            InvoiceId = "INV-TEST-002",
            WithDetails = false
        };
        var invoice = new IssuedInvoice
        {
            Id = "INV-TEST-002",
            SyncHistoryCount = 0
        };
        var mappedDto = new IssuedInvoiceDetailDto { Id = "INV-TEST-002" };

        _repositoryMock
            .Setup(r => r.GetByIdAsync("INV-TEST-002", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _mapperMock
            .Setup(m => m.Map<IssuedInvoiceDetailDto>(invoice))
            .Returns(mappedDto);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Invoice.Should().Be(mappedDto);
        _repositoryMock.Verify(r => r.GetByIdAsync("INV-TEST-002", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.GetByIdWithSyncHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 4: Run both new tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceDetailHandlerTests.Handle_WithDetailsTrue_CallsGetByIdWithSyncHistoryAsync|FullyQualifiedName~GetIssuedInvoiceDetailHandlerTests.Handle_WithDetailsFalse_CallsGetByIdAsync"`

Expected: `Passed! - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs
git commit -m "test: cover WithDetails repository dispatch in GetIssuedInvoiceDetailHandler"
```

---
