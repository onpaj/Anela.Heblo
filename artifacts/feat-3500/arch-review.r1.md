# Architecture Review: Close Coverage Gap in GraphCatalogDocumentsStorage

## Skip Design: true

## Architectural Fit Assessment

This is a test-only tech-debt task with zero production surface. There is no new component, no new interface, no new module — the deliverable is exclusively additional `[Fact]`/`[Theory]` methods (plus one small test-only helper `Stream`) appended to an existing, already-conventional test file:

`backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/GraphCatalogDocumentsStorageTests.cs`

I read the production file (`backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs`), the existing test file, the model/contract types it depends on (`CatalogGraphModels.cs`, `FolderSearchResult.cs`, `Contracts/FolderStatus.cs`), and `GraphApiHelpers.cs`. The spec's description of the production code and the existing test harness is accurate — no amendments needed there. The stack matches `docs/architecture/testing-strategy.md`: xUnit + Moq + FluentAssertions, `HttpMessageHandler` faking rather than a real Graph sandbox (there is precedent for this exact pattern in `Features/KnowledgeBase/Services/GraphDriveItem.cs`'s sibling models, confirming Graph-via-fake-handler is the established convention for this codebase, not something invented for this file).

The existing `CreateStorage(responder)` / `RecordingHandler` harness is sufficient for every scenario in the spec — including multi-request sequences (upload-session-then-chunks, paginated folder listing) — provided the `responder` lambda closes over local state (a counter, or branches on `request.RequestUri`/`request.Method`). No harness changes are required. This keeps risk to shared test infrastructure at zero, which matters because other tests in the same file already depend on `RecordingHandler`'s current (simple) behavior.

## Proposed Architecture

### Component Overview

No architectural components change. For completeness, this is what's under test and how the new tests plug into it:

```
GraphCatalogDocumentsStorageTests.cs   (existing file — ONLY file touched)
│
├── CreateStorage(responder)  ─────────► GraphCatalogDocumentsStorage (SUT, unchanged)
│     • Mock<ITokenAcquisition>                │
│     • Mock<IHttpClientFactory>               ├─ FindFolderAsync(...)      [app token]
│     • RecordingHandler(responder)            ├─ UploadFileAsync(...)      [delegated token]
│                                               │    ├─ UploadSmallFileAsync   (≤ 4 MB)
│                                               │    └─ UploadLargeFileAsync  (> 4 MB, chunked)
├── RecordingHandler : HttpMessageHandler
│     • Requests: List<HttpRequestMessage>   ◄──── new tests assert against this
│     • responder: Func<HttpRequestMessage, HttpResponseMessage>
│                                                    (existing tests use `_ =>` ignoring input;
│                                                     new tests use closures/counters — no class change)
│
└── NEW: ThrottledReadStream (private nested helper, test-only)
      • wraps a MemoryStream/byte[], caps bytes returned per ReadAsync call
      • used only by the FR-2 "multi-read fill" scenario
```

### Key Design Decisions

#### Decision 1: How to make `RecordingHandler` answer multi-request sequences

**Options considered:**
- (a) Extend `RecordingHandler` itself with built-in URL-routing / sequencing support (e.g. a `Dictionary<Predicate, HttpResponseMessage>` table).
- (b) Keep `RecordingHandler` exactly as-is; do all branching inside each test's `responder` lambda via closures (a local counter, or pattern-matching on `request.RequestUri!.ToString()` / `request.Method`).

**Chosen approach:** (b).

**Rationale:** `RecordingHandler` is shared by all tests in the file, including the three existing ones. Option (a) would require touching code the passing tests already rely on, for a benefit only the new tests need. Closures are sufficient — C# lambdas can trivially maintain a mutable counter or switch on `request.RequestUri.ToString().Contains(...)`. This matches the spec's own recommendation and keeps the blast radius of this change to "new test methods only," consistent with the "surgical changes" principle for this repo.

#### Decision 2: Individual `[Fact]`s vs. `[Theory]` for the threshold routing cases (FR-1)

**Options considered:**
- (a) Three separate `[Fact]` methods for `threshold`, `threshold+1`, `threshold-1`.
- (b) A single `[Theory]` with `[InlineData]` for the three sizes, asserting a shared "path taken" outcome.

