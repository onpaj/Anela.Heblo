# GetBankStatementListRequest Date Field Retype — Implementation Plan

**Goal:** Retype the four date filter fields on `GetBankStatementListRequest` (and the matching `BankStatementsController` query parameters) from `string?` to `DateTime?`, deleting the now-redundant `DateTime.TryParse` logic duplicated across the handler and validator, and adapting the frontend client/hook to match.

**Architecture:** Parsing/rejection of malformed date input moves from the MediatR pipeline (validator) to the ASP.NET Core model-binding stage, which already sits before mediator dispatch for every other `[FromQuery] DateTime?` endpoint in this codebase (`LogisticsController`, `CatalogController`, `InvoiceClassificationController`, etc.). `BankStatementListFilter` (domain filter) is already `DateTime?`-typed, so the handler's `ParseDateOrNull` helper and the validator's `BeParseableDate` rule become pure boilerplate once the DTO carries typed values — both are deleted. The one retained validation rule (`DateFrom` not later than `DateTo`) is rewritten as a direct `DateTime` comparison.

**Tech Stack:** .NET 8, MediatR, FluentValidation, ASP.NET Core model binding, NSwag-generated TypeScript client, React + TanStack Query.

---

## Ground truth (read before starting)

Current state of every file this plan touches, confirmed by reading the repository at `/home/user/worktrees/feature-3653-Arch-Review-Bank-Getbankstatementlistrequest-Uses`:

- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs` — 4 fields are `string?`.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs` — has a private `ParseDateOrNull(string?)` helper called 4 times in `Handle`.
- `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs` — has `BeParseableDate(string?)`, two `.Must(BeParseableDate)` rules, and `DateFromIsNotLaterThanDateTo(GetBankStatementListRequest)` which re-parses strings.
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — `GetBankStatements` action has 4 `[FromQuery] string? = null` params (`statementDate`, `importDate`, `dateFrom`, `dateTo`).
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementListFilter.cs` — already `DateTime?` for these 4 fields. **No change needed.**
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs` — contains `GetBankStatementListHandlerTests` and `GetBankStatementListRequestValidatorTests`, both using string date literals; 3 tests (`Handle_IgnoresUnparseableDateStrings`, `Validate_RejectsUnparseableDateFrom`, `Validate_RejectsUnparseableDateTo`) become impossible to compile once fields are `DateTime?`.
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs` — already an `IClassFixture<BankStatementImportTestFactory>` integration test class hitting `/api/bank-statements/...` via a real `HttpClient` against `HebloWebApplicationFactory`; it already has `GetAsync` calls against this exact controller (`GetBankStatement_WithExistingId_Returns200WithDtoBody`). This is the precedent for the optional model-binding 400 test.
- `frontend/src/api/generated/api-client.ts` — line 1627, `bankStatements_GetBankStatements` currently types `statementDate/importDate/dateFrom/dateTo` as `string | null | undefined`. This file is **regenerated, never hand-edited**.
- `frontend/src/api/hooks/useBankStatements.ts` — `GetBankStatementListRequest` TS interface (string fields) and `useBankStatementsList` (passes strings straight through to the generated client); `useBankStatementImport` in the same file already does `new Date(request.dateFrom)` — this is the pattern to mirror.
- `frontend/src/api/hooks/__tests__/useBankStatements.test.ts` — only tests `useBankStatementAccounts` today; no existing coverage of `useBankStatementsList`. `mockAuthenticatedApiClient` / `createQueryClientWrapper` come from `frontend/src/api/testUtils.ts`.

Regeneration command (per `docs/development/api-client-generation.md`):
```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

Sole caller of `bankStatements_GetBankStatements(` in the frontend confirmed via grep: `frontend/src/api/hooks/useBankStatements.ts` (plus the generated file itself). No other consumer will be affected by the parameter-type change.

**Compilation-order note:** `GetBankStatementListRequest.cs`, `GetBankStatementListHandler.cs`, `GetBankStatementListRequestValidator.cs`, and `BankStatementsController.cs` are mutually coupled by the retype — the `Application`/`API` projects will **not** compile until all of Tasks 1–3 are done. This is expected; each task states clearly whether a full build is expected to succeed yet.

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

