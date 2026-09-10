### task: update-invoice-classification-service-tests-for-abra-invoice-id

**Why:** `InvoiceClassificationServiceTests.cs` has four test methods, each asserting `capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber)` — i.e. asserting the *current buggy* behavior where both fields are conflated. Per TDD, this task updates those tests (and their `ReceivedInvoice` fixtures) to assert the *correct* behavior first — using genuinely distinct `AbraInvoiceId`/`InvoiceNumber` fixture values — which will make all four tests **fail** against the current (not-yet-fixed) `InvoiceClassificationService.RecordClassificationHistory`, because that method still passes `invoice.InvoiceNumber` for both constructor arguments. The next task then fixes the call site to make these tests pass. This task depends on `add-abra-invoice-id-domain-property` already being done, since the fixtures below set `ReceivedInvoice.AbraInvoiceId`, which must already exist as a settable property for the file to compile.

The four occurrences are at (pre-edit) lines 76, 156, 237, 299 of `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs`, one per test method. Each is preceded (30-45 lines above) by that test's `ReceivedInvoice` object initializer. Apply all four edits below.

1. In `ClassifyInvoiceAsync_NoMatchingRule_MarksForManualReviewAndRecordsHistory` (starts at line 30), update the fixture at lines 33-39 from:

```csharp
        var invoice = new ReceivedInvoice
        {
            InvoiceNumber = "INV-001",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice"
        };
```

to:

```csharp
        var invoice = new ReceivedInvoice
        {
            AbraInvoiceId = "ABRA-001",
            InvoiceNumber = "INV-001",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice"
        };
```

and update the assertion at line 76 from:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber);
```

to:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.AbraInvoiceId);
```

2. In `ClassifyInvoiceAsync_RuleMatchedAndAbraSucceeds_RecordsSuccessAndReturnsRuleResult` (starts at line 100), update the fixture at lines 104-110 from:

```csharp
        var invoice = new ReceivedInvoice
        {
            InvoiceNumber = "INV-002",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with Rule"
        };
```

to:

```csharp
        var invoice = new ReceivedInvoice
        {
            AbraInvoiceId = "ABRA-002",
            InvoiceNumber = "INV-002",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with Rule"
        };
```

and update the assertion at line 156 from:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber);
```

to:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.AbraInvoiceId);
```

3. In `ClassifyInvoiceAsync_RuleMatchedAndAbraFails_RecordsErrorAndReturnsRuleIdForDisplay` (starts at line 181), update the fixture at lines 185-191 from:

```csharp
        var invoice = new ReceivedInvoice
        {
            InvoiceNumber = "INV-003",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with ABRA Failure"
        };
```

to:

```csharp
        var invoice = new ReceivedInvoice
        {
            AbraInvoiceId = "ABRA-003",
            InvoiceNumber = "INV-003",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with ABRA Failure"
        };
```

and update the assertion at line 237 from:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber);
```

to:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.AbraInvoiceId);
```

4. In `ClassifyInvoiceAsync_ExceptionThrown_RecordsErrorWithMessageAndReturnsErrorResult` (starts at line 262), update the fixture at lines 266-272 from:

```csharp
        var invoice = new ReceivedInvoice
        {
            InvoiceNumber = "INV-004",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with Exception"
        };
```

to:

```csharp
        var invoice = new ReceivedInvoice
        {
            AbraInvoiceId = "ABRA-004",
            InvoiceNumber = "INV-004",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with Exception"
        };
```

and update the assertion at line 299 from:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber);
```

to:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.AbraInvoiceId);
```

5. Do **not** touch any other assertion in this file — `capturedHistory.InvoiceNumber.Should().Be(invoice.InvoiceNumber)` and every other line stay exactly as they are.

6. Run the scoped test suite and confirm all four tests now **fail** (the service under test hasn't been fixed yet):

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassificationServiceTests"
```

Expected: `Failed: 4, Passed: 0` (or similar — all 4 tests in this class fail), each failure showing an assertion mismatch like `Expected capturedHistory.AbraInvoiceId to be "ABRA-001" but found "INV-001"`. This confirms the test now correctly exercises the bug.

7. Commit:

```bash
git add backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs
git commit -m "Update InvoiceClassificationServiceTests to expect distinct AbraInvoiceId

Tests now use a distinct AbraInvoiceId fixture value per case and assert
against it, so they correctly fail against the current call-site bug
that conflates AbraInvoiceId with InvoiceNumber.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9hXSww9WLhMo5YTaZwmv2"
```

---

