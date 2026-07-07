## Module / File
`backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs`

## Coverage
Line coverage: 43% (filter threshold: 60%)

## What's not tested
**`UploadFileAsync` — small vs large routing:**
Files ≤ 4 MB use a single-PUT upload; files > 4 MB use a chunked upload session. No test verifies that the size threshold routes to the correct upload path. A change to the 4 MB constant (or an off-by-one) would silently mis-route uploads without a failing test.

**`UploadLargeFileAsync` — chunk loop:**
The chunked upload loop reads the stream in 10 MB increments and sends `Content-Range` headers. No test covers what happens when the last chunk is smaller than the buffer, or when the stream requires multiple reads to fill a single chunk (`totalRead < chunkSize` inner loop). A bug in the offset tracking would produce a corrupt upload with no observable error until the file is opened.

**`FindFolderAsync` — pagination and multi-match:**
The folder search follows `@odata.nextLink` pages. No test exercises the multi-page case. The multiple-match branch (`matches.Count > 1 && !allowMultiple` → `MultipleMatches`) and the `allowMultiple` path (picks alphabetically first) are also untested.

## Why it matters
This is the integration point for uploading PIF documents to SharePoint/OneDrive via the Graph API. A routing bug silently selects the wrong upload path; a chunk-loop offset bug corrupts the uploaded file; a multi-match bug either rejects a valid folder or picks the wrong one.

## Suggested approach
- Unit-test `UploadFileAsync` with mocked `IHttpClientFactory` at sizeBytes = threshold, threshold+1, threshold−1.
- Test `FindFolderAsync` with a mock returning two pages of results and assert all items from both pages are considered.
- Test the multi-match path with `allowMultiple = false` and assert `MultipleMatches` is returned. ~1.5 day effort.

---
_Filed by weekly coverage-gap routine on 2026-07-06. Based on CI run #28716987459 (2ad2a2593e1834798a3def9ac2551b46c2e595cb)._