Do **not** attempt `dotnet build` yet. `GetBankStatementListHandler.cs` still calls `ParseDateOrNull(request.StatementDate)` where `ParseDateOrNull` expects `string?` — this will now fail to compile with `CS1503` (cannot convert `DateTime?` to `string?`). That is expected; Task 2 fixes it. Do not attempt to work around it in this task.

**Step 4 — commit.**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs
git commit -m "Retype GetBankStatementListRequest date fields and controller params to DateTime?"
```

---

### task: remove-handler-date-parsing

**File:** `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs`

**Step 1 — remove the parsing calls and the helper.**

Current `Handle` body and helper:

```csharp
    public async Task<GetBankStatementListResponse> Handle(GetBankStatementListRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting bank statement list with Skip={Skip}, Take={Take}", request.Skip, request.Take);

        DateTime? statementDate = ParseDateOrNull(request.StatementDate);
        DateTime? importDate = ParseDateOrNull(request.ImportDate);
        DateTime? dateFrom = ParseDateOrNull(request.DateFrom);
        DateTime? dateTo = ParseDateOrNull(request.DateTo);

        var trimmedTransferId = NormalizeNullableString(request.TransferId);
        var trimmedAccount = NormalizeNullableString(request.Account);

        var filter = new BankStatementListFilter(
            Id: request.Id,
            TransferId: trimmedTransferId,
            Account: trimmedAccount,
            StatementDate: statementDate,
            ImportDate: importDate,
            DateFrom: dateFrom,
            DateTo: dateTo,
            ErrorsOnly: request.ErrorsOnly);

        var (items, totalCount) = await _repository.GetFilteredAsync(
            filter,
            skip: request.Skip,
            take: request.Take,
            orderBy: request.OrderBy ?? "ImportDate",
            ascending: request.Ascending,
            cancellationToken: cancellationToken);

        var dtoList = _mapper.Map<List<BankStatementImportDto>>(items);

        _logger.LogInformation("Retrieved {Count} bank statements (total: {TotalCount})", dtoList.Count, totalCount);

        return new GetBankStatementListResponse
        {
            Items = dtoList,
            TotalCount = totalCount
        };
    }

    private static DateTime? ParseDateOrNull(string? value) =>
        !string.IsNullOrWhiteSpace(value) && DateTime.TryParse(value, out var parsed) ? parsed : null;

    private static string? NormalizeNullableString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
```

Replace with (removes the 4 local variables and the `ParseDateOrNull` method; passes `request.StatementDate`/`request.ImportDate`/`request.DateFrom`/`request.DateTo` straight into the filter constructor; `NormalizeNullableString` and its two call sites are untouched):

```csharp
    public async Task<GetBankStatementListResponse> Handle(GetBankStatementListRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting bank statement list with Skip={Skip}, Take={Take}", request.Skip, request.Take);

        var trimmedTransferId = NormalizeNullableString(request.TransferId);
        var trimmedAccount = NormalizeNullableString(request.Account);

        var filter = new BankStatementListFilter(
            Id: request.Id,
            TransferId: trimmedTransferId,
            Account: trimmedAccount,
            StatementDate: request.StatementDate,
            ImportDate: request.ImportDate,
            DateFrom: request.DateFrom,
            DateTo: request.DateTo,
            ErrorsOnly: request.ErrorsOnly);

        var (items, totalCount) = await _repository.GetFilteredAsync(
            filter,
            skip: request.Skip,
            take: request.Take,
            orderBy: request.OrderBy ?? "ImportDate",
            ascending: request.Ascending,
            cancellationToken: cancellationToken);

        var dtoList = _mapper.Map<List<BankStatementImportDto>>(items);

        _logger.LogInformation("Retrieved {Count} bank statements (total: {TotalCount})", dtoList.Count, totalCount);

        return new GetBankStatementListResponse
        {
            Items = dtoList,
            TotalCount = totalCount
        };
    }

    private static string? NormalizeNullableString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
