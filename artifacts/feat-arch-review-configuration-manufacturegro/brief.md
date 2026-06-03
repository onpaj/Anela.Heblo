## Module
Configuration

## Finding
`GetConfigurationResponse` (line 37) exposes `ManufactureGroupId`, and `GetConfigurationHandler` reads it from `IConfiguration` with a raw string literal `"ManufactureGroupId"` (line 34 — no constant defined for this key):

- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs:37`
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs:34`

`ManufactureGroupId` is an Azure Entra group identifier belonging to the **Manufacture** module's domain (it identifies "responsible persons" for manufacture orders). The Configuration module now has to be modified whenever the Manufacture module changes how it identifies its Entra group, and the Configuration response DTO carries Manufacture-specific knowledge.

The comment on the response property acknowledges the anonymous-endpoint rationale, but does not address the module boundary concern.

## Why it matters
This is a creeping pattern: if every module adds its bootstrap-time config values to `GetConfigurationResponse`, the Configuration module becomes an implicit shared bag for all other modules — violating the Vertical Slice principle that modules do not share DTOs or leak domain concepts across boundaries. The raw string `"ManufactureGroupId"` (no constant) also means typos would be silent failures.

## Suggested fix
Two viable approaches (smallest first):

1. **Introduce a typed constant and keep it in Configuration, but document it as an intentional cross-module bootstrap exception.** At minimum add `ConfigurationConstants.MANUFACTURE_GROUP_ID = "ManufactureGroupId"` so the key is not a floating literal. This is the lowest-effort stabilisation.

2. **Move the Entra group lookup to the Manufacture module.** Add a `/api/manufacture/config` endpoint (or a `GET /api/manufacture/settings`) that returns Manufacture-specific bootstrap values. The SPA calls both endpoints at startup. This cleanly separates the concerns at the cost of one extra HTTP request.

Option 2 is architecturally correct; option 1 is acceptable if the team deliberately accepts this as a pragmatic exception and documents it as such in the feature or architecture docs.

---
_Filed by daily arch-review routine on 2026-05-29._