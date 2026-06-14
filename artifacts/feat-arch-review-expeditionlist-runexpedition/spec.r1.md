# Specification: Remove Dead `ErrorMessage` Field from `RunExpeditionListPrintFixResponse`

## Summary
The `RunExpeditionListPrintFixResponse` DTO declares an `ErrorMessage` field that is never assigned by any handler and is redundant with the structured error contract already provided by `BaseResponse` (`ErrorCode` + `Params`). This spec removes that dead field and a related redundant `Success = true` assignment, cleaning up an artifact of template copy-paste before frontend code grows dependencies on a permanently-null property.

## Background
The `RunExpeditionListPrintFix` use case re-prints expedition list documents. Its response type currently exposes an `ErrorMessage` string that is structurally guaranteed to be `null`:

- `RunExpeditionListPrintFixResponse.ErrorMessage` is declared at `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixResponse.cs:6`.
- The single handler that returns this type (`RunExpeditionListPrintFixHandler.cs:37`) only constructs success responses and never assigns `ErrorMessage`.
- The base class `BaseResponse` already provides `ErrorCode` and `Params` for structured failure reporting, which is the project-wide pattern for error responses (recently re-aligned across `ExpeditionListArchive` in commit `bee8e4fa`).
- The handler also sets `Success = true` explicitly, even though the `BaseResponse()` constructor already defaults `Success` to `true`.

Because the backend type flows into the auto-generated TypeScript client on every build (`OpenAPI TypeScript client is auto-generated on build`), the field appears in the public API surface as `errorMessage: string | null`. Frontend code could (incorrectly) condition on this value, masking real error handling that should go through the standard `errorCode` / `params` path. Removing the field now — before any consumer grows a dependency on it — is YAGNI cleanup with no behavioural impact.

This finding was filed by the daily architecture-review routine on 2026-06-05.

## Functional Requirements

### FR-1: Remove `ErrorMessage` property from response DTO
Delete the `public string? ErrorMessage { get; set; }` property from `RunExpeditionListPrintFixResponse`.

**Acceptance criteria:**
- `RunExpeditionListPrintFixResponse.cs` no longer declares `ErrorMessage`.
- The class continues to inherit from `BaseResponse` and continues to expose `TotalCount`.
- `dotnet build` succeeds for the backend solution with no warnings introduced by this change.
- No other backend file (production code or tests) references `RunExpeditionListPrintFixResponse.ErrorMessage` after the change.

### FR-2: Remove redundant `Success = true` assignment in handler
In `RunExpeditionListPrintFixHandler`, remove the explicit `Success = true` from the response object initializer. The `BaseResponse` constructor already defaults `Success` to `true`.

**Acceptance criteria:**
- `RunExpeditionListPrintFixHandler.cs:37` constructs the response without an explicit `Success = true`.
- A returned response still reports `Success == true` at runtime (verified by existing or new handler test).
- No other functional change to the handler's logic, ordering, or returned values.

### FR-3: Regenerate the TypeScript client and update consumers as needed
After the backend changes, the OpenAPI-generated TypeScript client must be regenerated so that the `errorMessage` field disappears from the response type. Any frontend code that referenced this field must be updated or removed.

**Acceptance criteria:**
- The generated TypeScript type for the print-fix response no longer contains `errorMessage`.
- A repo-wide search for `errorMessage` in the context of the run-expedition-list-print-fix hook/usages (notably under `frontend/src/api/hooks/useRunExpeditionListPrintFix*` and the recently relocated module per commit `7b837bc5`) returns no remaining references.
- `npm run build` and `npm run lint` succeed in `frontend/`.

### FR-4: Behaviour preservation under success and failure paths
Errors from the underlying use case must continue to be communicated via the existing `BaseResponse` mechanism (`Success`, `ErrorCode`, `Params`) — not via a free-form message field. The deletion must not change the wire shape of error responses beyond the removal of the always-null `errorMessage` field.

