## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests.cs:155` — `CapturingCommandInterceptor` is copy-pasted verbatim (identical implementation) into this file and at least four other SQL-shape test classes (`IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests`, `PurchaseOrderRepositoryHistorySqlShapeTests`, `PhotobankRepositoryGetTagsSqlShapeTests`, `ArticleRepositoryFeedbackProjectionSqlTests`). Extracting it once into `Anela.Heblo.Tests.Common` would remove the duplication; not something this PR needs to fix since it follows the established (if repetitive) convention.
