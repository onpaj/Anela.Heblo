# Implementation Plan: Align GiftSettings Module Boundaries with Logistics

## Goal

Relocate `backend/src/Anela.Heblo.Application/Features/GiftSettings/` (8 files) into
`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/`, changing only
namespaces and `using` directives so that the Application layer for GiftSettings joins its
Domain (`Domain/Features/Logistics/GiftSettings/`) and Persistence
(`Persistence/Logistics/GiftSettings/`) counterparts under the `Logistics` module — exactly
mirroring the existing `GiftPackageManufacture` precedent
(`Application/Features/Logistics/UseCases/GiftPackageManufacture/`).

This is a pure namespace/folder move. No behavior, no public HTTP contract, no DI registration
semantics, no Domain/Persistence code, and no frontend code changes. Reference source docs:
`artifacts/feat-3607/spec.r1.md`, `artifacts/feat-3607/arch-review.r1.md`,
`artifacts/feat-3607/design.r1.md`.

### Files in scope

Moved (git mv), namespace changed:
1. `backend/src/Anela.Heblo.Application/Features/GiftSettings/GiftSettingsModule.cs`
2. `backend/src/Anela.Heblo.Application/Features/GiftSettings/Dto/GiftSettingDto.cs`
3. `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingQuery.cs`
4. `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingHandler.cs`
5. `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingCommand.cs`
6. `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs`
7. `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingResponse.cs`
8. `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingValidator.cs`

Not moved, `using`-only edits:
- `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs`
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs`
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/GetGiftSettingHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingValidatorTests.cs`

Untouched (confirmed already correct, must show zero diff): `Domain/Features/Logistics/GiftSettings/*`, `Persistence/Logistics/GiftSettings/*`, `ApplicationDbContext.cs`, all EF migrations, `frontend/**`.

Namespace mapping used throughout this plan:

| Old namespace | New namespace |
|---|---|
| `Anela.Heblo.Application.Features.GiftSettings` | `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings` |
| `Anela.Heblo.Application.Features.GiftSettings.Dto` | `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.Dto` |
| `Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting` | `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.GetGiftSetting` |
| `Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting` | `Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting` |

Note: none of the 8 source files under the current `GiftSettings/` tree contain a stray
self-referential `using` for their own namespace (verified by reading each file) — no dead-`using`
cleanup is needed beyond the mechanical namespace substitution below.

---

### task: relocate-giftsettings-application-files

Move the 8 Application-layer files to their new location and update only their `namespace` /
internal `using` lines. No logic changes.

**Step 1 — Confirm starting state (baseline check).**

Run:
```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities
find backend/src/Anela.Heblo.Application/Features/GiftSettings -type f
```
Expected output (8 lines, order may vary):
```
backend/src/Anela.Heblo.Application/Features/GiftSettings/GiftSettingsModule.cs
backend/src/Anela.Heblo.Application/Features/GiftSettings/Dto/GiftSettingDto.cs
backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingQuery.cs
backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingHandler.cs
backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingCommand.cs
backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs
backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingResponse.cs
backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingValidator.cs
```

**Step 2 — Create the new directory tree and git-mv each file, preserving history.**

```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities
mkdir -p backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/Dto
mkdir -p backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/GetGiftSetting
mkdir -p backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting

git mv backend/src/Anela.Heblo.Application/Features/GiftSettings/GiftSettingsModule.cs \
       backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/GiftSettingsModule.cs

git mv backend/src/Anela.Heblo.Application/Features/GiftSettings/Dto/GiftSettingDto.cs \
       backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/Dto/GiftSettingDto.cs

git mv backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingQuery.cs \
       backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingQuery.cs

git mv backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingHandler.cs \
       backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingHandler.cs

git mv backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingCommand.cs \
       backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingCommand.cs

git mv backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs \
       backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs

git mv backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingResponse.cs \
       backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingResponse.cs

git mv backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingValidator.cs \
       backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingValidator.cs
```

**Step 3 — Delete the now-empty old directory tree.**

```
find backend/src/Anela.Heblo.Application/Features/GiftSettings -type f
```
Expected: no output (empty). Then remove leftover empty directories:
```
find backend/src/Anela.Heblo.Application/Features/GiftSettings -type d -empty -delete
```
Verify the whole tree is gone:
```
ls backend/src/Anela.Heblo.Application/Features/GiftSettings 2>&1
```
Expected: `ls: cannot access '...': No such file or directory`.

**Step 4 — Edit `GiftSettingsModule.cs` at its new path.**

