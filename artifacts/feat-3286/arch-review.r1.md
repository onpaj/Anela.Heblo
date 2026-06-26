# Architecture Review: Close Test Coverage Gap in AzureBlobStorageService

## Skip Design: true

This is a test-only change — no UI components, screens, layouts, or visual design decisions. FR-7 explicitly forbids any production code change, and the deliverable is unit tests in `AzureBlobStorageServiceTests.cs`. No design work is required.

## Architectural Fit Assessment

The feature aligns cleanly with existing conventions. I verified the following against the codebase:

- **Unit under test:** `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs` — a `sealed` class implementing `IBlobStorageService` (`backend/src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs`). All four target paths (URL-download blobName fallback at lines 45–56, the two private switch methods at lines 282–318, the container cache `TryAdd` branch at lines 271–280, and `ListVirtualDirectoriesAsync` trailing-slash trim at lines 200–236) exist exactly as the spec describes.
- **Existing test file:** `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs` already establishes every convention the spec wants to reuse: `Mock<BlobServiceClient>`/`Mock<BlobContainerClient>`/`Mock<BlobClient>`, the nested `StubHttpMessageHandler` (supports `overrideContent` for custom `Content-Type`) and `ThrowingHttpMessageHandler`, and a `Mock<IHttpClientFactory>` wired to `FileStorageModule.FileDownloadClientName`. The placeholder test at lines 117–133 is exactly the dead test the spec calls out.
- **Named-client constant:** confirmed `FileStorageModule.FileDownloadClientName == "FileDownload"` at `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs:16`.
- **Azure SDK availability:** `Azure.Storage.Blobs` is version **12.25.0** (referenced by `Anela.Heblo.Application` and `Anela.Heblo.Adapters.Azure`). The test project `Anela.Heblo.Tests.csproj` does **not** reference `Azure.Storage.Blobs` directly but obtains it **transitively** via its `ProjectReference` to both `Anela.Heblo.Application` and `Anela.Heblo.Adapters.Azure` (csproj lines 48 and 50). The existing tests already consume `BlobsModelFactory`'s neighbours (`BlobUploadOptions`, `BlobContainerEncryptionScopeOptions`, `PublicAccessType`) without a direct package reference, proving the transitive path works. No new package reference is needed.
- **Testing strategy doc** (`docs/architecture/testing-strategy.md`) endorses xUnit + Moq + AAA and explicitly lists "Extensions/Utilities: Helper methods and transformations" as required unit-test targets — this work fits squarely in that mandate.

One notable convention deviation to flag: the testing-strategy doc prefers FluentAssertions, but the existing `AzureBlobStorageServiceTests.cs` uses raw xUnit `Assert.*`. **Match the existing file (raw `Assert`)** per NFR-3 and the project's "match existing style" rule. Do not introduce FluentAssertions into this file.

This is a low-risk, well-bounded change. The only genuine engineering difficulty is mocking `GetBlobsByHierarchyAsync` to return an `AsyncPageable<BlobHierarchyItem>` (FR-6).

## Proposed Architecture

### Component Overview

```
AzureBlobStorageServiceTests (extended, single file)
│
├── Test fixtures (existing, reuse as-is)
│     ├── Mock<BlobServiceClient>      _mockBlobServiceClient
│     ├── Mock<ILogger<...>>           _mockLogger
│     ├── Mock<IHttpClientFactory>     _mockHttpClientFactory  → "FileDownload"
│     ├── StubHttpMessageHandler(statusCode, content, overrideContent?)
│     └── ThrowingHttpMessageHandler(exception)
│
├── NEW shared private helper(s)  (NFR-3 "encouraged")
│     ├── SetupContainerAndCaptureBlobName(out captured, container)
│     │     → wires Mock<BlobContainerClient> + Mock<BlobClient>,
│     │       GetBlobClient(It.IsAny<string>()) Callback records the name,
│     │       returns (mockContainer, mockBlob, captured-name-holder)
│     ├── BuildHttpClientWithContentType(mediaType?)        // for FR-2/FR-3
│     ├── BuildHttpClientWithoutContentTypeHeader()          // for FR-4
│     └── CaptureUploadContentType(mockBlob, out captured)   // for FR-4
│
└── NEW tests
      ├── FR-1  filename-from-path           [Fact]
      ├── FR-2  GUID + ext / .bin fallback   [Fact] x2
      ├── FR-3  GetExtensionFromContentType  [Theory] (9 cases)
      ├── FR-4  GetContentTypeFromExtension  [Theory] (15+ cases, replaces placeholder)
      ├── FR-5  download-path cache once     [Fact]
      └── FR-6  ListVirtualDirectoriesAsync  [Fact] (mix prefix/blob + empty)
                  └── needs AsyncPageable<BlobHierarchyItem> test double
```

