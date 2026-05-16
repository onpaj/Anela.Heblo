# Logistics: no UserId helper, no sentinel GUID

**Date:** 2026-05-17
**Context:** `LogisticsController.CreateGiftPackageManufacture` and `DisassembleGiftPackage` historically parsed `ICurrentUserService.GetCurrentUser().Id` to a GUID and fell back to the sentinel `00000000-0000-0000-0000-000000000001`. The resolved GUID was stamped onto `request.UserId`.

**Discovery:** `request.UserId` was read by no handler, service, repository, or persisted column. The only audit field on `GiftPackageManufactureLog` is `CreatedBy` (string), already populated by `GiftPackageManufactureService` via its existing `ICurrentUserService.GetCurrentUser().Name` call.

**Decision:** Delete the resolution and the DTO `UserId` properties outright. Do not extract a `UserIdResolver` helper; there is no consumer.

**If a real GUID audit column is later required:**
- Add it to `GiftPackageManufactureLog` via migration + domain change.
- Populate it inside `GiftPackageManufactureService` (next to the existing `_currentUserService` usage), not in a handler or a shared resolver.
- Use the existing `TransportBox` pattern: `Guid.TryParse(user.Id, out var userId) ? userId : null` — nullable, no sentinel. See `CreateNewTransportBoxHandler.cs:43` and `OpenOrResumeBoxByCodeHandler.cs:72`.

**Out of scope (still latent):** `DashboardController` has the same dead-code shape. Same delete-and-simplify treatment will apply when it is touched.
