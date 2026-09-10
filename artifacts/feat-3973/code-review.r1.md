## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs:42-56` — the runner-availability check now runs twice per successful request: once in the new `validationScope` (`Any(...)`) and again inside the fire-and-forget `Task.Run` (`SingleOrDefault(...)`). This is intentional per the spec (defense-in-depth) and is not a bug, but it is a small amount of duplicated lookup logic across two scopes; a future cleanup could thread the resolved runner (or at least the `IDqtJobRunner` type) through the closure if the double-scope-creation cost/complexity ever becomes worth removing.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs:38` — the `string` → `string?` parameter-type fix is unrelated to this feature's scope (a pre-existing nullable-annotation mismatch for the `[InlineData(null)]` case); harmless and correct, but drive-by relative to the stated fix.
