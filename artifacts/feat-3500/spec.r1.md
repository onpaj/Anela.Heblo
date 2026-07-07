# Specification: Close Coverage Gap in GraphCatalogDocumentsStorage

## Summary
`GraphCatalogDocumentsStorage` (the Microsoft Graph integration used to upload PIF/material documents to SharePoint/OneDrive) sits at 43% line coverage against a 60% gate. Three code paths are entirely untested: the small-vs-large upload routing decision in `UploadFileAsync`, the chunked-upload offset/read-loop in `UploadLargeFileAsync`, and the pagination / multi-match logic in `FindFolderAsync`. This is a test-only tech-debt task — no production code in `GraphCatalogDocumentsStorage.cs` is expected to change; the deliverable is a set of targeted unit tests added to the existing `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/GraphCatalogDocumentsStorageTests.cs` file that exercise these paths and would fail if the underlying logic regressed.

## Background
`GraphCatalogDocumentsStorage` implements `ICatalogDocumentsStorage` and is the sole production adapter to Microsoft Graph for catalog document storage (there is also a `NoOpCatalogDocumentsStorage` used presumably for local/dev). It is consumed by `UploadPifDocumentHandler` and `UploadMaterialDocumentHandler` to place uploaded files into the correct SharePoint/OneDrive folder.

A test file already exists for this class and establishes the mocking pattern to reuse:
- `CreateStorage(Func<HttpRequestMessage, HttpResponseMessage> responder)` — builds a `GraphCatalogDocumentsStorage` wired to a mocked `ITokenAcquisition` (app token `"app-token"`, delegated token `"delegated-token"`) and a `RecordingHandler` (a fake `HttpMessageHandler`) that records every outgoing `HttpRequestMessage` and answers via the supplied `responder` callback.
- `RecordingHandler` — captures requests in an ordered `List<HttpRequestMessage>` for later assertion (URL, method, headers) and returns whatever `HttpResponseMessage` the test's responder function produces, keyed however the test wants (e.g., by inspecting `request.RequestUri`).

The existing three tests cover token selection (`UploadFileAsync` uses the delegated token, `FindFolderAsync` uses the app token) and the consent-missing failure path. They do not touch the three gaps this ticket targets. The new tests must be added to this same file, following its established patterns, rather than introducing a parallel test harness.

Relevant production code (read in full from `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs`):
- `UploadSessionThresholdBytes = 4 * 1024 * 1024` (4 MB). `UploadFileAsync` routes to `UploadSmallFileAsync` when `sizeBytes <= UploadSessionThresholdBytes`, and to `UploadLargeFileAsync` when `sizeBytes > UploadSessionThresholdBytes`.
- `UploadSmallFileAsync` issues a single `PUT` to `.../items/{folderId}:/{encodedName}:/content?@microsoft.graph.conflictBehavior=rename`.
- `UploadLargeFileAsync` first `POST`s to `.../createUploadSession` to obtain an `uploadUrl`, then loops: for each iteration it fills a 10 MB (`chunkSize = 10 * 1024 * 1024`) buffer via a nested `while (totalRead < chunkSize)` loop that calls `content.ReadAsync` repeatedly until the buffer is full or the stream returns `0` (EOF), sends a `PUT` with a `Content-Range` header covering `[offset, offset + totalRead - 1]` of `sizeBytes`, and advances `offset += totalRead`. The outer loop continues `while (offset < sizeBytes)`.
- `FindFolderAsync` lists the immediate children of `basePath` via Graph, following `@odata.nextLink` across pages into a single accumulated list, filters to folder items whose name starts with `prefix` (ordinal, case-insensitive), and then: returns `NotFound` if there are no matches; returns `MultipleMatches` if there is more than one match and `allowMultiple` is `false`; otherwise returns `Found` with the alphabetically-first match (by `StringComparer.OrdinalIgnoreCase`) — this alphabetical-first selection also applies when `allowMultiple` is `true` and there are multiple matches.

