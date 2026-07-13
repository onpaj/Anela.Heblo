# Code Review: rename LogisticsModule.AddTransportModule to AddLogisticsModule

## Summary
The implementation performs exactly the four textual edits specified: the method declaration in `LogisticsModule.cs`, the call site in `ApplicationModule.cs`, and the two documentation examples in `development_guidelines.md` and `infrastructure.md`. Verified directly against `git show e4435e7` — the diff contains only these four one-line changes, matching FR-1 through FR-4 exactly, with no unrelated edits. Build succeeds with 0 errors and `dotnet format --verify-no-changes` reports no violations.

## Review Result: PASS

### task: rename-add-transport-module-to-add-logistics-module
**Status:** PASS

## Verification performed
- `git show e4435e7`: confirms a 4-file, 4-line diff (2 backend files, 2 docs), each an exact `AddTransportModule` → `AddLogisticsModule` swap in place, no reordering, no body/signature changes beyond the identifier.
- FR-1 (`LogisticsModule.cs:17`): method renamed to `public static IServiceCollection AddLogisticsModule(this IServiceCollection services)`, body untouched — confirmed.
- FR-2 (`ApplicationModule.cs:92`): call site updated to `services.AddLogisticsModule();`, retains its original position between `AddManufactureModule(configuration)` and `AddGiftPackageManufactureModule()` — confirmed.
- FR-3 (`docs/architecture/development_guidelines.md:158`): `.AddLogisticsModule()` now appears between `.AddManufactureModule()` and `.AddPurchaseModule()` in the fluent chain — confirmed.
- FR-4 (`docs/architecture/infrastructure.md:143`): `.AddLogisticsModule()` now appears between `.AddPurchaseModule()` and `.AddApplicationServices()` — confirmed.
- Repo-wide `grep -rn "AddTransportModule"` (excluding `.git`): only hits remaining are historical/pipeline artifacts explicitly out of scope per spec — `docs/superpowers/plans/2026-06-01-decouple-catalog-repository-from-providers.md` and various `artifacts/**` records (arch-review reports, briefs, specs, task-context, impl summary for this very task). No live code or living-doc reference to the old name remains.
- `grep -rn "AddLogisticsModule\|AddTransportModule" backend --include="*.cs"`: only the two expected `.cs` hits (declaration + call site), no test files reference either name.
- `dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj`: 0 errors, 156 warnings — all pre-existing and unrelated to this change (nullable-reference warnings elsewhere, one pre-existing MSB3073 from an unrelated code-gen post-build step), matching the impl summary's claim.
- `dotnet format src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --include <touched files> --verify-no-changes`: no output, i.e., no formatting violations.
- Architecture guidance in `arch-review.r1.md` (no compatibility shim, no structural change, no DTO/OpenAPI surface involved) is honored — the diff is a pure identifier rename with no shim added.

## Docs to Update
None beyond what's already covered by this task. The out-of-scope historical documents (`docs/superpowers/plans/2026-06-01-...md`, prior `artifacts/**` records) correctly remain untouched per spec's explicit scope boundary — they are dated/historical records, not living documentation.

## Overall Notes
Clean, minimal, spec-compliant rename. All four functional requirements and their acceptance criteria are met precisely, with no scope creep and no missed occurrences. No issues found.