```

**Step 2 — expected build state.**

Still do **not** attempt `dotnet build`. `GetBankStatementListRequestValidator.cs` still calls `BeParseableDate(x.DateFrom)` where `BeParseableDate` expects `string?` — this still fails to compile (`CS1503`). Task 3 fixes it.

**Step 3 — commit.**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs
git commit -m "Remove redundant ParseDateOrNull helper from GetBankStatementListHandler"
```

---

### task: simplify-validator-date-rules

**File:** `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs`

**Step 1 — rewrite the validator.**

Current file:

```csharp
using FluentValidation;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

namespace Anela.Heblo.Application.Features.Bank.Validators;

public class GetBankStatementListRequestValidator : AbstractValidator<GetBankStatementListRequest>
{
    private const int MaxStringLength = 100;

    public GetBankStatementListRequestValidator()
    {
        RuleFor(x => x.Take)
            .GreaterThan(0).WithMessage("Take must be greater than 0")
            .LessThanOrEqualTo(100).WithMessage("Take must not exceed 100");

        RuleFor(x => x.Skip)
            .GreaterThanOrEqualTo(0).WithMessage("Skip must be greater than or equal to 0");

        RuleFor(x => x.TransferId!)
            .MaximumLength(MaxStringLength)
            .WithMessage($"TransferId must not exceed {MaxStringLength} characters")
            .When(x => x.TransferId != null);

        RuleFor(x => x.Account!)
            .MaximumLength(MaxStringLength)
            .WithMessage($"Account must not exceed {MaxStringLength} characters")
            .When(x => x.Account != null);

        RuleFor(x => x.DateFrom!)
            .Must(BeParseableDate)
            .WithMessage("DateFrom must be a valid date")
            .When(x => !string.IsNullOrWhiteSpace(x.DateFrom));

        RuleFor(x => x.DateTo!)
            .Must(BeParseableDate)
            .WithMessage("DateTo must be a valid date")
            .When(x => !string.IsNullOrWhiteSpace(x.DateTo));

        RuleFor(x => x.DateFrom!)
            .Must((req, _) => DateFromIsNotLaterThanDateTo(req))
            .WithMessage("DateFrom must not be later than DateTo")
            .When(x => BeParseableDate(x.DateFrom) && BeParseableDate(x.DateTo));
    }

    private static bool BeParseableDate(string? value) =>
        string.IsNullOrWhiteSpace(value) || DateTime.TryParse(value, out _);

    private static bool DateFromIsNotLaterThanDateTo(GetBankStatementListRequest req)
    {
        if (!DateTime.TryParse(req.DateFrom, out var from)) return true;
        if (!DateTime.TryParse(req.DateTo, out var to)) return true;
        return from.Date <= to.Date;
    }
}
```

Replace it entirely with:

```csharp
using FluentValidation;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

namespace Anela.Heblo.Application.Features.Bank.Validators;

public class GetBankStatementListRequestValidator : AbstractValidator<GetBankStatementListRequest>
{
    private const int MaxStringLength = 100;

    public GetBankStatementListRequestValidator()
    {
        RuleFor(x => x.Take)
            .GreaterThan(0).WithMessage("Take must be greater than 0")
            .LessThanOrEqualTo(100).WithMessage("Take must not exceed 100");

        RuleFor(x => x.Skip)
            .GreaterThanOrEqualTo(0).WithMessage("Skip must be greater than or equal to 0");

        RuleFor(x => x.TransferId!)
            .MaximumLength(MaxStringLength)
            .WithMessage($"TransferId must not exceed {MaxStringLength} characters")
            .When(x => x.TransferId != null);

        RuleFor(x => x.Account!)
            .MaximumLength(MaxStringLength)
            .WithMessage($"Account must not exceed {MaxStringLength} characters")
            .When(x => x.Account != null);

        RuleFor(x => x.DateFrom)
            .Must((req, dateFrom) => !dateFrom.HasValue || !req.DateTo.HasValue || dateFrom.Value.Date <= req.DateTo.Value.Date)
            .WithMessage("DateFrom must not be later than DateTo");
    }
}
```

