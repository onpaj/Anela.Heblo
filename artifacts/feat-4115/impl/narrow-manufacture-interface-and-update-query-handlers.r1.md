# Implementation: narrow-manufacture-interface-and-update-query-handlers

## What was implemented
Narrowed `IGiftPackageManufactureService` to only the two write methods
(`CreateManufactureAsync`, `DisassembleGiftPackageAsync`), removing
`GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync`. Repointed the
two read-only query handlers (`GetAvailableGiftPackagesHandler`,
`GetGiftPackageDetailHandler`) to depend on the previously-added
`IGiftPackageQueryService` instead — completing the ISP split for
`GiftPackageManufactureService`. No behavioral change; the concrete service
implementation and the two write handlers/tests were untouched.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageManufactureService.cs` — narrowed to `CreateManufactureAsync` and `DisassembleGiftPackageAsync` only
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/GetAvailableGiftPackages/GetAvailableGiftPackagesHandler.cs` — constructor/field type changed from `IGiftPackageManufactureService` to `IGiftPackageQueryService`
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/GetGiftPackageDetail/GetGiftPackageDetailHandler.cs` — constructor/field type changed from `IGiftPackageManufactureService` to `IGiftPackageQueryService`

## Tests
No new test files (per task scope — none exist for these two handlers today
and adding them was declared out of scope). Verified that
`CreateGiftPackageManufactureHandlerTests.cs` and
`DisassembleGiftPackageHandlerTests.cs` were not touched (`git diff --stat`
empty for both), confirming FR-6.

## How to verify
```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GiftPackageManufacture"
```
Expect 0 build errors and all 21 GiftPackageManufacture-related tests passing.

## Notes
- Build progression matched the task's expected intermediate states exactly:
  after Step 1 only two CS1061 errors (one per handler) appeared; after Step 6
  the solution built with 0 errors, 165 warnings (pre-existing, unrelated
  nullable-reference warnings elsewhere in the solution).
- Grepped the whole backend (src + test) for `GetAvailableGiftPackagesAsync`
  and `GetGiftPackageDetailAsync`: the only interface-typed call sites are the
  two handlers just updated; all other occurrences are on the concrete
  `GiftPackageManufactureService`/`_service` (its own implementation, its
  internal calls, and `GiftPackageManufactureServiceTests.cs`), which are
  unaffected by narrowing the interface.
- The `[DisplayName("GiftPackageManufacture-{0}-{1}x")]` attribute text
  mismatch against the implementation's `{0}-{1}` was kept byte-for-byte as
  instructed — out of scope for this change.

## PR Summary
Splits `IGiftPackageManufactureService` per ISP: it now exposes only the two
write operations (create/disassemble manufacture), while the two read-only
gift-package query handlers consume the previously-introduced
`IGiftPackageQueryService`. Pure interface/DI-wiring change with no behavior
change — verified by a clean full-solution build and the existing
GiftPackageManufacture test suite (21/21 passing).

### Changes
- `IGiftPackageManufactureService.cs` — removed the two read methods
- `GetAvailableGiftPackagesHandler.cs` — depends on `IGiftPackageQueryService`
- `GetGiftPackageDetailHandler.cs` — depends on `IGiftPackageQueryService`

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01ShBJBzrsondLpmHVcTrKUq

## Status
DONE
