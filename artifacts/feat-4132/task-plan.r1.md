# Move OutlookEventDto to Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `OutlookEventDto`, `GraphEventBody`, `GraphEventDateTime` from `Features/Marketing/Services/` to `Features/Marketing/Infrastructure/`, updating the namespace and every consumer's `using` directives, with zero behavior change.

**Architecture:** Pure file relocation (`git mv`) plus a namespace-declaration edit on the moved file, plus `using`-directive-only edits on eight consumer files (six production, two test) — six gain an additional `using` (they also need symbols still in `Services/`), one swaps its `using` outright (it used nothing else from `Services/`). No signatures, logic, or behavior change anywhere.

**Tech Stack:** .NET 8, C#, xUnit (`backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`). Solution root: `/home/user/worktrees/feature-4132-Arch-Review-Marketing-Outlookeventdto-Microsoft-Gr/Anela.Heblo.sln` — run all `dotnet`/`git` commands from the repository root.

## Verified full consumer inventory (re-checked against source on disk)
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventDto.cs` — the file being moved (declares all three types).
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/IOutlookCalendarSync.cs` — uses `OutlookEventDto` in `ListEventsAsync`/`GetEventAsync`; interface stays in `Services/`.
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/NoOpOutlookCalendarSync.cs` — uses `OutlookEventDto` in the same two methods; class stays in `Services/`.
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs` — uses `OutlookEventDto` as a parameter; also needs `SyncActor` from `Services/` — keep both usings.
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs` — uses `OutlookEventDto` as return type and in `JsonSerializer.DeserializeAsync<OutlookEventDto>`; also needs `IOutlookCalendarSync`/`IMarketingCategoryMapper`/`OutlookCalendarSyncException` from `Services/` — keep both usings.
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookInternalDtos.cs` — uses only `OutlookEventDto` from `Services/` (`GraphEventCollection.Value`) — **swap** the using, don't add.
- `backend/test/Anela.Heblo.Tests/Features/Marketing/ImportFromOutlookHandlerTests.cs` — constructs `OutlookEventDto`/`GraphEventBody`/`GraphEventDateTime`; also needs `IOutlookCalendarSync`/`IMarketingCategoryMapper` from `Services/` — keep both usings.
- `backend/test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCalendarSyncServiceTests.cs` — constructs `OutlookEventDto`/`GraphEventDateTime`; also needs `SyncActor`/`IOutlookCalendarSync`/`IMarketingCategoryMapper`/`MarketingCalendarSyncService` from `Services/` — keep both usings.
- A repo-wide `grep -rn "OutlookEventDto\|GraphEventBody\|GraphEventDateTime" backend/` returns exactly these 8 files — no other call sites exist. `MarketingCalendarSyncService.cs` was checked and confirmed to reference none of the three type names directly (inferred `var` typing only) — **not edited**.

---

### task: relocate-outlookeventdto-and-update-production-usings

This task moves the file, updates its namespace, and fixes the five production consumers (interface, no-op implementation, mapper, and the two adapter files), so the whole solution except the test project compiles cleanly against the new namespace.

1. From the repository root, move the file with `git mv` so history is preserved:

   ```bash
   cd /home/user/worktrees/feature-4132-Arch-Review-Marketing-Outlookeventdto-Microsoft-Gr
   git mv backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventDto.cs backend/src/Anela.Heblo.Application/Features/Marketing/Infrastructure/OutlookEventDto.cs
   ```

   Expected output: no error; `git status` shows the file staged as a rename.

