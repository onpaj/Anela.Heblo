# Architecture Review: Remove Dead `ErrorMessage` Field from `RunExpeditionListPrintFixResponse`

## Skip Design: true

This is a backend DTO cleanup with corresponding frontend type pruning. No UI components, screens, or visual decisions are introduced or modified.

## Architectural Fit Assessment

The proposed change **aligns cleanly** with established conventions in this repo:

- `BaseResponse` (at `backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs`) is the canonical error envelope: `Success` defaults to `true` in the parameterless constructor; failures are reported via `ErrorCode` (typed `ErrorCodes` enum) and `Params` (`Dictionary<string, string>`). The error-message constructor `BaseResponse(Exception)` already stuffs the raw message into `Params["ErrorMessage"]` — there is **no DTO-level `ErrorMessage` field** in the contract. The dead field on `RunExpeditionListPrintFixResponse` is purely template residue.
- The recent `ExpeditionListArchive` realignment (commit `bee8e4fa`) is the precedent this work mirrors: bring response shape back to the `BaseResponse` contract.
- The handler (`RunExpeditionListPrintFixHandler.cs:38-42`) constructs only success responses. The handler's `Success = true` is redundant given `BaseResponse()`'s default.

**Integration points that must move together:**
1. Backend DTO (`RunExpeditionListPrintFixResponse.cs:8`)
2. Backend handler (`RunExpeditionListPrintFixHandler.cs:40`)
3. Generated TS client (`frontend/src/api/generated/api-client.ts:17649-17684`) — regenerates on build
4. Hand-coded frontend type `RunExpeditionListPrintFixResult` (`frontend/src/api/hooks/useExpeditionListArchive.ts:32-35`) — also carries the dead `errorMessage` field
5. Hook test (`frontend/src/api/hooks/__tests__/useExpeditionList.test.ts:39`) — success-path mock includes `errorMessage: null` and should be trimmed

**One nuance the spec does not call out:** the hand-coded `useExpeditionList` hook at `frontend/src/api/hooks/useExpeditionList.ts:17-21` reads `errorData?.errorMessage` from non-OK HTTP response bodies as a fallback error string. This is **not** consuming the typed DTO — it's reading whatever shape ASP.NET's exception middleware emits when the pipeline fails before the handler returns a `BaseResponse`. Therefore removing `ErrorMessage` from the DTO does **not** affect this code path. The fallback chain (`?? \`HTTP error! status: ${response.status}\``) keeps it resilient.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│ Backend (Application layer — Features/ExpeditionList)              │
│                                                                     │
│   BaseResponse  (Shared)                                            │
│     ├── Success (default true via parameterless ctor)               │
│     ├── ErrorCode  (ErrorCodes?)                                    │
│     └── Params     (Dictionary<string,string>?)                     │
│            ▲                                                        │
│            │ inherits                                               │
│   RunExpeditionListPrintFixResponse                                 │
│     └── TotalCount: int                                             │
│         (ErrorMessage REMOVED)                                      │
│            ▲                                                        │
│            │ returned by                                            │
│   RunExpeditionListPrintFixHandler                                  │
│     └── new Response { TotalCount = result.TotalCount }            │
│         (explicit Success = true REMOVED)                           │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                │ OpenAPI generation (build-time)
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Frontend                                                             │
│                                                                     │
│   api-client.ts (GENERATED)                                         │
│     └── RunExpeditionListPrintFixResponse { totalCount }            │
│         (errorMessage field REMOVED)                                │
│                                                                     │
│   useExpeditionListArchive.ts                                       │
│     └── RunExpeditionListPrintFixResult { totalCount }              │
│         (hand-coded mirror — errorMessage REMOVED)                  │
│                                                                     │
│   useExpeditionList.ts                                              │
│     └── error fallback reads errorData?.errorMessage from           │
│         middleware-shaped error bodies (UNCHANGED)                  │
└─────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Single-source-of-truth for the response shape
**Options considered:**
- (A) Only delete the backend field and let the generator propagate.
- (B) Delete the backend field AND prune the hand-coded `RunExpeditionListPrintFixResult` mirror in `useExpeditionListArchive.ts`.

**Chosen approach:** (B).

**Rationale:** `RunExpeditionListPrintFixResult` is a hand-coded interface that duplicates the generated shape. Leaving its `errorMessage: string | null` field in place after backend removal reintroduces exactly the YAGNI risk the spec is closing — a frontend consumer could grow a dependency on an always-null field. Surgical-changes principle still holds: this is the same dead field across two files, one logical fix.

#### Decision 2: Do not introduce a structured failure path now
**Options considered:**
- (A) Replace the dead `ErrorMessage` with a real `try/catch` that returns `BaseResponse(ErrorCode, Params)` on failure.
- (B) Leave the handler as success-only; rely on global exception middleware for failures.

**Chosen approach:** (B) — keep current behaviour.

**Rationale:** Spec explicitly scopes failure-path design out (`Out of Scope` and FR-4). Adding a failure path now would expand blast radius, require new tests, and conflict with surgical-changes guidance. The handler today is "always succeeds or throws"; the ambient exception middleware handles the throw case. That is the unchanged contract.

#### Decision 3: Hook error-fallback stays as-is
**Options considered:**
- (A) Refactor `useExpeditionList.ts:17-21` to read `errorData?.errorCode` + `params` instead of `errorMessage`.
- (B) Leave it. The fallback consumes middleware-shaped exception responses, not the typed DTO.

**Chosen approach:** (B).

