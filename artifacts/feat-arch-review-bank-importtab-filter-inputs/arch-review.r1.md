I have enough to write the review. The key things I verified:

- Existing repository template (`PackageRepository.GetPaginatedAsync`) shows the canonical filter+sort+paginate pattern with `EF.Functions.ILike` + `EscapeLike` helper — the spec's pattern aligns but omits `LIKE` escaping.
- The repository test suite uses EF Core InMemory, which does **not** translate `EF.Functions.ILike` — a concrete risk for the spec's stated test coverage.
- The frontend hook is hand-rolled (not consumed from the generated OpenAPI client) — spec's "regenerate the client" wording should be tightened.
- `TransferId` has a unique B-tree index that cannot accelerate `%substring%` ILike; spec's "no proactive index work" stands but the reasoning belongs in the doc.
- The spec says `Contains` (case-insensitive) for TransferId but `ILike` for Account — inconsistent; PostgreSQL `string.Contains` is case-sensitive, so both need ILike.

# Architecture Review: Wire Up Bank Import Tab Filter Inputs

## Skip Design: true

No new UI controls, no layout, no copy, no visual decisions. Every control already exists in `ImportTab.tsx`; this work is end-to-end plumbing of values that the user already sees and types into.

## Architectural Fit Assessment

The feature is a textbook fit for the existing slice. The Bank module already follows the Vertical Slice + MediatR + Repository convention (`Features/Bank/UseCases/GetBankStatementList/*`), and a near-identical multi-filter list query already lives in `PackageRepository.GetPaginatedAsync` (`backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs:12`). That repository is the canonical template — it combines optional string predicates via `EF.Functions.ILike`, optional date-range bounds, and pagination/sorting in one method. Replicating that shape in `BankStatementImportRepository.GetFilteredAsync` is the lowest-risk path.

Integration points are narrow and well isolated:

- One MediatR request DTO (`GetBankStatementListRequest`)
- One handler (`GetBankStatementListHandler`)
- One repository method (`GetFilteredAsync`) and its interface
- One controller action (`BankStatementsController.GetBankStatements`)
- One FluentValidation validator (`GetBankStatementListRequestValidator`)
- One frontend hook (`useBankStatementsList`)
- One consumer (`ImportTab.tsx`)

No new aggregates, no new modules, no cross-module communication. AutoMapper profile is untouched (the existing `ErrorType` mapping already encodes the FR-4 predicate). No schema changes.

The only architectural friction is the growth of the positional parameter list on the repository method (8 → 13 parameters). The codebase has tolerated this style elsewhere (`PackageRepository` uses 11), so introducing a criteria object now would diverge from in-repo precedent and is not warranted by this feature alone.

## Proposed Architecture

### Component Overview

```
ImportTab.tsx
   │   (transferId, account, dateFrom, dateTo, errorsOnly)
   │   committed-filters state (only mutated by Filtrovat / Vyčistit)
   ▼
useBankStatementsList(request)            ← frontend hook (hand-rolled fetch)
   │   GET /api/bank-statements?transferId=…&account=…&dateFrom=…&dateTo=…&errorsOnly=…
   ▼
BankStatementsController.GetBankStatements   ← [FromQuery] params, builds DTO
   ▼
MediatR ── GetBankStatementListRequest
   │
   ├── ValidationBehavior → GetBankStatementListRequestValidator (extended)
   │       400 on: dateFrom > dateTo, transferId/account > 100 chars,
   │                unparseable dates (TryParse failure surfaced via validator)
   ▼
GetBankStatementListHandler
   │   parses dateFrom/dateTo via DateTime.TryParse (same pattern as existing
   │   statementDate/importDate); trims account; passes 13 args to repository
   ▼
IBankStatementImportRepository.GetFilteredAsync
   │   IQueryable<BankStatementImport>.AsNoTracking()
   │     .Where(id?) .Where(transferId? → ILike '%x%')
   │     .Where(account? → ILike '%x%')   ← trimmed, LIKE-escaped
   │     .Where(dateFrom? → StatementDate >= dateFrom.Date)
   │     .Where(dateTo?   → StatementDate <  dateTo.Date.AddDays(1))
   │     .Where(errorsOnly? → ImportResult != "OK")
   │     .Where(statementDate?) .Where(importDate?)    ← existing equality filters
   ▼
PostgreSQL (BankStatements table; existing indexes only)
```