Notes on this rewrite:
- `BeParseableDate` and both `.Must(BeParseableDate)` rules are gone — unparseable strings can no longer reach the validator, they are rejected earlier by ASP.NET Core model binding (Task 1).
- `DateFromIsNotLaterThanDateTo` as a named private method is gone; the comparison is now an inline lambda directly on the `RuleFor(x => x.DateFrom)` chain, matching the shape already specified in the design doc.
- No `.When(...)` guard is needed on this rule any more (the old guard existed only to skip the check when either date failed to parse as a string — that scenario can't occur now); the null-guard inside the `Must` lambda (`!dateFrom.HasValue || !req.DateTo.HasValue`) already makes the rule pass when either side is null, matching current `Validate_AcceptsAllNullOptionalFields`/`Validate_AcceptsValidDateRange` semantics.
- `TransferId`/`Account` rules are untouched.

**Step 2 — verify the backend now compiles.**

Run:

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: build succeeds (0 errors). This confirms `Application` and `API` projects compile with the new types end-to-end. The test project (`Anela.Heblo.Tests`) is **not** part of this build target and is still broken — that's expected, fixed in the `update-backend-unit-tests` task.

**Step 3 — commit.**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs
git commit -m "Simplify GetBankStatementListRequestValidator to compare typed DateTime values"
```

---

### task: update-backend-unit-tests

**File:** `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs`

**Step 1 — confirm the test project currently fails to compile.**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build fails with `CS0029`/`CS1503`-style errors in `GetBankStatementListHandlerTests.cs`, because `DateFrom = "2026-01-01"` (a `string`) can no longer be assigned to a `DateTime?` property. This confirms the "test" step of TDD for this task — the failure is the retype itself, already implemented in prior tasks; this task's job is to make the test file agree with the new types.

**Step 2 — update `Handle_PassesAllFilterFieldsToRepository`.**

Find:

```csharp
        var request = new GetBankStatementListRequest
        {
            TransferId = "  ABC  ",
            Account = "  shoptet  ",
            DateFrom = "2026-01-01",
            DateTo = "2026-01-31",
            ErrorsOnly = true,
        };
```

Replace with:

```csharp
        var request = new GetBankStatementListRequest
        {
            TransferId = "  ABC  ",
            Account = "  shoptet  ",
            DateFrom = new DateTime(2026, 1, 1),
            DateTo = new DateTime(2026, 1, 31),
            ErrorsOnly = true,
        };
```

The rest of the test (assertions on `captured.DateFrom`/`captured.DateTo` already use `new DateTime(2026, 1, 1)` / `new DateTime(2026, 1, 31)`) needs no change.

**Step 3 — delete `Handle_IgnoresUnparseableDateStrings`.**

Delete this entire test method (it is no longer reachable — a `DateTime?` property cannot hold an unparseable string, so the compiler rejects the scenario outright):

```csharp
    [Fact]
    public async Task Handle_IgnoresUnparseableDateStrings()
    {
        // Arrange
        var request = new GetBankStatementListRequest
        {
            DateFrom = "not-a-date",
            DateTo = "still-not-a-date",
        };
        BankStatementListFilter? captured = null;
        _repository
            .Setup(r => r.GetFilteredAsync(
                It.IsAny<BankStatementListFilter>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<BankStatementListFilter, int, int, string, bool, CancellationToken>(
                (f, _, _, _, _, _) => captured = f)
            .ReturnsAsync((Enumerable.Empty<BankStatementImport>(), 0));

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        captured!.DateFrom.Should().BeNull();
        captured.DateTo.Should().BeNull();
    }
```

`Handle_OmitsEmptyOrWhitespaceStringFilters` (no date fields involved) needs no change.

**Step 4 — delete `Validate_RejectsUnparseableDateFrom` and `Validate_RejectsUnparseableDateTo`.**

Delete both methods (same reason as Step 3 — the scenario can no longer be constructed):

```csharp
    [Fact]
    public void Validate_RejectsUnparseableDateFrom()
    {
        var request = new GetBankStatementListRequest { DateFrom = "not-a-date" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.DateFrom));
    }

    [Fact]
    public void Validate_RejectsUnparseableDateTo()
    {
        var request = new GetBankStatementListRequest { DateTo = "not-a-date" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.DateTo));
    }
```

**Step 5 — update `Validate_RejectsDateFromLaterThanDateTo`.**

Find:

```csharp
    [Fact]
    public void Validate_RejectsDateFromLaterThanDateTo()
    {
        var request = new GetBankStatementListRequest { DateFrom = "2026-02-01", DateTo = "2026-01-01" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.DateFrom));
    }
