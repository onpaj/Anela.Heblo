## Goal (from the overall plan)

Retype the four date filter fields on `GetBankStatementListRequest` (and the matching `BankStatementsController` query parameters) from `string?` to `DateTime?`, deleting the now-redundant `DateTime.TryParse` logic duplicated across the handler and validator, and adapting the frontend client/hook to match.

**Architecture:** Parsing/rejection of malformed date input moves from the MediatR pipeline (validator) to the ASP.NET Core model-binding stage, which already sits before mediator dispatch for every other `[FromQuery] DateTime?` endpoint in this codebase (`LogisticsController`, `CatalogController`, `InvoiceClassificationController`, etc.). `BankStatementListFilter` (domain filter) is already `DateTime?`-typed, so the handler's `ParseDateOrNull` helper and the validator's `BeParseableDate` rule become pure boilerplate once the DTO carries typed values — both are deleted. The one retained validation rule (`DateFrom` not later than `DateTo`) is rewritten as a direct `DateTime` comparison.

## Ground truth (read before starting)

Current state of every file this overall change touches:

- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs` — 4 fields are `string?`.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs` — has a private `ParseDateOrNull(string?)` helper called 4 times in `Handle`.
- `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs` — has `BeParseableDate(string?)`, two `.Must(BeParseableDate)` rules, and `DateFromIsNotLaterThanDateTo(GetBankStatementListRequest)` which re-parses strings.
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — `GetBankStatements` action has 4 `[FromQuery] string? = null` params (`statementDate`, `importDate`, `dateFrom`, `dateTo`).
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementListFilter.cs` — already `DateTime?` for these 4 fields. **No change needed.**

**Compilation-order note:** `GetBankStatementListRequest.cs`, `GetBankStatementListHandler.cs`, `GetBankStatementListRequestValidator.cs`, and `BankStatementsController.cs` are mutually coupled by the retype — the `Application`/`API` projects will **not** compile until all three of the retype/handler/validator tasks are done. This task is the first of those three. This is expected — do not attempt `dotnet build` until told to in a later step/task.

This task is task 1 of 5 in the overall plan. The others (in order): `remove-handler-date-parsing`, `simplify-validator-date-rules`, `update-backend-unit-tests`, `regenerate-client-and-update-frontend-hook`.

---

### task: retype-request-and-controller-date-fields

**Files:** `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs`, `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`

**Step 1 — retype the DTO.**

Open `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs`. Current content:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

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

Replace the four date property declarations so the file becomes:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

public class GetBankStatementListRequest : IRequest<GetBankStatementListResponse>
{
    public int? Id { get; set; }
    public string? TransferId { get; set; }
    public string? Account { get; set; }
    public DateTime? StatementDate { get; set; }
    public DateTime? ImportDate { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool? ErrorsOnly { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 10;
    public string? OrderBy { get; set; } = "ImportDate";
    public bool Ascending { get; set; } = false;
}
```

The class stays a plain `class` (not a record) — do not change that.

**Step 2 — retype the controller's query parameters.**

Open `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`. Find the `GetBankStatements` action signature:

```csharp
    [HttpGet]
    public async Task<ActionResult<GetBankStatementListResponse>> GetBankStatements(
        [FromQuery] int? id = null,
        [FromQuery] string? transferId = null,
        [FromQuery] string? account = null,
        [FromQuery] string? statementDate = null,
        [FromQuery] string? importDate = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null,
        [FromQuery] bool? errorsOnly = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10,
        [FromQuery] string? orderBy = "ImportDate",
        [FromQuery] bool ascending = false)
```

Replace the four date parameters' type only (leave everything else — including parameter names, defaults, and the `id`/`transferId`/`account`/`errorsOnly`/`skip`/`take`/`orderBy`/`ascending` parameters — unchanged):

```csharp
    [HttpGet]
    public async Task<ActionResult<GetBankStatementListResponse>> GetBankStatements(
        [FromQuery] int? id = null,
        [FromQuery] string? transferId = null,
        [FromQuery] string? account = null,
        [FromQuery] DateTime? statementDate = null,
        [FromQuery] DateTime? importDate = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] bool? errorsOnly = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10,
        [FromQuery] string? orderBy = "ImportDate",
        [FromQuery] bool ascending = false)
```

The method body (constructing `GetBankStatementListRequest` from these locals) needs no change — it already assigns `StatementDate = statementDate`, etc., positionally by name, and those now match types on both sides:

```csharp
        var request = new GetBankStatementListRequest
        {
            Id = id,
            TransferId = transferId,
            Account = account,
            StatementDate = statementDate,
            ImportDate = importDate,
            DateFrom = dateFrom,
            DateTo = dateTo,
            ErrorsOnly = errorsOnly,
            Skip = skip,
            Take = take,
            OrderBy = orderBy,
            Ascending = ascending
        };
```

Leave the XML doc comments above the action (`/// <param name="statementDate">...`) exactly as they are — they already describe these as dates and remain accurate.

**Step 3 — expected build state.**

Do **not** attempt `dotnet build` yet. `GetBankStatementListHandler.cs` still calls `ParseDateOrNull(request.StatementDate)` where `ParseDateOrNull` expects `string?` — this will now fail to compile with `CS1503` (cannot convert `DateTime?` to `string?`). That is expected; the next task fixes it. Do not attempt to work around it in this task.

**Step 4 — commit.**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs
git commit -m "Retype GetBankStatementListRequest date fields and controller params to DateTime?"
```
