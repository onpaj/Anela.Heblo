# Unit Test Coverage for GetIssuedInvoiceDetailHandler Implementation Plan

**Goal:** Close the line-coverage gap on `GetIssuedInvoiceDetailHandler` (currently 40%, threshold 60%) by adding unit tests for its three untested branches — `WithDetails` repository-method dispatch, invoice-not-found, and the outer exception handler — plus extending the existing empty/whitespace-ID theory to also cover `null`.

**Architecture:** This is a test-only change. All new test methods are added to the existing `GetIssuedInvoiceDetailHandlerTests` class in `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs`, reusing its existing `_repositoryMock` (`Mock<IIssuedInvoiceRepository>`), `_mapperMock` (`Mock<IMapper>`), and `_handler` fields. No production code changes. Each test arranges the repository/mapper mocks for one branch, invokes `_handler.Handle(request, CancellationToken.None)`, and asserts on `response.Success`/`ErrorCode`/`Invoice`/`Params`, plus `Verify(..., Times.Once/Never)` on the repository mock where dispatch matters.

**Tech Stack:** .NET 8, xUnit (`[Fact]`/`[Theory]`/`[InlineData]`), Moq, FluentAssertions — all already in use in the target test file. No new packages.

---

### task: extend-null-invoice-id-validation-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs:34-36`

This task extends the existing `Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError` theory with a `null` case (FR-1), so `string.IsNullOrWhiteSpace(null)` is exercised too.

- [ ] **Step 1: Add the `[InlineData(null)]` case to the existing theory's attribute list**

Current code (lines 34-37):

```csharp
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError(string invoiceId)
```

Change to:

```csharp
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError(string invoiceId)
```

Use the Edit tool with this exact old/new pair:

old_string:
```
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError(string invoiceId)
```

new_string:
```
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError(string invoiceId)
```

The method body is unchanged — it already asserts `Success == false`, `ErrorCode == ErrorCodes.ValidationError`, `Invoice == null`, and `_repositoryMock.VerifyNoOtherCalls()`, all of which hold for the `null` case too since `GetIssuedInvoiceDetailRequest.InvoiceId = invoiceId` accepts `null` at runtime despite the non-nullable `string` declaration (C#'s nullable-reference-type annotations are not enforced at runtime).

- [ ] **Step 2: Run the test to verify the new case passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceDetailHandlerTests.Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError"`

Expected: 3 tests pass (`""`, `"   "`, `null`), 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs
git commit -m "test: cover null InvoiceId in GetIssuedInvoiceDetailHandler validation theory"
```

---

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
