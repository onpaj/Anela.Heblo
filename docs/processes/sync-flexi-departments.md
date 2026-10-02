---
process: sync-flexi-departments
kind: sync
module: user-management
summary: On-demand, 10-minute-cached read of the cost centres (střediska) from Flexi, offered as the department filter in the financial overview and as a choice in invoice classification rules.
owns:
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/**
  - backend/src/Anela.Heblo.Domain/Features/UserManagement/**
  - backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IDepartmentQueryService.cs
  - backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/DepartmentDto.cs
  - backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetDepartments/**
  - backend/src/Anela.Heblo.API/Controllers/DepartmentsController.cs
  - frontend/src/api/hooks/useDepartments.ts
verified_at: "5e993f9e2"
related: [sync-flexi-analytics]
---

# Departments (Flexi cost centres)

## Purpose
"Departments" in Heblo are the accounting **cost centres (střediska)** kept in Flexi
(ABRA FlexiBee), not HR departments. Heblo reads their code and name so people can pick them:
- **Financial overview** (Finanční přehled) — filter "exclude departments"
  (`FinancialFilters`, sent as `excludedDepartments` to the financial overview query).
- **Invoice classification** (Klasifikace faktur) — rule form and rules list, to assign a
  cost centre to a classification rule.

Despite living in the UserManagement code folder, it has nothing to do with users.

## Trigger
On demand: `GET /api/Departments` (`GetDepartmentsHandler`), called by the frontend hook
`useDepartments`. Allowed for anyone holding **either** `finance.financial_overview.read` **or**
`purchase.invoice_classification.read` (`[FeatureAuthorize(Feature.Finance_FinancialOverview,
Feature.Purchase_InvoiceClassification)]`, OR semantics).

## Data flow
1. `FlexiDepartmentQueryService` → `FlexiDepartmentClient` (Heblo's
   `Domain.Features.UserManagement.IDepartmentClient`).
2. Cache hit on `flexi_departments` (in-memory, **10 minutes**) → return.
3. Otherwise the Flexi SDK client `Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient.GetAsync`
   reads all cost centres from Flexi (evidence `stredisko`).
4. Each becomes `Department { Id = Flexi code, Name }` → `DepartmentDto { Id, Name }`.
5. Response `{ success, departments[] }`.

## Logic & formulas
- No filtering, sorting or validity check: every cost centre Flexi returns is listed, in
  Flexi's order. `Id` is the Flexi **code** (e.g. what accounting uses on documents), not a
  numeric id.
- Any exception → `success = false`, `ErrorCode = InternalServerError`, empty list (logged).
- `GetDepartmentByIdAsync` (lookup by code from the cached list) exists on the interface.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Cache TTL (code constant `CacheDuration`) | 10 min | How long the list is reused |
| Flexi connection (`FlexiBeeSettings`) | per environment | Which Flexi company is read |

## Runtime facts
None.

## Known quirks
- **A new or renamed cost centre appears after up to 10 minutes** (per server process).
- **Separate from reporting.** The nightly `sync-flexi-analytics` job copies the same cost
  centres into `flexi_raw` for Metabase through a different service (`DepartmentSyncService`);
  this on-demand list does not use that copy.
- **Errors look like an empty filter.** A Flexi outage returns `success = false` with an empty
  list; the filter then simply has no options.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs` — Flexi read, cache
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetDepartments/GetDepartmentsHandler.cs` — error handling
- `backend/src/Anela.Heblo.API/Controllers/DepartmentsController.cs` — endpoint and permission
- `frontend/src/components/pages/financial-overview/FinancialFilters.tsx`, `frontend/src/pages/InvoiceClassification/components/RuleForm.tsx` — consumers