**Rationale:** The hook's error read is decoupled from the typed DTO — it operates on whatever JSON the exception middleware produces. Project-wide standardization of error parsing is a separate concern. Touching it here violates surgical-changes and risks regressions in the unrelated middleware-response path.

## Implementation Guidance

### Directory / Module Structure
No new files. Edits only:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixResponse.cs` | Delete line 8 (`public string? ErrorMessage { get; set; }`) |
| `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs` | Delete line 40 (`Success = true,`) |
| `frontend/src/api/generated/api-client.ts` | Regenerated by build; do not hand-edit |
| `frontend/src/api/hooks/useExpeditionListArchive.ts` | Remove `errorMessage: string \| null` from `RunExpeditionListPrintFixResult` (lines 32-35) |
| `frontend/src/api/hooks/__tests__/useExpeditionList.test.ts` | Trim `errorMessage: null` from the success-path mock body at line 39 |

### Interfaces and Contracts

**Backend DTO after change (target shape):**
```csharp
public class RunExpeditionListPrintFixResponse : BaseResponse
{
    public int TotalCount { get; set; }
}
```

**Backend handler return after change:**
```csharp
return new RunExpeditionListPrintFixResponse
{
    TotalCount = result.TotalCount,
};
```

**Frontend hand-coded type after change:**
```typescript
export interface RunExpeditionListPrintFixResult {
  totalCount: number;
}
```

**Wire contract (success path):**
```json
{ "success": true, "errorCode": null, "params": null, "totalCount": 42 }
```

**Wire contract (failure path — unchanged, governed by middleware/`BaseResponse`):**
```json
{ "success": false, "errorCode": "<code>", "params": { ... } }
```

### Data Flow

```
Controller POST /api/expedition-list/run-fix
        │
        ▼
MediatR → RunExpeditionListPrintFixHandler.Handle()
        │
        ├── builds ExpeditionPickingRequest from PrintPickingListOptions
        ├── awaits IExpeditionListService.PrintPickingListAsync(...)
        └── returns new RunExpeditionListPrintFixResponse { TotalCount = result.TotalCount }
                        │ (Success implicitly true from BaseResponse() ctor)
        ▼
JSON serialization → { success: true, errorCode: null, params: null, totalCount: N }
        ▼
useRunExpeditionListPrintFix mutation receives parsed JSON
        ▼
ExpeditionListArchivePage consumes mutation result
```

**Failure path:** unchanged. Exceptions thrown by `IExpeditionListService.PrintPickingListAsync` bubble to the global exception middleware, which produces its own error envelope. The hook's `errorData?.errorMessage` fallback consumes that envelope; removal of the typed DTO field is invisible to this path.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Build-time client regeneration not triggered → stale `errorMessage` lingers in `api-client.ts` | Medium | Run a full backend build (`dotnet build`) before frontend lint/build. Verify by `grep -n "errorMessage" frontend/src/api/generated/api-client.ts` near `RunExpeditionListPrintFixResponse` and confirm only adjacent unrelated DTOs match. |
| Frontend hand-coded mirror `RunExpeditionListPrintFixResult` left intact, reintroducing the YAGNI surface | Medium | Edit `useExpeditionListArchive.ts:32-35` in the same change (see Decision 1). |
| Hook test mock at `useExpeditionList.test.ts:39` carries `errorMessage: null` and would still pass — silently asserting the dead shape | Low | Trim the mock body to `{ totalCount: 7 }` so the test reflects the new contract. The error-path mock at line 63 stays as-is (it tests middleware-shaped error responses, not the DTO). |
| Other DTOs in the codebase may share the same dead-`ErrorMessage` pattern (e.g. `ReprintExpeditionListResponse` returns `{ success, errorCode, params }` without it but other modules might) | Low | Out of scope per spec. If discovered during this change, note in PR description, do not fix. |
| Removing redundant `Success = true` accidentally masks a regression where someone later returns an error response via this constructor | Low | Add a one-line unit test asserting `new RunExpeditionListPrintFixResponse().Success == true` to lock the inherited default (per FR-2 and NFR-5). |

## Specification Amendments

The spec is largely accurate. Three clarifications/additions:

1. **FR-3 scope expansion — add explicit hand-coded mirror cleanup.** The spec lists "frontend code that referenced this field" but does not name `RunExpeditionListPrintFixResult` in `frontend/src/api/hooks/useExpeditionListArchive.ts:32-35`. This interface is hand-coded (not generated), carries `errorMessage: string | null`, and must be pruned in the same change to avoid leaving a dead surface in the frontend module.

2. **FR-3 test mock cleanup.** `frontend/src/api/hooks/__tests__/useExpeditionList.test.ts:39` mocks the success response body as `{ totalCount: 7, errorMessage: null }`. The `errorMessage: null` literal should be removed so the test reflects the post-change wire shape. (The error-path mock at line 63 must stay — it exercises an unrelated middleware-shaped error response.)

3. **NFR-5 lock-in test (recommended).** Add one xUnit test asserting that `new RunExpeditionListPrintFixResponse().Success` is `true` without explicit assignment. This locks in FR-2 against accidental regression of `BaseResponse()` ctor semantics. Location: alongside any existing tests for the handler, or in a new file under the matching test project mirroring `Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/`.

## Prerequisites

None. No migrations, no infrastructure, no config changes. The implementation is local edits to four files plus an automatic OpenAPI regeneration on `dotnet build`. Validation per `CLAUDE.md`:

- `dotnet build` + `dotnet format` (backend)
- `npm run build` + `npm run lint` (frontend)
- Existing unit tests pass (`dotnet test` for backend, `npm test` for the touched hook test)
- E2E not required (property removal of always-null field; nightly E2E suite covers integration)