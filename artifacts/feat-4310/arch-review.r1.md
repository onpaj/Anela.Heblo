# Architecture Review: FileStorage DownloadFromUrlResponse nullability fix

## Skip Design: true
Backend-only, type-level bug fix. No new or changed UI components, screens, or layouts. `FileStorageController.DownloadFromUrl` already exists and its wire shape is unchanged (only the nullability annotation on three JSON fields changes, not their presence/absence at runtime).

## Architectural Fit Assessment
This fits cleanly as a one-file (plus a companion test file and a generated-client regen) correction inside the existing `FileStorage` vertical slice — no new component, module, or integration point is introduced.

- `DownloadFromUrlResponse` (`backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlResponse.cs`) is a plain MediatR response DTO, correctly implemented as a `class` (not a record), consistent with the project rule that OpenAPI client generators mishandle record parameter order.
- I verified `DownloadFromUrlHandler.Failure()` (lines 135–153) against the live source: it sets `Success`, `ErrorCode`, and `Params` only, confirming the spec's claim that `BlobUrl`/`BlobName`/`ContainerName` are left at their `= null!` default on all three failure paths (timeout, `HttpRequestException`, generic `Exception`).
- I verified the success path (lines 72–79): it already sets all three fields to real values, so `FR-2` (no success-path change) requires no code change — it's an invariant to preserve, not build.
- I checked `FileStorageController.DownloadFromUrl` (`backend/src/Anela.Heblo.API/Controllers/FileStorageController.cs`): it only forwards the MediatR response via `ActionResult<DownloadFromUrlResponse>` and does not itself dereference `BlobUrl`/`BlobName`/`ContainerName`, confirming the spec's claim that no controller change is required.
- The existing test suite (`backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`) already has one test per failure path (`Handle_RetryExhausted_...`, `Handle_HardHttpStatus_...`, `Handle_InnerTimeout_...`, `Handle_BlobStorageThrowsHttpRequestException_...`, `Handle_UnexpectedException_...`) — none of them currently assert on `BlobUrl`/`BlobName`/`ContainerName`, which is exactly the gap FR-3 closes.
- No cross-module boundary is touched: `DownloadFromUrlResponse` is consumed only within the `FileStorage` module and by the generated TypeScript client. This does not violate the module-independence or DTO-ownership rules in `docs/architecture/development_guidelines.md`.
- Note (not in scope to fix here): this response type lives in `UseCases/DownloadFromUrl/`, not a `Contracts/` folder, which differs from the `Contracts/` convention documented in `development_guidelines.md`. That is a pre-existing, module-wide layout choice for `FileStorage`, not something introduced or worsened by this change — left untouched per the spec's stated scope.

## Proposed Architecture

### Component Overview
No new components. Existing flow, unchanged:

```
FileStorageController.DownloadFromUrl
        │  (MediatR Send)
        ▼
DownloadFromUrlHandler.Handle
   ├─ success ──► new DownloadFromUrlResponse { Success=true, BlobUrl, BlobName, ContainerName, FileSizeBytes }
   └─ failure ──► Failure(...) → new DownloadFromUrlResponse { Success=false, ErrorCode, Params }
                                   (BlobUrl/BlobName/ContainerName intentionally unset)
        │
        ▼
DownloadFromUrlResponse : BaseResponse   ← only this type's declared nullability changes
        │
        ▼
OpenAPI spec → generated TypeScript client (frontend/src/api/generated/api-client.ts)
```

### Key Design Decisions

#### Decision 1: Nullable properties, not placeholder defaults
**Options considered:**
- (a) Make `BlobUrl`/`BlobName`/`ContainerName` nullable (`string?`), leaving `Failure()` unchanged.
- (b) Keep them non-nullable and have `Failure()` populate them with empty strings / sentinel values.
- (c) Split into separate `Success`/`Failure` response types (discriminated union style).

**Chosen approach:** (a) — nullable properties, `Failure()` unchanged.

**Rationale:** (b) would compile but lie semantically — an empty-string blob URL is not a real blob URL, and a consumer checking `string.IsNullOrEmpty` vs `null` would behave inconsistently. (c) is a correct long-term pattern but is a genuine breaking change to `DownloadFromUrlResponse`'s shape, touches the controller's return type and every consumer, and is disproportionate to a compile-time nullability bug — explicitly out of scope per the spec. (a) is the smallest change that makes the C# type system state the truth about what `Failure()` already does today, matches the brief's own suggested fix, and requires zero handler logic changes.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Modify in place:
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlResponse.cs`

Extend in place:
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`

Regenerate (do not hand-edit):
- `frontend/src/api/generated/api-client.ts`

### Interfaces and Contracts
```csharp
public class DownloadFromUrlResponse : BaseResponse
{
    public string? BlobUrl { get; set; }
    public string? BlobName { get; set; }
    public string? ContainerName { get; set; }
    public long FileSizeBytes { get; set; }
}
```
Property declaration order is not contractually significant (this is a class, not a record — no positional-constructor coupling), so the order may stay as-is or match the brief's suggested ordering; either is acceptable.

`Failure()` in `DownloadFromUrlHandler.cs` requires **no code change** — with the properties nullable, its existing object initializer (which omits these three) now compiles cleanly and expresses actual, already-existing behavior.

### Data Flow
Unchanged. The only difference is what the C# compiler and the generated OpenAPI schema *say* about the failure-path payload — the bytes on the wire (JSON with `blobUrl`/`blobName`/`containerName` omitted or `null` on failure) do not change, since `System.Text.Json`/ASP.NET Core already serializes an unset reference-type property as JSON `null` regardless of the nullable annotation.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A currently-undetected consumer dereferences `BlobUrl`/`BlobName`/`ContainerName` without a `Success` check and now gets a compiler nullable warning instead of a silent `null!` | Low | Grep confirmed the only in-repo consumer is `FileStorageController`, which does not dereference these fields; the change surfaces (rather than hides) any future misuse at compile time via CS8602, which is the intended improvement |
| Regenerated TypeScript client changes the field type from `string` to `string | undefined`, and some frontend caller does non-null assumption on it | Low | Spec grounds this in `docs/development/api-client-generation.md`'s standard regeneration flow; run `npm run build` per validation checklist to surface any frontend type errors introduced by the narrower type |
| Test additions could be written loosely enough to not actually exercise all three catch blocks | Low | FR-3 explicitly requires one assertion per failure path (timeout, `HttpRequestException`, generic `Exception`); the existing test file already has one test method per path to extend |

## Specification Amendments
None. The spec (`spec.r1.md`) is accurate and proportionate to the fix; verified against the actual source of `DownloadFromUrlResponse.cs`, `DownloadFromUrlHandler.cs`, and `FileStorageController.cs`.

## Prerequisites
None. No migrations, config, or infrastructure changes are needed — this is a same-PR code + test + generated-client change.
