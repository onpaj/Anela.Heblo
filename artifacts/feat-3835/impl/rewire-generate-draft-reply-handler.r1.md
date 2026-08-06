# Implementation: rewire-generate-draft-reply-handler

## What was implemented
Swapped `GenerateDraftReplyHandler`'s dependency on `IMediator`/`SearchDocumentsRequest` for the new `ISmartsuppKnowledgeSource` abstraction. Updated the existing unit tests to mock `ISmartsuppKnowledgeSource` instead of `IMediator`; no new test scenarios were added, every existing assertion is preserved.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs` — constructor now takes `ISmartsuppKnowledgeSource` instead of `IMediator`; `Handle` calls `_knowledgeSource.SearchAsync(...)` instead of dispatching `SearchDocumentsRequest` via MediatR
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs` — mocks `ISmartsuppKnowledgeSource` instead of `IMediator`

## Tests
`dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GenerateDraftReplyHandlerTests"` → Passed! - Failed: 0, Passed: 16, Skipped: 0, Total: 16.

## How to verify
Run the filtered test command above, then `dotnet build Anela.Heblo.sln` (build succeeded, no new compile errors).

## Notes
None.

## Status
DONE
