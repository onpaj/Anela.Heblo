### task: update-outlookeventdto-test-usings

This task updates the remaining two test files that construct `OutlookEventDto`/`GraphEventBody`/`GraphEventDateTime` directly, then verifies the whole solution builds and all Marketing tests pass.

1. Confirm the expected compile break first. From the repository root, build the test project:

   ```bash
   cd /home/user/worktrees/feature-4132-Arch-Review-Marketing-Outlookeventdto-Microsoft-Gr
   dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
   ```

   Expected output: build fails with `CS0246` ("The type or namespace name 'OutlookEventDto' could not be found") errors pointing at `ImportFromOutlookHandlerTests.cs` and `MarketingCalendarSyncServiceTests.cs`.

2. Open `backend/test/Anela.Heblo.Tests/Features/Marketing/ImportFromOutlookHandlerTests.cs`. Its current top is:

   ```csharp
   using System;
   using System.Collections.Generic;
   using System.Linq;
   using System.Threading;
   using Anela.Heblo.Application.Features.Marketing.Contracts;
   using Anela.Heblo.Application.Features.Marketing.Services;
   using Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;
   using Anela.Heblo.Application.Shared;
   using Anela.Heblo.Domain.Features.Marketing;
   using Anela.Heblo.Domain.Features.Users;
   using Anela.Heblo.Tests.Domain.Marketing;
   using FluentAssertions;
   using Microsoft.Extensions.Logging.Abstractions;
   using Moq;
   using Xunit;
   ```

   Add the new `using` (keep the existing `Services` using — still required for `IOutlookCalendarSync`/`IMarketingCategoryMapper` mocks), alphabetized:
   ```csharp
   using System;
   using System.Collections.Generic;
   using System.Linq;
   using System.Threading;
   using Anela.Heblo.Application.Features.Marketing.Contracts;
   using Anela.Heblo.Application.Features.Marketing.Infrastructure;
   using Anela.Heblo.Application.Features.Marketing.Services;
   using Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;
   using Anela.Heblo.Application.Shared;
   using Anela.Heblo.Domain.Features.Marketing;
   using Anela.Heblo.Domain.Features.Users;
   using Anela.Heblo.Tests.Domain.Marketing;
   using FluentAssertions;
   using Microsoft.Extensions.Logging.Abstractions;
   using Moq;
   using Xunit;
   ```
   Nothing else in the file changes — every `new OutlookEventDto { ... }`, `new GraphEventBody { ... }`, `new GraphEventDateTime { ... }` construction and every assertion keeps its exact current text.

3. Open `backend/test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCalendarSyncServiceTests.cs`. Its current top is:

   ```csharp
   using System;
   using System.Collections.Generic;
   using System.Linq;
   using System.Threading;
   using System.Threading.Tasks;
   using Anela.Heblo.Application.Features.Marketing.Contracts;
   using Anela.Heblo.Application.Features.Marketing.Services;
   using Anela.Heblo.Domain.Features.Marketing;
   using Anela.Heblo.Tests.Domain.Marketing;
   using FluentAssertions;
   using Microsoft.Extensions.Logging.Abstractions;
   using Moq;
   using Xunit;
   ```

   Add the new `using` (keep the existing `Services` using — still required for `SyncActor`, `IOutlookCalendarSync`, `IMarketingCategoryMapper`, `MarketingCalendarSyncService`), alphabetized:
   ```csharp
   using System;
   using System.Collections.Generic;
   using System.Linq;
   using System.Threading;
   using System.Threading.Tasks;
   using Anela.Heblo.Application.Features.Marketing.Contracts;
   using Anela.Heblo.Application.Features.Marketing.Infrastructure;
   using Anela.Heblo.Application.Features.Marketing.Services;
   using Anela.Heblo.Domain.Features.Marketing;
   using Anela.Heblo.Tests.Domain.Marketing;
   using FluentAssertions;
   using Microsoft.Extensions.Logging.Abstractions;
   using Moq;
   using Xunit;
   ```
   Nothing else in the file changes.

4. Build the test project again and confirm it now succeeds:

   ```bash
   cd /home/user/worktrees/feature-4132-Arch-Review-Marketing-Outlookeventdto-Microsoft-Gr
   dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
   ```

   Expected output: `Build succeeded.` with `0 Error(s)`.

5. Run the two directly-affected test classes and confirm every test still passes, unmodified in behavior:

   ```bash
   cd /home/user/worktrees/feature-4132-Arch-Review-Marketing-Outlookeventdto-Microsoft-Gr
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportFromOutlookHandlerTests"
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingCalendarSyncServiceTests"
   ```

   Expected output: both runs report a `Passed!` summary line with `0` failed.

