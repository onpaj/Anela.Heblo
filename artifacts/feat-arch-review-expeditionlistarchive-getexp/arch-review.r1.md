# Architecture Review: Server-Side Virtual Directory Listing for Expedition Date Sidebar

## Skip Design: true

Backend-only change — no UI, no API contract, no visual element touched. The frontend keeps consuming `GetExpeditionDatesResponse` unchanged.

## Architectural Fit Assessment

The proposal aligns cleanly with the existing structure:

- **Layering is preserved.** `IBlobStorageService` already lives in `Anela.Heblo.Domain.Features.FileStorage/IBlobStorageService.cs` and is implemented by `Anela.Heblo.Application.Features.FileStorage.Services.AzureBlobStorageService`. Adding one more method to the abstraction follows the established Domain-defines/Application-implements split.
- **Vertical slice integrity holds.** The change touches only the FileStorage abstraction (shared infrastructure) and the single feature slice `ExpeditionListArchive/UseCases/GetExpeditionDates/`. No other handler is impacted.
- **Error-handling and logging style is reused.** `ListBlobsAsync` (`AzureBlobStorageService.cs:154`) and `UploadAsync` (`AzureBlobStorageService.cs:75`) already follow the try/catch + structured `LogError(ex, "...{ContainerName}...")` + rethrow idiom. The new method extends that pattern, not a new one.
- **Test harness is reusable.** `MockBlobStorageService.cs:138` already mirrors the production listing semantics from an in-memory `Dictionary<string, …>`. Deriving virtual-directory prefixes from the same dictionary is a small, local change.
- **No conflict with project rules.** No DTO changes, no new module, no new DbContext, no new persistence concern. The change is below the MediatR seam, fully encapsulated behind an interface.

The one place architectural care matters: the `Mock` implementation must mirror Azure's *hierarchical* semantics (not its flat `StartsWith` semantics). Otherwise tests pass while production behavior diverges. Mitigation in "Risks".

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────┐
│  ExpeditionListArchive (vertical slice)                     │
│                                                             │
│   MediatR Request                                           │
│        │                                                    │
│        ▼                                                    │
│   GetExpeditionDatesHandler  ◄── replaced call site         │
│        │                                                    │
└────────┼────────────────────────────────────────────────────┘
         │ ListVirtualDirectoriesAsync(container, ct)    (NEW)
         ▼
┌─────────────────────────────────────────────────────────────┐
│  IBlobStorageService  (Domain.Features.FileStorage)         │
│                                                             │
│   + DownloadFromUrlAsync                                    │
│   + UploadAsync                                             │
│   + DeleteAsync                                             │
│   + GetBlobUrl                                              │
│   + ExistsAsync                                             │
│   + ListBlobsAsync                                          │
│   + DownloadAsync                                           │
│   + ListVirtualDirectoriesAsync  ◄── (NEW, additive)        │
└─────────────────────────────────────────────────────────────┘
         │                                  │
         │ implements                       │ implements
         ▼                                  ▼
   AzureBlobStorageService          MockBlobStorageService
   (Application/.../Services)       (test double)
         │                                  │
         ▼                                  ▼
   BlobContainerClient                Dictionary<string,
   .GetBlobsByHierarchyAsync          Dictionary<string,
        (prefix: null,                MockBlobInfo>>
         delimiter: "/")
