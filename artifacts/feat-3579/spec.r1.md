# Specification: Relocate Purchase stock-analysis enums to `Contracts/`

## Summary
Three enums (`StockSeverity`, `StockStatusFilter`, `StockAnalysisSortBy`) currently live inside the `GetPurchaseStockAnalysis` use-case's request/response files, but are consumed by module-level services (`IStockSeverityCalculator`, `StockSeverityCalculator`) and by a dashboard tile (`LowStockEfficiencyTile`) outside that use case. This is a pure type-relocation refactor: move the three enums into `Features/Purchase/Contracts/`, one file per enum, and update all `using` directives across production and test code. No behavioral, signature, or serialization changes.

## Background
The Purchase module follows Vertical Slice organization: each use case lives under `UseCases/<UseCaseName>/` and cross-cutting types meant to be shared across use cases, services, and dashboard tiles live under `Contracts/` (see existing files such as `PurchaseOrderLineDto.cs`, `SupplierDto.cs`, `MaterialInfo.cs`). Today, `StockSeverity` is defined inside `GetPurchaseStockAnalysisResponse.cs` (in namespace `Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis`), and `StockStatusFilter` / `StockAnalysisSortBy` are defined inside `GetPurchaseStockAnalysisRequest.cs` in the same namespace.

`Services/IStockSeverityCalculator.cs` and `Services/StockSeverityCalculator.cs` both `using` that use-case namespace solely to reach `StockSeverity`, and `DashboardTiles/LowStockEfficiencyTile.cs` does the same to reach `StockStatusFilter`. This inverts the intended dependency direction: module-level services and dashboard tiles should depend on `Contracts/`, not on a specific use case's namespace. Practically, it means a developer reading `Services/IStockSeverityCalculator.cs` must follow a `using` directive into `UseCases/GetPurchaseStockAnalysis/` to find a fundamental domain enum, and any future restructuring of that use case risks silently breaking unrelated services/tiles with no compiler signal that they exist. Note that `GetPurchaseStockAnalysisHandler.cs` already has a `using Anela.Heblo.Application.Features.Purchase.Contracts;` directive (for other Contracts types), which supports moving these enums there as the natural, already-referenced home.

This finding was filed by the daily arch-review routine (2026-07-10) against the Purchase module.

## Functional Requirements

### FR-1: Move `StockSeverity` to `Contracts/`
Relocate the `StockSeverity` enum (currently at the bottom of `GetPurchaseStockAnalysisResponse.cs`, lines 96–103) into a new file `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockSeverity.cs`, in namespace `Anela.Heblo.Application.Features.Purchase.Contracts`, following the existing file-scoped-namespace, one-type-per-file convention used by other files in that folder (e.g. `SupplierDto.cs`).

**Acceptance criteria:**
- `Contracts/StockSeverity.cs` exists and declares `public enum StockSeverity { Critical, Low, Optimal, Overstocked, NotConfigured }` with identical member names, order, and (implicit) underlying values as today.
- The enum no longer appears in `GetPurchaseStockAnalysisResponse.cs`.
- `GetPurchaseStockAnalysisResponse.cs` (which still references `StockSeverity` via `StockAnalysisItemDto.Severity` and `StockAnalysisSummaryDto` counts) gains a `using Anela.Heblo.Application.Features.Purchase.Contracts;` directive if not already effectively covered.

### FR-2: Move `StockStatusFilter` and `StockAnalysisSortBy` to `Contracts/`
Relocate `StockStatusFilter` and `StockAnalysisSortBy` (currently at the bottom of `GetPurchaseStockAnalysisRequest.cs`, lines 30–48) into `Contracts/`, each in its own file: `Contracts/StockStatusFilter.cs` and `Contracts/StockAnalysisSortBy.cs`, namespace `Anela.Heblo.Application.Features.Purchase.Contracts`.

**Acceptance criteria:**
- `Contracts/StockStatusFilter.cs` declares `public enum StockStatusFilter { All, Critical, Low, Optimal, Overstocked, NotConfigured }` with identical members/order to today.
- `Contracts/StockAnalysisSortBy.cs` declares `public enum StockAnalysisSortBy { ProductCode, ProductName, AvailableStock, Consumption, StockEfficiency, LastPurchaseDate }` with identical members/order to today.
- Neither enum remains defined in `GetPurchaseStockAnalysisRequest.cs`.
- `GetPurchaseStockAnalysisRequest.cs` gains a `using Anela.Heblo.Application.Features.Purchase.Contracts;` directive (it still uses `StockStatusFilter` and `StockAnalysisSortBy` as property types/defaults).

