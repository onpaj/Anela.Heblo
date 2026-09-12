## Module
MarketingInvoices

## Finding
`backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingImportResult.cs` lives directly at the feature root. Per the filesystem guidelines (`docs/architecture/filesystem.md`), the valid locations for a type in a complex feature are: `UseCases/{UseCase}/` (per-use-case), `Contracts/` (shared across use cases), `Services/`, `Infrastructure/`, or `Validators/`. The feature root is reserved for module-level files (`{Feature}Module.cs`, `{Feature}MappingProfile.cs`, `{Feature}Constants.cs`, `{Feature}Repository.cs`).

`MarketingImportResult` is a shared result type consumed by both `IMarketingInvoiceImportService` (in `Services/`) and `ImportMarketingInvoicesHandler` (in `UseCases/`), which makes `Contracts/` the canonical location.

## Why it matters
Types at the feature root are unexpected — a developer scanning the module will not look there for shared DTOs. It also contradicts the filesystem convention uniformly applied across other modules.

## Suggested fix
Move `MarketingImportResult.cs` to `Application/Features/MarketingInvoices/Contracts/MarketingImportResult.cs` and update the namespace and using statements in `MarketingInvoiceImportService.cs` and `ImportMarketingInvoicesHandler.cs` accordingly. No logic change required.

---
_Filed by daily arch-review routine on 2026-09-11._
