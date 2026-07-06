## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/GraphCatalogDocumentsStorageTests.cs:190-207,232-249,279-296,328-346,373-390` — the `createUploadSession`/chunk-response `responder` lambda (branch on `HttpMethod.Post` → session JSON, else → `{"id":"item-1","name":"final.pdf"}`) is duplicated near-verbatim across five large-file tests. Extracting a small `CreateLargeUploadResponder(...)` helper (optionally parameterized by chunk response name) would remove the repetition without changing behavior.
- `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/GraphCatalogDocumentsStorageTests.cs:140,164,188,229,276,319,369` — `const long threshold = 4 * 1024 * 1024` and `const long chunkSize = 10 * 1024 * 1024` are re-declared locally in nearly every new test instead of being hoisted to shared `private const` fields on the test class (mirroring the production constants), which would also make the threshold/chunk-size relationship to the SUT more visually explicit.

### Verification performed
- Read the full diff; confirmed the only non-artifact change is `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/GraphCatalogDocumentsStorageTests.cs` (14 new `[Fact]` tests + `ThrottledReadStream` helper), no production code touched.
- Cross-checked every new test's arrange/act/assert against the actual production logic in `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs` (threshold `<=` boundary, chunk `Content-Range` offset math, `uploadedName` update-from-last-chunk, early-EOF pre-existing behavior, `FindFolderAsync` pagination/matching/`allowMultiple` ordering, 404 short-circuit) — all assertions match actual behavior, no tautological checks.
- Confirmed `ThrottledReadStream` overriding only the `byte[]`-based `ReadAsync` override is sufficient: production calls `content.ReadAsync(buffer.AsMemory(totalRead), ct)`, and `Stream`'s default `ReadAsync(Memory<byte>, ...)` delegates to the array-based overload when the destination memory is array-backed (it is, since `buffer` is a `byte[]`), so the throttling logic is actually exercised.
- Ran `dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — 0 errors (pre-existing unrelated warnings only).
- Ran `dotnet test --filter "FullyQualifiedName~GraphCatalogDocumentsStorageTests"` — 17/17 passed (3 pre-existing + 14 new).
