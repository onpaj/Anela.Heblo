All tasks complete. Here is the output summary:

---

# Implementation: Decouple ManufactureGroupId from Configuration Module

## What was implemented

Moved `ManufactureGroupId` off the cross-cutting `/api/configuration` response into a new Manufacture-owned anonymous endpoint `/api/manufacture/settings`. The raw `"ManufactureGroupId"` string literal was replaced with a typed constant scoped to the Manufacture module.

## Files created/modified

### Backend — created
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureConfigurationKeys.cs` — typed constant `GroupId = "ManufactureGroupId"`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureSettings/GetManufactureSettingsRequest.cs` — empty MediatR request
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureSettings/GetManufactureSettingsResponse.cs` — class DTO inheriting BaseResponse, exposes `ManufactureGroupId?`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureSettings/GetManufactureSettingsHandler.cs` — reads config via typed constant, null-coalesces empty to null
- `backend/src/Anela.Heblo.API/Controllers/ManufactureSettingsController.cs` — `[Route("api/manufacture/settings")]`, `[AllowAnonymous]` on action
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/GetManufactureSettingsHandlerTests.cs` — 3 unit tests (configured / missing / empty)
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/GetManufactureSettingsEndpointTests.cs` — 3 integration tests (HTTP 200, content-type, anonymous access)

### Backend — modified
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — removed ManufactureGroupId read
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs` — removed ManufactureGroupId property
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationEndpointTests.cs` — removed `GetConfiguration_ShouldExposeManufactureGroupIdField` test

### Backend — deleted
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — entire file (contained only the 3 relocated ManufactureGroupId tests)

### Frontend — created
- `frontend/src/api/hooks/useManufactureSettings.ts` — React Query hook mirroring `useConfiguration.ts` pattern

### Frontend — modified
- `frontend/src/components/modals/CreateManufactureOrderModal.tsx` — swapped to `useManufactureSettingsQuery`
- `frontend/src/components/manufacture/list/ManufactureOrderFilters.tsx` — swapped to `useManufactureSettingsQuery`
- `frontend/src/components/manufacture/detail/BasicInfoSection.tsx` — swapped to `useManufactureSettingsQuery`
- `frontend/src/api/generated/api-client.ts` — regenerated via NSwag manual target

### Docs — modified
- `docs/architecture/development_guidelines.md` — added bullet on module-owned bootstrap endpoints

## Tests

- `GetManufactureSettingsHandlerTests.cs` — 3 unit tests for handler (configured / missing / empty value)
- `GetManufactureSettingsEndpointTests.cs` — 3 integration tests (200 response, content-type, anonymous access)
- All 6 new tests pass; all existing Configuration endpoint tests pass (5); pre-existing 32 Docker/Testcontainer failures are unrelated to this work

## How to verify

```bash
# Backend
cd backend
dotnet build
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetManufactureSettings"
dotnet format --verify-no-changes

# Frontend
cd frontend
npm run build
```

## Notes

- NSwag PostBuild client generation is disabled in the csproj (Condition="false") — regeneration was done via `dotnet msbuild -t:GenerateFrontendClientManual`
- 32 Docker/Testcontainers test failures are pre-existing and unrelated
- 130 frontend lint errors are pre-existing and unrelated
- `ManufactureModule.cs` was deliberately not modified — MediatR discovers the new handler via assembly scanning

## PR Summary

Decouples `ManufactureGroupId` from the cross-cutting Configuration module by moving it to a new Manufacture-owned anonymous endpoint `GET /api/manufacture/settings`. This removes a domain boundary violation where the Configuration module carried Manufacture-specific knowledge.

The change replaces the raw `"ManufactureGroupId"` string literal with a typed constant `ManufactureConfigurationKeys.GroupId`, adds a `ManufactureSettingsController` with explicit `[AllowAnonymous]` and a literal route (not `[controller]`), and updates the three frontend components that consumed the field to use a new `useManufactureSettingsQuery` hook — keeping React Query's automatic parallelism and identical cache settings (`staleTime: Infinity`). The backend and frontend ship together (single Docker image) so no versioning shim is needed.

### Changes
- `ManufactureConfigurationKeys.cs` — new typed constant for the bootstrap config key
- `UseCases/GetManufactureSettings/` — new MediatR use case (request, response, handler)
- `ManufactureSettingsController.cs` — new anonymous controller at `api/manufacture/settings`
- `GetManufactureSettingsHandlerTests.cs` + `GetManufactureSettingsEndpointTests.cs` — 6 new tests (3 unit + 3 integration including anonymous-access gate)
- `GetConfigurationHandler.cs` + `GetConfigurationResponse.cs` — ManufactureGroupId removed
- `GetConfigurationHandlerTests.cs` — deleted (was 3 ManufactureGroupId tests now relocated)
- `GetConfigurationEndpointTests.cs` — one stale test removed
- `useManufactureSettings.ts` — new React Query hook
- `CreateManufactureOrderModal.tsx`, `ManufactureOrderFilters.tsx`, `BasicInfoSection.tsx` — hook swap
- `development_guidelines.md` — documents module-owned bootstrap endpoint pattern

## Status
DONE