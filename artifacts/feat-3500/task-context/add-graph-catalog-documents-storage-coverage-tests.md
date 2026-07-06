### task: add-graph-catalog-documents-storage-coverage-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/GraphCatalogDocumentsStorageTests.cs`

**Reference — production code under test** (`backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs`), confirmed by direct read as of this plan:
- `private const long UploadSessionThresholdBytes = 4 * 1024 * 1024;` — `UploadFileAsync` routes via `if (sizeBytes <= UploadSessionThresholdBytes) return UploadSmallFileAsync(...); return UploadLargeFileAsync(...);` (line 135-138). **Note the boundary: `<=` routes to the small path, so `sizeBytes == threshold` is small-path, not large-path.**
- `UploadSmallFileAsync` (private static): single `PUT` to `.../items/{folderId}:/{encodedName}:/content?@microsoft.graph.conflictBehavior=rename`, returns `item.Name` from the JSON body.
- `UploadLargeFileAsync` (private static, reached only via `UploadFileAsync`): `POST .../items/{folderId}:/{encodedName}:/createUploadSession` → deserializes `CatalogGraphUploadSession.UploadUrl` → `const int chunkSize = 10 * 1024 * 1024;` → outer `while (offset < sizeBytes)`, inner `while (totalRead < chunkSize) { read = await content.ReadAsync(buffer.AsMemory(totalRead), ct); if (read == 0) break; totalRead += read; }`, then `if (totalRead == 0) break;` (outer loop exit for early EOF) → builds `ByteArrayContent(buffer, 0, totalRead)` with `Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + totalRead - 1, sizeBytes)` → sends `new HttpRequestMessage(HttpMethod.Put, session.UploadUrl)` **directly (bypasses `GraphApiHelpers.CreateRequest`, so no `Authorization` header on chunk PUTs)** → on `200`/`201` response, deserializes `CatalogGraphDriveItem` and sets `uploadedName = item.Name`; on any other status except `202`, calls `EnsureSuccessAsync` (which throws) → `offset += totalRead`. Returns `uploadedName` (initially `filename`, only updated by a successful chunk response).
- `FindFolderAsync`: `GET {GraphBaseUrl}/drives/{driveId}/root:/{encodedPath}:/children` using **app token** (`GetAccessTokenForAppAsync`). If first response is `404`, returns `FolderStatus.NotFound` immediately (no pagination attempted). Otherwise deserializes `CatalogGraphDriveItemCollection` (`Value: List<CatalogGraphDriveItem>`, `NextLink: string?` from `@odata.nextLink`), accumulates `Value` into `allFolderItems`, and while `nextLink != null`, issues `GET nextLink` (via `GraphApiHelpers.CreateRequest`, same app token) and appends that page's `Value`, updating `nextLink` each time. After accumulating all pages: `matches = allFolderItems.Where(i => i.Folder is not null && i.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();`. Then: `matches.Count == 0` → `NotFound`; `matches.Count > 1 && !allowMultiple` → `MultipleMatches` (with `FolderId`/`FolderName` left at their class defaults, i.e. `string.Empty`, since the object initializer only sets `Status`); otherwise `chosen = matches.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).First();` → returns `Found` with `chosen.Id`/`chosen.Name` (this "pick alphabetically first" path is taken both for the single-match case and the `allowMultiple: true` multi-match case).

**Reference — wire model shapes** (`backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/CatalogGraphModels.cs`, all `internal`, never referenced directly from tests — fabricate as raw JSON only):
- `CatalogGraphDriveItem`: `id`, `name`, `webUrl`, `size`, `lastModifiedDateTime`, `file` (nullable `{mimeType}`), `folder` (nullable `{childCount}`).
- `CatalogGraphDriveItemCollection`: `value: CatalogGraphDriveItem[]`, `@odata.nextLink: string?`.
- `CatalogGraphUploadSession`: `uploadUrl`.
- `FolderSearchResult` (`backend/.../FolderSearchResult.cs`): `Status`, `FolderId = string.Empty` (default), `FolderName = string.Empty` (default). `FolderStatus` enum (`backend/.../Contracts/FolderStatus.cs`): `Found`, `NotFound`, `MultipleMatches`.

