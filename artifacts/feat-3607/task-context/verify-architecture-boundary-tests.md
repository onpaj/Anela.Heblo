### task: verify-architecture-boundary-tests

Run the full backend test suite, with particular attention to
`ModuleBoundariesTests.cs` (which now scans GiftSettings Application types under the
`Anela.Heblo.Application.Features.Logistics` prefix for the first time), and confirm the overall
diff is confined to what the spec allows.

**Step 1 — Run the architecture boundary tests in isolation.**

```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities/backend
dotnet test --filter "FullyQualifiedName~Anela.Heblo.Tests.Architecture.ModuleBoundariesTests" --logger "console;verbosity=normal"
```
Expected: all tests in this class pass, in particular (per spec FR-5) these tests that treat
`Anela.Heblo.Application.Features.Logistics` as part of the Logistics namespace trio must still
pass with **no allowlist changes**:
- `Catalog -> Logistics` (uses `CatalogLogisticsAllowlist`)
- `ExpeditionList -> Logistics` (uses `ExpeditionListLogisticsAllowlist`)
- `ShoptetApi Adapters -> Logistics` (uses `ShoptetApiAdaptersLogisticsAllowlist`)
- `Logistics -> Manufacture` (uses `LogisticsAllowlist`)
- `Logistics -> Catalog` (uses `LogisticsCatalogAllowlist`)
- `Logistics_types_should_not_reference_Purchase_owned_namespaces`

If any of these fail with a new violation, the failure message will name the offending
`SourceType -> TargetType` pair. GiftSettings' only legitimate cross-module dependency is
`Anela.Heblo.Domain.Features.Users.ICurrentUserService` (used in `SetGiftSettingHandler`), which
is not Manufacture/Catalog/Purchase-owned and should not trigger any of the above checks. If a
different, unexpected reference is flagged:
- Do not silently add it to an allowlist without justification.
- Check whether it can be resolved via the existing contract-inversion pattern described in
  `docs/architecture/development_guidelines.md` (the `ILeafletKnowledgeSource` example).
- If an allowlist addition is genuinely warranted, add a one-line comment in
  `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` following the existing
  style (see the comment above `LogisticsAllowlist` at line 29 or `ExpeditionListLogisticsAllowlist`
  at line 240 for the expected format), and re-run this step until green. This should not be
  necessary for this specific move — treat any occurrence as a signal to stop and investigate
  before proceeding, since FR-5's acceptance criterion expects zero allowlist changes.

**Step 2 — Run the full backend test suite.**

```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities/backend
dotnet test
```
Expected: `Passed!` summary with `Failed: 0`. Compare the total test count against the count on
`main` before this branch's changes (via `git log`/CI history if available) to confirm no tests
were lost or silently skipped during the move — the GiftSettings suite specifically must still
report 13 passing tests (2 + 6 + 5, per the previous task's Step 9).

**Step 3 — Confirm the diff is confined to the expected files.**

```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities
git diff --stat main...HEAD
```
Expected: only these paths appear, with the 8 moved files shown as renames:
- `backend/src/Anela.Heblo.Application/Features/GiftSettings/...` → `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/...` (8 files, renamed)
- `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs`
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs`
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/GetGiftSettingHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingValidatorTests.cs`

Nothing under `backend/src/Anela.Heblo.Domain/`, `backend/src/Anela.Heblo.Persistence/`, any
`Migrations/` folder, or `frontend/` should appear. If anything else shows up, investigate before
proceeding — per spec FR-4, those layers must be byte-for-byte unchanged.

**Step 4 — Confirm zero remaining old-namespace references repo-wide (final check).**

```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities
grep -rn "Anela\.Heblo\.Application\.Features\.GiftSettings\b" --include="*.cs" .
```
Expected: no output. (This re-runs the FR-2 acceptance check as a final gate, now against the
entire repository rather than just `backend/`.)

**Step 5 — Confirm `dotnet build` is still clean after all edits.**

```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities/backend
dotnet build
```
Expected: `Build succeeded.`, `0 Error(s)`.

**Step 6 — No commit in this task unless Step 1's allowlist branch was exercised.**

This task is verification-only. If Step 1 required no allowlist change (the expected outcome),
there is nothing new to commit — the previous two tasks' commits already contain the complete
fix. If an allowlist entry genuinely had to be added in Step 1, commit it separately:
```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "Add justified allowlist entry surfaced by GiftSettings Logistics move"
```
