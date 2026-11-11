### task: validate-and-document
**Goal:** Final validation and note the follow-up.

**Steps:**
1. Run the full Flexi test project (`dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests`) and confirm no regressions.
2. Confirm no `docs/processes` doc owns the touched files (only tests touched); no doc change needed.
3. In the PR description mention the out-of-scope partial-failure semantics of `MarkInvoiceForManualReviewAsync` as a possible follow-up.

**Done when:** full project passes and PR notes written.
