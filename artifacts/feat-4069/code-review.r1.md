## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs:52` — The pre-existing comment above the `Where` clause still hardcodes the literal `"Import faktur: {0}"` instead of referencing `InvoiceImportServiceConstants`. It's not executable code so it can't drift the actual matching logic, but per FR-1's intent ("no second copy of the literal... anywhere in the touched files") it would be tidier to reword it to point at `InvoiceImportServiceConstants.DisplayNameFormat`/`ImportPrefix` by name instead of spelling out the string.
