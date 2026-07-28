# Design: consolidate `ImportBankStatementResponse` into `BankStatementImportResultDto`

Backend-only refactor (MediatR pipeline + one controller action). No UI surface — the API contract's JSON shape and route are unchanged, so no UX/UI section.

## Component design

### Affected components and their new responsibilities

**`ImportBankStatementRequest`** (`Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementRequest.cs`)
- Responsibility: carries import parameters (`AccountName`, `DateFrom`, `DateTo`) through MediatR.
- Interface change: `IRequest<ImportBankStatementResponse>` → `IRequest<BankStatementImportResultDto>`.
- Needs `using Anela.Heblo.Application.Features.Bank.Contracts;` (already present, since it already uses `BankStatementImportDto`... actually it doesn't reference any Contracts type today beyond the using already imported — confirm it compiles with the existing using).

**`ImportBankStatementHandler`** (`.../ImportBankStatement/ImportBankStatementHandler.cs`)
- Responsibility unchanged: orchestrates client fetch → dedup → per-statement import → watermark state update → result aggregation. This is where "how many succeeded/failed/were skipped" is decided; that decision now materializes directly as the API contract type instead of an intermediate response.
- Interface change: `IRequestHandler<ImportBankStatementRequest, ImportBankStatementResponse>` → `IRequestHandler<ImportBankStatementRequest, BankStatementImportResultDto>`. `Handle`'s return type changes to `Task<BankStatementImportResultDto>`. The single construction site at the end of the try block (`return new ImportBankStatementResponse { ... }`) becomes `return new BankStatementImportResultDto { ... }` with the same four field assignments (`Statements`, `SuccessCount`, `ErrorCount`, `SkippedCount`). No other line in the method changes — `HasErrors` is computed, not assigned, in both the old and new type.
- New dependency: this file gains a reference to `Anela.Heblo.Application.Features.Bank.Contracts.BankStatementImportResultDto` — already imported via the existing `using Anela.Heblo.Application.Features.Bank.Contracts;` at the top of the file, so no new `using` is required.

**`ImportBankStatementResponse`** (`.../ImportBankStatement/ImportBankStatementResponse.cs`)
- Deleted. It was a `BaseResponse`-derived application-layer type whose only reason to exist was as the handler's return type; that role is now filled by the contract DTO directly, mirroring `GetBankStatementByIdHandler` → `BankStatementImportDto?` and `GetBankStatementListHandler` → `GetBankStatementListResponse`.

**`BankStatementImportResultDto`** (`Application/Features/Bank/Contracts/BankStatementImportResultDto.cs`)
- Responsibility expands from "API response shape" to "API response shape *and* handler return type" — single source of truth for the import-result shape. No field or property changes: `Statements`, `SuccessCount`, `ErrorCount`, `SkippedCount` stay settable; `TotalCount` and `HasErrors` stay computed.

**`BankStatementsController.ImportStatements`** (`API/Controllers/BankStatementsController.cs:43-60`)
- Responsibility narrows to pure routing, matching its sibling actions (`GetAccounts`, `GetBankStatements`, `GetBankStatement`) in the same controller.
- Body collapses from mediator-send + 8-line manual field mapping to:
  ```csharp
  var response = await _mediator.Send(importRequest);
  return Ok(response);
  ```
- Action signature (`Task<ActionResult<BankStatementImportResultDto>>`, `[HttpPost("import")]`, `[FromBody] BankImportRequestDto`) is unchanged — no OpenAPI contract diff, no TS client regeneration impact.

**`BankImportJobBase`** (`Application/Features/Bank/Infrastructure/Jobs/BankImportJobBase.cs`)
- No change. It already consumes the mediator response purely through `response.HasErrors`, `response.SuccessCount`, `response.ErrorCount`, `response.SkippedCount` — all present on `BankStatementImportResultDto` with identical names and semantics. Its generic call site (`_mediator.Send<ImportBankStatementRequest, ...>` or equivalent, whatever form it takes) resolves to the new return type automatically via `ImportBankStatementRequest`'s updated `IRequest<T>`.

### Component interaction (unchanged shape, new return type)

```
BankStatementsController.ImportStatements
        │  ImportBankStatementRequest
        ▼
   IMediator.Send
        │
        ▼
ImportBankStatementHandler.Handle
        │  returns BankStatementImportResultDto   (was: ImportBankStatementResponse)
        ▼
   IMediator.Send (caller side)
        │
        ├── BankStatementsController → Ok(response)      [HTTP path]
        └── BankImportJobBase.ExecuteAsync → reads        [scheduled-job path]
              response.HasErrors / SuccessCount / ErrorCount / SkippedCount
```

Both call sites of `ImportBankStatementRequest` (the controller and the job base) receive the same `BankStatementImportResultDto` instance shape post-change; neither needs new mapping code.

### Test components requiring updates (mechanical, no behavioral change)

- `BankImportJobBaseTests.cs`: 8 occurrences of `new ImportBankStatementResponse(...)` → `new BankStatementImportResultDto(...)`; 3 occurrences of the `Callback<IRequest<ImportBankStatementResponse>, CancellationToken>` generic cast → `Callback<IRequest<BankStatementImportResultDto>, CancellationToken>`.
- `BankStatementImportIntegrationTests.cs`: 4 occurrences of `JsonSerializer.Deserialize<ImportBankStatementResponse>(...)` → `JsonSerializer.Deserialize<BankStatementImportResultDto>(...)`.
- `ImportBankStatementHandlerTests.cs`: no change needed — it captures the handler's result via `var response = await _handler.Handle(...)` and only touches fields present on both types.

## Data schemas

### Type removed

```csharp
// DELETED: Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementResponse.cs
public class ImportBankStatementResponse : BaseResponse
{
    public List<BankStatementImportDto> Statements { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public int SkippedCount { get; set; }
    public bool HasErrors => ErrorCount > 0;
}
```

### Type retained, now doing double duty (handler return type + HTTP response body)

```csharp
// Application/Features/Bank/Contracts/BankStatementImportResultDto.cs — UNCHANGED shape
public class BankStatementImportResultDto
{
    public List<BankStatementImportDto> Statements { get; set; } = new();
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public int SkippedCount { get; set; }
    public int TotalCount => Statements.Count;      // computed, was implicit before (controller never set it)
    public bool HasErrors => ErrorCount > 0;
}
```

### HTTP contract — before/after (identical wire shape)

`POST /api/bank-statements/import`

Request body (`BankImportRequestDto`) — unchanged.

Response body (200 OK) — byte-identical JSON before and after this change:
```json
{
  "statements": [ { "...": "BankStatementImportDto fields" } ],
  "successCount": 0,
  "errorCount": 0,
  "skippedCount": 0,
  "totalCount": 0,
  "hasErrors": false
}
```
Before, `totalCount` and `hasErrors` were already produced by the controller-constructed `BankStatementImportResultDto` (`TotalCount => Statements.Count`, its own `HasErrors => ErrorCount > 0`) — the handler's `ImportBankStatementResponse.HasErrors` was computed but discarded, since the controller never read it. After this change, the same two computed properties are produced by the same DTO type, just constructed one level earlier (in the handler instead of the controller). No client-visible difference; OpenAPI schema `$ref` name for the response was already `BankStatementImportResultDto` (the controller's declared `ActionResult<T>`), so no client regeneration diff either.

### In-process contract (MediatR)

```csharp
// Before
IRequest<ImportBankStatementResponse>  (ImportBankStatementRequest)
IRequestHandler<ImportBankStatementRequest, ImportBankStatementResponse>  (ImportBankStatementHandler)

// After
IRequest<BankStatementImportResultDto>
IRequestHandler<ImportBankStatementRequest, BankStatementImportResultDto>
```

No new events, no persistence schema changes — `BankStatementImport` entity and repository are untouched.

## Scope boundary

In scope: the five files listed in the plan's "Dependencies and scope" (request, handler, controller, deleted response type, two test files). Out of scope: `BankImportJobBase.cs` logic, `BankStatementImportResultDto`'s shape, any other Bank-module arch-review findings, frontend.
