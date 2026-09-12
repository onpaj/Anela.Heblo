# Specification: Move MarketingImportResult to Contracts/ (MarketingInvoices module)

## Summary
`MarketingImportResult.cs` currently sits at the root of the `MarketingInvoices` feature folder, violating the module's filesystem convention for complex features. This is a mechanical, zero-behavior-change refactor: relocate the file to `Contracts/`, update its namespace, and adjust any `using` directives that become stale as a result. No public API, DTO shape, or runtime logic changes.

## Background
`docs/architecture/filesystem.md` defines that for a "complex feature" (one using the `UseCases/` pattern), the feature root (`Features/{Feature}/`) is reserved for module-level files only: `{Feature}Module.cs`, `{Feature}MappingProfile.cs`, `{Feature}Constants.cs`, `{Feature}Repository.cs`. Shared DTOs consumed by multiple use cases/services belong in `Features/{Feature}/Contracts/`.

The `MarketingInvoices` feature already follows the complex-feature layout (`UseCases/`, `Services/`, and a `Contracts/` folder all exist), but `MarketingImportResult.cs` was left at the feature root — the only type in this module still out of place. This is a pure architecture/code-hygiene fix identified by an automated architecture review; it does not change behavior.

**Current state (verified in the repo):**
- File: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingImportResult.cs`
- Namespace: `Anela.Heblo.Application.Features.MarketingInvoices`
- Content:
  ```csharp
  namespace Anela.Heblo.Application.Features.MarketingInvoices;

  public class MarketingImportResult
  {
      public int Imported { get; set; }
      public int Skipped { get; set; }
      public int Failed { get; set; }
  }
  ```
- `Contracts/` already exists in this feature and holds `IMarketingTransactionSource.cs` and `MarketingTransaction.cs`.

**Consumers of `MarketingImportResult` (verified via repo-wide search):**
1. `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/IMarketingInvoiceImportService.cs` — return type of `ImportAsync(...)`.
2. `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — return type of `ImportAsync(...)` and constructs `new MarketingImportResult()`.
3. `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesHandler.cs` — consumes the value returned by `IMarketingInvoiceImportService.ImportAsync(...)` via an implicitly-typed (`var`) local; does not name `MarketingImportResult` explicitly in source.
4. `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs` — constructs `new MarketingImportResult { Imported = 1, Skipped = 0, Failed = 0 }` in a test setup.
5. `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` — does not reference `MarketingImportResult` by name; no change needed.

**Important correction to the brief's suggested fix:** both `IMarketingInvoiceImportService.cs` and `MarketingInvoiceImportService.cs` already have `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;` at the top (needed today for `IMarketingTransactionSource`). Once `MarketingImportResult` moves into that same `Contracts` namespace, these two files require **no new `using` statement** — the existing one already covers it, and no `using` line needs to be added or removed in either file. `ImportMarketingInvoicesHandler.cs` likewise needs no `using` change (it never references the type name directly, and already imports `...MarketingInvoices.Contracts` and `...MarketingInvoices.Services`).

The one file that *does* need a `using` change is the test file `ImportMarketingInvoicesHandlerTests.cs`: it currently has `using Anela.Heblo.Application.Features.MarketingInvoices;` (the feature-root namespace) specifically to resolve `MarketingImportResult` via C#'s enclosing-namespace lookup, alongside an already-present `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;`. After the move, the feature-root `using` is no longer needed for this purpose. It must be removed (rather than left as a now-unused, dead import) — this is a mechanical hygiene requirement of the move, not scope creep, since leaving it would introduce an unused-using that `dotnet format`/analyzers would flag.

## Functional Requirements

### FR-1: Relocate `MarketingImportResult.cs` to `Contracts/`
Move the file from `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingImportResult.cs` to `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingImportResult.cs`, using a `git mv` (or equivalent) so history is preserved.

**Acceptance criteria:**
- The file no longer exists at the old path.
- The file exists at `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingImportResult.cs`.
- The class body (three `int` auto-properties: `Imported`, `Skipped`, `Failed`) is byte-for-byte unchanged apart from the namespace line.

