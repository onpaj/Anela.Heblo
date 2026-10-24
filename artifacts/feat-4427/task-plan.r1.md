# Task Plan

### task: move-interface
Move the interface and fix references.
1. `git mv backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxCompletionService.cs backend/src/Anela.Heblo.Application/Features/Logistics/Services/ITransportBoxCompletionService.cs`
2. Change namespace to `Anela.Heblo.Application.Features.Logistics.Services` (file-scoped style matching the sibling file). Keep contents otherwise identical.
3. Grep `backend/` (incl. tests) for `ITransportBoxCompletionService`; confirm `TransportBoxCompletionService.cs` and `LogisticsModule.cs` compile with correct usings; remove a using only if it became unused (Domain using is still needed for `ITransportBoxRepository`).
4. `cd backend && dotnet build && dotnet format` ; run Logistics tests.
**Acceptance:** build passes; no references to the old namespace for this type; no behaviour change.

### task: update-process-doc
Bump process doc for CI gate.
1. In `docs/processes/flow-transport-box.md` set `verified_at` to today; add the new interface path to owned code list if the front matter lists individual files.
2. Run `python3 scripts/process-docs/check.py index` and fix any reported issue.
**Acceptance:** check.py passes.