### Key Design Decisions

#### Decision 1: Match `PackageRepository`'s `ILike` + `EscapeLike` pattern; do not use `string.Contains`

**Options considered:**
- (a) `query.Where(bs => bs.TransferId.Contains(value))` — what the spec literally says for TransferId.
- (b) `query.Where(bs => EF.Functions.ILike(bs.TransferId, $"%{Escape(value)}%", "\\"))` — what `PackageRepository` and `IssuedInvoiceRepository` already do.

**Chosen approach:** (b) for both TransferId **and** Account.

**Rationale:** The spec's "case-insensitive `Contains`" wording is internally inconsistent — Npgsql translates `string.Contains` to a case-sensitive `LIKE '%x%'`. Only `EF.Functions.ILike` yields case-insensitive matching on PostgreSQL. The spec already prescribes `ILike` for Account; using the same primitive for TransferId is both correct and uniform with two prior in-repo precedents. Additionally, both inputs are free-form user text and must be `LIKE`-escaped (reuse `EscapeLike` from `PackageRepository`, or extract it to a small `LikeEscape` helper under `Anela.Heblo.Persistence`). Without escaping, a user typing `100%` produces a query that matches everything.

#### Decision 2: Date range comparison — half-open `>= from && < to.AddDays(1)` rather than `.Date <=` on both sides

**Options considered:**
- (a) `bs.StatementDate.Date >= dateFrom.Date && bs.StatementDate.Date <= dateTo.Date` (spec wording).
- (b) `bs.StatementDate >= dateFrom.Date && bs.StatementDate < dateTo.Date.AddDays(1)`.

**Chosen approach:** (b), the half-open form, matching `PackageRepository` (`PackageRepository.cs:38`).

**Rationale:** Both forms translate, but (b) is the form already in use in the codebase, is sargable in a way that lets an `IX_BankStatements_StatementDate` B-tree index help when present, and avoids `date_trunc`-style server-side casts on every row. Functionally equivalent for the spec's day-granularity requirement, and the user-facing semantics (inclusive on both ends) are preserved.

#### Decision 3: Validation lives in the existing FluentValidation validator; controller stays thin

**Options considered:**
- (a) Add `if (dateFrom > dateTo) return BadRequest(...)` and length checks inline in the controller action.
- (b) Extend `GetBankStatementListRequestValidator` with the new rules; let MediatR's existing `ValidationBehavior` produce the 400.

**Chosen approach:** (b).

**Rationale:** The validator already exists and is the project's defined seam for request validation. Adding rules there keeps the controller a thin pass-through (its current shape) and ensures the same validation runs if any other caller dispatches the MediatR request directly. Date string parseability is checked by the handler today via `TryParse` with a silent fallback to `null`; the spec requires unparseable values to return 400, so a `Must(BeParseableDateOrNull)` rule is added in the validator, and the handler keeps its current parse-then-pass pattern (now safe because the validator rejects unparseable input before the handler runs).

#### Decision 4: Frontend "committed filters" object, not five `useEffect`-driven props

**Options considered:**
- (a) Pass the live input state directly to `useBankStatementsList` — every keystroke refetches.
- (b) Keep separate "input" and "committed" state; only `handleApplyFilters`/`handleClearFilters` write the committed state; pass the committed state into the hook.

**Chosen approach:** (b), as the spec mandates explicit "Filtrovat"-driven submission.

**Rationale:** The component already half-implements this pattern for `transferIdFilter`/`accountFilter` (`ImportTab.tsx:27-28`). Generalising it to a single `committedFilters` object reduces dependency-array surface for React Query's `queryKey`, gives one obvious place to reset on "Vyčistit", and removes the now-misleading `await refetch()` calls inside the apply/clear handlers (they're no-ops once `queryKey` changes drive the refetch).

#### Decision 5: `errorsOnly` semantics anchored to the `ImportResult != "OK"` string predicate — no enum, no new column

**Options considered:**
- (a) Introduce an `ImportStatus` enum column.
- (b) Keep `ImportResult` as the source of truth; predicate is `ImportResult != "OK"`.

**Chosen approach:** (b).

