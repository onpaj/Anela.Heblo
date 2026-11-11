# Design

No UI or API changes. Code-structure change only:

Before: `Domain/Features/Logistics/Transport/ITransportBoxCompletionService.cs` (ns `Anela.Heblo.Domain.Features.Logistics.Transport`).
After: `Application/Features/Logistics/Services/ITransportBoxCompletionService.cs` (ns `Anela.Heblo.Application.Features.Logistics.Services`), next to `TransportBoxCompletionService`.

DI (`AddTransient<ITransportBoxCompletionService, TransportBoxCompletionService>`) and `RegisterRefreshTask<ITransportBoxCompletionService>` unchanged.
