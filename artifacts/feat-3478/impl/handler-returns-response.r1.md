# Implementation: handler-returns-response

## What was implemented
Replaced the `throw new EmptyRetrievalException(...)` in `GenerateLeafletHandler.Handle` with an early
`return new GenerateLeafletResponse(ErrorCodes.LeafletEmptyRetrieval, ...)` when both `kbHits` and
`leafletHits` are empty. This uses the `GenerateLeafletResponse(ErrorCodes, Dictionary<string,string>?)`
constructor and `ErrorCodes.LeafletEmptyRetrieval = 2504`, both already committed in prior tasks
(`add-error-code`, `response-error-constructor`). The `EmptyRetrievalException` class itself was left
untouched — it is still referenced by `LeafletController` and `LeafletTools`, which will be updated in
later tasks in this pipeline.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` — added `using Anela.Heblo.Application.Shared;`; replaced the throw on dual-empty retrieval with a `return new GenerateLeafletResponse(ErrorCodes.LeafletEmptyRetrieval, new() { { "detail", "Knowledge Base does not yet cover this topic; try a broader phrasing" } })`. No other lines changed.
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs` — added `using Anela.Heblo.Application.Shared;` (alphabetically ordered among existing usings); renamed/rewrote `Handle_dual_empty_retrieval_throws_EmptyRetrievalException` to `Handle_dual_empty_retrieval_returns_LeafletEmptyRetrieval_error`, asserting `response.Success == false`, `response.ErrorCode == ErrorCodes.LeafletEmptyRetrieval`, and `response.Params["detail"]` containing the expected diagnostic text, instead of asserting a thrown exception.

## Tests
- `GenerateLeafletHandlerTests.Handle_dual_empty_retrieval_returns_LeafletEmptyRetrieval_error` — verifies that when both KB and leaflet similarity search return zero hits, the handler returns (rather than throws) a `GenerateLeafletResponse` with `Success = false`, `ErrorCode = ErrorCodes.LeafletEmptyRetrieval`, and a `Params["detail"]` entry containing "Knowledge Base does not yet cover this topic".
- All other existing tests in the file were read and confirmed unaffected (mock field names `_kb`/`_leaflets`, `SetupEmbeddings()`, `CreateHandler()`, and request-construction pattern all matched the sketch in the task exactly — no adaptation was needed).

## How to verify
- `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — succeeds with 0 errors (139 pre-existing warnings unrelated to this change).
- Test file was verified by careful manual reading against the actual handler signature (`GenerateLeafletResponse(ErrorCodes, Dictionary<string,string>?)`), `BaseResponse` fields (`Success`, `ErrorCode`, `Params`), and the other passing tests' helper/mock usage in the same file — types, method signatures, and mock setups all line up.
- Once the unrelated `Anela.Heblo.Tests` build break (`ConfigurationConstants.APP_VERSION` missing in `GetConfigurationHandlerTests.cs`) is fixed by another task, run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GenerateLeafletHandlerTests"` to confirm the whole test class (including the new/renamed test) passes.

## Notes
- **Known limitation**: `Anela.Heblo.Tests` currently fails to *build* (not just fails tests) due to a pre-existing, unrelated compile error in `GetConfigurationHandlerTests.cs` (`ConfigurationConstants.APP_VERSION` missing), confirmed present on `origin/main` before this feature branch existed. `dotnet test` could not be run as a result. Correctness of the new/renamed test was instead verified by careful reading: the mock objects (`_kb`, `_leaflets`, `_embeddings`), `SetupEmbeddings()`, `CreateHandler()`, and `GenerateLeafletRequest` construction pattern all matched exactly what other passing tests in the same file already use, and the response shape (`Success`, `ErrorCode`, `Params`) matches `BaseResponse`'s error constructor exactly.
- Only the intended lines changed in the handler; no other behavior (word-length switch, audience label switch, cold-start logging, chat calls) was touched.

## Status
DONE_WITH_CONCERNS
