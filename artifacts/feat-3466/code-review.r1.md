## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs:95` — this hunk fixes an unrelated pre-existing build break (`ConfigurationConstants.APP_VERSION` → `InfrastructureConfigurationKeys.APP_VERSION`) that has nothing to do with `ProductMarginSegmentDto`. It's verified correct (matches the constant actually used in `GetConfigurationHandler.cs:75` and the other test cases in the same file), but bundling an unrelated fix into this PR is worth calling out — consider splitting it into its own commit/PR for a cleaner history, even though it was already applied as a separate commit per the impl notes.
- `frontend/src/api/generated/api-client.ts:12930-13000` — this generated file was hand-edited rather than produced by a full `npm run generate-client`/NSwag regeneration, per the implementation notes (to avoid pulling in unrelated pre-existing drift such as the new Packaging statistics endpoint and `ArticleGenerationStepStatus` reordering). The hand-edit is verified to exactly match what a scoped regeneration would produce for this DTO, but it leaves the generated client further out of sync with the actual backend contract elsewhere in the file — worth a follow-up task to reconcile the full client drift separately.
