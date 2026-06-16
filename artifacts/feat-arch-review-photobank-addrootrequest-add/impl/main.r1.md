# Implementation: Decouple Photobank MediatR Requests from HTTP Body DTOs

## What was implemented

Three slim `*Body` contract DTOs were added to the Photobank module's `Contracts/` directory, and `PhotobankController` was updated so that `AddRoot`, `AddRule`, and `RetagPhotos` bind `[FromBody]` to the new DTOs and map them to the existing MediatR `*Request` types via inline object initializers — matching the pattern already used by `AddPhotoTag`, `BulkAddPhotoTag`, `CreateTag`, and `BulkAddPhotoTagByIds`. Three controller-level xUnit tests were added to cover the field-by-field `Body → Request` mapping and verify action-specific HTTP status codes. The OpenAPI TypeScript client was regenerated; generated class names for the three endpoints changed from `*Request` to `*Body` but JSON wire shapes are byte-for-byte identical.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/AddRootBody.cs` — new plain class DTO with `SharePointPath`, `DisplayName`, `DriveId`
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/AddRuleBody.cs` — new plain class DTO with `PathPattern`, `TagName`, `SortOrder`
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/RetagPhotosBody.cs` — new plain class DTO with `PhotoIds` (default `Array.Empty<int>()`), `ClearExistingAiTags`
- `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` — three actions updated: `AddRoot` (lines 233–241), `AddRule` (lines 281–289), `RetagPhotos` (lines 203–209)
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankControllerBodyMappingTests.cs` — new test class with three tests
- `frontend/src/api/generated/api-client.ts` — regenerated (21 insertions, 21 deletions — class renames only)

## Tests

`backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankControllerBodyMappingTests.cs`:
- `AddRoot_MapsBodyToRequest_AndReturnsCreated` — verifies all 3 fields copied, action returns `CreatedAtActionResult` pointing to `GetRoots`
- `AddRule_MapsBodyToRequest_AndReturnsCreated` — verifies all 3 fields copied, action returns `CreatedAtActionResult` pointing to `GetRules`
- `RetagPhotos_MapsBodyToRequest_AndReturnsAccepted` — verifies both fields copied, action returns `AcceptedResult` (202)

All 3 pass. Full Photobank suite: 158 passed, 3 failed (pre-existing Docker/Testcontainers env failures in `PhotobankRepositoryGetTagsSqlShapeTests` — same count on `main`). Full backend suite: 5003 passed, 56 failed (all Docker/Testcontainers, pre-existing).

## How to verify

```bash
# Run new body-mapping tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~PhotobankControllerBodyMappingTests"
# Expected: 3 passed

# Run full Photobank suite
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Photobank"
# Expected: all previously-passing tests still pass

# Verify formatting
dotnet format backend/Anela.Heblo.sln --verify-no-changes
# Expected: exit 0

# Verify UpdateRule not touched
git diff main -- backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs \
  | grep -E "UpdateRule|UpdateRuleRequest" | head
# Expected: no output
```

## Notes

- `npm run lint` returns 161 errors on the frontend — all pre-existing (same count on `main`), located in test utilities, entirely unrelated to the Body DTO changes. `npm run build` compiles cleanly.
- `AddRoot` and `AddRule` preserve `CreatedAtAction(nameof(GetRoots/GetRules), response)` on success (201). `RetagPhotos` preserves `Accepted(result) : BadRequest(result)` (202/400). None were regressed to `Ok()`.
- `UpdateRule` (line 299) has the same coupling pattern but is explicitly out of scope per spec; it was not touched.
- The frontend (`usePhotobank.ts`, `usePhotobankSettings.ts`) calls these endpoints via raw `apiPost` with inline JSON — no call sites reference generated class names, so no frontend code required manual updates.

## PR Summary

Decoupled three `PhotobankController` actions (`AddRoot`, `AddRule`, `RetagPhotos`) from directly binding the MediatR request types by introducing slim `*Body` DTOs in `Contracts/` and mapping them to the existing `*Request` types inside the controller. This closes a latent security exposure (future server-populated fields on `*Request` can't be spoofed from the client), aligns the endpoints with the project's established contract pattern, and matches the four endpoints in the same controller that already follow the convention.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/AddRootBody.cs` — new DTO mirroring client-supplied fields of `AddRootRequest`
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/AddRuleBody.cs` — new DTO mirroring client-supplied fields of `AddRuleRequest`
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/RetagPhotosBody.cs` — new DTO mirroring client-supplied fields of `RetagPhotosRequest`
- `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` — three actions updated to use Body types with inline object-initializer mapping
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankControllerBodyMappingTests.cs` — new controller-level tests for Body→Request mapping and HTTP status codes
- `frontend/src/api/generated/api-client.ts` — regenerated; generated class names updated from `*Request` to `*Body`, JSON wire shapes unchanged

## Status
DONE_WITH_CONCERNS

Concerns:
1. `npm run lint` returns 161 pre-existing errors (same on `main`). Unrelated to this change, but technically violates the strict reading of FR-5.
2. 3 Photobank integration tests fail due to Docker/Testcontainers not available in the local environment. Pre-existing, same on `main`. Not caused by this change.