### FR-3: Update all consuming `using` directives
Update every file that referenced these three enums via the old `UseCases.GetPurchaseStockAnalysis` namespace so it compiles against the new `Contracts` namespace instead.

**Acceptance criteria:**
- `Services/IStockSeverityCalculator.cs`: `using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;` replaced with `using Anela.Heblo.Application.Features.Purchase.Contracts;`.
- `Services/StockSeverityCalculator.cs`: same replacement.
- `DashboardTiles/LowStockEfficiencyTile.cs`: `using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;` replaced with `using Anela.Heblo.Application.Features.Purchase.Contracts;` — note this file also uses `GetPurchaseStockAnalysisRequest` from the use-case namespace, so if `Contracts` alone does not resolve that type, both `using` directives are kept (one for `Contracts`, one for the use-case namespace).
- `GetPurchaseStockAnalysisHandler.cs`: already has `using Anela.Heblo.Application.Features.Purchase.Contracts;`; verify no duplicate/unused using remains and the file still compiles given its direct enum member references (`StockStatusFilter.Critical`, `StockSeverity.Critical`, `StockAnalysisSortBy.ProductCode`, etc.).
- Test files that reference the moved enums directly or transitively — at minimum `test/Anela.Heblo.Tests/Features/Purchase/StockSeverityCalculatorTests.cs`, `test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs`, and `test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` — are updated to add `using Anela.Heblo.Application.Features.Purchase.Contracts;` where the moved enums are referenced by unqualified name, and to drop the `UseCases.GetPurchaseStockAnalysis` using if it becomes unused after the move (only if no other symbol from that namespace is still referenced by the file).
- Full-repository grep for `StockSeverity`, `StockStatusFilter`, and `StockAnalysisSortBy` after the change shows no remaining reference to the old `UseCases.GetPurchaseStockAnalysis` namespace for these three symbols. (The unrelated `Manufacture` module enum `ManufacturingStockSeverity` is a distinct type in a distinct namespace and is explicitly out of scope — do not touch it.)

### FR-4: No behavioral change
The refactor must not alter enum member names, member order, numeric values, JSON serialization output (property names/enum string values used by the frontend/OpenAPI contract), MediatR request/response shapes, or any business logic in `StockSeverityCalculator`, `GetPurchaseStockAnalysisHandler`, or `LowStockEfficiencyTile`.

**Acceptance criteria:**
- Enum member names, order, and values are byte-for-byte identical before and after the move (diff of the enum body only shows file/namespace relocation, not content changes).
- No changes to method bodies, business logic, or public method signatures anywhere outside the enum relocation and `using` directive updates.
- Generated OpenAPI/TypeScript client (`npm run build` regenerates it) produces the same enum names/values as before — confirm no naming drift since the enum names themselves are unchanged, only their C# namespace moved (OpenAPI schema names are typically derived from type name, not full namespace, but this must be verified per FR/NFR below).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a compile-time namespace/file reorganization with no runtime behavior change. No performance impact expected or to be measured.

### NFR-2: Security
Not applicable — no security-sensitive code, auth, or data handling is touched by this change.

### NFR-3: Build & compile integrity
The solution must compile cleanly with zero new warnings or errors introduced by this change.