File: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/GiftSettingsModule.cs`

Before:
```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Persistence.Logistics.GiftSettings;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.GiftSettings;
```

After:
```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Persistence.Logistics.GiftSettings;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings;
```
The rest of the file (the `AddGiftSettingsModule` method body) is unchanged — do not touch the
`using Anela.Heblo.Persistence.Logistics.GiftSettings;` or
`using Anela.Heblo.Domain.Features.Logistics.GiftSettings;` lines, they are already correct.

Use the Edit tool with `old_string` set to the two changed lines
(`using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;` and
`namespace Anela.Heblo.Application.Features.GiftSettings;`) replaced individually with their
"After" counterparts above.

**Step 5 — Edit `Dto/GiftSettingDto.cs` at its new path.**

File: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/Dto/GiftSettingDto.cs`

Before (line 1):
```csharp
namespace Anela.Heblo.Application.Features.GiftSettings.Dto;
```

After (line 1):
```csharp
namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.Dto;
```
No other lines in this file change.

**Step 6 — Edit `UseCases/GetGiftSetting/GetGiftSettingQuery.cs` at its new path.**

File: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingQuery.cs`

Before:
```csharp
using Anela.Heblo.Application.Features.GiftSettings.Dto;
using MediatR;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting;
```

After:
```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.Dto;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.GetGiftSetting;
```
The rest of the file (`public sealed class GetGiftSettingQuery : IRequest<GiftSettingDto> { }`) is
unchanged.

**Step 7 — Edit `UseCases/GetGiftSetting/GetGiftSettingHandler.cs` at its new path.**

File: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingHandler.cs`

Before:
```csharp
using Anela.Heblo.Application.Features.GiftSettings.Dto;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using MediatR;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting;
```

After:
```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.Dto;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.GetGiftSetting;
```
Do not touch `using Anela.Heblo.Domain.Features.Logistics.GiftSettings;` — already correct. The
rest of the class body (`GetGiftSettingHandler`, constructor, `Handle` method) is unchanged.

**Step 8 — Edit `UseCases/SetGiftSetting/SetGiftSettingCommand.cs` at its new path.**

File: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingCommand.cs`

Before:
```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
```

After:
```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting;
```
Class body (`IsEnabled`, `ThresholdCzk`, `Text` properties) is unchanged.

**Step 9 — Edit `UseCases/SetGiftSetting/SetGiftSettingHandler.cs` at its new path.**

File: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs`

Before:
```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
```

After:
```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting;
```
Do not touch `using Anela.Heblo.Domain.Features.Logistics.GiftSettings;` or
`using Anela.Heblo.Domain.Features.Users;` — already correct. The entire `Handle` method body
(validation branches, `GiftSetting` construction, `_repository.SaveAsync` call) is unchanged.

**Step 10 — Edit `UseCases/SetGiftSetting/SetGiftSettingResponse.cs` at its new path.**

File: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingResponse.cs`

Before:
```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
```

After:
```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting;
```
`public sealed class SetGiftSettingResponse : BaseResponse { }` is unchanged.

**Step 11 — Edit `UseCases/SetGiftSetting/SetGiftSettingValidator.cs` at its new path.**

File: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingValidator.cs`

Before:
```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
```

After:
```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting;
```
The validator body (`RuleFor`/`When` rules) is unchanged.

**Step 12 — Verify no old-namespace references remain in the moved files.**

```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities
grep -rn "Anela\.Heblo\.Application\.Features\.GiftSettings" backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/
```
Expected: no output (empty — the grep matches zero lines because the old namespace string no
longer appears anywhere in the moved files).

**Step 13 — Commit.**

```
cd /home/user/worktrees/feature-3607-Arch-Review-Logistics-Giftsettings-Domain-Entities
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings backend/src/Anela.Heblo.Application/Features/GiftSettings
git status
```
Confirm `git status` shows the 8 files as renames (`renamed:`) from
`Features/GiftSettings/...` to `Features/Logistics/UseCases/GiftSettings/...`, and nothing under
`Domain/`, `Persistence/`, or `frontend/`. Then:
```
git commit -m "Move GiftSettings Application layer under Logistics.UseCases

Relocates backend/src/Anela.Heblo.Application/Features/GiftSettings/ to
Features/Logistics/UseCases/GiftSettings/, matching the GiftPackageManufacture
precedent. Namespace-only change; no behavior or contract changes."
```
(Note: this task does not yet build — call sites in `ApplicationModule.cs`, the controller, and
the test files still reference the old namespace. That is fixed in the next task.)

---

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