**Reference — existing test harness** (already in the file, do not modify):
- `CreateStorage(Func<HttpRequestMessage, HttpResponseMessage> responder)` returns `(GraphCatalogDocumentsStorage Storage, Mock<ITokenAcquisition> TokenAcquisition, RecordingHandler Handler)`. Mocks `GetAccessTokenForAppAsync` → `"app-token"`, `GetAccessTokenForUserAsync` → `"delegated-token"`. Wires an `HttpClient(handler)` returned by `IHttpClientFactory.CreateClient("MicrosoftGraph")`.
- `RecordingHandler(responder)` : `HttpMessageHandler` — appends every request to `List<HttpRequestMessage> Requests`, returns `responder(request)`. One fresh instance per `CreateStorage` call; safe to close over local mutable state (counters) inside a test's `responder` lambda since each `[Fact]` builds its own handler.
- Namespace: `Anela.Heblo.Tests.Application.CatalogDocuments`. Class: `public sealed class GraphCatalogDocumentsStorageTests`. Framework: xUnit (`[Fact]`), FluentAssertions (`.Should()`), Moq. `NullLogger<GraphCatalogDocumentsStorage>.Instance` for the logger arg.

⚠️ Before transcribing any test below, re-open the production file and re-diff signatures/constants against what's written here — this plan was written from a point-in-time read and has not been compiled.

---

- [ ] **Step 1: Add region banners and the `ThrottledReadStream` helper.**

  In `GraphCatalogDocumentsStorageTests.cs`, after the existing `// ─── UploadFileAsync — delegated token ───` block (i.e. after the three existing `[Fact]` methods, before `// ─── Recording infrastructure ───`), add three new banners (empty for now, tests fill them in in later steps):
  ```csharp
  // ─── UploadFileAsync — size routing ──────────────────────────────────────

  // ─── UploadLargeFileAsync — chunk loop ───────────────────────────────────

  // ─── FindFolderAsync — pagination & matching ─────────────────────────────
  ```

  Inside the existing `// ─── Recording infrastructure ───` region, alongside `RecordingHandler`, add a private nested `Stream` that caps bytes returned per `ReadAsync` call, to force the production inner fill loop (`while (totalRead < chunkSize)`) through multiple reads:

  ```csharp
  /// Wraps an in-memory byte source but returns at most <see cref="_maxBytesPerRead"/>
  /// bytes per ReadAsync call, forcing callers with a larger buffer to loop.
  private sealed class ThrottledReadStream : Stream
  {
      private readonly byte[] _data;
      private readonly int _maxBytesPerRead;
      private int _position;

      public ThrottledReadStream(long totalBytes, int maxBytesPerRead)
      {
          _data = new byte[totalBytes]; // zero-filled content; tests assert on request shape, not bytes
          _maxBytesPerRead = maxBytesPerRead;
      }

      public override int Read(byte[] buffer, int offset, int count)
          => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

      public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
      {
          var remaining = _data.Length - _position;
          var toCopy = Math.Min(Math.Min(count, _maxBytesPerRead), remaining);
          if (toCopy > 0)
              Array.Copy(_data, _position, buffer, offset, toCopy);
          _position += toCopy;
          return Task.FromResult(toCopy);
      }

      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => _data.Length;
      public override long Position
      {
          get => _position;
          set => throw new NotSupportedException();
      }
      public override void Flush() { }
      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }
  ```

  Verify against the real `Stream.ReadAsync` overload actually invoked by production code: production calls `content.ReadAsync(buffer.AsMemory(totalRead), ct)` — the `Memory<byte>`-based overload, not the `byte[]` overload. `Stream` provides a default `ReadAsync(Memory<byte>, ...)` implementation that delegates to `ReadAsync(byte[], int, int, ...)` via `ArrayPool` when not overridden directly — overriding the `byte[]`-based `ReadAsync` above is sufficient and is the simpler override surface; confirm this still triggers correctly (i.e. that overriding only the array-based overload actually gets hit) by running Step 8's tests — if the `Memory<byte>` entry point does not route through the overridden method in practice, override `ReadAsync(Memory<byte> buffer, CancellationToken)` directly instead, matching the exact signature used in production.