**Acceptance criteria:**
- The success-path JSON payload for `RunExpeditionListPrintFix` differs from the current payload only by the absence of the `errorMessage` key.
- If/when the handler is later extended to surface failures, it does so via `ErrorCode` + `Params` (consistent with the `ExpeditionListArchive` alignment from commit `bee8e4fa`); no new free-form `ErrorMessage` is reintroduced.
- Existing unit/integration tests for the use case continue to pass without modification, except for any test that asserted on `errorMessage` (none are known to exist; if discovered, they are updated rather than the property re-added).

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance implications. Payload size is marginally reduced (one fewer null field per response). No new allocations, queries, or I/O.

### NFR-2: Security
No security impact. The removed field never carried sensitive content (it was always null). The standard `BaseResponse` error envelope continues to govern what is surfaced to clients.

### NFR-3: Backward compatibility / API surface
This is a public API surface reduction. Per project conventions:
- The application is a solo-developer monorepo where backend + frontend ship together as a single Docker image; there are no external API consumers to coordinate with.
- Therefore removing the field is acceptable without a deprecation window, provided all in-repo frontend references are removed in the same change.

### NFR-4: Code quality / maintainability
- Removes YAGNI dead code and reduces template-copy-paste drift.
- Reinforces the project-wide pattern that error context lives in `BaseResponse.ErrorCode` + `Params`, not ad-hoc string fields.
- Keeps response DTOs minimal and intention-revealing.

### NFR-5: Test coverage
Per project standards (80% minimum, AAA pattern). Existing tests for the use case must continue to pass; no new test types are required. A trivial assertion that the constructed response reports `Success == true` without explicit assignment is desirable to lock in FR-2.

## Data Model
No persistence change. Only the in-memory DTO shape changes:

**Before**
```
RunExpeditionListPrintFixResponse : BaseResponse
├── TotalCount: int
└── ErrorMessage: string?   ← removed
```

**After**
```
RunExpeditionListPrintFixResponse : BaseResponse
└── TotalCount: int
```

`BaseResponse` (unchanged) provides:
- `Success: bool` (defaulted to `true` by constructor)
- `ErrorCode` (structured error identifier)
- `Params` (structured error parameters)

## API / Interface Design

**Endpoint affected:** the existing MVC controller action that returns `RunExpeditionListPrintFixResponse` (the use case under `Features/ExpeditionList/UseCases/RunExpeditionListPrintFix`). No route, verb, or request shape changes.

**Response shape (success):**
```json
{
  "success": true,
  "errorCode": null,
  "params": null,
  "totalCount": 42
}
```

**Response shape (failure, governed by `BaseResponse`):**
```json
{
  "success": false,
  "errorCode": "<structured code>",
  "params": { "...": "..." },
  "totalCount": 0
}
```

**Frontend impact:**
- The auto-generated TypeScript response interface loses its `errorMessage?: string | null` member.
- Any UI code that reads `response.errorMessage` must instead consult `response.errorCode` / `response.params` (the project's standard error-display path).
- The `useRunExpeditionListPrintFix` hook (recently relocated to the ExpeditionList module per commit `7b837bc5`) is the primary touchpoint to audit.

## Dependencies
- **Internal:**
  - `BaseResponse` contract (must not change as part of this work).
  - OpenAPI TypeScript client generator (runs on backend build per `docs/development/api-client-generation.md`).
  - The `useRunExpeditionListPrintFix` hook in the ExpeditionList frontend module.
- **External:** none.
- **Coordination:** none — solo developer, single deployable image, no external API consumers.

## Out of Scope
- Refactoring other response DTOs that may carry similar dead `ErrorMessage` fields. (If discovered during this change, they are to be noted but not fixed here — surgical-changes principle.)
- Changes to `BaseResponse` itself or to the project-wide error-reporting contract.
- Any behavioural change to the print-fix use case (queries, side effects, success/failure semantics).
- Reintroducing a structured failure path in this handler — the handler currently always returns success; designing a real failure path is a separate piece of work.
- Database migrations or schema changes (none required).
- E2E test additions — existing test coverage is sufficient for a property removal of an always-null field.

## Open Questions
None.

## Status: COMPLETE