## Functional Requirements

### FR-1: `UploadFileAsync` routing tests (small vs. large upload path)
Add tests that pin the size-threshold routing decision in `UploadFileAsync` so a future change to `UploadSessionThresholdBytes` (or an off-by-one in the comparison) fails a test instead of silently mis-routing uploads.

Use the existing `CreateStorage` helper. Distinguish which path was taken by asserting on the recorded request(s) in `RecordingHandler.Requests`:
- Small-file path: exactly one request is sent, and its `RequestUri` matches the single-PUT `.../content?@microsoft.graph.conflictBehavior=rename` pattern (method `PUT`).
- Large-file path: at least two requests are sent, the first being a `POST` to a URL containing `/createUploadSession`, and subsequent request(s) being `PUT`s to the mocked session `uploadUrl`.

**Acceptance criteria:**
- A test calling `UploadFileAsync` with `sizeBytes == UploadSessionThresholdBytes` (4 MB exactly) asserts the small-file (single-PUT) path is used.
- A test calling `UploadFileAsync` with `sizeBytes == UploadSessionThresholdBytes + 1` asserts the large-file (chunked/session) path is used.
- A test calling `UploadFileAsync` with `sizeBytes == UploadSessionThresholdBytes - 1` asserts the small-file path is used.
- Each test supplies a content `Stream` whose length matches its declared `sizeBytes` and a mocked Graph response sequence sufficient for that path to complete without error (small: one `200 OK` item response; large: a `createUploadSession` response plus one or more chunk `200/201` responses).
- Tests assert on recorded request shape (method + URL pattern and/or request count), not on internal/private method calls.

### FR-2: `UploadLargeFileAsync` chunk-loop offset-tracking tests
Add tests that exercise the chunked-upload read/offset loop, reachable only through the public `UploadFileAsync` entry point with `sizeBytes > UploadSessionThresholdBytes`.

**Acceptance criteria:**
- **Final partial chunk:** a test uploads a file whose size is larger than one chunk (10 MB) but not an exact multiple of the chunk size (e.g., 10 MB + 2 MB = 12 MB), using a content stream that yields data on each `ReadAsync` call up to the requested amount. The test asserts:
  - Two chunk `PUT` requests are sent to the upload-session URL.
  - The first chunk's `Content-Range` header covers bytes `0` through `chunkSize - 1` of the total `sizeBytes`.
  - The second (final) chunk's `Content-Range` header covers bytes `chunkSize` through `sizeBytes - 1` (i.e., its length is the remainder, not a full chunk) of the total `sizeBytes`.
- **Multi-read fill of a single chunk:** a test uses a custom/fake `Stream` whose `ReadAsync` deliberately returns fewer bytes than requested on at least one call (e.g., returns at most N bytes per call, where N is much smaller than `chunkSize`), for a total file size of exactly one chunk (10 MB) or a bit more. The test asserts:
  - The inner fill loop correctly assembles a full chunk from multiple short reads before sending the `PUT` (i.e., the `Content-Range` header for the chunk reflects the full expected chunk length, not the length of a single short read).
  - `Content-Range` offsets remain correct across chunks when short reads are involved (no double-counting or gap in `offset`).
- **Two full chunks + exact-multiple boundary:** a test with `sizeBytes` an exact multiple of `chunkSize` (e.g., 20 MB) asserts exactly two chunk requests are sent (no trailing empty/zero-length final chunk request), verifying the outer `while (offset < sizeBytes)` loop terminates correctly at the boundary.
- Each chunk `PUT` response in these tests should return `200 OK` (or `201 Created`) with a valid `CatalogGraphDriveItem` JSON body so the loop's status-code branch is exercised on the success path; the method's returned filename should be asserted to equal the name from the **last** chunk response (confirming `uploadedName` is updated from the final chunk, not a stale first-chunk value).

