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