- [ ] **Step 2 (FR-1): `UploadFileAsync` size-threshold routing — small path at/below threshold.**

  Add two `[Fact]`s under `// ─── UploadFileAsync — size routing ───`:

  ```csharp
  [Fact]
  public async Task UploadFileAsync_SizeEqualsThreshold_UsesSmallFilePath()
  {
      // Arrange — 4 MB exactly; UploadFileAsync uses `<=` so this must take the small-file path
      const long threshold = 4 * 1024 * 1024;
      var (storage, _, handler) = CreateStorage(_ =>
          new HttpResponseMessage(HttpStatusCode.OK)
          {
              Content = new StringContent(
                  """{"id":"item-1","name":"test.pdf"}""",
                  Encoding.UTF8, "application/json")
          });

      using var stream = new MemoryStream(new byte[threshold]);

      // Act
      await storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", threshold);

      // Assert — exactly one PUT to the .../content endpoint, no createUploadSession call
      handler.Requests.Should().HaveCount(1);
      handler.Requests[0].Method.Should().Be(HttpMethod.Put);
      handler.Requests[0].RequestUri!.ToString().Should().Contain("/content?@microsoft.graph.conflictBehavior=rename");
  }

  [Fact]
  public async Task UploadFileAsync_SizeOneBelowThreshold_UsesSmallFilePath()
  {
      // Arrange
      const long threshold = 4 * 1024 * 1024;
      var (storage, _, handler) = CreateStorage(_ =>
          new HttpResponseMessage(HttpStatusCode.OK)
          {
              Content = new StringContent(
                  """{"id":"item-1","name":"test.pdf"}""",
                  Encoding.UTF8, "application/json")
          });

      using var stream = new MemoryStream(new byte[threshold - 1]);

      // Act
      await storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", threshold - 1);

      // Assert
      handler.Requests.Should().HaveCount(1);
      handler.Requests[0].Method.Should().Be(HttpMethod.Put);
      handler.Requests[0].RequestUri!.ToString().Should().Contain("/content?@microsoft.graph.conflictBehavior=rename");
  }
  ```

  Do not allocate the full 4 MB `byte[]` unnecessarily large in a loop across many cases — these are two one-off `[Fact]`s, acceptable per the arch review's risk table.

- [ ] **Step 3 (FR-1): `UploadFileAsync` size-threshold routing — large path one above threshold.**

  Add under the same banner:

  ```csharp
  [Fact]
  public async Task UploadFileAsync_SizeOneAboveThreshold_UsesLargeFileSessionPath()
  {
      // Arrange
      const long threshold = 4 * 1024 * 1024;
      const long size = threshold + 1;
      var (storage, _, handler) = CreateStorage(request =>
      {
          if (request.Method == HttpMethod.Post)
          {
              return new HttpResponseMessage(HttpStatusCode.OK)
              {
                  Content = new StringContent(
                      """{"uploadUrl":"https://graph.microsoft.com/upload-session/abc"}""",
                      Encoding.UTF8, "application/json")
              };
          }
          return new HttpResponseMessage(HttpStatusCode.OK)
          {
              Content = new StringContent(
                  """{"id":"item-1","name":"final.pdf"}""",
                  Encoding.UTF8, "application/json")
          };
      });

      using var stream = new MemoryStream(new byte[size]);

      // Act
      await storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", size);

      // Assert — createUploadSession POST, then chunk PUT(s) to the session's uploadUrl
      handler.Requests.Should().HaveCountGreaterOrEqualTo(2);
      handler.Requests[0].Method.Should().Be(HttpMethod.Post);
      handler.Requests[0].RequestUri!.ToString().Should().Contain("/createUploadSession");
      handler.Requests.Skip(1).Should().OnlyContain(r =>
          r.Method == HttpMethod.Put &&
          r.RequestUri!.ToString() == "https://graph.microsoft.com/upload-session/abc");
  }
  ```

  Note: with `size = threshold + 1` (one byte over 4 MB, well under the 10 MB chunk size), the outer chunk loop runs exactly once, so this also incidentally exercises "one small partial chunk" — that's fine, FR-2 below adds the dedicated multi-chunk/boundary cases.