### FR-3: `FindFolderAsync` pagination and multi-match tests
Add tests covering the `@odata.nextLink` pagination loop and the match-count branching (`NotFound` / `MultipleMatches` / `Found` with alphabetical selection), reusing the `CreateStorage` responder pattern by branching the mocked response on the requested URL.

**Acceptance criteria:**
- **Multi-page pagination:** a test mocks a first-page response containing one or more items plus a non-null `@odata.nextLink`, and a second-page response (returned when the handler receives a request to that `nextLink` URL) containing additional items and no `@odata.nextLink`. The test asserts the method considers items from **both** pages when computing matches (e.g., a matching folder that exists only on page two is still found/returned, or a multi-match spanning both pages still yields `MultipleMatches`).
- **No matches:** a test with zero items matching `prefix` (either because the folder listing is empty or no name starts with `prefix`) asserts `FolderStatus.NotFound` is returned.
- **Single match:** a test with exactly one folder item whose name starts with `prefix` asserts `FolderStatus.Found` is returned with the matching `FolderId`/`FolderName` populated.
- **Multiple matches, `allowMultiple = false`:** a test with two or more folder items matching `prefix` and `allowMultiple: false` asserts `FolderStatus.MultipleMatches` is returned (and that no `FolderId`/`FolderName` is populated, per current `FolderSearchResult` construction for that branch).
- **Multiple matches, `allowMultiple = true`:** a test with two or more matching folder items in non-alphabetical order and `allowMultiple: true` asserts `FolderStatus.Found` is returned with the **alphabetically first** match's `FolderId`/`FolderName` (ordinal, case-insensitive comparison), confirming the `OrderBy(..., StringComparer.OrdinalIgnoreCase).First()` selection.
- **Non-folder items excluded:** a test includes at least one item whose name matches `prefix` but which has a `null` `folder` facet (i.e., a file, not a folder) and asserts it is excluded from match consideration (does not count toward `Found`/`MultipleMatches`).
- **404 short-circuit (existing behavior, not currently asserted at this level — optional but recommended):** a test where the first-page request returns HTTP 404 asserts `FolderStatus.NotFound` is returned without attempting pagination.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a unit-test-only change with no runtime/production code path affected. Tests must run fast (no real network calls; `RecordingHandler` fully substitutes for the Graph HTTP endpoint) and complete within the existing test suite's normal execution budget (sub-second per test).

