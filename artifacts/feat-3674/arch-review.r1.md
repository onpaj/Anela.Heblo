# Architecture Review: Remove Azure Adapter's Compile-Time Dependency on the Application Layer (FileStorage)

## Skip Design: true

## Architectural Fit Assessment

The finding is real and the spec's fix is correctly scoped. I verified every claim against the actual source:

- `AzureBlobStorageService.cs:39` does call `_httpClientFactory.CreateClient(FileStorageModule.FileDownloadClientName)`, and line 4 imports `Anela.Heblo.Application.Features.FileStorage` for no other reason — nothing else in the file touches the Application layer. This is a genuine violation of the Domain ← Application ← Adapters dependency direction that `docs/architecture/filesystem.md` and `memory/decisions/clean-architecture-vertical-slice.md` establish (API/Adapters depend inward on Application/Domain, never the reverse — and an outer-ring project reaching into Application for a bare string constant is exactly the kind of coupling that rule exists to prevent).
- `FileStorageModule.cs:14` does declare `public const string FileDownloadClientName = "FileDownload";` inside `AddFileStorageModule`'s static class, alongside DI wiring — confirming the constant is genuinely stranded in a composition-root file, not a natural Application-owned concept.
- `AzureBlobStorageServiceTests.cs` imports `Anela.Heblo.Application.Features.FileStorage` and references `FileStorageModule.FileDownloadClientName` in exactly the pattern described (11 occurrences, not "~10" but close enough not to matter).
- `PurchaseOrderConstants.cs` is a solid, directly-comparable precedent: a plain `public static class` of `const` fields sitting in `Anela.Heblo.Domain.Features.Purchase`, used across layers. `FileStorageConstants` in `Anela.Heblo.Domain.Features.FileStorage` follows the identical shape and sits next to the existing `IBlobStorageService.cs` / `BlobItemInfo` in that same namespace.
- `DownloadFromUrlHandler.cs` (Application layer) uses `FileStorageModule.FileDownloadClientName` twice (lines 95, 144) and is correctly left untouched by the spec — it already legitimately lives in Application, so referencing an Application-layer module constant is not a boundary violation for it.
- `AzureAdapterModule.cs` legitimately references `Anela.Heblo.Application.Features.FileStorage` (for `FileStorageOptions`) and `Anela.Heblo.Application.Shared.Printing` (for `PrintPickingListOptions`) purely for DI composition-root wiring. I checked all 20 adapter `.csproj` files under `backend/src/Adapters/`: 15 of them reference `Anela.Heblo.Application`, and only 4 (HomeAssistant, OpenMeteo, Smartsupp, SendGrid-via-Xcc-only) reference Domain exclusively. So "Adapter → Application ProjectReference for options binding" is the dominant, accepted pattern in this codebase, not an outlier — the spec is right to leave `AzureAdapterModule.cs` and the `.csproj` alone and scope this fix narrowly to the one runtime-logic reference (`AzureBlobStorageService.cs`) that has no DI-wiring justification.
- No `NetArchTest`/architecture-fitness-function project exists in the repo today. `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` exists but its allowlist mechanism polices **intra-Application cross-module** namespace boundaries (e.g. `Catalog -> Logistics`), not the **Adapters → Application layering rule** this spec addresses. There is nothing to update there, and the spec correctly avoids inventing new tooling in this change (explicitly out of scope).

Net: this is a minimal, low-risk, single-purpose refactor that removes a genuine architectural smell without touching runtime behavior, DI wiring, or the project's dependency graph. It's exactly the kind of "surgical fix" this codebase's own conventions call for. I endorse the spec as written, with one clarifying amendment below (constant type, not visibility or placement).

## Proposed Architecture

### Component Overview

No new components. One new static holder class in Domain, one one-line change in the Adapter, one const-forwarding edit in the existing Application module, and a mechanical test update. No DI graph changes, no new interfaces, no new abstractions.

```
Anela.Heblo.Domain.Features.FileStorage
  ├── IBlobStorageService.cs        (existing)
  └── FileStorageConstants.cs       (NEW — const string FileDownloadClientName)

Anela.Heblo.Application.Features.FileStorage
  └── FileStorageModule.cs          (EDIT — FileDownloadClientName becomes a forwarding const)

Anela.Heblo.Adapters.Azure.Features.FileStorage
  └── AzureBlobStorageService.cs    (EDIT — drop Application using, reference Domain constant)
```

### Key Design Decisions