- [ ] **Step 4 (FR-2): Final partial chunk (12 MB: one full 10 MB chunk + 2 MB remainder).**

  Add under `// ─── UploadLargeFileAsync — chunk loop ───`:

  ```csharp
  [Fact]
  public async Task UploadFileAsync_LargeFileWithPartialFinalChunk_SendsTwoChunksWithCorrectContentRange()
  {
      // Arrange — 12 MB: chunk 1 = full 10 MB, chunk 2 = remaining 2 MB
      const long chunkSize = 10 * 1024 * 1024;
      const long size = chunkSize + 2 * 1024 * 1024;

      var (storage, _, handler) = CreateStorage(request =>
      {
          if (request.Method == HttpMethod.Post)
          {
              return new HttpResponseMessage(HttpStatusCode.OK)
              {
                  Content = new StringContent(
                      """{"uploadUrl":"https://graph.microsoft.com/upload-session/abc"}""",
                      Encoding.UTF8, "application/json")
              };
          }
          return new HttpResponseMessage(HttpStatusCode.OK)
          {
              Content = new StringContent(
                  """{"id":"item-1","name":"final.pdf"}""",
                  Encoding.UTF8, "application/json")
          };
      });

      using var stream = new MemoryStream(new byte[size]);

      // Act
      await storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", size);

      // Assert
      var chunkRequests = handler.Requests.Where(r => r.Method == HttpMethod.Put).ToList();
      chunkRequests.Should().HaveCount(2);

      var range1 = chunkRequests[0].Content!.Headers.ContentRange!;
      range1.From.Should().Be(0);
      range1.To.Should().Be(chunkSize - 1);
      range1.Length.Should().Be(size);

      var range2 = chunkRequests[1].Content!.Headers.ContentRange!;
      range2.From.Should().Be(chunkSize);
      range2.To.Should().Be(size - 1);
      range2.Length.Should().Be(size);
  }
  ```

- [ ] **Step 5 (FR-2): Multi-read fill of a single chunk, using `ThrottledReadStream`.**

  ```csharp
  [Fact]
  public async Task UploadFileAsync_LargeFileWithThrottledStream_AssemblesFullChunkFromShortReads()
  {
      // Arrange — exactly one chunk (10 MB), but the stream only yields 64 KB per ReadAsync call,
      // forcing UploadLargeFileAsync's inner fill loop to run many iterations before sending the PUT.
      const long chunkSize = 10 * 1024 * 1024;
      const int maxBytesPerRead = 64 * 1024;

      var (storage, _, handler) = CreateStorage(request =>
      {
          if (request.Method == HttpMethod.Post)
          {
              return new HttpResponseMessage(HttpStatusCode.OK)
              {
                  Content = new StringContent(
                      """{"uploadUrl":"https://graph.microsoft.com/upload-session/abc"}""",
                      Encoding.UTF8, "application/json")
              };
          }
          return new HttpResponseMessage(HttpStatusCode.OK)
          {
              Content = new StringContent(
                  """{"id":"item-1","name":"final.pdf"}""",
                  Encoding.UTF8, "application/json")
          };
      });

      using var stream = new ThrottledReadStream(chunkSize, maxBytesPerRead);

      // Act
      await storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", chunkSize);

      // Assert — exactly one chunk PUT, whose Content-Range reflects the FULL chunk length,
      // not the length of a single short read
      var chunkRequests = handler.Requests.Where(r => r.Method == HttpMethod.Put).ToList();
      chunkRequests.Should().HaveCount(1);

      var range = chunkRequests[0].Content!.Headers.ContentRange!;
      range.From.Should().Be(0);
      range.To.Should().Be(chunkSize - 1);
      range.Length.Should().Be(chunkSize);
  }
  ```

