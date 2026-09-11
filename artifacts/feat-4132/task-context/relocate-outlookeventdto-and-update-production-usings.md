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
