Now I have enough context to write the architecture review.

# Architecture Review: Wire Up Bank Import Tab Filter Inputs

## Skip Design: true

## Architectural Fit Assessment

The feature fits squarely within the existing vertical-slice pattern for the Bank module. Every layer already has a slot for what this change needs:

- The MediatR query `GetBankStatementListRequest` is the established extension point for new optional filter parameters.
- The handler already uses the `string? → DateTime?` parse convention for `statementDate` / `importDate`, so the new `dateFrom` / `dateTo` parameters slot in identically.
- The repository's `GetFilteredAsync` already centralises database-level filter composition.
- A PostgreSQL `ILIKE` pattern with an `EscapeLike` helper is already in use in `Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs` — this is the canonical pattern to copy, not to invent from scratch.
- The frontend hook (`useBankStatements.ts`) builds query strings with `URLSearchParams`, so new optional fields drop in without restructuring.

Two real frictions exist:

1. **Repository signature smell.** `GetFilteredAsync` already has seven positional optional parameters. Adding three more (`transferId`, `account`, `errorsOnly`) pushes the signature past a readability threshold and makes call sites in `BankStatementsController.GetBankStatement(int id)` (which still uses the same handler/repo for single-item lookup) opaque.
2. **InMemory test database does not support `EF.Functions.ILike`.** The existing `BankStatementImportRepositoryTests` use `UseInMemoryDatabase`. Other modules (`MeetingTranscriptRepositorySearchIntegrationTests`, `IssuedInvoiceRepositoryTests`) handle this with one of two patterns: (a) cover ILike-driven filters via Testcontainers PostgreSQL integration tests, (b) leave the ILike path uncovered at the InMemory level and add an explanatory comment. We must pick one — the spec says "use existing repository test patterns" but does not name the pattern.

Both are addressable without leaving the established architecture.

## Proposed Architecture

### Component Overview

```
Frontend (React)
├── ImportTab.tsx
│     • Local state per input (transferIdInput, accountInput, dateFrom, dateTo, showOnlyErrors)
│     • Committed-filter state (object) → drives useBankStatementsList
│     • handleApplyFilters: validate (dateFrom <= dateTo) → setCommittedFilters → reset page
│     • handleClearFilters: empty committedFilters → reset page
│
└── useBankStatements.ts
      • GetBankStatementListRequest: + transferId, account, dateFrom, dateTo, errorsOnly
      • Hook serialises trimmed/non-empty values into URLSearchParams
      • Query key already includes the request object → cache keyed correctly
                              │
                              ▼ GET /api/bank-statements?...
Backend (.NET 8 + MediatR)
├── BankStatementsController.GetBankStatements
│     • [FromQuery] binding into GetBankStatementListRequest directly
│     • (Replaces 7 individual [FromQuery] parameters with model binding)
│
├── GetBankStatementListRequest (MediatR)
│     • + TransferId?: string, Account?: string, DateFrom?: string,
│       DateTo?: string, ErrorsOnly?: bool
│
├── GetBankStatementListRequestValidator (FluentValidation)
│     • + Length<=100 for TransferId, Account
│     • + Parseable date for DateFrom, DateTo
│     • + DateFrom <= DateTo when both provided
│
├── GetBankStatementListHandler
│     • Parses DateFrom/DateTo (mirrors existing StatementDate/ImportDate parsing)
│     • Builds BankStatementListFilter record (see below)
│     • Calls _repository.GetFilteredAsync(filter, paging, sorting)
│
└── BankStatementImportRepository.GetFilteredAsync
      • EF.Functions.ILike(bs.TransferId, $"%{escaped}%", "\\")
      • EF.Functions.ILike(bs.Account,    $"%{escaped}%", "\\")
      • bs.StatementDate.Date >= dateFrom.Value.Date
      • bs.StatementDate.Date <= dateTo.Value.Date
      • bs.ImportResult != ImportStatus.Success when errorsOnly==true
      • Existing AsNoTracking, Count-before-paging, deterministic ordering preserved
```