- [ ] **Step 6 (FR-2): Two full chunks, exact multiple boundary (20 MB) — verifies outer loop termination and last-chunk-wins naming.**

  ```csharp
  [Fact]
  public async Task UploadFileAsync_LargeFileExactMultipleOfChunkSize_SendsExactlyTwoChunksNoTrailingRequest()
  {
      // Arrange — 20 MB = exactly two 10 MB chunks; outer `while (offset < sizeBytes)` must
      // stop after the second chunk with no trailing empty/zero-length request.
      const long chunkSize = 10 * 1024 * 1024;
      const long size = 2 * chunkSize;

      var responses = new Queue<string>(new[]
      {
          "chunk-1-name",
          "final-name.pdf"
      });

      var (storage, _, handler) = CreateStorage(request =>
      {
          if (request.Method == HttpMethod.Post)
          {
              return new HttpResponseMessage(HttpStatusCode.OK)
              {
                  Content = new StringContent(
                      """{"uploadUrl":"https://graph.microsoft.com/upload-session/abc"}""",
                      Encoding.UTF8, "application/json")
              };
          }
          var name = responses.Dequeue();
          return new HttpResponseMessage(HttpStatusCode.OK)
          {
              Content = new StringContent(
                  $$"""{"id":"item-1","name":"{{name}}"}""",
                  Encoding.UTF8, "application/json")
          };
      });

      using var stream = new MemoryStream(new byte[size]);

      // Act
      var result = await storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", size);

      // Assert — exactly two chunk PUTs, no trailing third request
      var chunkRequests = handler.Requests.Where(r => r.Method == HttpMethod.Put).ToList();
      chunkRequests.Should().HaveCount(2);

      // Returned filename comes from the LAST chunk response, not the first
      result.Should().Be("final-name.pdf");

      // No Authorization header on chunk PUTs (bypass GraphApiHelpers.CreateRequest)
      chunkRequests.Should().OnlyContain(r => r.Headers.Authorization == null);
  }
  ```

  Verify the `$$"""..."""` raw-string-with-interpolation syntax compiles against this project's configured C# language version (check `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` / `Directory.Build.props` for `<LangVersion>`); if not supported, replace with plain string concatenation/`string.Format`.

- [ ] **Step 7 (FR-2, Open Question): Pin early-stream-exhaustion behavior (documentation test, not a bug fix).**

  Per the spec's Open Questions and the arch review's recommendation, add one test pinning current (arguably questionable) behavior: if the content stream returns EOF before `sizeBytes` is reached, `UploadLargeFileAsync`'s outer loop exits early without error.

  ```csharp
  [Fact]
  public async Task UploadFileAsync_StreamExhaustedBeforeDeclaredSize_StopsEarlyWithoutThrowing()
  {
      // Documents existing (pre-existing, out-of-scope-to-fix) behavior: UploadLargeFileAsync's
      // outer loop exits as soon as the stream returns 0 bytes, even if offset < sizeBytes.
      // See spec Open Questions — flagged as a candidate follow-up bug ticket, not fixed here.
      const long chunkSize = 10 * 1024 * 1024;
      const long declaredSize = 3 * chunkSize; // 30 MB declared
      const long actualStreamBytes = chunkSize; // but stream only has 10 MB

      var (storage, _, handler) = CreateStorage(request =>
      {
          if (request.Method == HttpMethod.Post)
          {
              return new HttpResponseMessage(HttpStatusCode.OK)
              {
                  Content = new StringContent(
                      """{"uploadUrl":"https://graph.microsoft.com/upload-session/abc"}""",
                      Encoding.UTF8, "application/json")
              };
          }
          return new HttpResponseMessage(HttpStatusCode.OK)
          {
              Content = new StringContent(
                  """{"id":"item-1","name":"final.pdf"}""",
                  Encoding.UTF8, "application/json")
          };
      });

      using var stream = new MemoryStream(new byte[actualStreamBytes]);

      // Act
      var act = () => storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", declaredSize);

      // Assert — no exception; loop stops after the one chunk the stream actually had
      await act.Should().NotThrowAsync();
      handler.Requests.Count(r => r.Method == HttpMethod.Put).Should().Be(1);
  }
  ```

