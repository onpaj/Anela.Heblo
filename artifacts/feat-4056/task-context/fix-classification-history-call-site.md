### task: fix-classification-history-call-site

**Why:** `InvoiceClassificationService.RecordClassificationHistory` (private method, lines 98-116) builds the `ClassificationHistory` audit row for every classification attempt. It currently passes `invoice.InvoiceNumber` for **both** the `abraInvoiceId` and `invoiceNumber` constructor parameters (lines 101-103), which is the root cause of the bug this whole plan fixes. With `ReceivedInvoice.AbraInvoiceId` now populated correctly (previous two tasks), this task changes the first constructor argument to `invoice.AbraInvoiceId`. This is the change that makes the four tests updated in `update-invoice-classification-service-tests-for-abra-invoice-id` pass.

1. Open `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs`. The `RecordClassificationHistory` method (lines 98-116) currently reads:

```csharp
    private async Task RecordClassificationHistory(ReceivedInvoice invoice, Guid? ruleId,
        ClassificationResult result, string? accountingTemplateCode, string? department, string? errorMessage, string processedBy)
    {
        var history = new ClassificationHistory(
            invoice.InvoiceNumber, // AbraInvoiceId
            invoice.InvoiceNumber, // InvoiceNumber
            invoice.InvoiceDate,   // InvoiceDate
            invoice.CompanyName,   // CompanyName
            invoice.Description,   // Description
            result,
            processedBy,
            ruleId,
            accountingTemplateCode,
            department,
            errorMessage
        );

        await _historyRepository.AddAsync(history);
    }
```

2. Change the first constructor argument from `invoice.InvoiceNumber` to `invoice.AbraInvoiceId`:

```csharp
    private async Task RecordClassificationHistory(ReceivedInvoice invoice, Guid? ruleId,
        ClassificationResult result, string? accountingTemplateCode, string? department, string? errorMessage, string processedBy)
    {
        var history = new ClassificationHistory(
            invoice.AbraInvoiceId, // AbraInvoiceId
            invoice.InvoiceNumber, // InvoiceNumber
            invoice.InvoiceDate,   // InvoiceDate
            invoice.CompanyName,   // CompanyName
            invoice.Description,   // Description
            result,
            processedBy,
            ruleId,
            accountingTemplateCode,
            department,
            errorMessage
        );

        await _historyRepository.AddAsync(history);
    }
```

No other line in this file changes. In particular, `ClassifyInvoiceAsync`'s calls to `_classificationsClient.MarkInvoiceForManualReviewAsync(invoice.InvoiceNumber, ...)` and `_classificationsClient.UpdateInvoiceClassificationAsync(invoice.InvoiceNumber, ...)` (lines 41 and 49-50) correctly continue to use `invoice.InvoiceNumber` (FlexiBee's `Code`), since those are outbound FlexiBee API calls that address invoices by code, not by internal ID — this is explicitly out of scope per the spec.

3. Run the scoped test suite and confirm all four previously-failing tests now pass:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassificationServiceTests"
```

Expected: `Passed: 4, Failed: 0`.

4. Run the two other InvoiceClassification test files that construct `ClassificationHistory` directly (`InvoiceClassificationMappingProfileTests.cs`, `ClassificationHistoryRepositoryTests.cs`) to confirm they are unaffected, as predicted by the spec/arch-review (they already pass explicit, distinct `abraInvoiceId:` arguments and don't go through `ReceivedInvoice`):

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassification"
```

Expected: all tests in the `InvoiceClassification` namespace pass, with no regressions.

5. Run the full backend build and full test suite (final validation for the whole plan, per `CLAUDE.md`):

```bash
dotnet build
dotnet test
```

Expected: `Build succeeded.` with 0 errors/warnings introduced by this change, and all tests pass.

6. Run formatting check and apply formatting if needed:

```bash
dotnet format
```

Expected: no unexpected diffs outside the four files touched by this plan (`ReceivedInvoice.cs`, `FlexiReceivedInvoiceMappingProfile.cs`, `InvoiceClassificationService.cs`, `InvoiceClassificationServiceTests.cs`). If `dotnet format` modifies any of these four files, re-run step 5 to confirm tests still pass, then include the formatting diff in this commit.

7. Commit:

```bash
git add backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs
git commit -m "Pass AbraInvoiceId instead of InvoiceNumber into ClassificationHistory

RecordClassificationHistory previously passed invoice.InvoiceNumber for
both the AbraInvoiceId and InvoiceNumber constructor arguments of
ClassificationHistory, so every audit row had AbraInvoiceId ==
InvoiceNumber. ReceivedInvoice.AbraInvoiceId (sourced from FlexiBee's
internal Id via FlexiReceivedInvoiceMappingProfile) now carries the
genuinely distinct value this field was meant to hold.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9hXSww9WLhMo5YTaZwmv2"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (add `AbraInvoiceId` to `ReceivedInvoice`, default `string.Empty`, plain settable property) → `add-abra-invoice-id-domain-property`.
- FR-2 (map FlexiBee's internal `Id` to `AbraInvoiceId` via AutoMapper, `.ToString()`-based conversion) → `map-abra-invoice-id-from-flexibee-id`, using `CultureInfo.InvariantCulture` as the arch-review's clarification (Specification Amendments section) directed.
- FR-3 (fix `RecordClassificationHistory` call site, no change to `ClassificationHistory` constructor / EF config / DTO, no migration) → `fix-classification-history-call-site`; confirmed no other file in this plan touches `ClassificationHistoryConfiguration.cs`, `InvoiceClassificationMappingProfile.cs`, or any migration.
- FR-4 (update exactly the 4 test occurrences at lines 76/156/237/299 with distinct fixture values, leave `InvoiceClassificationMappingProfileTests.cs`/`ClassificationHistoryRepositoryTests.cs` untouched) → `update-invoice-classification-service-tests-for-abra-invoice-id`; step 4 of the final task explicitly runs those two other files to confirm they remain green without modification.
- NFR-1 (no DTO/contract shape change) → satisfied by construction; no DTO file appears in any task.
- NFR-2 (no truncation, invariant culture, fits `HasMaxLength(100)`) → satisfied by `CultureInfo.InvariantCulture` in the mapping profile task; no schema/column change made.
- Out-of-scope items (backfill, changing `Code`-based FlexiBee call parameters, exposing `AbraInvoiceId` on `ReceivedInvoiceDto`, documenting in `flexibee-api.md`) → none of these are touched by any task, matching the spec.

**Placeholder scan:** no "TBD"/"TODO"/"add appropriate error handling"/"similar to Task N" phrasing anywhere above; every code block is the complete before/after file content or method body, not a diff fragment requiring inference.

**Type consistency:** `AbraInvoiceId` is `string` (non-nullable, defaults to `string.Empty`) consistently across `ReceivedInvoice` (task 1), the AutoMapper `MapFrom` target (task 2), the test fixtures (task 3, string literals `"ABRA-00N"`), and the `ClassificationHistory` constructor's existing `string abraInvoiceId` parameter (task 4, unchanged) — no mismatch.
