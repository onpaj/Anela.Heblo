## Module
InvoiceClassification

## Finding
`IDepartmentClient` and `Department` are defined in `Anela.Heblo.Domain.Features.InvoiceClassification` but have **zero consumers inside the InvoiceClassification module**:

- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IDepartmentClient.cs` (lines 1–8)
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Department.cs` (lines 1–6)

All actual consumers sit outside this module:
- **Implementation**: `Adapters.Flexi.Accounting.Departments.FlexiDepartmentClient` (`FlexiDepartmentClient.cs` line 7 — `public class FlexiDepartmentClient : Domain.Features.InvoiceClassification.IDepartmentClient`)
- **Consumer 1**: `Adapters.Flexi.Analytics.DepartmentSyncService` (`DepartmentSyncService.cs` line 21)
- **Consumer 2**: `Adapters.Flexi.Accounting.Departments.FlexiDepartmentQueryService` (`FlexiDepartmentQueryService.cs` line 11)

The InvoiceClassification module itself references departments only as plain `string?` values (`ClassificationRule.Department`, `ClassificationHistory.Department`) — it never calls `IDepartmentClient` or constructs a `Department` instance.

## Why it matters
The contract that a domain module exports should reflect what that module needs. `IDepartmentClient` and `Department` are organisational/accounting concepts currently squatting in InvoiceClassification's namespace. This:

- **Misleads readers** into thinking InvoiceClassification manages departments.
- **Couples the adapter layer** to InvoiceClassification's namespace for no technical reason.
- Violates the development guidelines rule that each module owns only the types it actually consumes.
- Creates a path for InvoiceClassification to accidentally gain a hard dependency on department data if someone wires `IDepartmentClient` into a handler.

## Suggested fix
Move both types to the module that owns the concept. `DepartmentSyncService` and `FlexiDepartmentQueryService` belong to the Analytics / Accounting context, so the natural home is `Anela.Heblo.Domain.Features.Analytics` (a folder that already exists) or a dedicated `Anela.Heblo.Domain.Features.OrgChart` folder if departments are treated as a first-class module.

1. Move `Department.cs` → `backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs` (update namespace to `Anela.Heblo.Domain.Features.Analytics`).
2. Move `IDepartmentClient.cs` → same folder, same namespace update.
3. Update the three external consumers' `using` statements accordingly.
4. No InvoiceClassification code needs to change — it never referenced these types.

---
_Filed by daily arch-review routine on 2026-09-14._