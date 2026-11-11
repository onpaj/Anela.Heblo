### task: move-interface
Move the interface and fix references.
1. `git mv backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxCompletionService.cs backend/src/Anela.Heblo.Application/Features/Logistics/Services/ITransportBoxCompletionService.cs`
2. Change namespace to `Anela.Heblo.Application.Features.Logistics.Services` (file-scoped style matching the sibling file). Keep contents otherwise identical.
3. Grep `backend/` (incl. tests) for `ITransportBoxCompletionService`; confirm `TransportBoxCompletionService.cs` and `LogisticsModule.cs` compile with correct usings; remove a using only if it became unused (Domain using is still needed for `ITransportBoxRepository`).
4. `cd backend && dotnet build && dotnet format` ; run Logistics tests.
**Acceptance:** build passes; no references to the old namespace for this type; no behaviour change.

