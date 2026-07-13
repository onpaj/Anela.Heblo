### task: update-call-sites-and-namespaces

Update the 5 files that reference the old `Anela.Heblo.Application.Features.GiftSettings`
namespace but are not themselves moved: the controller, `ApplicationModule.cs`, and the 3 test
files. After this task the solution must build cleanly.

**Step 1 — Edit `GiftSettingsController.cs`.**

File: `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs`

Before (lines 1-2):
```csharp
using Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting;
using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
```

After (lines 1-2):
```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.GetGiftSetting;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting;
```
No other line in this file changes — route (`api/gift-settings`), `[FeatureAuthorize(...)]`
attributes, and method bodies (`GetGiftSetting`, `SetGiftSetting`) stay exactly as-is.

**Step 2 — Edit `ApplicationModule.cs`.**

File: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

Before (line 36):
```csharp
using Anela.Heblo.Application.Features.GiftSettings;
```

After (line 36):
```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings;
```
Do **not** change line 111 (`services.AddGiftSettingsModule();`) — the extension method name and
call-site position are unchanged per spec FR-3. Do not reorder the `using` line relative to its
neighbors (keep surgical — the design doc notes reordering next to the
`GiftPackageManufacture` using is optional/cosmetic, and is skipped here to minimize the diff).

**Step 3 — Edit `GetGiftSettingHandlerTests.cs`.**

File: `backend/test/Anela.Heblo.Tests/Application/GiftSettings/GetGiftSettingHandlerTests.cs`

Before (lines 1-2):
```csharp
using Anela.Heblo.Application.Features.GiftSettings.Dto;
using Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting;
```

After (lines 1-2):
```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.Dto;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.GetGiftSetting;
```
Do not change line 8 (`namespace Anela.Heblo.Tests.Application.GiftSettings;`) — this is the
test project's own namespace (independent of production code, stays flat per spec FR-6) and is
out of scope. Do not change any assertions or arrangements.

**Step 4 — Edit `SetGiftSettingHandlerTests.cs`.**

File: `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs`

Before (line 1):
```csharp
using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
```

After (line 1):
```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting;
```
Do not change line 9 (`namespace Anela.Heblo.Tests.Application.GiftSettings;`) or any test
method body.

**Step 5 — Edit `SetGiftSettingValidatorTests.cs`.**

File: `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingValidatorTests.cs`

Before (line 1):
```csharp
using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
```

After (line 1):
```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting;
```
Do not change line 6 (`namespace Anela.Heblo.Tests.Application.GiftSettings;`) or any test
method body.

**Step 6 — Repo-wide search for any remaining old-namespace reference.**

```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities
grep -rn "Anela\.Heblo\.Application\.Features\.GiftSettings" --include="*.cs" backend/
```
Expected: no output (empty). If any match remains outside the 5 files above, update it using the
same before/after mapping in the table in the plan intro, then re-run this grep until it is
empty.

Also confirm the Domain/Persistence namespaces (which look similar but must NOT be touched) are
still present and unchanged:
```
grep -rn "Anela\.Heblo\.Domain\.Features\.Logistics\.GiftSettings\|Anela\.Heblo\.Persistence\.Logistics\.GiftSettings" --include="*.cs" backend/ | wc -l
```
Expected: a non-zero count (these lines must still exist, unmodified, in
`GetGiftSettingHandler.cs`, `SetGiftSettingHandler.cs`, `GiftSettingsModule.cs`, and the 2 test
files that assert against the `GiftSetting`/`IGiftSettingRepository` domain types).

**Step 7 — Build the solution.**

```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities/backend
dotnet build
```
Expected: `Build succeeded.` with `0 Error(s)`. If there are new warnings not present before this
change, investigate — FR-2's acceptance criterion requires zero new warnings. (Pre-existing
warnings unrelated to GiftSettings are out of scope.)

**Step 8 — Run `dotnet format` (per CLAUDE.md validation requirement).**

```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities/backend
dotnet format --verify-no-changes
```
If this reports formatting differences confined to the moved/edited files (e.g. whitespace), run
`dotnet format` without `--verify-no-changes` to apply fixes, then re-run `dotnet build` to
confirm it still succeeds.

**Step 9 — Run the GiftSettings test suite and confirm the same test count as before the move.**

Confirm the suite passes and the count matches the known fixture set: `GetGiftSettingHandlerTests`
has 2 `[Fact]` methods, `SetGiftSettingHandlerTests` has 6 `[Fact]` methods, and
`SetGiftSettingValidatorTests` has 5 `[Fact]` methods — 13 tests total:

```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities/backend
dotnet test --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.GiftSettings" --logger "console;verbosity=normal"
```
Expected: `Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13` (or the equivalent summary
line — exact count must be 13; if it differs, one of the 3 test files was not discovered
correctly and its namespace/using edits must be re-checked).

**Step 10 — Commit.**

```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities
git add backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs \
        backend/src/Anela.Heblo.Application/ApplicationModule.cs \
        backend/test/Anela.Heblo.Tests/Application/GiftSettings/GetGiftSettingHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingValidatorTests.cs
git status
git commit -m "Update GiftSettings call sites to new Logistics.UseCases namespace

Updates using directives in GiftSettingsController, ApplicationModule, and the
3 GiftSettings test files to reference the relocated
Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings namespace.
No behavior, route, or DI registration changes."
```

---

