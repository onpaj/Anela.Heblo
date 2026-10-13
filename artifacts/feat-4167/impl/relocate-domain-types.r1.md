# Implementation: relocate-domain-types

## What was implemented
Moved the `Department` class and `IDepartmentClient` interface out of `Features/InvoiceClassification/` into `Features/Analytics/`, updating only the `namespace` declaration in each file. No other code was touched in this task — the four consumer files that still reference the old namespace are handled by subsequent tasks.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs` — moved from `InvoiceClassification/`, namespace updated to `Anela.Heblo.Domain.Features.Analytics`
- `backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs` — moved from `InvoiceClassification/`, namespace updated to `Anela.Heblo.Domain.Features.Analytics`
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Department.cs` — removed (git rename)
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IDepartmentClient.cs` — removed (git rename)

Both files were moved with `git mv` so history is preserved as renames.

## Tests
None added or modified — this task only relocates two files and updates their namespace declarations; behavior is unchanged.

## How to verify
```bash
ls backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ | grep -i department   # expect: no output
ls backend/src/Anela.Heblo.Domain/Features/Analytics/ | grep -i department               # expect: Department.cs, IDepartmentClient.cs
git show HEAD:backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs
git show HEAD:backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs
```
The task context explicitly notes the solution will not build after this step alone — four consumer files still reference the old `InvoiceClassification` namespace for these two types, and those are fixed by tasks 2-4 (`update-flexi-department-client`, `update-flexi-department-query-service-and-tests`, `update-di-registration-and-verify`). No build was run here, per the task's explicit instruction to defer that to later tasks.

## Notes
No deviations from the task context — files, namespace text, and commit message all match the spec verbatim. Confirmed both moved files read exactly as specified in the task context before moving on.

## PR Summary
Relocated the `Department` domain class and `IDepartmentClient` interface from `Features/InvoiceClassification/` to `Features/Analytics/`, since their only real consumers are in the Flexi Accounting adapter area and they have no relationship to `InvoiceClassification`'s own `Department` string fields. Only the namespace declarations changed; class/interface bodies are untouched. This is the first of four tasks that together move these types and update every consumer/DI registration to match.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs` — new location, namespace updated
- `backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs` — new location, namespace updated

## Status
DONE
