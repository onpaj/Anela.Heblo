# validate-and-document r1
- Ran `dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests`: 377 passed, 5 skipped, 72 failed. All 72 failures are Testcontainers/Docker integration tests (Docker unavailable in this environment); none relate to the change. Classification tests: 5/5 pass.
- No docs/processes doc owns the touched files (tests only); no doc change.
- PR description notes the out-of-scope partial-failure semantics of `MarkInvoiceForManualReviewAsync` as a possible follow-up.