**Acceptance criteria:**
- `dotnet build` succeeds with no new errors/warnings attributable to this change.
- `dotnet format` (or the project's formatting check) reports no issues on the touched files.
- All existing unit tests referencing these enums (`StockSeverityCalculatorTests`, `GetPurchaseStockAnalysisHandlerTests`, `GetPurchaseStockAnalysisHandlerDiacriticsTests`, and any others surfaced by the FR-3 grep) compile and pass unchanged in behavior/assertions — only `using` directives may change in test files, never test logic or expected values.
- The auto-generated OpenAPI TypeScript client (built via `npm run build` per `docs/development/api-client-generation.md`) is regenerated and shows no unexpected diff in the shape/names of `StockSeverity`, `StockStatusFilter`, or `StockAnalysisSortBy` as exposed to the frontend.

## Data Model
No data model changes. The three enums are pure in-memory/DTO-level types with no persistence mapping (not EF-mapped entities, not stored as columns) — this is confirmed by their current location in `Application`-layer use-case files rather than `Domain` or `Persistence` projects. Their relocation only changes their C# namespace (`...UseCases.GetPurchaseStockAnalysis` → `...Contracts`), not their assembly, accessibility (`public`), or member layout.

| Enum | New file | New namespace | Members (unchanged) |
|---|---|---|---|
| `StockSeverity` | `Contracts/StockSeverity.cs` | `Anela.Heblo.Application.Features.Purchase.Contracts` | Critical, Low, Optimal, Overstocked, NotConfigured |
| `StockStatusFilter` | `Contracts/StockStatusFilter.cs` | `Anela.Heblo.Application.Features.Purchase.Contracts` | All, Critical, Low, Optimal, Overstocked, NotConfigured |
| `StockAnalysisSortBy` | `Contracts/StockAnalysisSortBy.cs` | `Anela.Heblo.Application.Features.Purchase.Contracts` | ProductCode, ProductName, AvailableStock, Consumption, StockEfficiency, LastPurchaseDate |

## API / Interface Design
No HTTP endpoint, route, request/response contract, or MediatR message shape changes. `GetPurchaseStockAnalysisRequest`/`GetPurchaseStockAnalysisResponse` keep their existing public shape and namespace (`UseCases.GetPurchaseStockAnalysis`); only the three enum *type definitions* they reference move to `Contracts`. Since C#/JSON serialization (System.Text.Json, used by ASP.NET Core/NSwag for the OpenAPI contract) serializes enums by type name and member name — not by C# namespace — the wire-level contract (JSON payloads, generated OpenAPI schema names `StockSeverity`, `StockStatusFilter`, `StockAnalysisSortBy`) is expected to remain unchanged. This must be verified by regenerating the OpenAPI/TypeScript client and diffing it (see NFR-3).

Files touched (production code):
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs` — remove `StockSeverity` enum, add `using` for `Contracts`.
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisRequest.cs` — remove `StockStatusFilter`/`StockAnalysisSortBy` enums, add `using` for `Contracts`.
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` — verify existing `using Contracts` covers all enum references; no other change expected.
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockSeverityCalculator.cs` — swap `using` to `Contracts`.
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockSeverityCalculator.cs` — swap `using` to `Contracts`.
- `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs` — swap/add `using` to `Contracts` (keep the use-case `using` too if `GetPurchaseStockAnalysisRequest` is still referenced from there).
- New: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockSeverity.cs`
- New: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockStatusFilter.cs`
- New: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockAnalysisSortBy.cs`

Files touched (tests, `using` directives only):
- `backend/test/Anela.Heblo.Tests/Features/Purchase/StockSeverityCalculatorTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs`
- Any other test file the FR-3 grep surfaces as referencing these three enums by unqualified name.

## Dependencies
- None external. Depends only on the existing `Features/Purchase/Contracts/` folder already present in the codebase and its established conventions (file-scoped namespace `Anela.Heblo.Application.Features.Purchase.Contracts`, one type per file).
- Indirectly touches the OpenAPI client generation pipeline (`docs/development/api-client-generation.md`) only in that it must be re-run/verified as part of validation — no changes to that pipeline itself are required.

## Out of Scope
- Any change to enum member names, order, or values.
- Any change to `StockSeverityCalculator` business logic, `GetPurchaseStockAnalysisHandler` filtering/sorting/summary logic, or `LowStockEfficiencyTile` dashboard behavior.
- The `Manufacture` module's separate, unrelated `ManufacturingStockSeverity` enum (`Features/Manufacture/UseCases/GetStockAnalysis/GetManufacturingStockAnalysisResponse.cs`) and its associated services (`ManufactureAnalysisMapper`, `ItemFilterService`, `ManufactureSeverityCalculator`) — explicitly not touched by this change.
- Any consolidation of `StockSeverity` and `ManufacturingStockSeverity` into a shared type — that is a separate, larger design decision not requested by this brief.
- Renaming, restructuring, or otherwise changing `GetPurchaseStockAnalysisRequest`/`Response` beyond removing the two/one enum bodies.
- Frontend code changes — the TypeScript client is auto-generated and, per NFR-3, is expected to be unaffected at the schema level; if the client regeneration does surface a diff, that is a signal to revisit this spec's assumption, not something to patch around in the frontend.

## Open Questions
None.

## Status: COMPLETE