### Key Design Decisions

#### Decision 1: Repository signature — parameter object vs. more optional params
**Options considered:**
- (a) Append three more positional optional parameters to `GetFilteredAsync` (`transferId`, `account`, `errorsOnly`).
- (b) Introduce a `BankStatementListFilter` record that packages all filter criteria, leaving paging/sorting as separate parameters.
- (c) Replace the tuple return with a query-object pattern across the whole feature.

**Chosen approach:** (b). Add `BankStatementListFilter` (record) under `Anela.Heblo.Domain/Features/Bank/`:

```csharp
public sealed record BankStatementListFilter(
    int? Id = null,
    string? TransferId = null,
    string? Account = null,
    DateTime? StatementDate = null,
    DateTime? ImportDate = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    bool? ErrorsOnly = null);
```

New `GetFilteredAsync` becomes:
```csharp
Task<(IEnumerable<BankStatementImport> Items, int TotalCount)> GetFilteredAsync(
    BankStatementListFilter filter,
    int skip = 0,
    int take = 50,
    string orderBy = "ImportDate",
    bool ascending = false,
    CancellationToken cancellationToken = default);
```

**Rationale:** A 10-parameter method is a maintainability hazard for every future filter addition. The filter is a cohesive concept; packaging it is YAGNI-compatible because we are not building speculative generality — we are recognising the existing parameter list has already exceeded the threshold. This is a domain-layer record (not a DTO crossing the API boundary), so the project's "DTOs are classes, not records" rule does not apply.

One existing call site needs updating: `BankStatementsController.GetBankStatement(int id)` builds a `GetBankStatementListRequest { Id = id, Take = 1 }`. It does not need new filters — the handler creates the filter object from request fields and the rest stay null. Backward-compatible at the handler boundary.