All tests drive private methods **indirectly** through `DownloadFromUrlAsync`, `UploadAsync`, and `ListVirtualDirectoriesAsync` (FR-7). The `Application`-level service is the SUT directly (not through the API layer), matching the existing test class.

### Key Design Decisions

#### Decision 1: Capture the blob name via `GetBlobClient` Callback, not URL-string assertions

**Options considered:**
(a) Assert on the returned blob URL string (what the existing placeholder-adjacent test at lines 165–212 does — it asserts only `Contains(containerName)`, which is why it proves nothing about the generated name).
(b) Set up `GetBlobClient(It.IsAny<string>())` with `.Callback<string>(name => captured = name)` and assert on the captured argument.
(c) Use `Verify(x => x.GetBlobClient(It.Is<string>(...)))`.

**Chosen approach:** (b) Callback-capture into a local holder, with (c) acceptable for the simple FR-1 exact-match case.

**Rationale:** The generated name contains a non-deterministic GUID (`downloaded-file-{Guid}{ext}`), so per NFR-4 we must assert `StartsWith("downloaded-file-")` + `EndsWith(ext)`, which `It.Is<>`/`Verify` express awkwardly across a `[Theory]`. Capturing the exact string passed to `GetBlobClient` and running `StartsWith`/`EndsWith` (or a regex) on it is the cleanest, most readable form and decouples the assertion from the mocked return URL. This also matches the spec's API-design note recommending the `Callback<string>` approach.

#### Decision 2: Drive `GetContentTypeFromExtension` (FR-4) by suppressing the response `Content-Type` header and asserting `BlobUploadOptions.HttpHeaders.ContentType`

**Options considered:**
(a) Reflection / `[InternalsVisibleTo]` to call the private method directly.
(b) Provide an explicit `blobName` + a response with **no** `Content-Type` header, forcing line 59's `?? GetContentTypeFromExtension(blobName)` branch, then assert the `ContentType` captured from the `BlobUploadOptions` passed to `BlobClient.UploadAsync`.

**Chosen approach:** (b).

**Rationale:** FR-7 forbids widening access modifiers or adding `[InternalsVisibleTo]` "if it can be avoided by testing through the public surface" — and here it can. The existing `UploadAsync` tests already verify `It.Is<BlobUploadOptions>(opts => opts.HttpHeaders.ContentType == ...)` (lines 254–259), so this pattern is proven in-file. The only new wrinkle: `StubHttpMessageHandler` currently always sets `Content = new StringContent(content)`, and **`StringContent` defaults `Content-Type` to `text/plain; charset=utf-8`**. That default would mask the fallback. See Specification Amendment SA-1.

#### Decision 3: Build the `AsyncPageable<BlobHierarchyItem>` test double with a minimal inline async-enumerable helper

**Options considered:**
(a) `AsyncPageable<T>.FromPages(new[] { Page<T>.FromValues(items, null, response) })`.
(b) A tiny private `TestAsyncPageable<T>` (or a local async-iterator method) that yields the items.

**Chosen approach:** Prefer (a) `AsyncPageable.FromPages` + `Page.FromValues` (both public static factories in Azure.Core 12.25.0); fall back to (b) a small inline helper only if a `Page<T>` cannot be constructed cleanly. Either lives **inside the test project** (per Dependencies: "implement it inline within the test project rather than adding a dependency").

**Rationale:** `FromPages`/`FromValues` are the idiomatic, dependency-free way to fabricate `AsyncPageable` and avoid hand-rolling `IAsyncEnumerator`. The items themselves come from `BlobsModelFactory.BlobHierarchyItem(prefix, blobItem)` — a prefix item has a non-null `Prefix` and the loop checks `item.IsPrefix`. **Confirm the exact `BlobHierarchyItem` factory signature against 12.25.0 during implementation** (the spec flags this; in this SDK line `BlobsModelFactory.BlobHierarchyItem(string blobPrefix, BlobItem blobItem)` returns a prefix item when `blobPrefix` is set). Build at least: two prefix items (`2024/`, `archive/`), one non-prefix blob item, and a separate empty-set case.

#### Decision 4: Fresh `AzureBlobStorageService` instance for the cache test (FR-5)

**Chosen approach:** Construct a new service instance inside the FR-5 test (as `UploadAsync_CalledMultipleTimes_*` already do at lines 447–453), not the shared `_service`.

**Rationale:** `_containerExists` is instance state. Reusing the shared `_service` would make the `CreateIfNotExistsAsync` `Times.Once` assertion order-dependent on other tests that touch the same container — violating NFR-4 determinism.

## Implementation Guidance

### Directory / Module Structure

No new files. Extend the single existing file:

```
backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs
```