6. Confirm no reference to `Anela.Heblo.Application.Features.Marketing.Services.OutlookEventDto`-style stale resolution remains and no `OutlookEventDto` construction was missed anywhere in the repo (the type now lives only under `Infrastructure`):

   ```bash
   grep -rln "OutlookEventDto\|GraphEventBody\|GraphEventDateTime" backend/ | xargs grep -L "Marketing.Infrastructure"
   ```

   Expected output: exactly one path — `backend/src/Anela.Heblo.Application/Features/Marketing/Infrastructure/OutlookEventDto.cs` (the declaration file itself does not need to import its own namespace). Every other file that matched the first grep must also contain a `Marketing.Infrastructure` using; if any other file is listed, it was missed and needs the same fix as steps 2–3.

7. Build the full solution to confirm nothing else regressed:

   ```bash
   cd /home/user/worktrees/feature-4132-Arch-Review-Marketing-Outlookeventdto-Microsoft-Gr
   dotnet build Anela.Heblo.sln
   ```

   Expected output: `Build succeeded.` with `0 Error(s)`.

8. Run the full backend test suite as the final regression gate:

   ```bash
   cd /home/user/worktrees/feature-4132-Arch-Review-Marketing-Outlookeventdto-Microsoft-Gr
   dotnet test Anela.Heblo.sln
   ```

   Expected output: `Passed!` summary line, `0` failed (the count matches whatever the suite reported before this change — this is a behavior-preserving refactor, so no new failures and no newly-skipped tests should appear).

9. Run `dotnet format` scoped to the two touched test files:

   ```bash
   cd /home/user/worktrees/feature-4132-Arch-Review-Marketing-Outlookeventdto-Microsoft-Gr
   dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --include backend/test/Anela.Heblo.Tests/Features/Marketing/ImportFromOutlookHandlerTests.cs backend/test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCalendarSyncServiceTests.cs
   ```

   Expected output: exits with no errors (whitespace-only changes at most — re-open both files afterward and confirm the edits from steps 2–3 are still intact).

10. Stage and commit the two test files:

    ```bash
    cd /home/user/worktrees/feature-4132-Arch-Review-Marketing-Outlookeventdto-Microsoft-Gr
    git add backend/test/Anela.Heblo.Tests/Features/Marketing/ImportFromOutlookHandlerTests.cs backend/test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCalendarSyncServiceTests.cs
    git commit -m "Update Marketing test usings for relocated OutlookEventDto"
    ```

    Expected output: a new commit containing exactly these two files.

---

## Self-review

**Spec coverage:**
- FR-1 (relocate file, change namespace, no `.csproj` change) — `relocate-outlookeventdto-and-update-production-usings` steps 1–2, step 6 confirms no `.csproj` edit was needed.
- FR-2 (`IOutlookCalendarSync.cs` using) — step 3.
- FR-3 (`NoOpOutlookCalendarSync.cs` using) — step 4.
- FR-4 (`OutlookEventImportMapper.cs` using, keep `Services` for `SyncActor`) — step 5.
- FR-5 (adapter `OutlookCalendarSyncService.cs` using, keep `Services`) — step 7.
- FR-6 (adapter `OutlookInternalDtos.cs` using **swap**) — step 8, verified by step 10's grep.
- FR-7 (`ImportFromOutlookHandlerTests.cs` using) — task 2 step 2.
- FR-8 (`MarketingCalendarSyncServiceTests.cs` using) — task 2 step 3.
- FR-9 (`MarketingCalendarSyncService.cs` untouched) — not edited by either task; confirmed by the inventory note above and by task 2 step 7's full-solution build (no unexplained diff).
- NFR-1 (behavior preservation) — every step shows exact before/after text with no logic change; task 2 steps 4–8 (build + targeted tests + full solution build + full test suite) verify nothing regressed.
- NFR-2 (security, N/A) — no action needed.
- Architecture review's amendment (use `git mv`, not delete+recreate) — task 1 step 1.

**Placeholder scan:** No "TBD"/"add validation"/"handle edge cases"/"similar to Task N" phrasing anywhere above; every code-bearing step shows the exact before/after file content or diff; every command step shows the exact command and expected output.

**Type consistency:** `OutlookEventDto`, `GraphEventBody`, `GraphEventDateTime`, and the namespace `Anela.Heblo.Application.Features.Marketing.Infrastructure` are identical across every step in both tasks — no drift. The `Services`-namespace symbols each file keeps (`SyncActor`, `IOutlookCalendarSync`, `IMarketingCategoryMapper`, `OutlookCalendarSyncException`, `MarketingCalendarSyncService`) match exactly what the verified consumer inventory says each file still needs.