#### Decision 2: ILike escaping
**Options considered:**
- (a) Inline `EF.Functions.ILike(..., $"%{value}%")` without escape — matches the simpler path used in `MeetingTranscriptRepository` / `IssuedInvoiceRepository`.
- (b) Replicate the `EscapeLike` helper from `PackageRepository` to neutralise `%`, `_`, `\` in user input.

**Chosen approach:** (b). Copy the `EscapeLike` helper (`PackageRepository.cs:75`) as a `private static` method on `BankStatementImportRepository`. Pass the escape char `"\\"` argument to `EF.Functions.ILike` so the wildcards in user input are matched literally.

**Rationale:** Without escaping, a user typing `%` or `_` in the Account or Transfer ID input will produce wildcard matches — a correctness defect, not a security issue. The cost is one helper method that already exists elsewhere. Two callers in the codebase do this inconsistently today; we adopt the safer of the two patterns. Do not extract a shared helper across modules in this PR — that is a separate refactor.

#### Decision 3: Errors-only predicate placement
**Options considered:**
- (a) Inline string literal: `bs.ImportResult != "OK"`.
- (b) Reference the existing `ImportStatus.Success` constant from `Anela.Heblo.Domain/Features/Bank/ImportStatus.cs`.

**Chosen approach:** (b). Use `ImportStatus.Success` in the repository `Where` clause. `BankMappingProfile.cs:12` and `BankStatementImportDto.cs:13` currently inline the `"OK"` literal — that drift is a separate cleanup item; do not touch it in this PR (per "surgical changes").

**Rationale:** Keeps the new code aligned with the named constant that already exists for this exact purpose. EF will translate the constant to a parameterised SQL value identically.

#### Decision 4: Controller binding
**Options considered:**
- (a) Add three more individual `[FromQuery]` parameters to `BankStatementsController.GetBankStatements`.
- (b) Switch the action signature to `GetBankStatements([FromQuery] GetBankStatementListRequest request)` so the framework binds query string directly into the MediatR request.

**Chosen approach:** (a). Keep individual `[FromQuery]` parameters; add the five new ones. The current explicit-parameter style preserves XML doc clarity and OpenAPI parameter descriptions per field, and matches the surrounding code's level of ceremony. Switching to model binding is a refactor that touches an already-working surface for no behavioural gain — out of scope.

**Rationale:** "Surgical changes" rule. The signature growth is one-off; we do not need to refactor the binding shape today.

#### Decision 5: Repository test coverage of ILike filters
**Options considered:**
- (a) Add Testcontainers PostgreSQL integration tests for the `transferId` / `account` paths (mirrors `MeetingTranscriptRepositorySearchIntegrationTests`).
- (b) Cover what InMemory supports (`errorsOnly`, date range, combined non-ILike) at unit level; add a single `[Trait("Category","Integration")]` Testcontainers test that asserts `transferId` and `account` ILike behaviour and the escape-char handling against a real Postgres.
- (c) Skip backend test coverage of the ILike paths entirely with an explanatory comment (the pattern used in `IssuedInvoiceRepositoryTests`).

**Chosen approach:** (b). Unit-test what InMemory can express; add one focused integration test class for the ILike substring filters and one assertion that `%` and `_` are escaped literally.

**Rationale:** The spec's NFR-4 requires per-filter coverage. ILike behaviour is the highest-risk path (escaping, case-insensitivity, dialect-specific). Pattern (a) is overkill for the other filters; pattern (c) leaves a real correctness path uncovered. The Testcontainers harness is already established in the test project — adding one class is cheap.

#### Decision 6: Frontend commit-on-apply pattern
**Options considered:**
- (a) Pass live input state directly into `useBankStatementsList` (re-fetch on every keystroke).
- (b) Introduce a "committed filters" state object that the hook depends on; "Filtrovat" copies inputs → committed; "Vyčistit" clears both.

**Chosen approach:** (b), as the spec prescribes. React Query keys the cache on the request object, so making the request object change only on commit is what makes "Filtrovat" meaningful and prevents per-keystroke refetches.

**Rationale:** Matches FR-6 acceptance criteria exactly and stops the current `refetch()` no-op pattern. The `transferIdFilter` / `accountFilter` state variables that exist today become the committed-filter object (consolidated, not duplicated).

## Implementation Guidance

### Directory / Module Structure

No new directories. Files touched:

**Backend:**
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementListFilter.cs` *(new)* — filter record.
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — change `GetFilteredAsync` signature.
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` — new filter clauses, `EscapeLike` helper, `CancellationToken` plumbed through.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs` — five new optional properties.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs` — parse `DateFrom`/`DateTo`, build filter, pass through.
- `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs` — length, date parse, date range rules.
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — five new `[FromQuery]` parameters on the list action; XML docs updated.
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs` — unit-test additions for `errorsOnly`, date range, combined non-ILike.
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryIntegrationTests.cs` *(new)* — Testcontainers test for ILike + escape on `TransferId` and `Account`.
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs` *(new — if not present)* — handler-level test covering 400 on `DateFrom > DateTo` and length rejection.

**Frontend:**
- `frontend/src/api/hooks/useBankStatements.ts` — extend `GetBankStatementListRequest`; trim and omit empty strings when serialising.
- `frontend/src/components/customer/tabs/ImportTab.tsx` — collapse `transferIdFilter` / `accountFilter` into a single `committedFilters` object including all five fields; client-side `dateFrom <= dateTo` guard with inline error; remove `refetch()` post-state-set anti-pattern.
- `frontend/src/components/customer/tabs/__tests__/ImportTab.test.tsx` *(new or extended)* — verify hook is called with expected payload on apply/clear, and that the validation guard blocks submission when `dateFrom > dateTo`.

The generated client at `frontend/src/api/generated/api-client.ts` regenerates from the OpenAPI definition on build — no manual edits.

### Interfaces and Contracts

**Domain (new):**
```csharp
public sealed record BankStatementListFilter(
    int? Id = null,
    string? TransferId = null,
    string? Account = null,
    DateTime? StatementDate = null,
    DateTime? ImportDate = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    bool? ErrorsOnly = null);
```

