Exploration is sufficient — I have the existing patterns, the domain shape, and the repository contract. Writing the review.

```markdown
# Architecture Review: Meeting Tasks Read Handlers — List & Detail

## Skip Design: true

Backend-only MediatR handlers and DTOs. No new UI surfaces, components, or visual decisions in this subtask — the consuming controller and frontend are explicitly out of scope.

## Architectural Fit Assessment

The proposal fits the existing **Clean Architecture + Vertical Slice + MediatR** layout cleanly. Anchor evidence:

- The Application project already auto-scans MediatR handlers (`ApplicationModule.cs:52` — `AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationModule).Assembly))`), so new handlers register themselves with no module-wiring step.
- `IMeetingTranscriptRepository` is already registered scoped in `PersistenceModule.cs:131`.
- `BaseResponse` (`Anela.Heblo.Application.Shared.BaseResponse`) and `ErrorCodes` (`Shared.ErrorCodes`) are the canonical error envelope used across every other feature.
- Existing analogs for "list + detail by id" pairs — `Bank/UseCases/GetBankStatementList` and `Logistics/UseCases/GetTransportBoxes` + `GetTransportBoxById` — colocate `*Request.cs`, `*Response.cs`, and `*Handler.cs` in the UseCase folder. The spec's file paths follow this convention.
- DTOs in `Features/{Slice}/Contracts/` is also the established pattern (Bank, Logistics, Catalog all do this).
- Domain entities (`MeetingTranscript`, `ProposedTask`) and enums match the DTO field set 1:1, with `RawTranscript` deliberately omitted from the list/detail DTO as the spec requires.

**Integration points:**
1. `IMeetingTranscriptRepository.GetListAsync` (returns `(List<MeetingTranscript>, int)`) — confirmed in `Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs:7-11`. Implementation in `Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs` already `Include(x => x.Tasks)` for both list and detail, so count projections in the handler use already-materialised collections — no additional roundtrip.
2. `ApplicationDbContext.MeetingTranscripts` — already present (verified via existing `MeetingTranscriptRepositoryTests`).
3. MediatR handler discovery — automatic.

**One spec-vs-code mismatch to note (minor):** the spec describes the repository signature as `(IEnumerable<MeetingTranscript>, int)` but the actual signature returns `List<MeetingTranscript>`. Both compile with `.Select(...)` in the handler, but the spec should be aligned to the actual signature. See *Specification Amendments*.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application/Features/MeetingTasks                  │
│                                                                │
│   Contracts/                                                   │
│     MeetingTranscriptDto    (class, OpenAPI-safe)              │
│     ProposedTaskDto         (class, OpenAPI-safe)              │
│                                                                │
│   UseCases/                                                    │
│     GetTranscriptList/                                         │
│       GetTranscriptListRequest   : IRequest<...Response>       │
│       GetTranscriptListResponse  : BaseResponse                │
│       GetTranscriptListHandler   : IRequestHandler<Req,Resp>   │
│                                                                │
│     GetTranscriptDetail/                                       │
│       GetTranscriptDetailRequest                               │
│       GetTranscriptDetailResponse                              │
│       GetTranscriptDetailHandler                               │
└────────────────────────────────────────────────────────────────┘
            │  depends on
            ▼
┌────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Domain/Features/MeetingTasks                       │
│   IMeetingTranscriptRepository (already exists)                │
│   MeetingTranscript, ProposedTask, *Status enums (exist)       │
└────────────────────────────────────────────────────────────────┘
            ▲  implemented by
            │
┌────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Persistence/MeetingTasks                           │
│   MeetingTranscriptRepository  (already exists, eager-loads    │
│                                 Tasks for both Get + List)     │
└────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Use a shared `MeetingTranscriptDto` for both list and detail (with `Tasks` empty in list mode) instead of two separate DTOs

**Options considered:**
- A. Single `MeetingTranscriptDto` with `Tasks` populated only in detail (spec choice).
- B. Separate `MeetingTranscriptSummaryDto` (no `Tasks`) and `MeetingTranscriptDetailDto` (with `Tasks`).

**Chosen approach:** A — single shared class DTO.

**Rationale:** The frontend will share most rendering logic between list rows and the detail header; reusing one DTO keeps the TS client thinner and matches the pattern used by `TransportBoxDto` (single DTO, `Items` collection populated in both `GetTransportBoxesHandler` and `GetTransportBoxByIdHandler`). The size of `Tasks` is bounded and the list handler currently populates it as `new()` — no payload bloat. Cost: the DTO has a "sometimes populated" field, which callers must understand. Acceptable because both consumption sites are owned by the same team.

#### Decision 2: Compute task counts in-memory in the handler, not in the repository

**Options considered:**
- A. Handler iterates `t.Tasks` and calls `.Count` / `.Count(predicate)` (spec choice).
- B. Add a `GetListSummaryAsync` repository method that returns a `(Items, ApprovedCount, RejectedCount, TotalCount)` projection via a `GroupBy` query, leaving `Tasks` uninstantiated.

**Chosen approach:** A.

**Rationale:** `MeetingTranscriptRepository.GetListAsync` already `Include(x => x.Tasks)` (`MeetingTranscriptRepository.cs:38`). The collections are materialised regardless, so in-memory `.Count()` is O(loaded rows) and costs nothing extra. Switching to a SQL-projection summary is a future optimisation when result-set sizes grow; the spec's NFR-1 correctly identifies this as a repository-layer concern, not a handler-layer one.

#### Decision 3: Treat invalid/empty `StatusFilter` as "no filter" (silent) rather than returning a validation error

**Options considered:**
- A. Silently ignore unparseable values → pass `null` to the repository (spec choice).
- B. Return `BaseResponse { Success = false, ErrorCode = InvalidValue }` for an unknown enum string.

**Chosen approach:** A.

**Rationale:** This is the pattern in `GetTransportBoxesHandler` (`Logistics/UseCases/GetTransportBoxes/GetTransportBoxesHandler.cs:30-41` — unknown state strings silently fall through). Consistency with sibling handlers outweighs strictness, and the controller layer (out of scope) is the natural place for input validation when it's added.

#### Decision 4: Detail handler returns `Success = false` with `ErrorCode = ResourceNotFound`, never throws

**Options considered:**
- A. Return failure response with `ErrorCode` set (spec proposes generic `ErrorCodes.NotFound`).
- B. Add a domain-specific `MeetingTranscriptNotFound` enum member.
- C. Throw `KeyNotFoundException` and let middleware convert.

**Chosen approach:** A, but using the **existing** code `ResourceNotFound` (`ErrorCodes.cs:25`). There is no `ErrorCodes.NotFound` member; the spec's pseudocode refers to a code that does not exist. See *Specification Amendments*.

**Rationale:** Non-throwing not-found is the established pattern (e.g. `GetJournalEntryHandler.cs:25` uses module-specific `ErrorCodes.JournalEntryNotFound`, `GetTransportBoxByIdHandler.cs:30` returns `TransportBox = null`). Either an existing generic code (`ResourceNotFound`) or a new module-specific code is acceptable; `ResourceNotFound` minimises churn and already maps to HTTP 404 via `HttpStatusCodeAttribute`. If product wants distinct error codes per resource later, add `MeetingTranscriptNotFound = 28xx` in a follow-up — out of scope for this subtask.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/MeetingTasks/
├─ Contracts/
│   ├─ MeetingTranscriptDto.cs
│   └─ ProposedTaskDto.cs
└─ UseCases/
    ├─ GetTranscriptList/
    │   ├─ GetTranscriptListRequest.cs
    │   ├─ GetTranscriptListResponse.cs
    │   └─ GetTranscriptListHandler.cs
    └─ GetTranscriptDetail/
        ├─ GetTranscriptDetailRequest.cs
        ├─ GetTranscriptDetailResponse.cs
        └─ GetTranscriptDetailHandler.cs

backend/test/Anela.Heblo.Tests/Features/MeetingTasks/
├─ GetTranscriptListHandlerTests.cs
└─ GetTranscriptDetailHandlerTests.cs
```

