# Code Review: fix-invoice-id-filter-assertion

## Summary

The change tightens the else-branch assertion in test 3 ("Invoice ID filter with Enter key") so that only the first `<td>` cell of the first table row is checked for "2024", rather than the full row text. The variable is correctly renamed from `firstRowText` to `firstCellText`. Exactly two lines were modified — no unrelated lines were touched.

## Review Result: PASS

### task: fix-invoice-id-filter-assertion
**Status:** PASS
