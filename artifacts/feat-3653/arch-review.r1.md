# Architecture Review: Type `GetBankStatementListRequest` date filters as `DateTime?`

## Skip Design: true

## Architectural Fit Assessment
This is a textbook "push parsing to the HTTP boundary" refactor, and it fits the codebase's existing conventions exactly rather than introducing a new pattern:

- **`[FromQuery] DateTime?` is already the dominant pattern in this API**, not a novel idea being introduced by this change. `LogisticsController` (`fromDate`/`toDate`, two actions), `SmartsuppWebhookAuditController` (`from`/`to`), `CatalogController` (`asOfDate`), `InvoiceClassificationController` (`fromDate`/`toDate`), and `StockUpOperationsController` (`createdFrom`/`createdTo`) all already bind optional date query parameters straight to `DateTime?`. `BankStatementsController.GetBankStatements` (`string? statementDate/importDate/dateFrom/dateTo`) and `ImportStatements` (`BankImportRequestDto` with typed `DateTime DateFrom/DateTo`, already bound from JSON body) are the outliers — the list endpoint is the last `string?`-typed date filter left in `Bank`. This change brings it into line, it does not set a new precedent.
- **Culture safety is already handled globally.** `Program.cs` sets `CultureInfo.DefaultThreadCurrentCulture` / `DefaultThreadCurrentUICulture` to `InvariantCulture` at startup, so ASP.NET Core's default `DateTime?` query-string binder parses invariant/ISO-8601-style strings (`2026-01-01`) consistently regardless of server locale. There is no `UseRequestLocalization` middleware overriding this per-request. This removes what would otherwise be the main risk of moving parsing into the framework.
- **`BankStatementListFilter` (domain filter) is already `DateTime?`.** The handler's `ParseDateOrNull` exists solely to bridge `string?` (request) → `DateTime?` (filter) — it is boilerplate with no business meaning, confirmed by reading `GetBankStatementListHandler.cs` lines 29–32/66–67 and `BankStatementListFilter.cs`.
- **The DTO stays a class**, per `docs/architecture/development_guidelines.md` / `docs/development/api-client-generation.md` ("DTOs are classes, never records — NSwag mishandles record parameter order"). Only property *types* change; `GetBankStatementListRequest` is already declared as `public class`, so this rule is not at risk of being violated by this change, but it's worth stating explicitly as a guardrail since a less careful implementation might be tempted to "clean up" the DTO into a record while touching it. **Don't.**
- **Frontend precedent for the `string` → `Date` conversion is real and immediately adjacent**, not hypothetical. In the same file (`frontend/src/api/hooks/useBankStatements.ts`), `useBankStatementImport` already does exactly this: `new Date(request.dateFrom)` / `new Date(request.dateTo)` when constructing `BankImportRequestDto`, whose generated fields are already typed `Date` (confirmed at `api-client.ts:16747` `BankImportRequestDto`, and `analytics_GetBankStatementImportStatistics` at line 336 already takes `Date | null | undefined`). `useBankStatementsList` (the function this spec touches) is the odd one out, still passing raw strings positionally into `apiClient.bankStatements_GetBankStatements(...)` (`api-client.ts:1627`, currently `string | null | undefined` for the four date params). The fix is mechanical, not exploratory.
- **`GetBankStatementListRequest.cs` and its validator live under `Application/Features/Bank/UseCases/GetBankStatementList/` and `Application/Features/Bank/Validators/`**, not `Application/Features/Bank/Contracts/` as the "File Organization" example in `development_guidelines.md` illustrates. This is a pre-existing structural inconsistency in the `Bank` module (also true of `GetBankAccounts`, `GetBankStatementById`, `ImportBankStatement` — the whole module uses a `UseCases/{UseCase}/` layout, not a flat `Contracts/`+`Application/` split). It is **out of scope** for this change; do not restructure folders while touching dates. Noted here only so the developer doesn't "fix" it as a drive-by.

No module-boundary, DI, or persistence concerns apply — this is entirely inside the `Bank` module's existing vertical slice (Domain filter already typed correctly; only Application (request/handler/validator), API (controller), and the generated/consumed frontend client change).

