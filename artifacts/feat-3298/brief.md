## Module
UserManagement

## Finding
`GraphService` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`) is an I/O-bound service that:
- acquires OAuth tokens via `ITokenAcquisition` (Microsoft.Identity.Web)
- creates HTTP clients via `IHttpClientFactory`
- calls the Microsoft Graph REST API over HTTP

The project already has an established pattern for exactly this kind of class: `PhotobankGraphService` (`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/PhotobankGraphService.cs`) implements `IPhotobankGraphService` from the Application layer and lives in the `Adapters.Microsoft365` project, registered via `Microsoft365AdapterServiceCollectionExtensions`.

`filesystem.md` documents the I/O placement rule explicitly:
> Concrete implementations and any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`.

`GraphService` violates this rule by living in `Application/Features/UserManagement/Services/` instead of `Anela.Heblo.Adapters.Microsoft365/UserManagement/`.

Additionally, `UserManagementModule.cs` (line 33) unconditionally registers `services.AddHttpClient("MicrosoftGraph")` in the `else` branch, which duplicates the named-client registration already owned by `Microsoft365AdapterServiceCollectionExtensions.AddMicrosoft365Adapter()`. If the `GraphService` were moved to the adapter project, this registration would be consolidated correctly there.

## Why it matters
- Breaks the I/O-layer boundary documented in `filesystem.md`: Application layer should contain business logic, not HTTP infrastructure.
- Inconsistent with `PhotobankGraphService`, creating two patterns for the same concern (Graph API HTTP calls).
- `UserManagementModule` (Application layer) currently references `Microsoft.Identity.Web` and `Microsoft.Graph` SDK — infrastructure dependencies that do not belong in the Application layer.

## Suggested fix
1. Move `GraphService.cs` and `MockGraphService.cs` to `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/` (or a similar subdirectory).
2. Move the `IGraphService` interface to `Application/Features/UserManagement/Services/` (it stays in Application as the contract — the provider pattern, same as `IPhotobankGraphService`).
3. Register `IGraphService → GraphService` (and the mock variant) inside `Microsoft365AdapterServiceCollectionExtensions.AddMicrosoft365Adapter()`, removing those registrations from `UserManagementModule`.
4. Remove the redundant `services.AddHttpClient("MicrosoftGraph")` from `UserManagementModule` (the adapter already owns that registration).

---
_Filed by daily arch-review routine on 2026-06-22._
