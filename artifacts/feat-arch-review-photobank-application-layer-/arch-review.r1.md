I have enough grounding. Key facts verified:
- `PhotobankRepository` lives in **Application** but references EF Core + `Persistence` (the real layering compromise).
- `Domain` has zero infra refs — `MediatR` + DI/logging abstractions only; matches the `IssuedInvoiceClientException` precedent.
- `Npgsql` reaches Application transitively via `Persistence` → `Npgsql.EntityFrameworkCore.PostgreSQL`; no package change needed.
- `ErrorCodes` is in `Application/Shared/`.
- `GetPhotosHandlerTests` already exists (mock-based) — the new case slots straight in.
- The `PostgresException 2201B` surfaces during query *materialization* (`CountAsync`/`ToListAsync`), not in `BuildFilterQuery`.

One contract subtlety to flag: the handler currently echoes `request.Search` (untrimmed) into `Params["pattern"]`, while `BuildFilterQuery` trims before matching. That drives a specification amendment.

# Architecture Review: Remove Npgsql dependency from Photobank Application layer

## Skip Design: true

## Architectural Fit Assessment

The proposed fix is well-aligned with this codebase. The exception-translation-at-the-seam pattern already has precedent in `Domain/Features/Invoices/IssuedInvoiceClientException.cs` — a plain `Exception` subclass carrying contextual data, defined in Domain with no infra dependencies. The spec's `InvalidPhotoSearchPatternException` follows this convention exactly.

The integration points are clean and minimal:
- **Domain** (`IPhotobankRepository.cs` already lives here) gains a sibling exception type. No new package references — Domain currently depends only on `MediatR` and Microsoft abstractions, so the rule "no infra leak in Domain" is upheld.
- **Application repository** (`PhotobankRepository.GetPhotosAsync`) becomes the single translation point. It already legitimately imports `Npgsql` territory via EF Core + `Persistence`, so the `PostgresException` catch belongs here.
- **Application handler** (`GetPhotosHandler`) drops `using Npgsql;` and catches the domain exception instead.

**Important caveat (scoped out, correctly):** the deeper Clean Architecture smell is that `PhotobankRepository` — a data-access adapter referencing EF Core and `Persistence` — physically resides in the **Application** project rather than a dedicated Infrastructure project. This fix does *not* fully purify the Application layer; `Npgsql` types remain reachable inside `PhotobankRepository`. What it *does* achieve is removing the leak from the **use-case handler** (the pure orchestration layer) and making the error path **unit-testable without a database**. That is the correct, surgical win. Relocating the repository is a larger refactor and rightly out of scope.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────── Domain ───────────────────────────┐
│  IPhotobankRepository                                          │
│  InvalidPhotoSearchPatternException : Exception   ← NEW        │
│      + Pattern : string                                        │
│  (no Npgsql, no EF Core)                                       │
└───────────────────────────────────────────────────────────────┘
              ▲ throws                       ▲ implements
              │                              │
┌────────────┴──────────────── Application ─┴───────────────────┐
│  GetPhotosHandler                                              │
│    catch (InvalidPhotoSearchPatternException)  ← was Postgres  │
│    → GetPhotosResponse { Success=false, PhotobankInvalidRegex }│
│                                                                │
│  PhotobankRepository.GetPhotosAsync                            │
│    try { Count + ToList }                                      │
│    catch (PostgresException) when (useRegex && 2201B)          │
│      → throw new InvalidPhotoSearchPatternException(...)  ← NEW │
│    (Npgsql visible transitively via Persistence)               │
└───────────────────────────────────────────────────────────────┘
              │ EF Core query (Regex.IsMatch → '~*')
              ▼
┌──────────── Persistence (Npgsql.EntityFrameworkCore.PostgreSQL) ┐
│  ApplicationDbContext → PostgreSQL (raises 2201B at execution)  │
└────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Translate in `GetPhotosAsync`, not `BuildFilterQuery`
**Options considered:** (a) wrap the `Regex.IsMatch` predicate construction inside `BuildFilterQuery`; (b) wrap query *materialization* (`CountAsync` + `ToListAsync`) inside `GetPhotosAsync`.
**Chosen approach:** (b) — wrap the execution block in `GetPhotosAsync`.
**Rationale:** `BuildFilterQuery` only composes an `IQueryable`; it does not execute. The `PostgresException 2201B` is raised by PostgreSQL only when the `~*` regex operator actually runs — i.e. during `CountAsync`/`ToListAsync`. Wrapping `BuildFilterQuery` would catch nothing. Critically, the `try` must cover **both** `await` calls (count and list), since either can be the first to trigger evaluation. The other three `BuildFilterQuery` callers (`CountFilteredPhotosAsync`, `GetFilteredPhotoIdsMissingTagAsync` — both pass `useRegex: false`) stay untouched, so the blast radius is exactly one method.

#### Decision 2: Domain exception carries `Pattern`, but the response param keeps sourcing from `request.Search`
**Options considered:** (a) handler reads `ex.Pattern` for `Params["pattern"]`; (b) handler keeps `request.Search ?? string.Empty`.
**Chosen approach:** (b) for the response, with `Pattern` retained on the exception for diagnostics/logging.
**Rationale:** preserving the *exact* client-facing contract is the stated goal. `BuildFilterQuery` trims the search (`pattern = search.Trim()`), so an exception populated from the trimmed value would diverge from today's untrimmed `request.Search`. See the Specification Amendment below — this is the one place the spec's wording ("carrying `Pattern`") could silently change behavior if the handler switched to `ex.Pattern`.