**No new `MeetingTasksModule.cs` is needed** — there are no extra services to register, and MediatR scanning picks up the handlers automatically. The repository is already wired in `PersistenceModule.cs`.

### Interfaces and Contracts

**Namespaces (must match the existing project layout exactly):**
- `Anela.Heblo.Application.Features.MeetingTasks.Contracts`
- `Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList`
- `Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail`

**DTO rules (project-wide invariant — do not deviate):**
- `class`, not `record` — OpenAPI generator constraint, restated in `CLAUDE.md` and `docs/architecture/development_guidelines.md`.
- Non-nullable reference type properties initialised with `= null!;` — matches the domain entities themselves.
- Collections initialised to empty (`= new();`) — matches `MeetingTranscript.Tasks`.

**Response inheritance:**
- Both responses inherit from `BaseResponse` (default constructor produces `Success = true`). Setting `Success = false` requires also setting `ErrorCode`.

**Request contracts:**
- `GetTranscriptListRequest : IRequest<GetTranscriptListResponse>` — `StatusFilter` (`string?`), `PageNumber = 1`, `PageSize = 20`.
- `GetTranscriptDetailRequest : IRequest<GetTranscriptDetailResponse>` — `Guid Id`.

**Repository contract used (read-only):**
- `Task<MeetingTranscript?> GetByIdAsync(Guid id, CancellationToken)`.
- `Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(MeetingTranscriptStatus?, int page, int pageSize, CancellationToken)`.

### Data Flow

