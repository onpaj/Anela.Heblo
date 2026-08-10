# Plan: Move MeetingTasks-specific keyed `IChatClient` out of the Anthropic adapter

## Overview

Small internal DI-wiring refactor, one task. Relocate the `"meeting-extractor"` keyed
`IChatClient` registration from `AnthropicAdapterServiceCollectionExtensions.cs` (generic
Anthropic adapter, which should have zero knowledge of MeetingTasks) into
`MeetingTasksModule.AddMeetingTasksModule` (Application layer, the sole consumer), mirroring
the existing `KnowledgeBaseModule` precedent (`KnowledgeBaseModule.cs:58-64`). The new
registration aliases the already-registered default `IChatClient` via
`sp.GetRequiredService<IChatClient>()` rather than reconstructing `AnthropicChatClient` from
scratch. The duplicate key-string literal is eliminated: `MeetingTasksConstants.ExtractionChatClientKey`
becomes the single source of truth; the adapter's `MeetingExtractionClientKey` constant is
deleted entirely. No behavior change beyond the keyed client now inheriting `.UseLogging()`
decoration (a benign, expected delta noted in the architecture review — call it out in the
commit message, don't try to "fix" it away).

This is one cohesive change across two files with no independent sub-parts worth splitting,
so it is a single task.

### task: relocate-keyed-chatclient-to-meetingtasks-module

**Goal:** Remove the MeetingTasks-specific keyed `IChatClient` registration and its duplicate
key constant from the generic Anthropic adapter; add the equivalent keyed registration inside
`MeetingTasksModule`, sourced from the existing `MeetingTasksConstants.ExtractionChatClientKey`.

**File 1 — `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs`**

Current content (44 lines):
```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Anthropic;

public static class AnthropicAdapterServiceCollectionExtensions
{
    public const string MeetingExtractionClientKey = "meeting-extractor";

    public static IServiceCollection AddAnthropicAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnthropicOptions>(opts =>
        {
            opts.ApiKey = configuration["Anthropic:ApiKey"] ?? "";
            opts.Model = configuration["KnowledgeBase:ChatModel"] ?? opts.Model;
            opts.MaxTokens = configuration.GetValue("KnowledgeBase:ChatMaxTokens", opts.MaxTokens);
            opts.HttpTimeoutSeconds = configuration.GetValue("Anthropic:HttpTimeoutSeconds", opts.HttpTimeoutSeconds);
        });

        services.AddHttpClient("Anthropic", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        });

        services.AddChatClient(sp =>
            new AnthropicChatClient(
                sp.GetRequiredService<IOptions<AnthropicOptions>>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<AnthropicChatClient>>()))
            .UseLogging();

        services.AddKeyedSingleton<IChatClient>(MeetingExtractionClientKey, (sp, _) =>
            new AnthropicChatClient(
                sp.GetRequiredService<IOptions<AnthropicOptions>>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<AnthropicChatClient>>()));

        return services;
    }
}
```

Make these two edits:
1. Delete line 11: `    public const string MeetingExtractionClientKey = "meeting-extractor";` (and the
   blank line immediately after it, so there's exactly one blank line between the class-opening
   brace and the `AddAnthropicAdapter` method, matching the original spacing style).
2. Delete the entire block (originally lines 36-40):
   ```csharp
   services.AddKeyedSingleton<IChatClient>(MeetingExtractionClientKey, (sp, _) =>
       new AnthropicChatClient(
           sp.GetRequiredService<IOptions<AnthropicOptions>>(),
           sp.GetRequiredService<IHttpClientFactory>(),
           sp.GetRequiredService<ILogger<AnthropicChatClient>>()));
   ```
   including the blank line that separated it from the `.UseLogging()` call above and the
   `return services;` line below — leave exactly one blank line between `.UseLogging();` and
   `return services;`.

Resulting file must contain only: the `AnthropicOptions` binding, the named `"Anthropic"`
`HttpClient` registration, and the single unkeyed `services.AddChatClient(...).UseLogging()`
registration. No `MeetingExtractionClientKey` symbol, no reference to `MeetingTasks`, anywhere
in the file.

**File 2 — `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`**

Current relevant section (lines 41-45):
```csharp
        services.AddScoped<IMeetingTaskExtractor>(sp =>
            new ClaudeMeetingTaskExtractor(
                sp.GetRequiredKeyedService<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey),
                sp.GetRequiredService<IMeetingUserDirectory>(),
                sp.GetRequiredService<ILogger<ClaudeMeetingTaskExtractor>>()));
```

Insert a new keyed registration immediately **before** this `services.AddScoped<IMeetingTaskExtractor>(...)` block (i.e. right after the `services.AddScoped<IMeetingTaskExporter, NoOpMeetingTaskExporter>();` / `if (!useMockAuth && !bypassJwt) { ... } else { ... }` block ends, before line 41):

```csharp
        // MeetingTasks-scoped alias for the default IChatClient, keyed for extraction use.
        // Kept in this module (not the generic Anthropic adapter) so the adapter has no
        // compile-time knowledge of MeetingTasks. Mirrors KnowledgeBaseModule's keyed-client pattern.
        services.AddKeyedSingleton<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey,
            (sp, _) => sp.GetRequiredService<IChatClient>());

        services.AddScoped<IMeetingTaskExtractor>(sp =>
            new ClaudeMeetingTaskExtractor(
                sp.GetRequiredKeyedService<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey),
                sp.GetRequiredService<IMeetingUserDirectory>(),
                sp.GetRequiredService<ILogger<ClaudeMeetingTaskExtractor>>()));
```

Do not change anything else in this file — `Microsoft.Extensions.AI` (for `IChatClient`) and
`Microsoft.Extensions.DependencyInjection` are already imported at the top of the file, no new
`using` statements are needed. `MeetingTasksConstants.ExtractionChatClientKey` stays `internal`
(no visibility change) — the new registration and the existing consumer are both inside
`MeetingTasksModule.cs`, same assembly and namespace.

**File 3 — `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksConstants.cs`**

No change. It already reads:
```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks;

internal static class MeetingTasksConstants
{
    internal const string ExtractionChatClientKey = "meeting-extractor";
}
```
This becomes the sole definition of the `"meeting-extractor"` literal after File 1's constant
is deleted. Do not touch this file — it is listed here only so the developer agent doesn't
mistakenly think a change is required here too.

**Why:** The adapter's keyed registration was a byte-for-byte duplicate of its own default
`AddChatClient(...)` call (same options, same named `HttpClient`, same logger type), applying
no MeetingTasks-specific decoration — it existed purely to give MeetingTasks a private name for
the same client. That's exactly the coupling `KnowledgeBaseModule` already fixed for KB
(finding #3770): the feature module that consumes a keyed client should be the one that
registers it. Aliasing via `sp.GetRequiredService<IChatClient>()` avoids `MeetingTasksModule`
(Application layer) needing to reference `AnthropicChatClient`/`AnthropicOptions`
(Adapter-layer concrete types) — it only needs the `IChatClient` abstraction it already
depends on. Registration order between `AddAnthropicAdapter` and `AddMeetingTasksModule` in the
composition root does not matter: `AddKeyedSingleton`'s factory delegate captures
`IServiceProvider` and resolves lazily on first use (when `IMeetingTaskExtractor` is first
constructed), by which point the whole container — including the adapter's default `IChatClient`
registration — is built regardless of `Add*` call order. This is proven by the identical,
already-working `KnowledgeBaseModule` pattern.

**Do not:**
- Add a new DI-container-validation/wiring test (e.g. a `MeetingTasksChatClientWiringTests.cs`
  analogous to `KnowledgeBaseChatClientWiringTests.cs`) — explicitly out of scope per the spec.
- Touch `ClaudeMeetingTaskExtractor`, `IMeetingTaskExtractor`, `KnowledgeBaseModule.cs`,
  `KnowledgeBaseConstants.cs`, or any composition-root file (`Program.cs`) — none require
  changes.
- Widen the visibility of `MeetingTasksConstants` or `ExtractionChatClientKey`.
- Change the `"meeting-extractor"` string value itself.

**Verification steps (run from repo root of this worktree):**

1. Full-solution grep confirms the duplicate symbol and adapter coupling are gone:
   ```bash
   grep -rn "MeetingExtractionClientKey" backend/ || echo "OK: no matches"
   grep -rni "meeting" backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/ || echo "OK: no matches"
   grep -rn '"meeting-extractor"' backend/ 
   ```
   The last command must return exactly one match, in
   `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksConstants.cs`.

2. Build the backend:
   ```bash
   cd backend && dotnet build
   ```
   Must succeed with no errors (a stray reference to the deleted `MeetingExtractionClientKey`
   would be a compile error, not just a runtime one, since it's a `public const` symbol
   reference).

3. Run `dotnet format` (per repo convention) and confirm it reports no changes needed beyond
   what was intentionally written, or apply its formatting if it reformats the touched files.

4. Run the full backend test suite (or at minimum the MeetingTasks and KnowledgeBase test
   folders, since those are the only ones touching this DI wiring):
   ```bash
   cd backend && dotnet test
   ```
   All tests must pass, in particular:
   - `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs`
     (unaffected — it constructs `ClaudeMeetingTaskExtractor` directly with a mocked
     `IChatClient`, no DI container involved, but must still pass to confirm no collateral
     damage).
   - `backend/test/Anela.Heblo.Tests/KnowledgeBase/KnowledgeBaseChatClientWiringTests.cs`
     (unaffected by this change, but exercises `AddAnthropicAdapter` + a feature module's
     keyed-client pattern in the same style being added here — a regression here would
     indicate the adapter edit broke something shared).

5. Confirm DI still resolves end-to-end for MeetingTasks. There is no existing dedicated
   wiring test for this (see arch-review's noted gap, intentionally out of scope to add one
   here), so verify manually: either start the application (`dotnet run` in the API project,
   or however the project is normally started per
   `docs/development/setup.md`) and confirm it starts without an
   `InvalidOperationException` from the DI container, or, if a faster in-repo check is
   preferred, write a short throwaway `Program.cs`-style scratch check (not committed) that
   builds a `ServiceCollection` with `services.AddLogging(); services.AddAnthropicAdapter(configuration); services.AddMeetingTasksModule(configuration);`
   then calls `provider.GetRequiredKeyedService<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey)`
   and confirms it returns a non-null instance without throwing. Do not leave this scratch
   check as a committed file — it exists only to prove the wiring resolves, per the spec's
   explicit decision not to add a permanent test for this.

**Acceptance criteria (all must hold before considering this task done):**
- `AnthropicAdapterServiceCollectionExtensions.cs` has no `MeetingExtractionClientKey` symbol
  and no reference to `MeetingTasks` anywhere.
- `MeetingTasksModule.cs` contains a `services.AddKeyedSingleton<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey, (sp, _) => sp.GetRequiredService<IChatClient>());`
  registration, placed before the `IMeetingTaskExtractor` factory that consumes it.
- Full-solution grep for `"meeting-extractor"` returns exactly one match
  (`MeetingTasksConstants.cs`).
- `dotnet build` succeeds; `dotnet test` passes with no new failures.
- The application/DI container resolves `IChatClient` keyed by
  `MeetingTasksConstants.ExtractionChatClientKey` without throwing
  `InvalidOperationException`, confirmed per step 5 above.
