# Implementation: register-di

## What was implemented
Registered the four `ITransportBoxTransitionSideEffect` strategy implementations
(`NewToOpenedSideEffect`, `OpenToReserveSideEffect`, `OpenToQuarantineSideEffect`,
`ReceivedSideEffect`) and the `ITransportBoxInventoryRestorer` implementation
(`TransportBoxInventoryRestorer`) with the DI container in `LogisticsModule.cs`, so that
`ChangeTransportBoxStateHandler` — which consumes `IEnumerable<ITransportBoxTransitionSideEffect>`
and `ITransportBoxInventoryRestorer` via constructor injection — resolves correctly at runtime.

Verified against the actual repo state before writing the registrations: all five type names
and their namespace (`Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState`)
match the task-context snippet exactly — no deviation needed. Added a `using` for that namespace
(the file had none previously) and used the unqualified type names in the five `AddTransient`
calls, per the task's stated preference for readability with five referenced types.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` — added a `using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;` import and five `services.AddTransient<...>()` registrations (four side effects + one inventory restorer) immediately after the existing `ITransportBoxCompletionService` registration in `AddLogisticsModule()`.

## Tests
none — DI registration only.

## How to verify
1. `cd backend && dotnet build src/Anela.Heblo.Application` — build succeeds with 0 errors (confirmed: 136 pre-existing warnings unrelated to this change, 0 errors).
2. Inspect `LogisticsModule.cs` to confirm the five new `AddTransient` lines resolve `ITransportBoxTransitionSideEffect` (multiple registrations, consumed as `IEnumerable<>`) and `ITransportBoxInventoryRestorer` (single registration).
3. Optionally run any existing Logistics module DI/handler tests (e.g. around `ChangeTransportBoxStateHandler`) to confirm the handler resolves without a missing-service exception at runtime.

## Notes
No deviations from the task context — all five type names existed exactly as specified in the
plan (created by earlier pipeline tasks). Chose the `using`-import style over fully-qualified
names as the task instructions suggested for readability, since five types from the namespace
are referenced.

## PR Summary
This change wires up dependency injection for the transport-box state-transition side effects and
inventory restorer that `ChangeTransportBoxStateHandler` depends on. Without this registration,
resolving the handler would fail at runtime (or silently receive an empty `IEnumerable`, causing
transitions to skip their side effects and inventory restoration on cancel-back-to-New). The four
side-effect strategies (New→Opened, Open→Reserve, Open→Quarantine, →Received) are registered as
transient services implementing the shared `ITransportBoxTransitionSideEffect` interface, and the
single `TransportBoxInventoryRestorer` is registered for `ITransportBoxInventoryRestorer`.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` — register `ITransportBoxTransitionSideEffect` (4 implementations) and `ITransportBoxInventoryRestorer` (1 implementation) in `AddLogisticsModule()`.

## Status
DONE
