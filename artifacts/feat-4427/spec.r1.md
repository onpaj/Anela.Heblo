# Specification: Move ITransportBoxCompletionService to Application layer

## Summary
Relocate `ITransportBoxCompletionService` from Domain to Application (co-located with `TransportBoxCompletionService`). Pure refactor, no behaviour change.

## Background
Interface is an orchestration contract (polls `ILogisticsStockOperationQueryService`), violating the Domain-layer scope in `docs/architecture/filesystem.md`. Its only consumers are `TransportBoxCompletionService` and `LogisticsModule` (both Application layer). Grep confirms no other .cs references (no tests, no Domain/API usage).

## Functional Requirements

### FR-1: Move interface
Move `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxCompletionService.cs` to `backend/src/Anela.Heblo.Application/Features/Logistics/Services/ITransportBoxCompletionService.cs` (git mv), changing namespace to `Anela.Heblo.Application.Features.Logistics.Services`. Signature and XML docs unchanged.
**Acceptance criteria:**
- Old file no longer exists; new file exists with new namespace.
- `nameof(ITransportBoxCompletionService.CompleteReceivedBoxesAsync)` still yields the same task id (type name unchanged), so the `BackgroundRefresh:ITransportBoxCompletionService:...` config keys remain valid.

### FR-2: Usings
`TransportBoxCompletionService.cs` is in the same namespace (no using needed); keep its Domain using (still needed for `ITransportBoxRepository`). `LogisticsModule.cs` already imports `...Logistics.Services` and still needs the Domain using for `ITransportBoxRepository`; verify, remove only if unused.

### FR-3: Process doc
`docs/processes/flow-transport-box.md` owns `Domain/Features/Logistics/Transport/**`; touching that path triggers the Process Docs CI check. Update/bump `verified_at` (and add the new interface path to owned code if appropriate); run `python3 scripts/process-docs/check.py index`.

## Non-Functional Requirements
- NFR-1: No runtime behaviour, DI registration, or config change.
- NFR-2: `dotnet build` and `dotnet format` clean; existing Logistics tests pass.

## Data Model / API
None.

## Dependencies
None. Out of scope: historical docs under `docs/plans`, `docs/features` (point-in-time records).
