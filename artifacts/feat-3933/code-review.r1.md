## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes
Reviewed the full feature diff (merge-base `e03bd604f4d00d99aad8eb4dd782b8aa07e92deb` vs `HEAD`) against `spec.r1.md`. The only production/test code change is the addition of four `[Fact]` tests plus one `[InlineData(null)]` case to `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs`. Cross-checked every new test's expected error codes, error messages, and mock dispatch/verify calls directly against `GetIssuedInvoiceDetailHandler.cs`:

- Null/empty/whitespace `InvoiceId` → `ErrorCodes.ValidationError`, no repository calls: matches.
- `WithDetails == true` → `GetByIdWithSyncHistoryAsync` called once, `GetByIdAsync` never called (and vice versa for `WithDetails == false`): matches the handler's ternary dispatch exactly.
- Repository returns `null` → `ErrorCodes.ResourceNotFound`, `Params["ErrorMessage"] == "Faktura nebyla nalezena"`, mapper never invoked: matches.
- Repository throws → caught by the outer `try/catch`, `ErrorCodes.Exception`, `Params["ErrorMessage"] == "Chyba při načítání detailu faktury"`, no rethrow: matches.

No production code was changed, consistent with the spec's "test-only" scope. All new tests use synthetic `InvoiceId` values (`INV-TEST-00N`) per NFR-2, and use only mocked dependencies (no I/O) per NFR-1.

One unrelated commit is present on the branch (`dcaa96b`, a one-line fix to `.claude/skills/_lib/gh_api.sh` adding a `Content-Type: application/json` header on JSON POST/PATCH bodies). It is out of the feature's spec scope but is a small, correct, self-contained infrastructure fix with no interaction with the invoice-handler test changes — not a correctness bug in the reviewed feature, so it is not flagged as blocking.

No correctness bugs and no reuse/simplification/efficiency issues found.
