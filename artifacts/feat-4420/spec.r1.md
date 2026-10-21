# Specification: Decouple Bank Statement Import Statistics API from Domain Type

## Summary
`GetBankStatementImportStatisticsResponse` currently exposes the Domain entity `DailyBankStatementStatistics` as its serialized payload. This work introduces a dedicated `DailyBankStatementStatisticsDto` in the Analytics `Contracts/` folder, switches the response to it, and maps explicitly in the handler, matching the sibling `GetInvoiceImportStatistics` use case. The wire format stays identical.

## Background
`docs/architecture/development_guidelines.md` requires API DTOs to live in the module's contracts and never be domain types. The Invoice statistics use case already follows this (`DailyInvoiceCountDto`). The generated TypeScript client currently emits a class `DailyBankStatementStatistics`, which the frontend hook (`useBankStatements.ts`), `BankStatementImportChart.tsx` and its test import by name. Renaming the C# payload type renames the generated TS class to `DailyBankStatementStatisticsDto`.

## Functional Requirements

### FR-1: Create DailyBankStatementStatisticsDto
Add a class (not a record) `DailyBankStatementStatisticsDto` in `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/` with `DateTime Date`, `int ImportCount`, `int TotalItemCount`.
**Acceptance criteria:**
- File exists in Contracts, namespace `Anela.Heblo.Application.Features.Analytics.Contracts`.
- Property names/types match the domain type so JSON is unchanged.

### FR-2: Response uses the DTO
`GetBankStatementImportStatisticsResponse.Statistics` becomes `List<DailyBankStatementStatisticsDto>`; the Domain `using` is removed from that file.
**Acceptance criteria:**
- Response file references no Domain type.

### FR-3: Handler maps explicitly
`GetBankStatementImportStatisticsHandler.Handle` projects each domain item to the DTO with `.Select(...).ToList()`.
**Acceptance criteria:**
- Mapped values equal source values (Date, ImportCount, TotalItemCount); empty input gives empty list.
- Repository interface and domain type are unchanged.

### FR-4: Frontend follows the regenerated client
The frontend references to `DailyBankStatementStatistics` (generated type) are updated to `DailyBankStatementStatisticsDto`.
**Acceptance criteria:**
- `npm run build` and `npm run lint` pass; `BankStatementImportChart` tests pass.

### FR-5: Tests
Existing handler tests keep passing; add a test asserting the mapping.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change; in-memory projection of at most ~31 daily items by default.

### NFR-2: Security
No change to auth or data exposure; only the same three fields are returned.

## Data Model
Domain `DailyBankStatementStatistics` (Date, ImportCount, TotalItemCount) unchanged. New DTO mirrors it.

## API / Interface Design
Endpoint `GET Analytics_GetBankStatementImportStatistics` keeps its route and JSON shape. Only the OpenAPI schema name changes from `DailyBankStatementStatistics` to `DailyBankStatementStatisticsDto`, so the generated TS type is renamed.

## Dependencies
- NSwag TypeScript client regeneration on frontend build.
- Frontend files: `frontend/src/api/hooks/useBankStatements.ts`, `frontend/src/components/charts/BankStatementImportChart.tsx`, `frontend/src/components/charts/__tests__/BankStatementImportChart.test.tsx`.

## Out of Scope
- Changing `IAnalyticsRepository` / `IBankStatementStatisticsSource` return types.
- Other DTO-leak findings in other modules.
- Adding fields to the DTO.

## Open Questions
None.

## Status: COMPLETE