- [ ] **Step 8: Run and verify FR-1/FR-2 tests before moving on.**

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GraphCatalogDocumentsStorageTests" \
    -v normal
  ```

  Expected: all tests in the file pass, including the 3 pre-existing tests plus the 6 added so far in this step range (Steps 2-7) — 9 total. If `ThrottledReadStream`'s overridden `ReadAsync(byte[], ...)` is not actually invoked (i.e. the `Memory<byte>`-based call in production routes elsewhere), Step 5's test will show a Content-Range shorter than expected or a hang — fix by overriding `ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)` directly with the equivalent logic, matching the exact overload used at the call site (`content.ReadAsync(buffer.AsMemory(totalRead), ct)`).

- [ ] **Step 9 (FR-3): `FindFolderAsync` — no matches, single match, non-folder exclusion.**

  Add under `// ─── FindFolderAsync — pagination & matching ───`:

  ```csharp
  [Fact]
  public async Task FindFolderAsync_NoMatchingItems_ReturnsNotFound()
  {
      var (storage, _, _) = CreateStorage(_ =>
          new HttpResponseMessage(HttpStatusCode.OK)
          {
              Content = new StringContent(
                  """{"value":[]}""",
                  Encoding.UTF8, "application/json")
          });

      var result = await storage.FindFolderAsync("drive-1", "/Materials", "MAT001__", false);

      result.Status.Should().Be(FolderStatus.NotFound);
  }

  [Fact]
  public async Task FindFolderAsync_ExactlyOneMatch_ReturnsFoundWithMatchedFolder()
  {
      var (storage, _, _) = CreateStorage(_ =>
          new HttpResponseMessage(HttpStatusCode.OK)
          {
              Content = new StringContent(
                  """{"value":[{"id":"folder-id-1","name":"PIF-2024","folder":{"childCount":0}}]}""",
                  Encoding.UTF8, "application/json")
          });

      var result = await storage.FindFolderAsync("drive-1", "/Materials", "PIF-2024", false);

      result.Status.Should().Be(FolderStatus.Found);
      result.FolderId.Should().Be("folder-id-1");
      result.FolderName.Should().Be("PIF-2024");
  }

  [Fact]
  public async Task FindFolderAsync_ExcludesNonFolderItemsMatchingPrefix()
  {
      var (storage, _, _) = CreateStorage(_ =>
          new HttpResponseMessage(HttpStatusCode.OK)
          {
              Content = new StringContent(
                  """
                  {
                    "value": [
                      { "id": "file-id-1", "name": "PIF-2024-notes.txt", "folder": null, "file": { "mimeType": "text/plain" } }
                    ]
                  }
                  """,
                  Encoding.UTF8, "application/json")
          });

      var result = await storage.FindFolderAsync("drive-1", "/Materials", "PIF-2024", false);

      result.Status.Should().Be(FolderStatus.NotFound);
  }
  ```

- [ ] **Step 10 (FR-3): Multiple matches — `allowMultiple: false` and `allowMultiple: true` (alphabetical pick).**

  ```csharp
  [Fact]
  public async Task FindFolderAsync_MultipleMatches_AllowMultipleFalse_ReturnsMultipleMatchesWithEmptyFolder()
  {
      var (storage, _, _) = CreateStorage(_ =>
          new HttpResponseMessage(HttpStatusCode.OK)
          {
              Content = new StringContent(
                  """
                  {
                    "value": [
                      { "id": "folder-id-2", "name": "PIF-2024-B", "folder": {"childCount":0} },
                      { "id": "folder-id-1", "name": "PIF-2024-A", "folder": {"childCount":0} }
                    ]
                  }
                  """,
                  Encoding.UTF8, "application/json")
          });

      var result = await storage.FindFolderAsync("drive-1", "/Materials", "PIF-2024", false);

      result.Status.Should().Be(FolderStatus.MultipleMatches);
      result.FolderId.Should().BeEmpty();
      result.FolderName.Should().BeEmpty();
  }

  [Fact]
  public async Task FindFolderAsync_MultipleMatches_AllowMultipleTrue_ReturnsAlphabeticallyFirstMatch()
  {
      // Items deliberately in non-alphabetical order in the response
      var (storage, _, _) = CreateStorage(_ =>
          new HttpResponseMessage(HttpStatusCode.OK)
          {
              Content = new StringContent(
                  """
                  {
                    "value": [
                      { "id": "folder-id-2", "name": "PIF-2024-B", "folder": {"childCount":0} },
                      { "id": "folder-id-1", "name": "PIF-2024-A", "folder": {"childCount":0} }
                    ]
                  }
                  """,
                  Encoding.UTF8, "application/json")
          });

      var result = await storage.FindFolderAsync("drive-1", "/Materials", "PIF-2024", true);

      result.Status.Should().Be(FolderStatus.Found);
      result.FolderId.Should().Be("folder-id-1");
      result.FolderName.Should().Be("PIF-2024-A");
  }
  ```

