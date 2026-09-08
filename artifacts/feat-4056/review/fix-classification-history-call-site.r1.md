```markdown
# Code Review: fix-classification-history-call-site

## Summary
The implementation makes exactly the one-line change specified: `RecordClassificationHistory` now passes `invoice.AbraInvoiceId` (instead of `invoice.InvoiceNumber`) as the first `ClassificationHistory` constructor argument. All other lines in the file, including the FlexiBee-facing calls that must keep using `invoice.InvoiceNumber`, are unchanged, and the accompanying test file exercises the corrected behavior with distinct `AbraInvoiceId`/`InvoiceNumber` values.

## Review Result: PASS

### task: fix-classification-history-call-site
**Status:** PASS

## Overall Notes
Verified against the live file and the provided diff: line 102 changes `invoice.InvoiceNumber` → `invoice.AbraInvoiceId`; lines 41, 49-50, and 88 (the `MarkInvoiceForManualReviewAsync`/`UpdateInvoiceClassificationAsync` calls and the log statement) still correctly use `invoice.InvoiceNumber`, matching the explicit out-of-scope note. The test file's four tests use distinct `AbraInvoiceId`/`InvoiceNumber` values per invoice and assert `capturedHistory.AbraInvoiceId.Should().Be(invoice.AbraInvoiceId)`, consistent with the fix. No architecture, correctness, or completeness issues found.
```