### FR-2: Update the namespace declaration
Change the namespace in the moved file from `Anela.Heblo.Application.Features.MarketingInvoices` to `Anela.Heblo.Application.Features.MarketingInvoices.Contracts`.

**Acceptance criteria:**
- The moved file declares `namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;`.
- No other change is made to the file's content.

### FR-3: Remove the now-unused `using` in `ImportMarketingInvoicesHandlerTests.cs`
Remove the `using Anela.Heblo.Application.Features.MarketingInvoices;` line from `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs`, since it exists only to resolve `MarketingImportResult` from the feature-root namespace and that type no longer lives there. The file's existing `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;` line already resolves the relocated type — no addition is needed.

**Acceptance criteria:**
- `ImportMarketingInvoicesHandlerTests.cs` no longer contains `using Anela.Heblo.Application.Features.MarketingInvoices;`.
- The file still compiles and `new MarketingImportResult { ... }` still resolves via the existing `Contracts` using.

### FR-4: No `using` changes required in production consumer files
Verify (do not blindly edit) that `IMarketingInvoiceImportService.cs`, `MarketingInvoiceImportService.cs`, and `ImportMarketingInvoicesHandler.cs` compile unchanged after the move, since each already imports `Anela.Heblo.Application.Features.MarketingInvoices.Contracts`.

**Acceptance criteria:**
- No `using` lines are added to or removed from these three files as part of this change.
- All three files compile against the moved/renamespaced `MarketingImportResult`.

### FR-5: No behavior change
No method signatures, property names, property types, class name, access modifiers, or logic in `MarketingImportResult`, `MarketingInvoiceImportService`, `IMarketingInvoiceImportService`, or `ImportMarketingInvoicesHandler` change as part of this work.

**Acceptance criteria:**
- `git diff` shows only: the file move/rename, the one-line namespace change in the moved file, and the one-line `using` removal in the test file.
- No other `.cs` file in the repository is touched.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a compile-time-only, structural refactor with no runtime code path affected.

### NFR-2: Security
Not applicable — no change to authentication, authorization, data handling, or exposed surface area. `MarketingImportResult` is an internal application-layer DTO, not exposed via any public API contract (it is not an OpenAPI-serialized response type; `ImportMarketingInvoicesResponse` is the type that crosses the MediatR/API boundary).

## Data Model
No data model change. `MarketingImportResult` remains:
```csharp
public class MarketingImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
```
Its only structural change is its namespace (from `...MarketingInvoices` to `...MarketingInvoices.Contracts`), consistent with the sibling shared DTOs already in that folder (`MarketingTransaction`, `IMarketingTransactionSource`).

## API / Interface Design
No API surface changes. This type is not a DTO class per the project's "DTOs are classes, never records" rule in the sense of an OpenAPI contract type — it is an internal application-layer result object returned by `IMarketingInvoiceImportService.ImportAsync(...)` and consumed inside the `ImportMarketingInvoicesHandler` to populate the actual API-facing `ImportMarketingInvoicesResponse`. No OpenAPI client regeneration is required.

## Dependencies
None. This is a self-contained, single-module, compile-time change with no external service, library, or feature-flag dependency.

## Out of Scope
- Any change to `MarketingImportResult`'s fields, name, or shape.
- Any change to `IMarketingInvoiceImportService`, `MarketingInvoiceImportService`, or `ImportMarketingInvoicesHandler` logic.
- Any change to other files in the `MarketingInvoices` module (e.g., `MarketingInvoicesModule.cs`, `MarketingTransaction.cs`, `IMarketingTransactionSource.cs`) beyond what FR-3 specifies.
- Any change to `MarketingInvoiceImportServiceTests.cs` (it does not reference `MarketingImportResult` and needs no edit).
- Broader repo-wide filesystem-convention cleanup of other modules/features — this spec covers only the `MarketingInvoices` / `MarketingImportResult` finding.
- OpenAPI client regeneration, database migrations, or any deployment step — none are triggered by this change.

## Open Questions
None.

## Status: COMPLETE
