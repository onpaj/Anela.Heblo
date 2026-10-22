# [arch-review] Logistics: ITransportBoxCompletionService lives in Domain layer but is an Application-layer orchestration interface

## Module
Logistics

## Finding
`ITransportBoxCompletionService` is declared in `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxCompletionService.cs`, but its implementation (`TransportBoxCompletionService`) already lives correctly in `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`.

The interface defines a background-processing orchestration contract: it polls `ILogisticsStockOperationQueryService` (an Application-layer abstraction) and transitions `TransportBox` entities to `Stocked`/`Error` based on external operation states. This is application-level coordination, not a pure domain service.

The only consumer is `LogisticsModule.RegisterRefreshTask<ITransportBoxCompletionService>(...)` in the Application layer, so the interface needs no Domain visibility.

## Why it matters
`docs/architecture/filesystem.md` restricts Domain to "Domain entities, aggregates, repository interfaces." An application-orchestration service interface in Domain violates that boundary.

## Suggested fix
Move the interface to `backend/src/Anela.Heblo.Application/Features/Logistics/Services/ITransportBoxCompletionService.cs` (co-located with its implementation). Update the `using` in `LogisticsModule.cs`. No behaviour changes needed.