#### Decision 3: No `PrivateAssets` change; rely on existing transitive flow
**Chosen approach:** leave project references as-is.
**Rationale:** `PostgresException` resolves through `Persistence`'s `Npgsql.EntityFrameworkCore.PostgreSQL` package, whose `Npgsql` dependency flows transitively to Application by default. The repository already compiles against this surface. No `.csproj` edits required.

## Implementation Guidance

### Directory / Module Structure
- **New:** `backend/src/Anela.Heblo.Domain/Features/Photobank/InvalidPhotoSearchPatternException.cs`
- **Edit:** `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs` (`GetPhotosAsync` only)
- **Edit:** `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetPhotos/GetPhotosHandler.cs` (remove `using Npgsql;`, swap catch)
- **Edit:** `backend/test/Anela.Heblo.Tests/Features/Photobank/GetPhotosHandlerTests.cs` (add the new case to the existing file — do **not** create a new test class; the spec's "New `GetPhotosHandlerTests`" should read "new test case in the existing `GetPhotosHandlerTests`").

### Interfaces and Contracts
- `InvalidPhotoSearchPatternException` — match the `IssuedInvoiceClientException` shape: `public class`, public `string Pattern { get; }`, single constructor `(string pattern)` calling `base($"Invalid search pattern: {pattern}")`. Place it in namespace `Anela.Heblo.Domain.Features.Photobank` (the folder uses block-scoped namespaces today; the new file may use either — match the file you're nearest to, prefer file-scoped per the global C# style, but Domain currently mixes both, so block-scoped here is acceptable for consistency with `IPhotobankRepository`).
- `IPhotobankRepository` — **no signature change.** `GetPhotosAsync` already receives `useRegex` and `search`, which is everything the translation needs. The exception is a documented runtime contract, not a method signature.
- `GetPhotosResponse` contract — **unchanged.** `Success=false`, `ErrorCode = ErrorCodes.PhotobankInvalidRegexPattern`, `Params["pattern"]`.

### Data Flow
1. `GetPhotosHandler.Handle` → `_repository.GetPhotosAsync(..., useRegex: true, ...)`.
2. `GetPhotosAsync` builds the query (`Regex.IsMatch` → `~*`) and awaits `CountAsync`/`ToListAsync`.
3. PostgreSQL rejects the bad pattern → `Npgsql` raises `PostgresException` (SqlState `2201B`).
4. `catch (PostgresException ex) when (useRegex && ex.SqlState == "2201B")` → `throw new InvalidPhotoSearchPatternException(search ?? string.Empty)`.
5. Handler `catch (InvalidPhotoSearchPatternException)` → returns the structured `GetPhotosResponse` with `Params["pattern"] = request.Search ?? string.Empty`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `try` wraps only one of the two `await` calls, missing the exception | Medium | Wrap the entire execution block (both `CountAsync` and `ToListAsync`) in one `try`. |
| Switching the response param to `ex.Pattern` silently trims the echoed pattern, changing the client contract | Medium | Keep `Params["pattern"] = request.Search ?? string.Empty` in the handler; use `ex.Pattern` only for logs. |
| `PostgresException` arriving wrapped (e.g. inside another EF exception) escaping the catch | Low | Reads/queries surface `PostgresException` directly today (the current handler catches it directly), so direct catch preserves existing behavior. No change in wrapping behavior is introduced. |
| Future relocation of `PhotobankRepository` to an Infra project assumed by reviewers | Low | Explicitly note in the PR that infra purification of the repository is out of scope and tracked separately. |
| Regression in the (currently untested) error path | Low | New unit test mocks `GetPhotosAsync` to throw `InvalidPhotoSearchPatternException` and asserts the exact response — this is the first real coverage of this path. |

## Specification Amendments
1. **FR-3 / FR-4 wording:** The spec implies a "new `GetPhotosHandlerTests`," but the file already exists (`backend/test/Anela.Heblo.Tests/Features/Photobank/GetPhotosHandlerTests.cs`) with mock-based cases. Amend to: *add a new `[Fact]` to the existing `GetPhotosHandlerTests`* asserting that when `GetPhotosAsync` throws `InvalidPhotoSearchPatternException`, the handler returns `Success=false`, `ErrorCode == ErrorCodes.PhotobankInvalidRegexPattern`, and `Params["pattern"]` equals the request search.
2. **FR-1 / FR-3 — response param source:** Make explicit that `Params["pattern"]` is sourced from `request.Search` in the handler (not from `ex.Pattern`), to preserve the exact untrimmed client-facing value. `Pattern` on the exception is for server-side diagnostics. Without this note, an implementer may "tidy up" to `ex.Pattern` and subtly alter the contract (trimmed vs untrimmed).
3. **FR-2 — translation location precision:** Confirm the catch lives in `GetPhotosAsync` around the materialization (`CountAsync` + `ToListAsync`), not in `BuildFilterQuery` (which never executes). The spec already leans this way ("`GetPhotosAsync`"); make it unambiguous and require both awaits inside the single `try`.

## Prerequisites
None. No migrations, config, infrastructure, or package changes. `PostgresException`/`Npgsql` remain available transitively through `Persistence`; `Domain` and `Application` already reference everything required. Implementation can start immediately.