### NFR-2: Security
Not applicable in the sense of introducing new security surface. Tests must not weaken or bypass any authentication/token-acquisition logic under test — they mock `ITokenAcquisition` exactly as the existing tests do (app token vs. delegated token), and must continue to assert the correct token is used for each call path where relevant (already covered by existing tests; new tests are not required to re-assert token selection unless it's incidental to the scenario).

## Data Model
No new or changed data model. Tests operate against existing types already defined in the codebase:
- `FolderSearchResult` (`Status`, `FolderId`, `FolderName`) and `FolderStatus` enum (`NotFound`, `MultipleMatches`, `Found`) — `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/FolderSearchResult.cs`, `Contracts/FolderStatus.cs`.
- `CatalogGraphDriveItem`, `CatalogGraphDriveItemCollection` (`Value`, `NextLink`), `CatalogGraphFolderFacet`, `CatalogGraphFileFacet`, `CatalogGraphUploadSession` (`UploadUrl`) — `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/CatalogGraphModels.cs`. These are the exact JSON shapes new tests must construct as mocked Graph API response bodies.
- `CatalogDocumentDto` — not directly exercised by the three gaps in scope but present in the same file; no changes needed.

## API / Interface Design
No API or interface changes. This ticket only adds test methods to `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/GraphCatalogDocumentsStorageTests.cs`, following the existing `CreateStorage` / `RecordingHandler` pattern already defined in that file. New tests should be grouped under clear region/comment banners consistent with the existing `// ─── UploadFileAsync — delegated token ───` style, e.g.:
- `// ─── UploadFileAsync — size routing ───`
- `// ─── UploadLargeFileAsync — chunk loop ───`
- `// ─── FindFolderAsync — pagination & matching ───`

For the multi-read/short-read scenario in FR-2, a small private helper `Stream` subclass (or a wrapper around `MemoryStream` that caps bytes returned per `ReadAsync` call) will need to be added to the test file's "Recording infrastructure" section, alongside the existing `RecordingHandler`.

For branching mock responses by request URL/method (needed for FR-1's session-then-chunk sequence and FR-3's pagination), the `responder` callback passed to `CreateStorage` should pattern-match on `request.RequestUri` (e.g., `Contains("createUploadSession")`, equality with the mocked `nextLink` URL) and/or a call counter, consistent with how a `Func<HttpRequestMessage, HttpResponseMessage>` is already used in the existing tests (each existing test currently ignores the input and returns a fixed response, since none of them yet needs multi-request branching — this ticket is the first to require it).

## Dependencies
- Existing test project `backend/test/Anela.Heblo.Tests` (xUnit, Moq, FluentAssertions — already referenced by the current test file).
- No new NuGet packages required. No production dependency changes.
- Depends on the current shape of `GraphCatalogDocumentsStorage.cs`, `CatalogGraphModels.cs`, `FolderSearchResult.cs`, and `Contracts/FolderStatus.cs` remaining as read for this spec; if the file changes before implementation, the spec's line-level references should be re-verified.

## Out of Scope
- Any change to production code in `GraphCatalogDocumentsStorage.cs`, `CatalogGraphModels.cs`, `ICatalogDocumentsStorage.cs`, or related contracts. This is a test-only coverage-gap fix.
- Testing `ListFilesAsync` (not called out in the brief; also uses pagination but is not part of the identified coverage gap).
- Testing `GetDelegatedTokenAsync` / `AcquireDelegatedTokenAsync` single-flight caching behavior beyond what the existing consent-failure test already covers.
- Integration or end-to-end tests against a real/sandboxed Microsoft Graph endpoint — this remains pure unit testing with a fake `HttpMessageHandler`.
- Retry/resume behavior for interrupted chunked uploads (the production code has no retry logic to test).
- Raising line coverage anywhere else in the codebase; this ticket targets only the three named gaps in this one file.

## Open Questions
- **Potential pre-existing correctness gap (not to be fixed under this ticket):** `UploadLargeFileAsync`'s outer loop (`while (offset < sizeBytes)`) exits as soon as the inner read loop reads `0` bytes (stream EOF), even if `offset` has not yet reached `sizeBytes`. There is no post-loop check that the total bytes uploaded equals the declared `sizeBytes`; if a caller passes a `sizeBytes` larger than the actual stream content, the method would silently return a "success" filename after uploading fewer bytes than declared, rather than throwing. Should a test merely document this current behavior (i.e., assert it does *not* throw and stops early — pinning the existing behavior), or should this be filed as a separate bug ticket for the production code? Recommend: add one test that pins current behavior (no exception, loop stops when stream is exhausted early) and separately flag this to the architect as a candidate follow-up bug ticket — do not change production code in this pass.
- Should the `RecordingHandler` change (URL/count-based branching) be made generic/reusable in the test file's "Recording infrastructure" section for future tests, or kept as small inline anonymous responders per new test? Recommend: keep the existing `RecordingHandler` class unchanged and do all branching inside each test's `responder` lambda (using closures/counters), to minimize risk of touching shared test infrastructure that other tests already depend on.
- The brief's suggested effort (~1.5 day) and the FR-2/FR-3 acceptance criteria above imply roughly 8-10 new test methods. Confirm this granularity is acceptable versus a more consolidated set of parameterized (`[Theory]`) tests for the threshold cases in FR-1 — no strong preference from this spec; either satisfies the acceptance criteria as written.

## Status: HAS_QUESTIONS
