---

# Implementation: Move ProductExportOptions to FileStorage Module

## What was implemented

Pure C# namespace-relocation refactor: `ProductExportOptions` moved from `Anela.Heblo.Domain.Features.Configuration` to `Anela.Heblo.Application.Features.FileStorage`. Class body, defaults, XML docs, and DI section key are byte-for-byte unchanged. Aligns with the project's Vertical Slice convention.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs` — new location (renamed from Domain, namespace updated)
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs` — removed stale `using`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs` — removed stale `using`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs` — removed stale `using`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — added `using Anela.Heblo.Application.Features.FileStorage;` (Domain using preserved for ConfigurationConstants)
- 5 test files under `backend/test/Anela.Heblo.Tests/Features/FileStorage/` — swapped or removed stale `using Anela.Heblo.Domain.Features.Configuration;`

## Tests

All 101 FileStorage tests pass. No new tests added (pure refactor, no behavior change per NFR-2).

## How to verify

```bash
cd backend
dotnet build src/Anela.Heblo.sln --nologo
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.FileStorage"
grep -rln "Anela.Heblo.Domain.Features.Configuration" src test | xargs -I {} grep -l "ProductExportOptions" {} 2>/dev/null  # must return empty
```

## Notes

- Git tracked the file move as a rename (`{Domain → Application}/ProductExportOptions.cs`), so commit shows 10 files changed rather than 11 (1 rename = 1 entry, not 2).
- Follow-up items (out of scope per NFR-4): move `Configure<ProductExportOptions>` into `FileStorageModule.AddFileStorageModule`, add `SectionName` constant, rename the misleading `Tests/.../Configuration/` test folder.

## PR Summary

Relocate `ProductExportOptions` to the FileStorage module where it exclusively belongs. The class modeled retry policy, timeouts, and download URL for the product export job — concepts internal to FileStorage with no relationship to the Configuration module. Moving it eliminates a cross-module Domain-level dependency and aligns with the project's Vertical Slice convention, where every other single-file options class (Article, Leaflet, OrgChart, MeetingTasks, KnowledgeBase) sits inside its own feature slice.

The change is a pure relocation: class body, defaults, section key (`"ProductExportOptions"`), and DI binding semantics are byte-for-byte unchanged. Only namespace and using directives moved.

### Changes
- `backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs` — new home of the class (namespace `Anela.Heblo.Application.Features.FileStorage`)
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs` — deleted
- 3 production consumers — stale `using Anela.Heblo.Domain.Features.Configuration;` removed (parent-namespace resolution takes over)
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — `using Anela.Heblo.Application.Features.FileStorage;` added; Domain using preserved for `ConfigurationConstants`
- 5 test files — `using` swapped or removed as appropriate

## Status
DONE