**Chosen approach:** (a) for FR-1, but only because the *arrange* step differs by branch (the small path needs one mocked response; the large path needs a `createUploadSession` response plus at least one chunk response) — a `[Theory]` would need internal branching in the test body to set up different responders per size, which defeats the readability benefit of `[Theory]`. Where the spec's own open question suggests `[Theory]` is acceptable, apply it narrowly: e.g. a `[Theory]` is fine for asserting "is small-file path taken?" (`threshold` and `threshold-1` share identical arrange/responder), with the `threshold+1` (large-path) case kept as its own `[Fact]`.

**Rationale:** Optimize for the assertions being self-evidently correct at a glance, per `docs/architecture/testing-strategy.md`'s "Maintainability Over Coverage" principle, over minimizing method count. Do not force a shared theory body across branches whose fixtures genuinely differ.

#### Decision 3: New helper `Stream` for short/partial reads (FR-2)

**Options considered:**
- (a) A one-off `Stream` subclass added inline to the test file.
- (b) A shared test-utilities project/class if one already exists for stream fakes.

**Chosen approach:** (a) — add a small private nested class (e.g. `ThrottledReadStream : Stream`) directly in `GraphCatalogDocumentsStorageTests.cs`, under a `// ─── Recording infrastructure ───` banner alongside `RecordingHandler`, matching the file's existing convention of colocating test infrastructure with the tests that use it.