2. Open `backend/src/Anela.Heblo.Application/Features/Marketing/Infrastructure/OutlookEventDto.cs` (its new location). Its current full contents are:

   ```csharp
   using System.Text.Json.Serialization;

   namespace Anela.Heblo.Application.Features.Marketing.Services
   {
       public class OutlookEventDto
       {
           [JsonPropertyName("id")]
           public string Id { get; set; } = string.Empty;

           [JsonPropertyName("subject")]
           public string Subject { get; set; } = string.Empty;

           [JsonPropertyName("body")]
           public GraphEventBody? Body { get; set; }

           public string? BodyText => Body?.Content;

           [JsonPropertyName("start")]
           public GraphEventDateTime? Start { get; set; }

           [JsonPropertyName("end")]
           public GraphEventDateTime? End { get; set; }

           [JsonPropertyName("categories")]
           public string[] Categories { get; set; } = Array.Empty<string>();

           public DateTime StartUtc => Start is not null
               ? DateTime.Parse(Start.DateTimeString, null, System.Globalization.DateTimeStyles.RoundtripKind)
               : DateTime.MinValue;

           public DateTime EndUtc => End is not null
               ? DateTime.Parse(End.DateTimeString, null, System.Globalization.DateTimeStyles.RoundtripKind)
               : DateTime.MinValue;
       }

       public class GraphEventBody
       {
           [JsonPropertyName("content")]
           public string Content { get; set; } = string.Empty;

           [JsonPropertyName("contentType")]
           public string ContentType { get; set; } = "text";
       }

       public class GraphEventDateTime
       {
           [JsonPropertyName("dateTime")]
           public string DateTimeString { get; set; } = string.Empty;

           [JsonPropertyName("timeZone")]
           public string TimeZone { get; set; } = string.Empty;
       }

   }
   ```

   Change only the `namespace` line, from:
   ```csharp
   namespace Anela.Heblo.Application.Features.Marketing.Services
   ```
   to:
   ```csharp
   namespace Anela.Heblo.Application.Features.Marketing.Infrastructure
   ```
   Every other line (all three class bodies, the `using System.Text.Json.Serialization;`) is unchanged.

3. Open `backend/src/Anela.Heblo.Application/Features/Marketing/Services/IOutlookCalendarSync.cs`. Its current full contents are:

   ```csharp
   using Anela.Heblo.Domain.Features.Marketing;

   namespace Anela.Heblo.Application.Features.Marketing.Services
   {
       public interface IOutlookCalendarSync
       {
           Task<string> CreateEventAsync(MarketingAction action, CancellationToken ct);
           Task UpdateEventAsync(MarketingAction action, CancellationToken ct);
           Task DeleteEventAsync(string outlookEventId, CancellationToken ct);
           Task<IReadOnlyList<OutlookEventDto>> ListEventsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);

           /// <summary>
           /// Fetches a single event by id. Returns <c>null</c> when Graph reports 404
           /// (the event was deleted); throws <see cref="OutlookCalendarSyncException"/> on other failures.
           /// </summary>
           Task<OutlookEventDto?> GetEventAsync(string outlookEventId, CancellationToken ct);
       }
   }
   ```

   Add a new `using` line so the top becomes:
   ```csharp
   using Anela.Heblo.Application.Features.Marketing.Infrastructure;
   using Anela.Heblo.Domain.Features.Marketing;
   ```
   Nothing else in the file changes.

4. Open `backend/src/Anela.Heblo.Application/Features/Marketing/Services/NoOpOutlookCalendarSync.cs`. Its current top is:

   ```csharp
   using Anela.Heblo.Domain.Features.Marketing;
   using Microsoft.Extensions.Logging;
   ```

   Add the new `using` so it becomes:
   ```csharp
   using Anela.Heblo.Application.Features.Marketing.Infrastructure;
   using Anela.Heblo.Domain.Features.Marketing;
   using Microsoft.Extensions.Logging;
   ```
   Nothing else in the file changes — the `ListEventsAsync`/`GetEventAsync` method bodies keep using `OutlookEventDto` and `Array.Empty<OutlookEventDto>()` exactly as they do today.

