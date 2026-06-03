Both reviews passed. Here is the output summary:

---

# Implementation: Replace `BypassJwtValidation` magic strings with `ConfigurationConstants.BYPASS_JWT_VALIDATION`

## What was implemented

A pure IL-equivalent refactor across five Application-layer C# module files. Each file received:
- One `using Anela.Heblo.Domain.Features.Configuration;` directive inserted in alphabetical position.
- One substitution of the raw string literal `"BypassJwtValidation"` with `ConfigurationConstants.BYPASS_JWT_VALIDATION` in the `GetValue<bool>(...)` call, preserving the positional `, false` argument style.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` — line 21 literal replaced, using added
- `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs` — line 38 literal replaced, using added
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — line 58 literal replaced, using added
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs` — line 27 literal replaced, using added
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` — line 41 literal replaced, using added

## Tests

No new tests added (per spec). Existing test suite ran via `dotnet test backend/backend.sln --no-build`. Pre-existing Docker/Testcontainer failures noted — unrelated to this change; no code-related failures.

## How to verify

```bash
# Zero magic string consumers in C# sources
grep -rn 'GetValue<bool>("BypassJwtValidation"' backend/src --include='*.cs'

# Exactly one string literal (the constant definition)
grep -rn '"BypassJwtValidation"' backend/src --include='*.cs'

# All 5 constant references present
grep -rn 'ConfigurationConstants\.BYPASS_JWT_VALIDATION' \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs \
  backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs \
  backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs \
  backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs \
  backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs

dotnet build backend/backend.sln
```

## Notes

- Local variable names (`bypassJwt` vs `bypassJwtValidation`) were preserved unchanged per spec.
- The adjacent `UseMockAuth` raw string literal on the preceding line in each file was intentionally left untouched — out of scope per spec and arch review.
- `appsettings.Development.json` and plan docs still contain `"BypassJwtValidation"` as expected (JSON config key and historical docs — not C# source).

## PR Summary

Replaced five raw `"BypassJwtValidation"` string literals in Application-layer module files with the existing `ConfigurationConstants.BYPASS_JWT_VALIDATION` constant, restoring the Domain constants file as the single source of truth for this configuration key. The API layer and `UserManagementModule` already used the constant correctly; these five modules were outliers.

The change is IL-equivalent — the compiler inlines `public const string` values, so no runtime behavior differs. A future rename of the configuration key is now compile-time safe across the entire codebase.

### Changes
- `MeetingTasksModule.cs` — added using, replaced literal on line 21
- `MarketingModule.cs` — added using, replaced literal on line 38
- `KnowledgeBaseModule.cs` — added using, replaced literal on line 58
- `CatalogDocumentsModule.cs` — added using, replaced literal on line 27
- `PhotobankModule.cs` — added using, replaced literal on line 41

## Status
DONE