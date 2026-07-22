# Implementation: remove-dead-getbyname

## What was implemented
Removed the unreachable `GetByNameAsync` method from both `FlexiSupplierRepository` (production Flexi adapter) and `MockSupplierRepository` (test double), since `ISupplierRepository` never declared this method and there were no call sites. Also removed the duplicate `using Anela.Heblo.Domain.Features.Purchase;` line at the top of `MockSupplierRepository.cs`.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/FlexiSupplierRepository.cs` — deleted the `GetByNameAsync(string name, CancellationToken)` method (previously between `GetByIdAsync` and `GetAllSuppliersFromCacheAsync`). `SearchSuppliersAsync` and `GetByIdAsync` untouched.
- `backend/test/Anela.Heblo.Tests/Controllers/MockSupplierRepository.cs` — deleted the corresponding `GetByNameAsync` method and removed the duplicate `using Anela.Heblo.Domain.Features.Purchase;` line (now exactly one occurrence).

## Tests
- `grep -rn "GetByNameAsync" backend/` — zero matches, confirming full removal.
- `dotnet build Anela.Heblo.sln` (from worktree root) — succeeded, 0 errors, 250 pre-existing warnings unrelated to this change (nullable reference warnings in test files).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Supplier" --no-build` — Passed: 7, Failed: 0, Skipped: 0.

## How to verify
1. `grep -rn "GetByNameAsync" backend/` should return nothing.
2. `dotnet build Anela.Heblo.sln` from the worktree root should build with 0 errors.
3. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Supplier"` should show all Supplier-related tests passing.

## Notes
- The solution file is at the worktree root (`Anela.Heblo.sln`), not under `backend/` as the task instructions assumed — used the correct path for the build.
- No changes made to `ISupplierRepository` (out of scope, confirmed it never declared `GetByNameAsync`).
- Only staged and committed the two `backend/` files; `artifacts/feat-3705/state.json` was left modified/unstaged as it is not mine to change in this task.

## PR Summary
This change removes dead code: an unreachable `GetByNameAsync` method existed on both the production `FlexiSupplierRepository` and the test `MockSupplierRepository`, despite `ISupplierRepository` never declaring it and no call sites existing anywhere in the codebase. Removing it eliminates unused surface area and a maintenance burden without any behavioral change. While cleaning up the mock, a duplicate `using Anela.Heblo.Domain.Features.Purchase;` import line was also removed. Verified via a repo-wide grep (zero remaining references to `GetByNameAsync`), a full solution build (0 errors), and the Supplier-tagged test suite (7/7 passing).

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/FlexiSupplierRepository.cs` — removed unused `GetByNameAsync` method.
- `backend/test/Anela.Heblo.Tests/Controllers/MockSupplierRepository.cs` — removed unused `GetByNameAsync` method and a duplicate `using` directive.

## Status
DONE
