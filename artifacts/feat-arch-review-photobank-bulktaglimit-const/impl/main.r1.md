Committed. Here is the output summary:

---

# Implementation: Extract BulkTagLimit to PhotobankConstants

## What was implemented

DRY refactor consolidating the duplicated `private const int BulkTagLimit = 5_000` from two Photobank bulk-tag handlers into a single shared `PhotobankConstants` class. No behavioral change — same limit value, same error code, same error payload shape.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankConstants.cs` — new file, `public static class` with `public const int BulkTagLimit = 5_000`, file-scoped namespace
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/BulkAddPhotoTag/BulkAddPhotoTagHandler.cs` — removed private const, replaced 2 references with `PhotobankConstants.BulkTagLimit`
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/BulkAddPhotoTagByIds/BulkAddPhotoTagByIdsHandler.cs` — same changes
- `backend/test/Anela.Heblo.Tests/Features/Photobank/BulkAddPhotoTagHandlerTests.cs` — added `using Anela.Heblo.Application.Features.Photobank;`, updated Limit assertion to `PhotobankConstants.BulkTagLimit.ToString()`
- `backend/test/Anela.Heblo.Tests/Features/Photobank/BulkAddPhotoTagByIdsHandlerTests.cs` — same updates

## Tests

All 24 `BulkAddPhotoTag*` handler tests pass. The 3 pre-existing failures in `PhotobankRepositoryGetTagsSqlShapeTests` are Testcontainers/Docker tests that require Docker — pre-existing and unrelated.

## How to verify

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BulkAddPhotoTag"
# Expect: 24 passed
```

## Notes

- No new `using` directives were needed in the handler files — they are in child namespaces of `Anela.Heblo.Application.Features.Photobank` and get parent namespace resolution automatically in C#.
- `PhotobankConstants` uses file-scoped namespace to match the convention of all other `*Constants.cs` files in the feature layer.

## PR Summary

Extracted the duplicated `BulkTagLimit = 5_000` constant from two Photobank bulk-tag handlers into a single `PhotobankConstants` class, eliminating the drift risk where a future limit change could update one handler silently leaving the other stale. Test assertions updated to reference the constant rather than the magic string `"5000"`.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankConstants.cs` — new constants class (single source of truth for `BulkTagLimit`)
- `backend/src/.../BulkAddPhotoTag/BulkAddPhotoTagHandler.cs` — removed private const, references `PhotobankConstants.BulkTagLimit`
- `backend/src/.../BulkAddPhotoTagByIds/BulkAddPhotoTagByIdsHandler.cs` — same
- `backend/test/.../BulkAddPhotoTagHandlerTests.cs` — Limit assertion uses `PhotobankConstants.BulkTagLimit.ToString()`
- `backend/test/.../BulkAddPhotoTagByIdsHandlerTests.cs` — same

## Status
DONE