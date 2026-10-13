# Implementation: add-gift-package-query-service-interface

## What was implemented
Added the new read-only contract `IGiftPackageQueryService` in the
`GiftPackageManufacture` module's `Services` folder. It declares exactly the two
query methods that today also live on `IGiftPackageManufactureService`
(`GetAvailableGiftPackagesAsync`, `GetGiftPackageDetailAsync`) with identical
signatures — same parameter names, order, types and default values.

This step is purely additive as specified: `IGiftPackageManufactureService`, the
concrete `GiftPackageManufactureService`, the DI registration in
`GiftPackageManufactureModule` and all query handlers were left untouched, so
nothing yet consumes the new interface and the solution compiles exactly as
before.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageQueryService.cs` — new public interface with the two read methods, in namespace `Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services`, importing the module's `Contracts` namespace for `GiftPackageDto`.

## Tests
No test files were added. The task context specifies no tests for this step — an
empty, unconsumed interface declaration has no behaviour to assert. Behavioural
coverage arrives with the later tasks that move consumers onto the new
interface.

## How to verify
```bash
dotnet build Anela.Heblo.sln
```
Result: `0 Error(s)`, 252 pre-existing warnings (all in unrelated test files,
unchanged by this task). Signature parity was verified by diffing the two new
method declarations against the corresponding declarations in
`IGiftPackageManufactureService.cs` — they match character for character apart
from the surrounding interface name.

## Notes
- `using System.ComponentModel;` from `IGiftPackageManufactureService.cs` was
  deliberately not carried over: it exists there only for the `[DisplayName]`
  attribute on `CreateManufactureAsync`, which is a command method and is not
  part of the query contract.
- No deviations from the task context's prescribed file content.

## PR Summary
Introduces `IGiftPackageQueryService`, the read-only contract that
`GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync` will move to as
part of the Interface Segregation split of `IGiftPackageManufactureService` in
the Logistics / GiftPackageManufacture module.

This first step is intentionally additive only — the new interface is declared
but not yet implemented, registered, or consumed, so the existing behaviour and
wiring are untouched and the solution builds unchanged.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageQueryService.cs` — new interface declaring the two gift-package read methods

## Status
DONE