```

Other consumers of `IBlobStorageService` (`GetExpeditionListsByDateHandler`, `DownloadExpeditionListHandler`, `ReprintExpeditionListHandler`, `DownloadFromUrlHandler`) are unchanged.

### Key Design Decisions

#### Decision 1: Add a new method, do not change `ListBlobsAsync`

**Options considered:**
- (A) Overload `ListBlobsAsync` with a `bool hierarchical` parameter.
- (B) Return a richer `BlobItemInfo`-like type that carries an `IsPrefix` flag, mirroring the SDK.
- (C) Add a dedicated `ListVirtualDirectoriesAsync` method returning `IReadOnlyList<string>`. **(chosen)**

**Chosen approach:** (C).

**Rationale:** The two operations have different return shapes (`BlobItemInfo` vs. string prefix) and different semantics (flat enumeration vs. one-level hierarchy walk). Cramming them behind one signature with a flag muddies the contract for every existing caller. A separate, narrow method keeps each call site self-documenting and avoids retrofitting the unrelated `BlobItemInfo` type with hierarchy concerns it does not represent.

#### Decision 2: Strip the trailing `/` at the abstraction boundary, not at the consumer

**Options considered:**
- (A) Return raw Azure prefixes (`"2026-03-24/"`) and let each consumer trim.
- (B) Strip the `/` inside `AzureBlobStorageService` and document the contract as "no trailing slash". **(chosen)**

**Chosen approach:** (B). The abstraction owns the SDK-isolation responsibility; consumers should not need to know that Azure happens to append `/` to its hierarchical prefixes. This also keeps the mock and the production impl symmetric without requiring trim logic on the consumer side.

#### Decision 3: Keep pagination in-memory in the handler

**Options considered:**
- (A) Push pagination into the storage layer via continuation tokens.
- (B) Keep `Skip`/`Take` in `GetExpeditionDatesHandler`. **(chosen)**

**Chosen approach:** (B). Domain growth is ~250 prefixes/year. Even at a 10-year horizon, that is two orders of magnitude below the Azure single-page cap (5000). Adding token-based paging today is YAGNI; the spec already flags this as future work. The new method's contract intentionally returns the full list so the choice can be revisited without an abstraction change.

#### Decision 4: Drop `.Distinct()` from the handler

The Azure hierarchical listing de-duplicates prefixes server-side. The spec drops the now-redundant `.Distinct()` call. The `Mock` implementation must therefore also return distinct prefixes (see Risk #2) — otherwise mocked tests will silently mask a production-vs-test divergence.

#### Decision 5: Use `StringComparer.Ordinal` for sorting

The spec switches `OrderByDescending(d => d)` to `OrderByDescending(d => d, StringComparer.Ordinal)`. For ISO 8601 date strings (`yyyy-MM-dd`) the result is identical to the default culture-aware sort, but `Ordinal` is locale-independent and faster. Accept the change; no migration needed.

## Implementation Guidance

### Directory / Module Structure

No new directories. Edits only:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs` | + `ListVirtualDirectoriesAsync` method on the interface |
| `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs` | + implementation using `GetBlobsByHierarchyAsync` |
| `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs` | swap `ListBlobsAsync` → `ListVirtualDirectoriesAsync`; drop `Select(b => b.Name.Split('/')[0])` and `Distinct()` |
| `backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageService.cs` | + in-memory `ListVirtualDirectoriesAsync` |
| `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs` | migrate three existing setups; add two new tests (FR-4) |

### Interfaces and Contracts

```csharp
// Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs
/// <summary>
/// Lists distinct top-level virtual directory prefixes ("folders") in a container,
/// using the "/" hierarchy delimiter. Returned prefixes do NOT include the trailing slash.
/// Loose top-level blobs (no "/" in the name) are not returned.
/// </summary>
Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(
    string containerName,
    CancellationToken cancellationToken = default);
```

Contract invariants (must hold for **every** implementation, prod and mock):

1. The returned list contains **only** virtual-directory segments (i.e. only items where the underlying blob name contains at least one `/`).
2. Each returned string has the trailing `/` stripped.
3. The list is de-duplicated — callers may rely on this and skip their own `.Distinct()`.
4. Ordering is **not** guaranteed; callers sort client-side.
5. The `CancellationToken` is honoured.
6. The method does **not** auto-create the container (matches `ListBlobsAsync`).
7. Errors are logged with the `{ContainerName}` structured property and rethrown.

### Data Flow

```
[Page load: GET /api/expedition-archive/dates?page=1&pageSize=20]
                │
                ▼
   GetExpeditionDatesRequest    (unchanged contract)
                │
                ▼
   GetExpeditionDatesHandler
        │
        │ 1. ListVirtualDirectoriesAsync(containerName, ct)
        ▼
   IBlobStorageService → Azure: GetBlobsByHierarchyAsync(prefix:null, delimiter:"/")
        │
        │     returns ~D prefix strings (was: N blob descriptors)
        ▼
   Filter: IsValidDatePrefix (DateOnly.TryParseExact "yyyy-MM-dd")
        │
        ▼
   Sort: OrderByDescending(StringComparer.Ordinal)
        │
        ▼
   Page: Skip/Take in memory
        │
        ▼
   GetExpeditionDatesResponse   (unchanged contract)
```