**Domain (changed):**
```csharp
public interface IBankStatementImportRepository
{
    Task<(IEnumerable<BankStatementImport> Items, int TotalCount)> GetFilteredAsync(
        BankStatementListFilter filter,
        int skip = 0,
        int take = 50,
        string orderBy = "ImportDate",
        bool ascending = false,
        CancellationToken cancellationToken = default);

    Task<BankStatementImport?> GetByIdAsync(int id);
    Task<BankStatementImport> AddAsync(BankStatementImport bankStatement);
}
```

**Application (changed):**
```csharp
public class GetBankStatementListRequest : IRequest<GetBankStatementListResponse>
{
    public int? Id { get; set; }
    public string? TransferId { get; set; }
    public string? Account { get; set; }
    public string? StatementDate { get; set; }
    public string? ImportDate { get; set; }
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public bool? ErrorsOnly { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 10;
    public string? OrderBy { get; set; } = "ImportDate";
    public bool Ascending { get; set; } = false;
}
```

DTO remains a class (project rule). Domain `BankStatementListFilter` is a record because it does not cross the OpenAPI generator.

**Frontend (changed):**
```typescript
export interface GetBankStatementListRequest {
  id?: number;
  transferId?: string;
  account?: string;
  statementDate?: string;
  importDate?: string;
  dateFrom?: string;   // ISO date 'YYYY-MM-DD'
  dateTo?: string;     // ISO date 'YYYY-MM-DD'
  errorsOnly?: boolean;
  skip?: number;
  take?: number;
  orderBy?: string;
  ascending?: boolean;
}
```

### Data Flow

**Apply filters happy path:**

1. User types into inputs → local `useState` only.
2. User clicks "Filtrovat" → `handleApplyFilters`:
   - Trims `transferIdInput`, `accountInput`; converts empty strings to `undefined`.
   - Validates `dateFrom <= dateTo`; if invalid, sets inline error state and returns (no network).
   - Builds `committedFilters` object and `setCommittedFilters(next)`.
   - `setPageNumber(1)`.
3. `useBankStatementsList` re-keys on the new request object → React Query fires.
4. Hook serialises only defined fields into `URLSearchParams`.
5. `GET /api/bank-statements?transferId=...&account=...&dateFrom=...&dateTo=...&errorsOnly=true&skip=0&take=20&orderBy=ImportDate&ascending=false` reaches the controller.
6. `[FromQuery]` binding populates `GetBankStatementListRequest`. FluentValidation runs (assuming the existing pipeline behaviour is in place; if not — see Prerequisites).
7. Handler parses `DateFrom`/`DateTo` via `DateTime.TryParse` (mirrors existing `StatementDate`/`ImportDate` parsing); builds `BankStatementListFilter`; trims `Account` and `TransferId` defensively.
8. Repository composes EF query: `IQueryable<BankStatementImport>` → optional `Where` clauses → `CountAsync` → ordering → pagination → `ToListAsync`.
9. Response shape unchanged (`Items`, `TotalCount`). DTO mapping via existing `BankMappingProfile`.

**Clear filters path:** `handleClearFilters` sets `committedFilters` to an empty object and `pageNumber` to 1 — the React Query key change triggers an unfiltered fetch. No explicit `refetch()` call needed.