## Proposed Architecture

### Component Overview
```
┌─────────────────────┐   raw query string    ┌──────────────────────────┐
│  Browser / ImportTab │ ─────────────────────▶│ ASP.NET Core model binder│
│  (still sends        │  ?dateFrom=2026-01-01 │ (DateTime? conversion +  │
│   YYYY-MM-DD strings)│                        │  400 on invalid syntax)  │
└─────────────────────┘                        └────────────┬─────────────┘
                                                              │ DateTime? (typed)
                                                              ▼
                                                 ┌──────────────────────────┐
                                                 │ BankStatementsController │
                                                 │ .GetBankStatements(...)  │
                                                 │ [FromQuery] DateTime?    │
                                                 └────────────┬─────────────┘
                                                              │ constructs
                                                              ▼
                                                 ┌──────────────────────────┐
                                                 │ GetBankStatementListReq  │
                                                 │ (DateTime? x4, class)    │
                                                 └────────────┬─────────────┘
                                            FluentValidation   │  MediatR
                                       (typed comparison only) ▼
                                                 ┌──────────────────────────┐
                                                 │ GetBankStatementListHndlr│
                                                 │ (no parsing — passes     │
                                                 │  request.* straight thru)│
                                                 └────────────┬─────────────┘
                                                              ▼
                                                 ┌──────────────────────────┐
                                                 │ BankStatementListFilter  │
                                                 │ (already DateTime?, no   │
                                                 │  change)                 │
                                                 └──────────────────────────┘
```
The only genuinely new architectural line drawn here is: **parsing/rejection of malformed date input moves from the MediatR pipeline (validator) to the ASP.NET Core model-binding stage**, which now sits strictly *before* the mediator dispatch. Everything downstream of the controller becomes parsing-free.

### Key Design Decisions

#### Decision 1: Where does string→DateTime conversion happen?
**Options considered:**
1. Keep `string?` on the DTO/controller, parse in the handler (status quo).
2. Keep `string?` on the DTO/controller, parse in the validator only, pass parsed value via a separate field (still duplicated: validator parses to check, handler parses again to use).
3. Retype DTO + controller params to `DateTime?`, let ASP.NET Core's model binder parse/reject at the boundary.

**Chosen approach:** Option 3, as proposed by the spec.

**Rationale:** Parsing a raw string into a typed value is HTTP-transport concern, not business logic — it belongs at the model-binding boundary, consistent with every other date-filtered `GET` endpoint already in this codebase (`Logistics`, `Catalog`, `InvoiceClassification`, `StockUpOperations`, `SmartsuppWebhookAudit`). Option 3 eliminates all three existing `DateTime.TryParse` call sites (validator's `BeParseableDate`, validator's `DateFromIsNotLaterThanDateTo`, handler's `ParseDateOrNull`) down to zero, and it's the only option that removes duplication rather than relocating it.

#### Decision 2: Validator's cross-field rule after retyping
**Options considered:**
1. Drop `DateFromIsNotLaterThanDateTo` entirely (ASP.NET Core model binding can't express cross-field rules, but this rule doesn't need parsing — it needs comparison, which is legitimate business/query-shape validation).
2. Keep it, rewritten as a direct `DateTime` comparison with null-guards.

**Chosen approach:** Option 2 (matches spec FR-4).

**Rationale:** "DateFrom must not be later than DateTo" is a validity rule about the *combination* of two already-typed values — that's exactly what FluentValidation is for, and it's meaningfully different from "is this string shaped like a date" (which model binding now subsumes). Keep this one rule; delete only the parsing-only rules (`BeParseableDate` usages).

#### Decision 3: Frontend hook boundary — where does `string` → `Date` conversion happen for the list endpoint?
**Options considered:**
1. Change `useBankStatementsList`'s public `GetBankStatementListRequest` interface to accept `Date` directly, pushing the conversion up into `ImportTab.tsx`.
2. Keep the hook's public interface accepting `string` (as today), convert to `Date` internally immediately before calling the generated client — mirroring `useBankStatementImport`.

**Chosen approach:** Option 2 (matches spec FR-5).

**Rationale:** `ImportTab.tsx` sources these values from native `<input type="date">` elements, which yield `YYYY-MM-DD` strings — converting to `Date` in the component would be pure churn with no benefit and would touch UI code outside this refactor's scope (out of scope per spec, and per this repo's "surgical changes" convention). `useBankStatementImport` already established the "hook receives string, hook constructs `Date` right before calling the generated client" pattern in this exact file — mirror it, don't invent a second pattern next to it.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Every file touched already exists; this is a pure retype-and-simplify change confined to:

- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs` — retype 4 fields.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs` — delete `ParseDateOrNull` and its 4 call sites.
- `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs` — delete `BeParseableDate`, the two `.Must(BeParseableDate)` rules, and rewrite `DateFromIsNotLaterThanDateTo`.
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — retype the 4 `[FromQuery]` params (lines 82–85).
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs` — update literals, delete now-unreachable tests.
- `frontend/src/api/generated/api-client.ts` — regenerated, not hand-edited.
- `frontend/src/api/hooks/useBankStatements.ts` — adapt `useBankStatementsList`'s call into `apiClient.bankStatements_GetBankStatements(...)`.

Do **not** touch `BankStatementListFilter.cs` (already correct), `ImportTab.tsx` beyond what's forced by type-checking (none expected, since the hook's public string-based interface is preserved), or the `Bank/UseCases/` vs `Bank/Contracts/` folder layout.

### Interfaces and Contracts
- `GetBankStatementListRequest` (class, unchanged shape otherwise):
  ```csharp
  public DateTime? StatementDate { get; set; }
  public DateTime? ImportDate { get; set; }
  public DateTime? DateFrom { get; set; }
  public DateTime? DateTo { get; set; }
  ```
- `BankStatementsController.GetBankStatements` signature: the 4 params become `[FromQuery] DateTime? {name} = null`. No route change, no other param changes. Keep `[ApiController]` on the controller (already present) — it's what turns model-binding failures into an automatic `400 ValidationProblemDetails` with no extra code.
- Validator: retain exactly one date-related rule —
  ```csharp
  RuleFor(x => x.DateFrom)
      .Must((req, dateFrom) => !dateFrom.HasValue || !req.DateTo.HasValue || dateFrom.Value.Date <= req.DateTo.Value.Date)
      .WithMessage("DateFrom must not be later than DateTo");
  ```
  (Exact expression is implementation's call; the acceptance criterion is: no `string`, no `DateTime.TryParse`, single rule attached to `DateFrom`, true when either side is null.)
- Handler: `BankStatementListFilter` constructor call takes `request.StatementDate`, `request.ImportDate`, `request.DateFrom`, `request.DateTo` directly — no local `DateTime?` intermediates for dates.
- Frontend `GetBankStatementListRequest` TS interface in `useBankStatements.ts` **keeps** `statementDate?: string; importDate?: string; dateFrom?: string; dateTo?: string;` — this is the hook's *public* contract to `ImportTab.tsx` and must not change. Internally, convert with the same `?? undefined` guard style already used for the other fields, e.g. `request?.dateFrom ? new Date(request.dateFrom) : undefined`.

### Data Flow
1. `ImportTab.tsx` reads `<input type="date">` values as `YYYY-MM-DD` strings (unchanged) and calls `useBankStatementsList({ dateFrom, dateTo, ... })` (unchanged call site).
2. `useBankStatementsList` converts each present string to a `Date` object immediately before invoking `apiClient.bankStatements_GetBankStatements(...)` (new — this is the only frontend behavior change).
3. The generated client serializes `Date` → ISO query string; the browser sends `?dateFrom=2026-01-01...`.
4. ASP.NET Core's `[ApiController]` model binder parses the query string into `DateTime?` under `InvariantCulture` (guaranteed by `Program.cs`'s thread-culture setup). A syntactically invalid value short-circuits to `400 ValidationProblemDetails` **before** `MediatR.Send` is called — the validator and handler never run.
5. `BankStatementsController.GetBankStatements` builds `GetBankStatementListRequest` with the already-typed `DateTime?` values (unchanged construction pattern).
6. FluentValidation's pipeline behavior runs the simplified validator — only the cross-field `DateFrom <= DateTo` rule can now fail.
7. `GetBankStatementListHandler.Handle` passes the typed dates straight into `BankStatementListFilter`, which flows to `IBankStatementImportRepository.GetFilteredAsync` unchanged (EF Core query already assumes `DateTime?`).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Regenerated `api-client.ts` changes `bankStatements_GetBankStatements`'s date params to `Date \| null \| undefined`, silently breaking any *other* caller of this generated method besides `useBankStatements.ts` | Low | Grep for `bankStatements_GetBankStatements(` across `frontend/src` after regeneration to confirm `useBankStatementsList` is the sole call site (spec's own investigation found only this one; verify it still holds at implementation time). |
| A developer "fixes" the `Bank/UseCases/` vs `Bank/Contracts/` folder mismatch while touching these files, turning a 6-file diff into a large, unrelated restructuring | Low | Explicitly out of scope (see Architectural Fit Assessment); call out in PR description if noticed, don't act on it. |
| `[ApiController]`'s automatic 400 on bad model binding is silently disabled somewhere (custom `InvalidModelStateResponseFactory`, `SuppressModelStateInvalidFilter = true`) making FR-2's "reject before handler" guarantee false | Low | Verify `BaseApiController`/`Program.cs` don't set `ApiBehaviorOptions.SuppressModelStateInvalidFilter = true` before relying on this; if found, FR-2's acceptance criterion needs an explicit check instead of relying on default behavior. Not found during this review's exploration, but wasn't exhaustively grepped for `ApiBehaviorOptions`. |
| Test file's `Handle_IgnoresUnparseableDateStrings` / `Validate_RejectsUnparseableDate{From,To}` deletions reduce coverage of "what happens on bad input" to zero at the unit level, since that behavior moves to ASP.NET Core's own (unit-untested-by-us) model binder | Low | Spec's FR-6 already flags an optional controller/integration test for the 400 path as "recommended, not blocking" — treat it as effectively required in spirit if time permits, since it's the only place this behavior is still exercised by this codebase's own tests. |

## Specification Amendments
None required. The spec (FR-1 through FR-6, NFR-1 through NFR-3) is architecturally sound, matches an established pattern already used five times elsewhere in this API (`LogisticsController` x2, `SmartsuppWebhookAuditController`, `CatalogController`, `InvoiceClassificationController`, `StockUpOperationsController`), correctly identifies the one real precedent for the frontend conversion (`useBankStatementImport`'s `new Date(...)` pattern in the same file), and correctly scopes what must NOT change (`BankStatementListFilter`, `ImportTab.tsx` UX, `Id`/`TransferId`/`Account`/etc.). One clarification worth folding into implementation (not a spec defect, just tightening FR-6's "optional" test):

- Promote FR-6's "optional... controller-level or integration test asserting 400 on invalid date query string" from optional to **do it if the repo has any existing controller-level test for another `[FromQuery] DateTime?` endpoint to pattern-match against** (check `LogisticsController`'s or `CatalogController`'s test coverage for a precedent first); if no such precedent test exists anywhere in the suite, it's reasonable to leave it as documented-gap per the spec's own fallback.

## Prerequisites
None. No migrations, no config, no new packages — `ASP.NET Core`'s built-in `DateTime?` query binder and the existing NSwag/OpenAPI regeneration pipeline (`docs/development/api-client-generation.md`) are already in place and already exercise this exact type mapping (`Date | null | undefined`) for `BankImportRequestDto` and `analytics_GetBankStatementImportStatistics`. Implementation can start immediately; regenerate the frontend client (`npm run generate-client` or `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual`) as the step that turns the backend signature change into a frontend compile error, then fix `useBankStatements.ts` to match.