**GetTranscriptList — happy path:**
1. Controller (future subtask) deserialises query string → `GetTranscriptListRequest`.
2. MediatR dispatches to `GetTranscriptListHandler.Handle`.
3. Handler parses `StatusFilter` via `Enum.TryParse<MeetingTranscriptStatus>(..., ignoreCase: true, out var parsed)` → `MeetingTranscriptStatus?` (null on miss).
4. Handler calls `_repository.GetListAsync(statusFilter, request.PageNumber, request.PageSize, ct)`. EF emits one `COUNT` query + one paged `SELECT` with `Tasks` JOIN.
5. Handler projects each `MeetingTranscript` → `MeetingTranscriptDto` with `Tasks = new()` and pre-computed counts (`t.Tasks.Count`, `t.Tasks.Count(x => x.Status == Approved)`, `t.Tasks.Count(x => x.Status == Rejected)`).
6. Returns response with `Items`, `TotalCount`, `PageNumber`, `PageSize`. `TotalPages` is a computed `get` property on the response.

**GetTranscriptDetail — happy path:**
1. Controller → `GetTranscriptDetailRequest { Id }`.
2. Handler calls `_repository.GetByIdAsync(request.Id, ct)`. EF returns aggregate with `Tasks` included.
3. Null → return `new GetTranscriptDetailResponse { Success = false, ErrorCode = ErrorCodes.ResourceNotFound }`. Do not throw.
4. Otherwise project to `MeetingTranscriptDto`; fully populate `Tasks` with `ProposedTaskDto` projections.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec references `ErrorCodes.NotFound` which does not exist in the enum. | Medium | Use the existing `ErrorCodes.ResourceNotFound` (`ErrorCodes.cs:25`, HTTP 404). Documented in *Specification Amendments*. |
| Spec describes `GetListAsync` returning `IEnumerable<MeetingTranscript>` but the real signature returns `List<MeetingTranscript>`. | Low | Cosmetic — `.Select(...)` works on both. Align spec wording; no code change required. |
| List response payload may grow large because the repository eager-loads `Tasks` even though list DTOs leave the collection empty. | Low (current) → Medium (scale) | Acceptable for current load. If transcript counts grow into the thousands, introduce a `GetListSummaryAsync` projection at the repository layer. **Do not** change repository behaviour in this subtask — out of scope. |
| Reviewer might add a new `MeetingTasksModule.cs` thinking DI registration is needed. | Low | Repository is registered in `PersistenceModule.cs:131`; MediatR scans the Application assembly automatically. No module file is required for this subtask. |
| Status string in DTO is `Enum.ToString()` — silent rename of an enum value would break clients. | Low | Standard project convention — every other handler does the same. Add no special protection here; treat enum renames as a breaking-change ritual that needs coordinated FE update. |
| `Tasks` list in the DTO is initialised to empty but the list handler also explicitly sets `Tasks = new()` — redundant but harmless. | Trivial | Leave it; explicitness mirrors the existing handler conventions. |

## Specification Amendments

1. **Error code:** Replace every reference to `ErrorCodes.NotFound` in FR-4 and the API design section with `ErrorCodes.ResourceNotFound` (HTTP 404 via `HttpStatusCodeAttribute`). The enum currently in `Anela.Heblo.Application/Shared/ErrorCodes.cs` does not contain a member named `NotFound`. Tests should assert `ErrorCode == ErrorCodes.ResourceNotFound` if they check the code (the current test fixture only asserts `Success == false`, which is already correct).

2. **Repository return type:** In the *Dependencies* section, change `GetListAsync(...) → (IEnumerable<MeetingTranscript>, int)` to `GetListAsync(...) → (List<MeetingTranscript>, int)` to match the actual interface signature.

3. **Detail response on not-found:** Add an explicit note that `Transcript` on a not-found response remains at its default (`null!` placeholder from the initialiser). Callers must check `Success` before dereferencing `Transcript`. This is already implied by inheriting `BaseResponse`, but worth being explicit for the controller layer that follows.

4. **No module file required:** Add an *Implementation note* under NFR-3: do not create `MeetingTasksModule.cs`. MediatR registration is automatic; repository registration is owned by `PersistenceModule`.

5. **Test assertion alignment with existing fixtures:** The example test transcripts omit `RawTranscript` (a non-nullable string with `= null!;` initialiser). EF Core won't care because tests use Moq, but for symmetry with the real domain entity, the test fixtures should set `RawTranscript = ""` (or any non-null value). Optional polish — does not block.

## Prerequisites

All prerequisites are already in place — no migrations, infrastructure, or config changes are required:

- `MeetingTranscript`, `ProposedTask`, `MeetingTranscriptStatus`, `ProposedTaskStatus` exist under `Anela.Heblo.Domain/Features/MeetingTasks/`.
- `IMeetingTranscriptRepository` interface and `MeetingTranscriptRepository` implementation exist and are DI-registered.
- `BaseResponse` and `ErrorCodes` exist and are stable.
- MediatR is wired (`ApplicationModule.cs:52`).
- The EF Core entity configuration and migration for `MeetingTranscripts` were delivered in the prior subtask (confirmed by `MeetingTranscriptRepositoryTests`).
- xUnit + Moq are already in the test project (verified by sibling test files such as `ImportBankStatementHandlerTests`).

Implementation can begin immediately.
```