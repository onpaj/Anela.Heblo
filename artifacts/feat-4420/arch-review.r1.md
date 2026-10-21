# Architecture Review: Bank Statement Statistics DTO

## Skip Design: true
Backend contract refactor plus a mechanical type rename in the frontend; no new or changed UI.

## Architectural Fit Assessment
Aligns the use case with `development_guidelines.md` and the existing `GetInvoiceImportStatistics` pattern (verified: handler projects with `.Select(c => new DailyInvoiceCountDto {...})`). Domain type is used by `IAnalyticsRepository`, `IBankStatementStatisticsSource` and `BankStatementStatisticsSourceAdapter`; these stay as-is (domain-internal).

## Proposed Architecture

### Component Overview
Repository (domain `DailyBankStatementStatistics`) -> Handler (maps) -> Response (`DailyBankStatementStatisticsDto`) -> Controller -> OpenAPI -> generated TS client.

### Key Design Decisions

#### Decision 1: Plain class DTO, manual mapping
**Options considered:** record DTO; AutoMapper; manual projection.
**Chosen approach:** plain class, manual `.Select`.
**Rationale:** CLAUDE.md forbids record DTOs; the sibling handler maps manually.

#### Decision 2: Accept the generated TS type rename
**Options considered:** keep schema name via attribute; accept rename.
**Chosen approach:** accept rename and update three frontend files.
**Rationale:** the DTO suffix is the project convention; the rename is small and compile-checked.

## Implementation Guidance

### Directory / Module Structure
- New: `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/DailyBankStatementStatisticsDto.cs`
- Edit: `.../UseCases/GetBankStatementImportStatistics/GetBankStatementImportStatisticsResponse.cs`, `...Handler.cs`
- Edit (frontend): `useBankStatements.ts`, `BankStatementImportChart.tsx`, `__tests__/BankStatementImportChart.test.tsx`
- Test: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetBankStatementImportStatisticsHandlerTests.cs`

### Interfaces and Contracts
DTO with `Date`, `ImportCount`, `TotalItemCount` (class, settable properties). JSON unchanged.

### Data Flow
Handler gets `List<DailyBankStatementStatistics>` from the repository and projects to `List<DailyBankStatementStatisticsDto>`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Frontend breaks on regenerated type name | Low | Update the three importing files; run `npm run build`, lint and chart tests |
| Generated api-client.ts is stale in repo | Low | Regenerate via frontend prebuild if it is tracked; confirm diff |
| Mapping drift if domain gains fields | Low | Mapping unit test |

## Specification Amendments
None.

## Prerequisites
None.
