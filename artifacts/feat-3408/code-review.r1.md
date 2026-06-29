## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs:407` — `Assert.IsType<InvalidOperationException>(ex)` in `_ExceptionTypeIsPreserved` is redundant: `Assert.ThrowsAsync<InvalidOperationException>` already guarantees the type. The message-equality assertion (`Assert.Equal("flush failed", ex.Message)`) is the only value-adding check in that test; the `IsType` line can be removed without losing coverage.