**400 paths (defence-in-depth):** `DateFrom > DateTo`, `transferId.Length > 100`, `account.Length > 100`, or unparseable date strings produce FluentValidation failures → MediatR pipeline returns 400. Frontend surfaces the existing error UI.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `EF.Functions.ILike` unsupported by InMemory provider, breaking existing test suite | High | Add `[Trait("Category","Integration")]` Testcontainers-backed tests for ILike paths; keep existing InMemory unit tests for non-ILike filters and confirm they still pass after the signature change. |
| Wildcard injection (user types `%` or `_` and gets surprising matches) | Medium | Use the `EscapeLike` helper pattern from `PackageRepository`; pass `"\\"` as the escape character to `EF.Functions.ILike`. |
| Repository signature change ripples to `BankStatementsController.GetBankStatement(int id)` and to existing call sites in tests | Medium | The new filter record has all-optional defaults; existing callers pass `new BankStatementListFilter(Id: id)` (or pass a defaulted instance) — one-line changes per call site. |
| Performance regression on `ILIKE '%...%'` for `TransferId` and `StatementDate` range scans on production-sized data | Medium | NFR-1 sets a target. The unique index `IX_BankStatements_TransferId` does not help substring scans, but the table is bounded by import frequency (batches, not high-volume). If a regression is measured, ship a follow-up index migration as the spec already permits. |
| `Account` filter against the configured-name column might confuse users who expect IBAN matching | Low | Spec explicitly clarifies this matches the configured account name (e.g. `"ShoptetPay-CZK"`), matching the "Účet" column. No code change — preserve existing column header semantics. |
| Empty-state copy ("Žádné bankovní výpisy nebyly nalezeny") misleads when emptiness is filter-induced | Low | Out of scope per spec; track separately. The copy is already neutral enough ("not found"), so risk is minor. |
| Frontend "(filtrováno)" indicator only reads `transferIdFilter || accountFilter` (`ImportTab.tsx:484`) | Low | Update the condition to consider all five committed filters so the indicator is honest when only date range or errorsOnly is active. Surgical change inside the same component. |

## Specification Amendments

1. **Add domain record `BankStatementListFilter`** to the spec's API/Interface section. The spec proposes extending the repository signature with five more parameters; the architecture instead introduces a filter record to keep the signature maintainable. The wire shape, handler behaviour, and acceptance criteria are unchanged.

2. **Pass `CancellationToken` through `GetFilteredAsync`.** The current signature does not accept one. The spec is silent on this. Adding it is consistent with C# async conventions in the rules file. Optional with a default value preserves call-site compatibility.

3. **Use `ImportStatus.Success` constant for the errors-only predicate**, not a `"OK"` string literal. The spec mentions this in passing ("reuses the exact predicate") but does not explicitly require the named constant. Make it explicit.

4. **Confirm test pattern.** The spec says "use the existing repository test patterns" but the existing pattern is InMemory, which cannot exercise ILike. Amend NFR-4 to specify: unit tests with InMemory for non-ILike filters; one Testcontainers integration test for ILike substring matching and escape-char behaviour on `TransferId` and `Account`.

5. **`EscapeLike` helper** must be applied to `transferId` and `account` substring inputs (escaping `%`, `_`, `\` before interpolation into the `LIKE` pattern, and passing `"\\"` as the escape char). The spec calls for `EF.Functions.ILike(bs.Account, $"%{trimmedAccount}%")` but does not mention wildcard escaping. Add the escaping requirement.

6. **Honest "(filtrováno)" indicator.** The existing pagination footer (`ImportTab.tsx:484`) checks only `transferIdFilter || accountFilter`. Extend the condition to all five committed-filter fields so it reflects reality after this change.

## Prerequisites

- **None infrastructural.** No migrations, no new packages, no config changes, no Key Vault entries, no environment variables.
- **OpenAPI client regenerates on build** — ensure the build pipeline that runs `npm run build` regenerates `frontend/src/api/generated/api-client.ts` after the controller signature changes. Per `docs/development/api-client-generation.md`, this is automatic on backend build → it does not require a manual step in this PR.
- **Verify FluentValidation pipeline is active for MediatR requests.** The existing `GetBankStatementListRequestValidator` is registered, but if validation is not currently wired into the MediatR pipeline (e.g. through a `ValidationBehavior<TRequest,TResponse>`), the new validation rules will not run — confirm before relying on them for 400 responses. If absent, either add the behaviour or move validation guards into the handler. (Quick read of `BankModule.cs` and `ApplicationModule.cs` will confirm; do this in the first implementation step.)
- **Docker available for Testcontainers integration tests.** The project already exercises this elsewhere; CI must continue to provide Docker for the new integration test class.