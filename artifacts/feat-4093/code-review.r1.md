## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/BlobPathValidatorTests.cs:9` — Uses `[InlineData(null!)]`, but every other null-`InlineData` case in the test project (42 occurrences, e.g. `backend/test/Anela.Heblo.Tests/Features/Journal/CreateJournalEntryHandlerTests.cs:157`) writes `[InlineData(null)]` (no `!`) and instead applies the null-forgiving operator at the call site (`title!`) inside the test body, same as this file already does at `BlobPathValidator.IsValid(blobPath!)`. The `null!` on the attribute is redundant given that pattern and is the one place in the suite that deviates from the established convention noted in spec NFR-1.
