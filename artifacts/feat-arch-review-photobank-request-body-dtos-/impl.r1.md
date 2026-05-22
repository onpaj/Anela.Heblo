# Implementation: Relocate Photobank request-body DTOs to the Application project

## What was implemented

Pure structural refactor: four request-body DTO classes (`AddPhotoTagBody`, `CreateTagBody`, `BulkAddPhotoTagBody`, `BulkAddPhotoTagByIdsBody`) were moved verbatim from inline declarations at the bottom of `PhotobankController.cs` (API project) into four individual files under `Application/Features/Photobank/Contracts/`. The controller gained one `using` directive to resolve the relocated types. No behavioral changes.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/AddPhotoTagBody.cs` — request body for `POST /api/photobank/photos/{id}/tags`
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/CreateTagBody.cs` — request body for `POST /api/photobank/tags`
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/BulkAddPhotoTagBody.cs` — request body for `POST /api/photobank/photos/bulk-tag`
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/BulkAddPhotoTagByIdsBody.cs` — request body for `POST /api/photobank/photos/tag-by-ids`
- `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` — added `using Anela.Heblo.Application.Features.Photobank.Contracts;`, removed the four inline class declarations (23 lines)

## Tests

No new tests written — this is a pure structural relocation with no new behavior. Existing suite verified:
- `dotnet test` — 3,764 tests pass (includes PhotobankControllerThumbnailTests, PhotobankTagsCacheTests, PhotobankRepositoryGetTagsTests, and 7 other Photobank-scoped test classes)

## How to verify

```bash
dotnet build                                                       # 0 errors
dotnet format --verify-no-changes                                  # clean
git diff --exit-code frontend/src/api/generated/api-client.ts     # empty (FR-3)
dotnet test --filter "FullyQualifiedName~Photobank"               # all pass
```

## Notes

- The code quality reviewer initially flagged `string.Empty`, `List<string>?` without initializer, and `= []` as inconsistent with sibling files. These suggestions were rejected: the spec requires verbatim copy of the original properties including their default initializers, and changing them (e.g., `string.Empty` → `null!`) would alter runtime behavior and violate FR-3's byte-identical client requirement.
- `= []` (collection expression) is valid C# 12 / .NET 8 syntax, consistent with the original source.

## PR Summary

Moves four Photobank request-body DTO classes (`AddPhotoTagBody`, `CreateTagBody`, `BulkAddPhotoTagBody`, `BulkAddPhotoTagByIdsBody`) from inline declarations in `PhotobankController.cs` (API project) into dedicated files in `Application/Features/Photobank/Contracts/`, restoring the documented "API project never owns DTOs" architectural boundary.

The classes are relocated verbatim — identical names, properties, types, and default initializers. The controller gains one `using` directive. The regenerated TypeScript client is byte-for-byte identical (NSwag names types by C# class name, not CLR namespace). All 3,764 backend tests pass.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/AddPhotoTagBody.cs` — new DTO file
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/CreateTagBody.cs` — new DTO file
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/BulkAddPhotoTagBody.cs` — new DTO file
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/BulkAddPhotoTagByIdsBody.cs` — new DTO file
- `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` — added Contracts using, removed inline class declarations

## Status
DONE