5. Open `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs`. Its current top is:

   ```csharp
   using System;
   using System.Text.RegularExpressions;
   using Anela.Heblo.Application.Features.Marketing.Services;
   using Anela.Heblo.Domain.Features.Marketing;
   ```

   Add the new `using` (keep the existing `Services` using — it's still required for `SyncActor`), alphabetized:
   ```csharp
   using System;
   using System.Text.RegularExpressions;
   using Anela.Heblo.Application.Features.Marketing.Infrastructure;
   using Anela.Heblo.Application.Features.Marketing.Services;
   using Anela.Heblo.Domain.Features.Marketing;
   ```
   Nothing else in the file changes.

6. Confirm the expected compile break before fixing the adapter project. From the repository root, build the Application project (it will now succeed, since all its own consumers were just fixed):

   ```bash
   cd /home/user/worktrees/feature-4132-Arch-Review-Marketing-Outlookeventdto-Microsoft-Gr
   dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
   ```

   Expected output: `Build succeeded.` with `0 Error(s)`.

7. Open `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs`. Its current top is:

   ```csharp
   using System.Net;
   using System.Net.Http.Headers;
   using System.Text;
   using System.Text.Json;
   using Anela.Heblo.Application.Features.Marketing.Configuration;
   using Anela.Heblo.Application.Features.Marketing.Services;
   using Anela.Heblo.Domain.Features.Marketing;
   using Microsoft.Extensions.Logging;
   using Microsoft.Extensions.Options;
   using Microsoft.Identity.Client;
   using Microsoft.Identity.Web;
   ```

   Add the new `using` (keep the existing `Services` using — it's still required for `IOutlookCalendarSync`, `IMarketingCategoryMapper`, `OutlookCalendarSyncException`), alphabetized:
   ```csharp
   using System.Net;
   using System.Net.Http.Headers;
   using System.Text;
   using System.Text.Json;
   using Anela.Heblo.Application.Features.Marketing.Configuration;
   using Anela.Heblo.Application.Features.Marketing.Infrastructure;
   using Anela.Heblo.Application.Features.Marketing.Services;
   using Anela.Heblo.Domain.Features.Marketing;
   using Microsoft.Extensions.Logging;
   using Microsoft.Extensions.Options;
   using Microsoft.Identity.Client;
   using Microsoft.Identity.Web;
   ```
   Nothing else in the file changes — the `ListEventsAsync`/`GetEventAsync` bodies and `JsonSerializer.DeserializeAsync<OutlookEventDto>(...)` calls keep their exact current text.

8. Open `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookInternalDtos.cs`. Its current full contents are:

   ```csharp
   using System.Text.Json.Serialization;
   using Anela.Heblo.Application.Features.Marketing.Services;

   namespace Anela.Heblo.Adapters.Microsoft365
   {
       internal class GraphEventCollection
       {
           [JsonPropertyName("value")]
           public List<OutlookEventDto> Value { get; set; } = new();

           [JsonPropertyName("@odata.nextLink")]
           public string? NextLink { get; set; }
       }

       internal class OutlookEventIdPayload
       {
           [JsonPropertyName("id")]
           public string Id { get; set; } = string.Empty;
       }
   }
   ```

   This file uses **only** `OutlookEventDto` from the `Services` namespace — no other symbol. **Replace** (don't add to) the using line:
   ```csharp
   using System.Text.Json.Serialization;
   using Anela.Heblo.Application.Features.Marketing.Infrastructure;
   ```
   Nothing else in the file changes.

9. Build the adapter project and confirm it now succeeds:

   ```bash
   cd /home/user/worktrees/feature-4132-Arch-Review-Marketing-Outlookeventdto-Microsoft-Gr
   dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj
   ```

   Expected output: `Build succeeded.` with `0 Error(s)`.

10. Confirm no reference to `Anela.Heblo.Application.Features.Marketing.Services` remains in `OutlookInternalDtos.cs` (the one file where the using was swapped, not added):

    ```bash
    grep -n "Marketing.Services" backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookInternalDtos.cs
    ```

    Expected output: no matches (empty output).

11. Run `dotnet format` on the touched production files only, scoped to each project, to match the project's formatting conventions:

    ```bash
    cd /home/user/worktrees/feature-4132-Arch-Review-Marketing-Outlookeventdto-Microsoft-Gr
    dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --include backend/src/Anela.Heblo.Application/Features/Marketing/Infrastructure/OutlookEventDto.cs backend/src/Anela.Heblo.Application/Features/Marketing/Services/IOutlookCalendarSync.cs backend/src/Anela.Heblo.Application/Features/Marketing/Services/NoOpOutlookCalendarSync.cs backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs
    dotnet format backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj --include backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookInternalDtos.cs
    ```

    Expected output: both commands exit with no errors (whitespace-only changes at most — re-open the files afterward and confirm the edits from steps 2–8 are still semantically intact).

12. Stage and commit exactly these six files:

    ```bash
    cd /home/user/worktrees/feature-4132-Arch-Review-Marketing-Outlookeventdto-Microsoft-Gr
    git add backend/src/Anela.Heblo.Application/Features/Marketing/Infrastructure/OutlookEventDto.cs backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventDto.cs backend/src/Anela.Heblo.Application/Features/Marketing/Services/IOutlookCalendarSync.cs backend/src/Anela.Heblo.Application/Features/Marketing/Services/NoOpOutlookCalendarSync.cs backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookInternalDtos.cs
    git commit -m "Move OutlookEventDto from Marketing Services/ to Infrastructure/ and update production usings"
    ```

    Expected output: a new commit; `git show --stat HEAD` shows the `OutlookEventDto.cs` move as a rename plus the five using-only edits.

---

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