```

Replace with:

```csharp
    [Fact]
    public void Validate_RejectsDateFromLaterThanDateTo()
    {
        var request = new GetBankStatementListRequest { DateFrom = new DateTime(2026, 2, 1), DateTo = new DateTime(2026, 1, 1) };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.DateFrom));
    }
```

**Step 6 — update `Validate_AcceptsValidDateRange`.**

Find:

```csharp
    [Fact]
    public void Validate_AcceptsValidDateRange()
    {
        var request = new GetBankStatementListRequest { DateFrom = "2026-01-01", DateTo = "2026-01-31" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeTrue();
    }
```

Replace with:

```csharp
    [Fact]
    public void Validate_AcceptsValidDateRange()
    {
        var request = new GetBankStatementListRequest { DateFrom = new DateTime(2026, 1, 1), DateTo = new DateTime(2026, 1, 31) };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeTrue();
    }
```

`Validate_RejectsTransferIdLongerThan100Chars`, `Validate_RejectsAccountLongerThan100Chars`, and `Validate_AcceptsAllNullOptionalFields` need no changes.

**Step 7 — run the scoped test suite.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.GetBankStatementList"
```

Expected: all remaining tests in `GetBankStatementListHandlerTests` and `GetBankStatementListRequestValidatorTests` pass (7 tests: `Handle_PassesAllFilterFieldsToRepository`, `Handle_OmitsEmptyOrWhitespaceStringFilters`, `Validate_RejectsTransferIdLongerThan100Chars`, `Validate_RejectsAccountLongerThan100Chars`, `Validate_RejectsDateFromLaterThanDateTo`, `Validate_AcceptsAllNullOptionalFields`, `Validate_AcceptsValidDateRange`).

**Step 8 — add the optional model-binding integration test (recommended, precedent exists).**

`backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs` already exercises `GET /api/bank-statements/{id}` through a real `HttpClient` against `HebloWebApplicationFactory` (see `GetBankStatement_WithExistingId_Returns200WithDtoBody` / `GetBankStatement_WithMissingId_Returns404WithMessageBody`, both inside the `BankStatementImportIntegrationTests` class). This is a direct, in-file precedent for testing the list endpoint's new model-binding rejection path. Add a new test method to that same class, immediately after `GetBankStatement_WithMissingId_Returns404WithMessageBody` (i.e., still inside `BankStatementImportIntegrationTests`, before the closing brace of the class and before the `BankStatementImportTestFactory` class definition):

```csharp
    [Fact]
    public async Task GetBankStatements_WithInvalidDateFromQueryParam_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync("/api/bank-statements?dateFrom=not-a-date");

        // Assert — ASP.NET Core model binding rejects this before MediatR.Send runs.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
```

This requires no new `using` directives — `System.Net` (for `HttpStatusCode`) is already imported at the top of the file.

Run the whole integration test class to confirm it and everything around it still passes:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.BankStatementImportIntegrationTests"
```

Expected: all tests in the class pass, including the new one.

**Step 9 — full backend verification.**

```bash
dotnet build
dotnet format --verify-no-changes
dotnet test
```

Expected: solution builds with 0 errors, formatting is clean, and the full backend test suite passes.

**Step 10 — commit.**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs
git commit -m "Update GetBankStatementList backend tests for DateTime? fields; add 400 model-binding test"
```

---

### task: regenerate-client-and-update-frontend-hook

**Files:** `frontend/src/api/generated/api-client.ts` (regenerated only), `frontend/src/api/hooks/useBankStatements.ts`, `frontend/src/api/hooks/__tests__/useBankStatements.test.ts`

**Step 1 — regenerate the TypeScript client.**

```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

This requires the `API` project to build successfully, which it now does (confirmed in `simplify-validator-date-rules`). Do not hand-edit `frontend/src/api/generated/api-client.ts`.

**Step 2 — verify the regenerated signature.**

```bash
grep -n "bankStatements_GetBankStatements(" frontend/src/api/generated/api-client.ts
```

Expected: the method signature's `statementDate`, `importDate`, `dateFrom`, `dateTo` parameters now read `Date | null | undefined` (previously `string | null | undefined`); `id`, `transferId`, `account`, `errorsOnly`, `skip`, `take`, `orderBy`, `ascending` parameter types are unchanged.

**Step 3 — confirm this is still the sole caller.**

```bash
grep -rn "bankStatements_GetBankStatements(" frontend/src --include=*.ts --include=*.tsx
```

Expected: two matches — the generated method definition itself (`frontend/src/api/generated/api-client.ts`) and the call site in `frontend/src/api/hooks/useBankStatements.ts`. If any other caller appears, stop and re-scope this task before continuing (out of scope for this plan as currently written).

**Step 4 — confirm the frontend currently fails to build.**

```bash
cd frontend && npm run build
```

Expected: TypeScript compile error in `useBankStatements.ts`, because `useBankStatementsList` passes `request?.dateFrom` (type `string | undefined`) where the regenerated client now expects `Date | null | undefined`.

**Step 5 — update `useBankStatementsList`.**

Open `frontend/src/api/hooks/useBankStatements.ts`. Find:

```typescript
export const useBankStatementsList = (
  request: GetBankStatementListRequest = {}
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.bankStatements, 'list', request],
    queryFn: (): Promise<GetBankStatementListResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.bankStatements_GetBankStatements(
        request?.id ?? undefined,
        request?.transferId?.trim() ?? undefined,
        request?.account?.trim() ?? undefined,
        request?.statementDate ?? undefined,
        request?.importDate ?? undefined,
        request?.dateFrom ?? undefined,
        request?.dateTo ?? undefined,
        request?.errorsOnly ?? undefined,
        request?.skip,
        request?.take,
        request?.orderBy ?? undefined,
        request?.ascending
      );
    },
    staleTime: 2 * 60 * 1000, // 2 minutes
  });
};
```

Replace with (only the four date arguments to `bankStatements_GetBankStatements` change; the `GetBankStatementListRequest` TypeScript interface a few lines above this function — with its `statementDate?: string; importDate?: string; dateFrom?: string; dateTo?: string;` fields — is **not** touched, preserving the hook's public string-based contract to `ImportTab.tsx`):

```typescript
export const useBankStatementsList = (
  request: GetBankStatementListRequest = {}
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.bankStatements, 'list', request],
    queryFn: (): Promise<GetBankStatementListResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.bankStatements_GetBankStatements(
        request?.id ?? undefined,
        request?.transferId?.trim() ?? undefined,
        request?.account?.trim() ?? undefined,
        request?.statementDate ? new Date(request.statementDate) : undefined,
        request?.importDate ? new Date(request.importDate) : undefined,
        request?.dateFrom ? new Date(request.dateFrom) : undefined,
        request?.dateTo ? new Date(request.dateTo) : undefined,
        request?.errorsOnly ?? undefined,
        request?.skip,
        request?.take,
        request?.orderBy ?? undefined,
        request?.ascending
      );
    },
    staleTime: 2 * 60 * 1000, // 2 minutes
  });
};
```

**Step 6 — add hook-level test coverage for the new conversion.**

`frontend/src/api/hooks/__tests__/useBankStatements.test.ts` currently only covers `useBankStatementAccounts`. Add coverage for the new `Date` conversion behavior in `useBankStatementsList`, using the same `mockAuthenticatedApiClient`/`createQueryClientWrapper` pattern already in the file. Add the import and a new `describe` block.

Find the import line:

```typescript
import { useBankStatementAccounts } from '../useBankStatements';
```

Replace with:

```typescript
import { useBankStatementAccounts, useBankStatementsList } from '../useBankStatements';
```

Add a new `describe` block after the closing `});` of the existing `describe('useBankStatements - Account Listing', ...)` block (i.e., at the end of the file, as a sibling top-level `describe`):

```typescript
describe('useBankStatements - List Query', () => {
    let mockClient: {
        bankStatements_GetAccounts: jest.Mock;
        bankStatements_GetBankStatements: jest.Mock;
        bankStatements_ImportStatements: jest.Mock;
    };

    beforeEach(() => {
        jest.clearAllMocks();
        mockClient = {
            bankStatements_GetAccounts: jest.fn(),
            bankStatements_GetBankStatements: jest.fn(),
            bankStatements_ImportStatements: jest.fn(),
        };
        mockAuthenticatedApiClient(mockClient);
    });

    it('converts dateFrom/dateTo strings to Date objects before calling the generated client', async () => {
        mockClient.bankStatements_GetBankStatements.mockResolvedValue({ items: [], totalCount: 0 });

        const { wrapper } = createQueryClientWrapper();
        const { result } = renderHook(
            () => useBankStatementsList({ dateFrom: '2026-01-01', dateTo: '2026-01-31' }),
            { wrapper }
        );

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        expect(mockClient.bankStatements_GetBankStatements).toHaveBeenCalledTimes(1);
        const call = mockClient.bankStatements_GetBankStatements.mock.calls[0];
        expect(call[5]).toEqual(new Date('2026-01-01'));
        expect(call[6]).toEqual(new Date('2026-01-31'));
    });

    it('passes undefined for dateFrom/dateTo/statementDate/importDate when absent', async () => {
        mockClient.bankStatements_GetBankStatements.mockResolvedValue({ items: [], totalCount: 0 });

        const { wrapper } = createQueryClientWrapper();
        const { result } = renderHook(() => useBankStatementsList({}), { wrapper });

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        const call = mockClient.bankStatements_GetBankStatements.mock.calls[0];
        expect(call[3]).toBeUndefined(); // statementDate
        expect(call[4]).toBeUndefined(); // importDate
        expect(call[5]).toBeUndefined(); // dateFrom
        expect(call[6]).toBeUndefined(); // dateTo
    });
});
```

Argument indices `call[3]`..`call[6]` correspond to the `bankStatements_GetBankStatements` positional parameters `statementDate, importDate, dateFrom, dateTo` (indices 0–2 are `id, transferId, account`), matching the call site written in Step 5.

**Step 7 — run the new frontend tests.**

```bash
cd frontend && npx react-scripts test src/api/hooks/__tests__/useBankStatements.test.ts --watchAll=false
```

Expected: all tests pass, including the two new ones in `useBankStatements - List Query`.

**Step 8 — full frontend verification.**

```bash
cd frontend && npm run build && npm run lint
```

Expected: both succeed with no new TypeScript or lint errors. `frontend/src/components/customer/tabs/ImportTab.tsx` requires no changes (its calls into `useBankStatementsList` still pass strings, matching the unchanged `GetBankStatementListRequest` TS interface).

**Step 9 — commit.**

```bash
git add frontend/src/api/generated/api-client.ts frontend/src/api/hooks/useBankStatements.ts frontend/src/api/hooks/__tests__/useBankStatements.test.ts
git commit -m "Regenerate OpenAPI client and convert useBankStatementsList date strings to Date objects"
```

---

## Final verification (run once, after all tasks are complete)

```bash
dotnet build
dotnet format --verify-no-changes
dotnet test
cd frontend && npm run build && npm run lint
```

All four commands must succeed with no errors before considering this change complete. No E2E changes are required (no existing E2E spec targets `/api/bank-statements`, per spec Out of Scope).