**Rationale:** The spec is explicit about this, and three call sites already encode the same predicate: the UI badge (`ImportTab.tsx:205`), the AutoMapper profile (`BankMappingProfile.cs:12`), and the DTO's computed `ErrorType` property (`BankStatementImportDto.cs:13`). Adding a fourth instance in the repository keeps the semantics consistent and avoids a migration. (Worth a separate, out-of-scope note: the DTO computed property and the AutoMapper `ForMember` are redundant — both compute `ErrorType` from `ImportResult`. The AutoMapper line is effectively dead. Not in scope here; flagging for a future cleanup.)

## Implementation Guidance

### Directory / Module Structure

No new files on the backend. Edits in place:

- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs` — add five optional properties (classes, not records — already a class).
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs` — parse the two new date strings; pass the seven additional values into the repository call; trim `Account` before passing.
- `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs` — add length, parseability, and range-ordering rules.
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — extend the `GetFilteredAsync` signature with five new optional parameters (default `null`/`false`), preserving backward compatibility for any other caller (`BankStatementsController` `GetBankStatement(id)` keeps working unchanged).
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` — implement the new predicates; reuse a shared `LikeEscape` helper.
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — add five `[FromQuery]` parameters to `GetBankStatements` and pass them into the request DTO. Update the XML doc comments.
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs` — new test methods per filter and one combined case (but see Risk 1 below — InMemory provider blocker).
- New: `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs` — handler-level tests, including the `dateFrom > dateTo` 400 case (via the validator).

One small shared utility (recommended, not required):

- `backend/src/Anela.Heblo.Persistence/Shared/LikeEscape.cs` — extract `PackageRepository`'s private `EscapeLike` into a `public static class LikeEscape { public static string Escape(string s) … }`. This avoids duplicating the escape logic and consolidates the (currently private) implementation. Strictly optional; if rejected, copy the helper into `BankStatementImportRepository` and accept the duplication.

Frontend edits (in place):

- `frontend/src/api/hooks/useBankStatements.ts` — extend `GetBankStatementListRequest` and the `URLSearchParams` builder in `useBankStatementsList` with the five new fields. **Note:** this hook is hand-rolled (`apiClient.http.fetch`), not consumed from the generated NSwag client; regenerating the OpenAPI client will refresh the generated types but does not auto-update this hook. The spec's "regenerate the client" line should be read as "regenerate so generated types stay in sync; also manually extend the hand-rolled hook to pass the new params."
- `frontend/src/components/customer/tabs/ImportTab.tsx` — collapse the two-state-per-filter pattern into a single `committedFilters` object, pass it into `useBankStatementsList`, drop the now-unnecessary `await refetch()` inside `handleApplyFilters`/`handleClearFilters`, and add a `dateFrom > dateTo` inline error.
- No new frontend files. No new components.

### Interfaces and Contracts

**Repository (`IBankStatementImportRepository.GetFilteredAsync`) — new signature:**

```csharp
Task<(IEnumerable<BankStatementImport> Items, int TotalCount)> GetFilteredAsync(
    int? id = null,
    DateTime? statementDate = null,
    DateTime? importDate = null,
    string? transferId = null,
    string? account = null,
    DateTime? dateFrom = null,
    DateTime? dateTo = null,
    bool? errorsOnly = null,
    int skip = 0,
    int take = 50,
    string orderBy = "ImportDate",
    bool ascending = false,
    CancellationToken cancellationToken = default);
```

Adding `CancellationToken` is a small unrelated improvement but worth doing while the signature is already churning — every other repository in the codebase takes one (`PackageRepository`, `MeetingTranscriptRepository`, `IssuedInvoiceRepository`). Tests pass `CancellationToken.None`.

**Request DTO additions (`GetBankStatementListRequest`):**

```csharp
public string? TransferId { get; set; }
public string? Account { get; set; }
public string? DateFrom { get; set; }   // ISO date string, parsed via DateTime.TryParse
public string? DateTo { get; set; }     // ISO date string, parsed via DateTime.TryParse
public bool? ErrorsOnly { get; set; }
```

Class, not record — consistent with the existing DTO and project-wide OpenAPI rule (`CLAUDE.md`, Project-specific rules).

**Validator additions:**

```csharp
RuleFor(x => x.TransferId).MaximumLength(100);
RuleFor(x => x.Account).MaximumLength(100);
RuleFor(x => x.DateFrom).Must(BeParseableDateOrNull).WithMessage("DateFrom is not a valid date");
RuleFor(x => x.DateTo).Must(BeParseableDateOrNull).WithMessage("DateTo is not a valid date");
RuleFor(x => x).Must(HaveValidDateRange).WithMessage("DateFrom must be on or before DateTo");
```