#### Decision 1: Where the constant lives
**Options considered:**
1. Move to Domain as a `const string` on a new `FileStorageConstants` static class (spec's proposal).
2. Move to Domain as an `IOptions<T>`-style options class (brief's alternative suggestion).
3. Leave as-is and add a NetArchTest allowlist exception instead of fixing the code.

**Chosen approach:** Option 1 — a Domain-layer `static class FileStorageConstants` with a `const string`.

**Rationale:** `FileDownloadClientName` is not configuration — it's a fixed, compile-time-known key that names an `IHttpClientFactory` registration. It never varies by environment or deployment, so wrapping it in `IOptions<T>` (which implies runtime-bindable, environment-specific config) would be over-engineering and would require DI resolution at a callsite (`AzureBlobStorageService.DownloadFromUrlAsync`) that has no other reason to depend on `IOptions<T>`. `PurchaseOrderConstants` is the established precedent in this exact codebase for "small, stable, cross-layer string/int constants belong in a Domain-layer `static class`." Option 3 (allowlisting instead of fixing) papers over a real violation instead of removing it, and there's no existing architecture-test infrastructure that even covers this class of violation — introducing one is explicitly out of scope for this change, so there's nothing to allowlist against yet.

#### Decision 2: Keep `FileStorageModule.FileDownloadClientName` as a forwarding const
**Options considered:**
1. Delete `FileStorageModule.FileDownloadClientName` entirely and repoint all Application-layer consumers (`DownloadFromUrlHandler.cs`, `FileStorageModuleTests.cs`) at `FileStorageConstants` directly.
2. Keep it as `public const string FileDownloadClientName = FileStorageConstants.FileDownloadClientName;` (spec's proposal).

**Chosen approach:** Option 2.

**Rationale:** This keeps the diff surgical — `DownloadFromUrlHandler.cs` is explicitly out of scope per the spec, and touching it (plus its tests) to satisfy a rename would violate "surgical changes" for no architectural benefit; both names now resolve to the same compile-time literal, so there is no drift risk. This is a short-lived compatibility shim, not a long-term dual-source-of-truth: once a future, separately-scoped cleanup touches `DownloadFromUrlHandler.cs` anyway, it should switch to `FileStorageConstants` directly and the forwarding const can be deleted. Flagging this now so it isn't forgotten (see Specification Amendments).

## Implementation Guidance

### Directory / Module Structure

New file: `backend/src/Anela.Heblo.Domain/Features/FileStorage/FileStorageConstants.cs` — same directory as the existing `IBlobStorageService.cs`, matching the `PurchaseOrderConstants.cs` precedent (`Domain/Features/Purchase/PurchaseOrderConstants.cs`) exactly. No new folders.

### Interfaces and Contracts

No interface changes. This is a pure constant-relocation; `IBlobStorageService` and its single implementation `AzureBlobStorageService` keep identical signatures and behavior.

### Data Flow

Unchanged at runtime. Compile-time reference graph changes from:

```
Adapters.Azure --(compile-time)--> Application.Features.FileStorage.FileStorageModule.FileDownloadClientName
```
to:
```
Adapters.Azure --(compile-time)--> Domain.Features.FileStorage.FileStorageConstants.FileDownloadClientName
Application.Features.FileStorage.FileStorageModule.FileDownloadClientName --(compile-time)--> Domain.Features.FileStorage.FileStorageConstants.FileDownloadClientName  (forwarding const, both resolve to the same literal at compile time)
```

The Adapters.Azure → Application `ProjectReference` in the `.csproj` is retained (per spec, out of scope to remove) but is no longer exercised by `AzureBlobStorageService.cs` specifically — it remains justified solely by `AzureAdapterModule.cs`'s legitimate options-binding usage.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Two `FileDownloadClientName` consts (Domain source + Application forwarder) drift if someone edits one without the other | Low | Both are `const`, so any value mismatch is impossible without deliberately editing both lines; `FileStorageModuleTests.cs::AddFileStorageModule_NamedClient_ConstantIsExported` already asserts the value is `"FileDownload"` and will catch any divergence. No new test needed. |
| Forwarding const becomes permanent clutter instead of a stepping stone | Low | Call it out explicitly in the PR description / a follow-up note (see Specification Amendments) so a future FileStorage-touching change removes it when `DownloadFromUrlHandler.cs` is next modified for unrelated reasons. Not worth a dedicated task on its own. |
| Reviewer expects this PR to also fix `AzureAdapterModule.cs`'s Application references, since it's the same file family | Low | Spec's FR-5 already states this is intentionally out of scope and explains why (legitimate DI-wiring pattern, consistent with 15/19 other adapters). Worth restating in the PR description to preempt review churn. |

Both risks are low because the change is compile-time-only, mechanically verifiable by `dotnet build`, and covered by an existing test that pins the constant's value.

## Specification Amendments

None required to FR-1 through FR-5 — they are accurate and correctly scoped as verified above. One non-blocking note for the implementer:

- **Optional follow-up (not part of this task):** once any future change legitimately touches `DownloadFromUrlHandler.cs` or `FileStorageModuleTests.cs`, prefer switching those two consumers to reference `Anela.Heblo.Domain.Features.FileStorage.FileStorageConstants.FileDownloadClientName` directly and deleting the `FileStorageModule.FileDownloadClientName` forwarding const at that point. Do not do this now — it would violate the "surgical changes" rule and expand the diff beyond what this task requires.

## Prerequisites

None. No schema changes, no feature flags, no other in-flight branches touch these files (confirmed via the file contents read directly from the worktree). Implementation can proceed straight from this review.
