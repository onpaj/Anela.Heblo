# Code Review: Invert Smartsupp → KnowledgeBase dependency in GenerateDraftReplyHandler (#3835)

## Review Result: CLEAN

## Summary

This change inverts the Smartsupp → KnowledgeBase module-boundary violation exactly as
specified, following the already-established Article/Leaflet pattern precisely:

- `ISmartsuppKnowledgeSource` + `SmartsuppKnowledgeChunk` (Smartsupp-owned contract) added
  in `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ISmartsuppKnowledgeSource.cs`,
  with a helpful XML-doc note explaining why it mirrors `IArticleKnowledgeSource`'s
  string-query shape rather than `ILeafletKnowledgeSource`'s embedding shape.
- `KnowledgeBaseSmartsuppKnowledgeSource` (KnowledgeBase-owned adapter) added as
  `internal sealed`, delegating to the unchanged `SearchDocumentsRequest`/`SearchDocumentsHandler`
  MediatR flow and mapping all five `ChunkResult` fields to `SmartsuppKnowledgeChunk` with no
  data loss (including `DocumentId`, needed by `DraftReplySource`).
- DI registration added in `KnowledgeBaseModule.AddKnowledgeBaseModule` with `Scoped` lifetime,
  matching the Article/Leaflet bindings.
- `GenerateDraftReplyHandler` rewired to depend on `ISmartsuppKnowledgeSource`; `IMediator` was
  correctly removed from the field list/constructor (it had no other use site) while the
  `using MediatR;` import correctly remains because `IRequestHandler<...>` still needs it.
- A new `Smartsupp -> KnowledgeBase` rule and empty allowlist were added to
  `ModuleBoundariesTests.cs`, structurally identical to the existing Article/Leaflet rules.
- `GenerateDraftReplyHandlerTests.cs` was reworked mechanically to mock `ISmartsuppKnowledgeSource`
  instead of `IMediator`, preserving every existing assertion (including the two tests that
  previously captured `SearchDocumentsRequest.Query` and now capture the `SearchAsync` `query`
  argument directly).
- New `KnowledgeBaseSmartsuppKnowledgeSourceTests.cs` covers the adapter's dispatch, field
  mapping, empty-result, and cancellation-token propagation behavior.

I independently verified this is a genuine fix, not one that merely evades the reflection-based
boundary check: `ModuleBoundariesTests`'s `EnumerateReferencedTypes` inspects fields, properties,
constructor/method parameters and return types (not method-body locals), and confirmed that no
member of any type under `Anela.Heblo.Application.Features.Smartsupp` references any
`KnowledgeBase` namespace after this change — a `grep` for `KnowledgeBase` under the Smartsupp
tree only turns up doc comments. I also verified:

- `dotnet build Anela.Heblo.sln` succeeds with 0 errors (pre-existing, unrelated warnings only).
- `dotnet format Anela.Heblo.sln --verify-no-changes` reports no changes needed.
- The targeted test suite (`ModuleBoundariesTests`, `GenerateDraftReplyHandlerTests`,
  `KnowledgeBaseSmartsuppKnowledgeSourceTests`) passes: 55/55.
- `SmartsuppModule.cs` was correctly left untouched — the provider (KnowledgeBase) owns the DI
  registration, consistent with the documented pattern.
- The reverse reference (`KnowledgeBaseModule.cs` importing `Anela.Heblo.Application.Features.Smartsupp.Contracts`)
  is the intended direction (provider implementing consumer's contract) and is not itself
  forbidden by any existing rule, matching the Article/Leaflet precedent.

No correctness bugs found.

## Blocking

None.

## Advisory

- `KnowledgeBaseModule.cs`'s new `using Anela.Heblo.Application.Features.Smartsupp.Contracts;`
  is inserted out of alphabetical order relative to its neighbors (between `Article.Contracts`
  and `KnowledgeBase.Pipeline`). This matches the file's pre-existing, already-unsorted using
  block (e.g. `KnowledgeBase.Infrastructure` and `Microsoft.Identity.Web` are likewise
  out of order above it), and `dotnet format` raises no complaint, so this is not a new
  regression — just an opportunity to tidy if anyone revisits that file's imports.
- The new adapter test file lives at
  `backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSourceTests.cs`,
  matching `KnowledgeBaseArticleKnowledgeSourceTests.cs`'s placement, while a third precedent
  (`KnowledgeBaseLeafletSourceAdapterTests.cs`) lives instead under
  `Features/KnowledgeBase/Infrastructure/`. This inconsistency predates this PR (the two
  existing adapters already disagree), so the new file's placement is a reasonable pick
  consistent with at least one precedent — flagging only in case a future cleanup wants to
  unify the two conventions.
