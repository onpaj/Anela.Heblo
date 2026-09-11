# Implementation: update-upload-document-tests

## What was implemented
Updated `UploadDocumentHandlerTests.cs` to construct `UploadDocumentRequest` using the new `Content` (byte[]) property instead of the removed `FileStream` (Stream) property, matching the shape introduced by the prior `migrate-upload-document-request-to-bytes` task.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs` — replaced `FileStream = new MemoryStream(...)` with `Content = ...u8.ToArray()` in all 5 test methods: `Handle_NewDocument_IndexesAndReturnsIndexedStatus`, `Handle_OctetStreamWithTxtExtension_ResolvesToTextPlainAndIndexes`, `Handle_OctetStreamWithDocxExtension_ResolvesToDocxContentType`, `Handle_UnsupportedFileType_ReturnsUnsupportedFileTypeErrorWithoutThrowing`, `Handle_IndexDocumentRequest_ContainsUploadSourcePath`.

## Tests
`backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs` — all 5 tests cover upload/index handling for various file types.

Run result:
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UploadDocumentHandlerTests"
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 16 ms - Anela.Heblo.Tests.dll (net8.0)
```

`grep -n "FileStream" backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs` — no matches.

## How to verify
1. `grep -n "FileStream" backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs` should return nothing.
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UploadDocumentHandlerTests"` should report 5 passed, 0 failed.

## Notes
Only the object-initializer lines were changed; no other lines, usings, or formatting were touched. `System.IO` remains used elsewhere in the file (unrelated `MemoryStream`/`Stream` usages in mocked dependencies), so no using was removed.

## PR Summary
Brings the `UploadDocumentHandlerTests` test file in line with the `UploadDocumentRequest.Content` (byte[]) shape introduced by the prior task in this issue, which had left this test file non-compiling against the old `FileStream` property.

### Changes
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs` — updated 5 test methods' `UploadDocumentRequest` construction from `FileStream = new MemoryStream(...)` to `Content = ...u8.ToArray()`

## Status
DONE