`HaveValidDateRange` returns true if either side is null or unparseable (length/parseability rules handle those independently); otherwise it parses both and asserts `from <= to`.

**Frontend type additions (`GetBankStatementListRequest`):**

```typescript
transferId?: string;
account?: string;
dateFrom?: string;   // YYYY-MM-DD
dateTo?: string;     // YYYY-MM-DD
errorsOnly?: boolean;
```

### Data Flow

User edits inputs → component-local input state updates, no network call.
User clicks "Filtrovat" →
1. Client-side guard: if `dateFrom > dateTo`, surface inline error and return.
2. Setter writes a new `committedFilters` object and resets `pageNumber` to 1.
3. React Query sees a new `queryKey` (because the request object changes) and triggers a fresh fetch.
4. `useBankStatementsList` builds a URL with all non-empty/non-default params and calls `GET /api/bank-statements?...`.
5. Controller binds the seven new query-string params plus the existing seven into `GetBankStatementListRequest` and dispatches via MediatR.
6. MediatR `ValidationBehavior` runs `GetBankStatementListRequestValidator`; failure → HTTP 400.
7. `GetBankStatementListHandler` parses `DateFrom`/`DateTo` (guaranteed parseable by the validator), trims `Account`, calls `IBankStatementImportRepository.GetFilteredAsync(...)`.
8. Repository composes the queryable, runs one `CountAsync` for `TotalCount`, then `Skip/Take` for the page, returns `(items, totalCount)`.
9. Handler maps to DTOs via AutoMapper; returns `GetBankStatementListResponse`. `TotalCount` reflects the filtered count.
10. Controller returns 200; React Query caches against the new key; pagination footer updates and shows "(filtrováno)" indicator (the existing copy already handles this for transferId/account; extend the condition to include the other three filters).