**Rationale:** I found no shared "fake stream" utility elsewhere in `backend/test/Anela.Heblo.Tests` worth reusing (confirmed by inspecting the test file's own structure) and this stream is single-purpose (cap bytes per `ReadAsync`, backed by a `MemoryStream`/`byte[]`). Introducing a shared abstraction for one caller would be premature generalization; keep it local per the "surgical changes" rule.

## Implementation Guidance

### Directory / Module Structure

No new files, no new folders. All new test code goes into the existing file:

`backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/GraphCatalogDocumentsStorageTests.cs`

Add three new region banners after the existing `// ─── UploadFileAsync — delegated token ───` block and before `// ─── Recording infrastructure ───`:
- `// ─── UploadFileAsync — size routing ───`
- `// ─── UploadLargeFileAsync — chunk loop ───`
- `// ─── FindFolderAsync — pagination & matching ───`

Add the `ThrottledReadStream` helper class inside the existing `// ─── Recording infrastructure ───` region, next to `RecordingHandler`.

### Interfaces and Contracts

No new interfaces or contracts. Tests exercise only the existing public members of `ICatalogDocumentsStorage` (`UploadFileAsync`, `FindFolderAsync`) through the existing `GraphCatalogDocumentsStorage` concrete type (constructed via `CreateStorage`, same as today). Response bodies must be raw JSON string literals matching the internal Graph model shapes already used by the existing tests (`CatalogGraphDriveItem`, `CatalogGraphDriveItemCollection` with `"value"` / `"@odata.nextLink"`, `CatalogGraphUploadSession` with `"uploadUrl"`) — these types are `internal` to the Application assembly and are never referenced directly from the test project; tests only ever produce/consume their JSON wire shape, exactly as the current three tests do. Do not attempt to instantiate them directly.

Two contract details developers must get exactly right, since they're easy to get subtly wrong:
- **`Content-Range` assertions**: read `request.Content!.Headers.ContentRange` (an `System.Net.Http.Headers.ContentRangeHeaderValue`) and assert on its `.From`, `.To`, `.Length` properties — not by string-parsing the header. `From`/`To` are inclusive byte offsets; `Length` is the *total* `sizeBytes`, not the chunk length.
- **`FolderSearchResult` on `MultipleMatches`**: per current code, `FolderId`/`FolderName` are left at their default (`string.Empty`) on this branch — assert emptiness explicitly if the test wants to pin that, per FR-3's acceptance criteria.

### Data Flow

For the two multi-request scenarios, the `responder` lambda must branch on the *outgoing* request, not just return a fixed value:

- **FR-1/FR-2 (large upload):** request #1 is `POST .../createUploadSession` → respond with a JSON body containing an arbitrary `"uploadUrl"` (e.g. `"https://graph.microsoft.com/upload-session/abc"`). Every subsequent request is `PUT` to that exact `uploadUrl` string (the production code constructs `new HttpRequestMessage(HttpMethod.Put, session.UploadUrl)` directly, bypassing `GraphApiHelpers.CreateRequest`, so these chunk requests carry **no Authorization header** — do not assert one on them). Branch the responder by request count (a closure-local counter) or by checking `request.Method == HttpMethod.Post` vs `Put`.
- **FR-3 (pagination):** page 1 response body must include a `"@odata.nextLink"` whose value the test controls (e.g. `"https://graph.microsoft.com/v1.0/next-page-2"`); the responder must recognize a subsequent request whose `RequestUri.ToString()` equals that exact string and return the page-2 body with `"@odata.nextLink": null` (or the property omitted). Branch on `request.RequestUri` equality/`Contains`, not on call order, since this most directly mirrors what the production code actually does (`GET nextLink` verbatim).

Content streams for upload tests must have a `Length`/readable size matching the declared `sizeBytes` argument; for the "multi-read fill" scenario, back `ThrottledReadStream` with exactly `chunkSize` (10 MB) or `chunkSize + remainder` bytes of arbitrary content (e.g. all zeros) — the test only needs to assert on request shape (`Content-Range`, request count), never on byte content.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| 10–20 MB `byte[]`/`MemoryStream` allocations per chunk test slow the suite or bloat CI memory | Low | Acceptable one-off per test; avoid allocating in a loop across many `[Theory]` cases. Keep the "two full chunks" (20 MB) case to a single test, not parameterized. |
| Chunk-response `Content-Range` assertions are brittle if written as raw string comparisons | Medium | Assert via `ContentRangeHeaderValue.From/To/Length` (typed), not string matching, per guidance above. |
| Branching responder logic (counters/URL matching) silently matches the wrong request if closures share mutable state incorrectly across parallel test execution | Low | xUnit test classes are not shared instances across parallel test methods by default in this project (each `[Fact]` calls `CreateStorage` fresh); no shared mutable state risk as long as counters are declared inside each test method, not as class fields. |
| Asserting on chunk requests' Authorization header (there isn't one — see Data Flow) leads to a false-negative test if a developer assumes `GraphApiHelpers.CreateRequest` was used | Medium | Explicitly call out in test comments that `UploadLargeFileAsync`'s chunk `PUT`s bypass `CreateRequest` and carry no Bearer header — do not assert on it for those requests. |
| Pre-existing correctness gap noted in spec's Open Questions (outer loop exits early on partial final read without validating total bytes == `sizeBytes`) gets "fixed" accidentally by an over-eager implementer | Low | Explicitly out of scope (per spec); the "pin current behavior" test (no exception, early stop) should be added, but no production code changed. Flag as a candidate follow-up bug ticket, not addressed here. |

## Specification Amendments

None required to the functional requirements — the spec accurately reflects the source file's structure (verified line-by-line against `GraphCatalogDocumentsStorage.cs`), the existing test harness, and the model/contract shapes. Two clarifications for the implementer, not spec changes:

1. Chunk `PUT` requests in `UploadLargeFileAsync` are constructed via `new HttpRequestMessage(HttpMethod.Put, session.UploadUrl)` directly — they do **not** go through `GraphApiHelpers.CreateRequest` and therefore carry no `Authorization` header. Tests must not assert a Bearer token on these requests (only on the initial `createUploadSession` POST and the small-file PUT, both of which do use `CreateRequest`).
2. Resolve the spec's two "Open Questions" before writing tests, per its own recommendations: (a) add one test pinning the early-stream-exhaustion behavior as a documentation test, filed separately as a follow-up bug candidate rather than fixed here; (b) keep `RecordingHandler` unchanged, do all branching via per-test closures (Decision 1 above formalizes this).

## Prerequisites

None. No migrations, no config, no new infrastructure. The existing test project (`backend/test/Anela.Heblo.Tests`, already referencing xUnit/Moq/FluentAssertions) is sufficient. Implementation can start immediately.