- **Replace** the placeholder `GetContentTypeFromExtension_DifferentExtensions_ShouldReturnCorrectContentType` (lines 117–133) with the real FR-4 `[Theory]`.
- **Add** new `[Fact]`/`[Theory]` methods for FR-1, FR-2, FR-3, FR-5, FR-6 grouped under existing-style banner comments.
- **Add** private helpers and any `TestAsyncPageable<T>` test double as nested members alongside `StubHttpMessageHandler` (lines 565–591).
- Keep all existing tests untouched and green (FR-5 note; UploadAsync cache tests).

### Interfaces and Contracts

Tests bind to the public `IBlobStorageService` surface only (no contract changes):

```csharp
Task<string> DownloadFromUrlAsync(string fileUrl, string containerName, string? blobName = null, CancellationToken ct = default);
Task<string> UploadAsync(Stream stream, string containerName, string blobName, string contentType, CancellationToken ct = default);
Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken ct = default);
```

Azure SDK seams the mocks must satisfy (all are `virtual` on the mocked classes — already proven mockable in-file):

- `BlobServiceClient.GetBlobContainerClient(string) → BlobContainerClient`
- `BlobContainerClient.GetBlobClient(string) → BlobClient` (capture arg via `Callback<string>`)
- `BlobContainerClient.CreateIfNotExistsAsync(PublicAccessType, IDictionary<string,string>, BlobContainerEncryptionScopeOptions, CancellationToken)` — **note the 4-arg overload**: the cache tests at lines 463/474 set up and verify the overload **with** `BlobContainerEncryptionScopeOptions`, while the download-path tests at lines 61/199 use the 3-arg overload (no encryption-scope param). Both resolve to the same call (`CreateIfNotExistsAsync(PublicAccessType.None, ct: ...)` at production line 276). For FR-5, set up and `Verify(..., Times.Once)` on the **same** overload signature you set up, to avoid a mismatch that silently records zero invocations.
- `BlobClient.UploadAsync(Stream, BlobUploadOptions, CancellationToken)` — assert `opts.HttpHeaders.ContentType` for FR-4.
- `BlobClient.Uri` — return a stub `Uri` so `UploadAsync` can build the return string.
- `BlobContainerClient.GetBlobsByHierarchyAsync(...)` → `AsyncPageable<BlobHierarchyItem>` — set up with named args matching production (`prefix: null, delimiter: "/"`), but a permissive `It.IsAny<...>` setup is acceptable and more robust.

### Data Flow

**FR-1 / FR-2 / FR-3 (DownloadFromUrlAsync, blobName + extension inference):**
```
Test → DownloadFromUrlAsync(url, container, blobName: null)
  → factory.CreateClient("FileDownload") → stub handler returns 200 + chosen Content-Type
  → blobName empty ⇒ Path.GetFileName(uri.LocalPath)
        FR-1: url has filename  ⇒ captured == "product.jpg"
        FR-2/3: url has none    ⇒ "downloaded-file-{guid}" + GetExtensionFromContentType(mediaType)
  → UploadAsync → GetBlobClient(captured)  [Callback records `captured`]
Assert: captured StartsWith/EndsWith per case
```

**FR-4 (GetContentTypeFromExtension):**
```
Test → DownloadFromUrlAsync(url, container, blobName: "file.<ext>")
  → response has NO Content-Type header (see SA-1)
  → contentType = null ?? GetContentTypeFromExtension("file.<ext>")
  → UploadAsync(stream, container, blobName, contentType)
        → BlobClient.UploadAsync(stream, BlobUploadOptions{HttpHeaders.ContentType=contentType})
Assert: captured BlobUploadOptions.HttpHeaders.ContentType == expected MIME
        (+ uppercase "FILE.PDF" case exercises ToLowerInvariant at line 300)
```

**FR-5 (container cache):**
```
fresh service → DownloadFromUrlAsync(container) ×2 (same container)
  → GetOrCreateContainerAsync: TryAdd true (1st) → CreateIfNotExistsAsync;  TryAdd false (2nd) → skip
Assert: CreateIfNotExistsAsync Times.Once
```

