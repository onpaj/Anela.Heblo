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
