## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `frontend/test/e2e/issued-invoices/filters.spec.ts:93` — Test 4 ("Invoice ID filter with Filtrovat button") still asserts `tableRows.first().textContent()` (the full row) for "2024", while the parallel Test 3 was tightened in this PR to check only the first `<td>` (the invoice-ID cell). The first column confirmed in `IssuedInvoicesPage.tsx:658` is indeed `invoice.id`, so a `date` column or a customer-name cell containing "2024" could produce a false-positive in Test 4. Consider applying the same `.locator("td").first()` narrowing there for consistency.