User clicks "Vyčistit" → all input + committed state cleared, `pageNumber` reset, queryKey changes, unfiltered fetch fires.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Existing repo tests use `UseInMemoryDatabase`, which does not translate `EF.Functions.ILike` — new tests for `transferId`/`account` will throw `InvalidOperationException` at runtime.** This is a concrete blocker for FR-1, FR-2 test coverage as the spec describes it. | High | Two acceptable paths: (a) keep the InMemory tests for `id`, date, and `errorsOnly` filters (which use plain `==`/`>=`/`<`/`!=` and translate fine) and add a separate Testcontainers-PostgreSQL-backed test class for the `ILike` filters, following the pattern used by `MeetingTranscriptRepositorySearchIntegrationTests` and `KnowledgeBaseRepositoryIntegrationTests`; or (b) abstract the predicate construction behind a tiny method on the repository so it can be unit-tested by inspecting the resulting `IQueryable` expression tree without executing it. Option (a) is consistent with existing precedent in this repo; option (b) is faster but less faithful. Recommend (a). |
| **`TransferId` has a unique B-tree index but `LIKE '%substr%'` cannot use it.** Spec says "no proactive index work" — correct, but a worst-case full scan on this column is possible at scale. | Low | Accept as documented in spec NFR-1. Production data volume is small (one row per imported statement file). If a regression appears, add a PostgreSQL `gin_trgm_ops` index in a separate migration. |
| **Unescaped LIKE wildcards in user input.** A user typing `%`, `_`, or `\` would silently expand the match set or break under the `"\\"` escape clause unless the input is escaped. | Medium | Reuse `EscapeLike` from `PackageRepository` (extract to a shared helper or duplicate). Spec is silent on this; it should be implemented regardless. |
| **Backward compatibility for repository callers**: `BankStatementsController.GetBankStatement(int id)` calls `GetFilteredAsync` indirectly via a different MediatR request. | Low | All new repository parameters default to `null`/`false`; the single-record-by-id path is unchanged. Existing tests cover this. |
| **Bank module BaseResponse / generic 500 catch in controller swallows validator errors as 500 today.** Look at `GetBankStatements` action — it wraps the `_mediator.Send` in a try/catch that returns a generic 500. FluentValidation throws `ValidationException`; without a MediatR `ValidationBehavior` registered application-wide, validator failures become 500s, not 400s. | Medium | Verify a `ValidationBehavior` is registered (the codebase uses FluentValidation broadly; this is likely already wired in `ApplicationModule`). If yes, no action. If no, register one before relying on validator-driven 400s; alternatively, catch `ValidationException` explicitly in the controller and translate. Implementer must confirm before merging. |
| **The hand-rolled frontend hook diverges from the generated OpenAPI types.** Adding fields manually means a future regen could produce mismatched types without anyone noticing. | Low | Spec already requires the OpenAPI client regen; treat the hook field list as the single source of truth for the wire contract. A type assertion (`request satisfies GetBankStatementListRequestGenerated`) would catch drift but is not required by spec. |
| **`StatementDate` is stored as `timestamp without time zone` with UTC kind on writes** (`BankStatementImport` ctor forces UTC) **but the filter input is a naive date string from `<input type="date">`** (no timezone). | Low | The half-open `>= from && < to.AddDays(1)` approach in Decision 2 is robust here; both bounds are constructed as `DateTime` (Unspecified kind), and the comparison happens on the column value directly. Spec's day-granularity semantics hold. |

## Specification Amendments

These should be reflected back into `spec.r2.md` before implementation:

1. **FR-1 & §"API / Interface Design" backend table — replace "Case-insensitive `Contains`" with "Case-insensitive match via `EF.Functions.ILike(bs.TransferId, $"%{escaped}%", "\\")`, with `%` / `_` / `\` LIKE-escaped server-side."** The spec already says ILike for `Account` (FR-2); the same primitive must be used for `TransferId` to deliver the spec's own case-insensitivity guarantee on PostgreSQL.
2. **FR-2 — add the LIKE-escape requirement** ("`%`, `_`, and `\` in the user input are escaped before composition into the `ILike` pattern"). Currently the spec says `EF.Functions.ILike(bs.Account, $"%{trimmedAccount}%")` with no escape clause; this allows a user typing `100%` to match all rows. The codebase has a precedent: `PackageRepository.EscapeLike`.
3. **FR-3 date range — change wording from "inclusive `[from, to]` via `.Date <=`" to "inclusive on both ends, implemented as `StatementDate >= from.Date && StatementDate < to.Date.AddDays(1)`"** to align with the in-codebase `PackageRepository` convention and avoid per-row `.Date` projections in SQL. User-facing semantics are identical.
4. **NFR-4 (Testability) — acknowledge the InMemory provider limitation for `ILike`.** Either replace "the existing repository test patterns" with "Testcontainers-backed PostgreSQL integration tests for `ILike`-based filters (per `MeetingTranscriptRepositorySearchIntegrationTests`); InMemory tests retained for `id`, date, and `errorsOnly` filters" — or drop the strict repository-level coverage requirement for the string filters and rely on a handler-level test with a mocked repository plus one end-to-end Postgres integration test.
5. **§Frontend — clarify the OpenAPI client / hand-rolled hook split:** "Regenerate the OpenAPI client so generated types include the new fields. Note: `useBankStatementsList` is a hand-rolled hook (`apiClient.http.fetch`) and must be updated manually to forward the new fields; regeneration does not modify it."
6. **§Backend validation — name the validation seam:** "All four backend validations (date-range ordering, length caps, date parseability) are added to the existing `GetBankStatementListRequestValidator` (FluentValidation). Controller code is not modified beyond adding the five `[FromQuery]` parameters and forwarding them into the request DTO."
7. **§Out of Scope — add: "Deduplicating the `ErrorType` computation between `BankStatementImportDto` and `BankMappingProfile` (both compute `ImportResult != "OK"`; one is redundant)."** Flagged as a follow-up cleanup.

## Prerequisites

None blocking. Specifically:

- **No database migration** — every column the spec reads from already exists on `BankStatements`.
- **No new infrastructure, config, or KV secrets.**
- **No new external dependencies** — the codebase already uses `EF.Functions.ILike` (Npgsql), AutoMapper, FluentValidation, MediatR, and React Query.
- **No feature flag** — the change is backward-compatible at the API level (all new query parameters are optional and default to "no constraint").
- **Verify before merge:** that a global FluentValidation `ValidationBehavior` is registered with MediatR (so validator failures become HTTP 400, not HTTP 500 via the controller's generic catch). If absent, register it as part of this PR.
- **If choosing Testcontainers for the new ILike tests:** ensure the local dev environment can run Docker (CI already does — Testcontainers-based suites exist elsewhere in the repo).