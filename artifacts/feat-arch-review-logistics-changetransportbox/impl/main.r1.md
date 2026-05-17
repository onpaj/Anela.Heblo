---

# Implementation: Relocate ChangeTransportBoxState Request/Response into Use Case Subfolder

## What was implemented

A pure structural refactor bringing `ChangeTransportBoxStateRequest.cs` and `ChangeTransportBoxStateResponse.cs` from the `UseCases/` root into the `UseCases/ChangeTransportBoxState/` subfolder. The namespace declarations in both files were updated to match. Four consuming files had their `using` directives fixed.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateRequest.cs` — moved here (was at `UseCases/`); namespace updated to `...UseCases.ChangeTransportBoxState`
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateResponse.cs` — moved here; namespace updated
- `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs` — bare `UseCases` using replaced with `UseCases.ChangeTransportBoxState`
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxControllerTests.cs` — orphaned bare `UseCases` using removed
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs` — orphaned bare `UseCases` using removed
- `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` — orphaned bare `UseCases` using removed

## Tests

No new tests added (spec's FR-5 and §Out of Scope confirm existing tests are sufficient). All 3,313 pre-existing tests pass unchanged.

## How to verify

```bash
cd /path/to/worktree
git show HEAD --stat          # 6 files: 2 renames + 4 using edits
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet format backend/Anela.Heblo.sln --verify-no-changes
grep -r "using Anela.Heblo.Application.Features.Logistics.UseCases;" backend --include="*.cs"
# Last command: zero matches expected
```

## Notes

- `git mv` was used for both files so history is preserved as renames, not delete+add.
- `ChangeTransportBoxStateHandler.cs` was intentionally left untouched — it already declares the target namespace and C# resolves the DTOs implicitly via the shared namespace.
- OpenAPI/NSwag identifies DTOs by class name, not CLR namespace — generated TypeScript client is unchanged.

## PR Summary

Relocates `ChangeTransportBoxStateRequest` and `ChangeTransportBoxStateResponse` from the `UseCases/` root into the `ChangeTransportBoxState/` subfolder to match the per-use-case layout used by every other use case in the Logistics module. The handler was already correctly placed; only the two DTO files and four `using` directives in consuming files required changes.

NSwag identifies DTOs by class name rather than CLR namespace, so the generated TypeScript client requires no changes.

### Changes
- `UseCases/ChangeTransportBoxState/ChangeTransportBoxStateRequest.cs` — moved from `UseCases/` root; namespace updated
- `UseCases/ChangeTransportBoxState/ChangeTransportBoxStateResponse.cs` — moved from `UseCases/` root; namespace updated
- `Controllers/TransportBoxController.cs` — bare `UseCases` import replaced with specific `UseCases.ChangeTransportBoxState` import
- `Tests/.../TransportBoxControllerTests.cs`, `ChangeTransportBoxStateHandlerTests.cs`, `TransportBoxUniquenessTests.cs` — orphaned bare `UseCases` import removed

## Status
DONE