Big-O shifts from O(N) blob enumeration to O(D) prefix enumeration where D = number of working days with at least one expedition list. Memory drops from `O(N · sizeof(BlobItemInfo))` to `O(D · sizeof(string))`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Mock semantics diverge from Azure semantics.** Current mock uses flat `StartsWith` matching (`MockBlobStorageService.cs:147`). If the new mock method does not correctly emulate the hierarchical *delimiter walk* (group blob names by first path segment, exclude loose top-level blobs, de-duplicate), tests will pass while production behaves differently. | Medium | New mock impl: project `_containers[containerName].Keys`, take everything before the first `/` (skip keys with no `/`), `.Distinct().ToList()`. Add the FR-4 test that mixes loose top-level blobs (e.g. `"readme.txt"`) and nested blobs (e.g. `"2026-03-24/list.pdf"`) and asserts only the latter contributes. |
| **Azure SDK parameter order.** `BlobContainerClient.GetBlobsByHierarchyAsync` signature is `(BlobTraits, BlobStates, string delimiter, string prefix, CancellationToken)` — *not* `(prefix, delimiter, …)`. Positional call will swap `delimiter` and `prefix`. | Medium | Use **named arguments** as the spec already shows: `GetBlobsByHierarchyAsync(prefix: null, delimiter: "/", cancellationToken: cancellationToken)`. Make this explicit in the implementation. |
| **Container does not exist on a fresh environment.** The method intentionally does not call `GetOrCreateContainerAsync`. First-ever sidebar load before any write will throw. | Low | This already matches today's `ListBlobsAsync` behaviour and is acceptable in this product (the container is created on first upload). Document the no-auto-create contract in the XML doc comment so future maintainers don't "helpfully" add it. |
| **Subtle sort behaviour change** (`Comparer<string>.Default` → `StringComparer.Ordinal`). | Low | For ISO 8601 date strings the output is byte-for-byte identical; safe. No code action required beyond the spec change. |
| **Sub-prefixes in the future.** If anyone ever writes a blob with a deeper path (e.g. `"2026-03-24/archive/list.pdf"`), the hierarchy listing still surfaces only the first segment (`"2026-03-24"`), so the date sidebar is unaffected. | Low | Document explicitly in the interface XML doc that only top-level prefixes are returned. No code action. |
| **5000-prefix Azure single-page cap.** At ~250 prefixes/year, this is ~20 years away but worth noting. | Low | Explicitly out of scope per spec. The async enumeration already pages transparently for *flat* listing; `GetBlobsByHierarchyAsync` does the same, so no consumer code needs continuation-token handling today. |
| **Test double under-coverage of the new method.** `MockBlobStorageService` is consumed by multiple test files; an incorrect mock will leak across the suite. | Low | Confine the new mock logic to a clearly named helper; ensure the new FR-4 handler tests assert the mock derives prefixes from the same `_containers` dictionary they populate (single source of truth). |

## Specification Amendments

The spec is implementation-ready. Two clarifications worth folding in before coding:

1. **`MockBlobStorageService` semantics — be explicit.** Add to FR-4: "The mock's `ListVirtualDirectoriesAsync` must (a) take the substring before the first `/` from each blob name, (b) skip names without a `/`, (c) `.Distinct()`. This mirrors Azure's `GetBlobsByHierarchyAsync(delimiter:"/")` semantics rather than the existing `StartsWith` semantics of the mock's `ListBlobsAsync`." Without this, FR-1's contract invariant 1 ("loose top-level blobs are not returned") is only enforced for the production impl.

2. **Document the no-auto-create contract in the XML doc.** Add to FR-1 acceptance: "The XML documentation on the interface method explicitly states that the method does not create the container." Today this is a comment in FR-2 only; lifting it to the interface contract prevents future "helpful" overrides.

Neither amendment requires a code change beyond what the spec already prescribes — they are stronger documentation and one extra mock-side requirement.

## Prerequisites

- **None.** No migrations, no infrastructure changes, no Azure RBAC changes, no NuGet bumps.
- `Azure.Storage.Blobs` already exposes `BlobContainerClient.GetBlobsByHierarchyAsync` at the version currently referenced.
- No OpenAPI regeneration needed (no API contract change). The TypeScript client will regenerate as part of the normal build but with zero diff.
- Validation gates per project rules: `dotnet build`, `dotnet format`, all touched tests pass via `dotnet test`. No E2E run required since the API contract is unchanged.