**FR-6 (ListVirtualDirectoriesAsync):**
```
Test → ListVirtualDirectoriesAsync(container)
  → GetBlobsByHierarchyAsync ⇒ AsyncPageable[ prefix "2024/", prefix "archive/", blob "x.txt" ]
  → keep IsPrefix, trim one trailing '/'
Assert: ["2024","archive"]; blob excluded; empty-set ⇒ empty list
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `StringContent` default `Content-Type: text/plain` masks the FR-4 fallback branch (line 59) | High | SA-1: build the no-header response by clearing `content.Headers.ContentType = null` (or using a `ByteArrayContent`/`StreamContent` and removing the header) and pass via `StubHttpMessageHandler.overrideContent`. Add an assertion in one FR-4 case that proves the header is genuinely absent before relying on the fallback. |
| `BlobsModelFactory.BlobHierarchyItem` / `AsyncPageable.FromPages` / `Page.FromValues` signatures differ in 12.25.0 from the spec's assumptions | Medium | Verify against the installed 12.25.0 assembly during implementation (spec explicitly defers this). If `FromPages` is awkward, fall back to a nested `TestAsyncPageable<T>` async-iterator helper. |
| `CreateIfNotExistsAsync` overload mismatch — setting up the 3-arg overload but the runtime binds the 4-arg one (or vice versa) ⇒ `Times.Once` verifies the wrong/unconfigured overload and the test passes/fails spuriously | Medium | Use the **same** overload signature for setup and verify; prefer the 4-arg `...EncryptionScopeOptions...` overload used by the existing passing cache tests (lines 463/474). Keep `It.IsAny<>` for the params. |
| FR-3 null-media-type case: a response with no `Content-Type` AND no filename feeds `null` to `GetExtensionFromContentType` ⇒ `.bin`; but the same null header would also be needed for FR-4 with-filename. Mixing them risks confusion | Low | Keep FR-3 (no filename, asserts extension suffix) and FR-4 (explicit filename, asserts upload `ContentType`) as separate `[Theory]` blocks with distinct helpers, as in the Component Overview. |
| Transitive `Azure.Storage.Blobs` reference could break if a project reference is pruned | Low | Existing tests already depend on it transitively and compile; no action needed. If a future build error appears, add an explicit `PackageReference` to `Azure.Storage.Blobs` 12.25.0 in the test csproj (last resort, not part of this work). |
| Coverage still < 60% after FR-1–FR-6 (FR-8) | Low | The targeted lines (45–59, 200–229, 271–280, 282–318) are the bulk of the uncovered surface; covering them mathematically clears 60%. Verify with `dotnet test --collect:"XPlat Code Coverage"` and inspect the cobertura line-rate for `AzureBlobStorageService.cs` before declaring done. |

## Specification Amendments

- **SA-1 (required): Make the "no `Content-Type` header" precondition explicit for FR-4.** The spec's FR-4 says "a response that carries no `Content-Type` header," but the existing `StubHttpMessageHandler` wraps content in `StringContent`, which **auto-sets `Content-Type: text/plain; charset=utf-8`**. Implementers must actively null out the header (e.g. `var c = new StringContent("x"); c.Headers.ContentType = null;` then pass as `overrideContent`, or use `ByteArrayContent`). Without this, line 59's `?? GetContentTypeFromExtension(...)` is never reached and every FR-4 case silently asserts against `text/plain`. Add one assertion confirming the response header is null to lock the precondition.

- **SA-2 (clarification): `CreateIfNotExistsAsync` overload.** The spec does not call out that the SUT calls `CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ...)` (production line 276) and that Moq must set up/verify a matching overload. The existing cache tests use the 4-parameter overload including `BlobContainerEncryptionScopeOptions`. FR-5's new download-path test should use that same overload for consistency.

- **SA-3 (clarification): assertions use raw xUnit `Assert`, not FluentAssertions.** The repo testing-strategy doc prefers FluentAssertions, but this specific test file uses `Assert.*`. NFR-3 ("follow existing conventions") wins; keep `Assert.*` to avoid an inconsistent mixed-style file.

- **SA-4 (minor): FR-3 should explicitly include `image/gif`, `image/webp`, `application/xml`** in addition to the cases the existing (weak) theory at lines 165–171 covers, since those arms (lines 289, 293) are otherwise unexercised. The spec's FR-3 list already names them; just ensure the replaced/added theory does not silently drop them by reusing the old 6-case `[InlineData]` set.

## Prerequisites

None blocking. All required infrastructure already exists:

- Test stack (`xUnit` 2.9.2, `Moq` 4.20.72, `coverlet.collector` 6.0.2) is referenced by `Anela.Heblo.Tests.csproj`.
- `Azure.Storage.Blobs` 12.25.0 (incl. `BlobsModelFactory`, `AsyncPageable`, `Page<T>`) is available transitively via the `Anela.Heblo.Application` / `Anela.Heblo.Adapters.Azure` project references.
- The target file, the SUT, the existing mock conventions, and `FileStorageModule.FileDownloadClientName` all exist and were verified.

No migrations, config, or new packages required. Before declaring done, run `dotnet build`, `dotnet format`, `dotnet test` (full `Anela.Heblo.Tests` suite green), and confirm `AzureBlobStorageService.cs` line coverage ≥ 60% via the cobertura report (FR-8). Confirm `git diff` touches only `backend/test/` (FR-7).