- [ ] **Step 11 (FR-3): Multi-page pagination — a match only on page two, and a 404 short-circuit.**

  ```csharp
  [Fact]
  public async Task FindFolderAsync_MultiPagePagination_ConsidersMatchesFromBothPages()
  {
      const string nextLinkUrl = "https://graph.microsoft.com/v1.0/next-page-2";

      var (storage, _, handler) = CreateStorage(request =>
      {
          if (request.RequestUri!.ToString() == nextLinkUrl)
          {
              return new HttpResponseMessage(HttpStatusCode.OK)
              {
                  Content = new StringContent(
                      """{"value":[{"id":"folder-id-page2","name":"PIF-2024-Page2","folder":{"childCount":0}}]}""",
                      Encoding.UTF8, "application/json")
              };
          }

          return new HttpResponseMessage(HttpStatusCode.OK)
          {
              Content = new StringContent(
                  $$"""
                  {
                    "value": [
                      { "id": "file-id-1", "name": "PIF-2024-notes.txt", "folder": null, "file": {"mimeType":"text/plain"} }
                    ],
                    "@odata.nextLink": "{{nextLinkUrl}}"
                  }
                  """,
                  Encoding.UTF8, "application/json")
          };
      });

      var result = await storage.FindFolderAsync("drive-1", "/Materials", "PIF-2024", false);

      // Only page 2 has a matching folder item; page 1's item is a file (excluded), not a folder.
      result.Status.Should().Be(FolderStatus.Found);
      result.FolderId.Should().Be("folder-id-page2");
      handler.Requests.Should().Contain(r => r.RequestUri!.ToString() == nextLinkUrl);
  }

  [Fact]
  public async Task FindFolderAsync_FirstPage404_ReturnsNotFoundWithoutPagination()
  {
      var (storage, _, handler) = CreateStorage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

      var result = await storage.FindFolderAsync("drive-1", "/Materials", "PIF-2024", false);

      result.Status.Should().Be(FolderStatus.NotFound);
      handler.Requests.Should().HaveCount(1);
  }
  ```

  Verify the exact `basePath`/`encodedPath` URL construction is irrelevant to these assertions (they only check `nextLink`/count), so no need to match the first-page URL precisely — the responder branches on the `nextLinkUrl` value only, falling back to the default response for every other request (including the very first request), which is correct here since there is exactly one "other" request per test.

- [ ] **Step 12: Run the full new test file and verify final counts.**

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GraphCatalogDocumentsStorageTests" \
    -v normal
  ```

  Expected: 3 (pre-existing) + 6 (Steps 2-7) + 3 (Step 9) + 2 (Step 10) + 2 (Step 11) = 16 tests, all passing. If any count mismatches (developer added/omitted a `[Fact]` during implementation), reconcile the count rather than treating it as acceptable drift — but do not chase an exact number if a genuinely better test shape (e.g. consolidating two `[Fact]`s into a `[Theory]`) was used; confirm intent against the FRs in `artifacts/feat-3500/spec.r1.md` instead.

  Also run the full test project to confirm no regressions elsewhere:
  ```bash
  cd backend
  dotnet build && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
  ```

- [ ] **Step 13: Commit.**

  ```bash
  git add backend/test/Anela.Heblo.Tests/Application/CatalogDocuments/GraphCatalogDocumentsStorageTests.cs
  git commit -m "$(cat <<'EOF'
  Add coverage for GraphCatalogDocumentsStorage upload routing, chunk loop, and folder pagination

  Closes the coverage gap on GraphCatalogDocumentsStorage's three untested
  paths: UploadFileAsync size-threshold routing, UploadLargeFileAsync's
  chunked offset/read loop, and FindFolderAsync's pagination and
  multi-match logic. Test-only change, no production code touched.
  EOF
  )"
  ```
