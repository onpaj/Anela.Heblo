### task: add-marketing-calendar-options-fields

**Goal:** Add `CategoryMappings` and `OutgoingCategories` properties to `MarketingCalendarOptions` so configuration-driven mapping is bindable.

**Context:**
The existing `MarketingCalendarOptions` class holds `GroupId` and `PushEnabled`. We must add two dictionaries:

| Field | Type | Notes |
|---|---|---|
| `CategoryMappings` | `Dictionary<string, MarketingActionType>` | Constructed with `StringComparer.OrdinalIgnoreCase`. Empty default. |
| `OutgoingCategories` | `Dictionary<MarketingActionType, string>` | Empty default. |

Backwards compatibility (NFR-3): empty/missing dictionaries MUST behave identically to the current code path. The `OrdinalIgnoreCase` comparer MUST be set in the property initializer; .NET options binding constructs a fresh `Dictionary<,>` per options reload, and without an initializer the comparer falls back to default (case-sensitive), silently breaking case-insensitive matching. Note: even with the initializer, the binder may not preserve the comparer across rebinds — defensive lookup logic lives in the mapper (separate task), not here.

`MarketingActionType` enum is unchanged: `General`, `Promotion`, `Launch`, `Campaign`, `Event`, `Other`.

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Marketing/Configuration/MarketingCalendarOptions.cs` — add two new properties.

**Implementation steps:**
1. Open `MarketingCalendarOptions.cs`.
2. Ensure `using System.Collections.Generic;` and `using System;` are present (for `StringComparer`).
3. Add the two properties to the class (preserve existing properties):
   ```csharp
   public Dictionary<string, MarketingActionType> CategoryMappings { get; init; }
       = new(StringComparer.OrdinalIgnoreCase);

   public Dictionary<MarketingActionType, string> OutgoingCategories { get; init; }
       = new();
   ```
4. Run `dotnet format` on the file to satisfy Allman/4-space rules.
5. Run `dotnet build` to confirm the project still compiles.

**Tests to write:**
None directly — the properties are plain data containers exercised through the mapper tests in a later task. (A binder-behavior test that loads JSON via `ConfigurationBuilder.AddJsonStream` is part of the `MarketingCategoryMapper` test task.)

**Acceptance criteria:**
- `MarketingCalendarOptions` exposes `CategoryMappings` (default: empty, `OrdinalIgnoreCase`) and `OutgoingCategories` (default: empty).
- Solution compiles with `dotnet build`.
- `dotnet format` reports no violations on the file.
- No existing tests break.

---

### task: create-mapper-contracts

**Goal:** Introduce `IMarketingCategoryMapper` and the `CategoryMappingResult` record that handlers and services will depend on.

**Context:**
The mapper is a single bidirectional service. Both directions read from the same `MarketingCalendarOptions`; splitting into two services would double the surface area for divergence and break the round-trip invariant. Singleton lifetime + `IOptionsMonitor` is required for hot reload (FR-6).

The result record is per-call (one event). Batch-level aggregation lives in the handler's `HashSet`, not the mapper.

`MarketingActionType` already exists in `Anela.Heblo.Domain.Features.Marketing` (or wherever it's currently defined) — do not redefine it.

Final contract shapes (do not vary):
```csharp
public interface IMarketingCategoryMapper
{
    CategoryMappingResult MapToActionType(IReadOnlyList<string> outlookCategories);
    string MapToOutlookCategory(MarketingActionType actionType);
}

public sealed record CategoryMappingResult(
    MarketingActionType ActionType,
    string? MatchedCategory,
    IReadOnlyList<string> UnmappedCategories);
```

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/IMarketingCategoryMapper.cs` — new file containing the interface and the `CategoryMappingResult` record.

**Implementation steps:**
1. Create `IMarketingCategoryMapper.cs` in `Application/Features/Marketing/Services/`.
2. Add the namespace matching neighbouring files in the same folder (e.g. `namespace Anela.Heblo.Application.Features.Marketing.Services;`). Verify by reading `IOutlookCalendarSync.cs` in the same folder.
3. Add usings: `System.Collections.Generic;` and the namespace where `MarketingActionType` lives.
4. Define the interface and the public sealed record exactly as shown in Context (both in this single file).
5. Run `dotnet format` and `dotnet build`.

**Tests to write:**
None — interface and DTO; behavior is covered in the implementation task.

**Acceptance criteria:**
- File `IMarketingCategoryMapper.cs` exists in `Application/Features/Marketing/Services/`.
- `IMarketingCategoryMapper` exposes exactly two methods with the signatures above.
- `CategoryMappingResult` is `public sealed record` with three positional members in the documented order.
- `dotnet build` succeeds.

---

### task: implement-marketing-category-mapper

**Goal:** Implement `MarketingCategoryMapper` with case-insensitive lookup, hot reload, and safe rebuild on options change.

**Context:**
The mapper is a Singleton consuming `IOptionsMonitor<MarketingCalendarOptions>`. Because the .NET options binder may not preserve the `OrdinalIgnoreCase` comparer across rebinds (Risk #1 in arch review), the mapper MUST rebuild a private case-insensitive snapshot — never index `options.CategoryMappings` directly.

Behavior:

`MapToActionType(IReadOnlyList<string> outlookCategories)`:
- Iterates in the order received from Graph.
- Skips entries that are null, empty, or whitespace (these do NOT contribute to `UnmappedCategories`).
- Returns the first non-whitespace category that resolves in the snapshot — `ActionType = mapped value`, `MatchedCategory = the original category string from the input`, `UnmappedCategories = empty`.
- If iteration finishes without a match: `ActionType = General`, `MatchedCategory = null`, `UnmappedCategories = all non-whitespace input entries (preserving case as provided)`.
- For an empty input list (or all-whitespace): `ActionType = General`, `MatchedCategory = null`, `UnmappedCategories = empty`.

`MapToOutlookCategory(MarketingActionType actionType)`:
- Returns the value from the outgoing snapshot when present.
- Falls back to `actionType.ToString()` when absent (NFR-3 backwards compatibility).

Hot reload:
- Constructor eagerly builds both snapshots from `IOptionsMonitor.CurrentValue`.
- `IOptionsMonitor.OnChange` rebuilds both snapshots inside `try/catch`. On failure, retain the prior snapshot and log `Warning`. Snapshots are stored in `volatile` fields (or behind a single immutable holder reference replaced atomically) to avoid torn reads.
- The incoming snapshot is `IReadOnlyDictionary<string, MarketingActionType>` constructed with `StringComparer.OrdinalIgnoreCase`.
- The outgoing snapshot is `IReadOnlyDictionary<MarketingActionType, string>`. Trim whitespace from values defensively.
- A round-trip non-injectivity comment near `MapToOutlookCategory` documents that multiple action types may map to the same Outlook name (Open Question 3).

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCategoryMapper.cs` — new implementation.
- `backend/test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCategoryMapperTests.cs` — new test file.

**Implementation steps:**
1. Create `MarketingCategoryMapper.cs`. Add usings: `System;`, `System.Collections.Generic;`, `System.Linq;`, `Microsoft.Extensions.Logging;`, `Microsoft.Extensions.Options;`, plus the namespaces of `MarketingCalendarOptions` and `MarketingActionType`.
2. Class skeleton:
   ```csharp
   public sealed class MarketingCategoryMapper : IMarketingCategoryMapper, IDisposable
   {
       private readonly ILogger<MarketingCategoryMapper> _logger;
       private readonly IDisposable? _changeSubscription;
       private volatile Snapshot _snapshot;

       public MarketingCategoryMapper(
           IOptionsMonitor<MarketingCalendarOptions> optionsMonitor,
           ILogger<MarketingCategoryMapper> logger)
       {
           _logger = logger;
           _snapshot = BuildSnapshot(optionsMonitor.CurrentValue);
           _changeSubscription = optionsMonitor.OnChange(opts =>
           {
               try { _snapshot = BuildSnapshot(opts); }
               catch (Exception ex)
               {
                   _logger.LogWarning(ex,
                       "Failed to rebuild marketing category snapshot; keeping prior snapshot.");
               }
           });
       }

       public CategoryMappingResult MapToActionType(IReadOnlyList<string> outlookCategories)
       {
           var snap = _snapshot;
           if (outlookCategories is null || outlookCategories.Count == 0)
           {
               return new CategoryMappingResult(MarketingActionType.General, null, Array.Empty<string>());
           }

           List<string>? unmapped = null;
           foreach (var raw in outlookCategories)
           {
               if (string.IsNullOrWhiteSpace(raw)) continue;
               if (snap.Incoming.TryGetValue(raw, out var actionType))
               {
                   return new CategoryMappingResult(actionType, raw, Array.Empty<string>());
               }
               (unmapped ??= new List<string>()).Add(raw);
           }

           return new CategoryMappingResult(
               MarketingActionType.General,
               null,
               (IReadOnlyList<string>?)unmapped ?? Array.Empty<string>());
       }

       public string MapToOutlookCategory(MarketingActionType actionType)
       {
           // Multiple action types may share the same Outlook name; round-trip is
           // not guaranteed to be injective. See Open Question 3 / FR-7.
           var snap = _snapshot;
           return snap.Outgoing.TryGetValue(actionType, out var name)
               ? name
               : actionType.ToString();
       }

       public void Dispose() => _changeSubscription?.Dispose();

       private static Snapshot BuildSnapshot(MarketingCalendarOptions opts)
       {
           var incoming = new Dictionary<string, MarketingActionType>(StringComparer.OrdinalIgnoreCase);
           foreach (var kv in opts.CategoryMappings ?? new())
           {
               if (string.IsNullOrWhiteSpace(kv.Key)) continue;
               incoming[kv.Key.Trim()] = kv.Value;
           }
           var outgoing = new Dictionary<MarketingActionType, string>();
           foreach (var kv in opts.OutgoingCategories ?? new())
           {
               outgoing[kv.Key] = (kv.Value ?? string.Empty).Trim();
           }
           return new Snapshot(incoming, outgoing);
       }

       private sealed record Snapshot(
           IReadOnlyDictionary<string, MarketingActionType> Incoming,
           IReadOnlyDictionary<MarketingActionType, string> Outgoing);
   }
   ```
3. Run `dotnet format`.
4. Create the test file `MarketingCategoryMapperTests.cs`. Use a test-helper that wraps an `IOptionsMonitor<MarketingCalendarOptions>` whose `CurrentValue` is mutable and whose `OnChange` callback can be triggered manually. Pattern:
   ```csharp
   private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
   {
       private T _current;
       private readonly List<Action<T, string?>> _listeners = new();
       public TestOptionsMonitor(T initial) { _current = initial; }
       public T CurrentValue => _current;
       public T Get(string? name) => _current;
       public IDisposable OnChange(Action<T, string?> listener)
       {
           _listeners.Add(listener);
           return new Subscription(() => _listeners.Remove(listener));
       }
       public void Set(T next)
       {
           _current = next;
           foreach (var l in _listeners.ToArray()) l(next, null);
       }
       private sealed class Subscription : IDisposable
       {
           private readonly Action _dispose;
           public Subscription(Action d) { _dispose = d; }
           public void Dispose() => _dispose();
       }
   }
   ```
5. Use `Microsoft.Extensions.Logging.Abstractions.NullLogger<MarketingCategoryMapper>.Instance` for the logger in tests.

**Tests to write:**
1. `MapToActionType_EmptyList_ReturnsGeneralWithNoUnmapped` — input `Array.Empty<string>()` → `(General, null, [])`.
2. `MapToActionType_NullList_ReturnsGeneralWithNoUnmapped` — input `null` (cast as `IReadOnlyList<string>`) → `(General, null, [])`.
3. `MapToActionType_AllWhitespace_ReturnsGeneralWithNoUnmapped` — input `["", " ", "\t", null!]` → `(General, null, [])`.
4. `MapToActionType_FirstMappedWins` — config `{"PR – léto": Campaign, "Sociální sítě": General}`, input `["Random", "PR – léto", "Sociální sítě"]` → `ActionType == Campaign`, `MatchedCategory == "PR – léto"`, `UnmappedCategories.Count == 0`.
5. `MapToActionType_NoMappings_ReturnsGeneralAndUnmappedListsAllNonWhitespace` — config empty, input `["Random", " ", "Another"]` → `(General, null, ["Random", "Another"])`.
6. `MapToActionType_CaseInsensitive` — config `{"Sociální sítě": General}`, input `["sociální SÍTĚ"]` → match found, `MatchedCategory == "sociální SÍTĚ"` (original case preserved), unmapped empty.
7. `MapToOutlookCategory_KnownActionType_ReturnsConfiguredName` — config `{Campaign: "PR – léto"}`, input `Campaign` → `"PR – léto"`.
8. `MapToOutlookCategory_UnknownActionType_FallsBackToToString` — empty `OutgoingCategories`, input `Campaign` → `"Campaign"`.
9. `OnChange_RebuildsSnapshot_NewMappingTakesEffect` — start with empty config, change to `{"X": Promotion}`, then call `MapToActionType(["X"])` → `Promotion`.
10. `OnChange_FailureRetainsPriorSnapshot` — initial config maps `"X" → Promotion`. Trigger `Set` with options whose `CategoryMappings` getter throws (use a custom `MarketingCalendarOptions` subclass for the test); assert subsequent `MapToActionType(["X"])` still returns `Promotion`.
11. `BinderProducedDictionary_WithoutComparer_StillResolvesCaseInsensitively` — bind options from JSON via `ConfigurationBuilder.AddJsonStream` with body:
    ```json
    {"MarketingCalendar":{"GroupId":"g","PushEnabled":true,"CategoryMappings":{"Sociální sítě":"General"},"OutgoingCategories":{}}}
    ```
    Construct `MarketingCategoryMapper` from the bound `IOptionsMonitor`. Assert `MapToActionType(["sociální sítě"])` returns `(General, "sociální sítě", [])`.

**Acceptance criteria:**
- `MarketingCategoryMapper` implements `IMarketingCategoryMapper`.
- All 11 unit tests pass.
- The mapper never directly indexes `options.CategoryMappings`; it always reads through the rebuilt snapshot.
- Hot reload via `IOptionsMonitor.OnChange` is observable: after `Set`, the next call returns the new mapping.
- A failing rebuild does not corrupt state — prior snapshot is retained.
- `dotnet format` and `dotnet build` clean.

---

### task: register-mapper-and-add-startup-validation

**Goal:** Register `IMarketingCategoryMapper` as Singleton in `MarketingModule` and add round-trip validation that fails startup when `OutgoingCategories` references names absent from `CategoryMappings`.

**Context:**
`MarketingModule` already conditionally registers `IOutlookCalendarSync` (real vs `NoOpOutlookCalendarSync`) and exposes (or should expose) a `Validate(...)` hook invoked at startup. Per arch review Decision 4, validation lives in `MarketingModule.Validate`, not in a parallel `IValidateOptions<T>` implementation. If `Validate` does not yet exist, add it following whichever pattern the rest of `Anela.Heblo.Application` uses (e.g., an extension method called from `Program.cs`/`Startup.cs` or an `IStartupFilter`); confirm by reading neighbouring `*Module.cs` files before implementing.

Validation rules (FR-7 + amendment 4):
- Iterate `OutgoingCategories.Values`.
- Trim each value.
- Assert the trimmed value is contained in `CategoryMappings.Keys` using `StringComparer.OrdinalIgnoreCase`.
- Pass when both dictionaries are empty.
- Pass when only `CategoryMappings` is populated.
- Fail with a clear message naming each offending pair: `"OutgoingCategories[Campaign] = 'PR – léto' has no matching key in CategoryMappings."` Concatenate multiple errors into one message separated by `; `.

DI registration:
- `services.AddSingleton<IMarketingCategoryMapper, MarketingCategoryMapper>();`
- Place inside the same conditional gate already used for `IOutlookCalendarSync` so mock-auth mode also gets the mapper. Both real and mock paths must register the same Singleton implementation (the mapper has no Graph dependencies; it works in both modes).

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs` — add registration and validation logic.

**Implementation steps:**
1. Open `MarketingModule.cs`. Confirm the existing conditional DI gate around `IOutlookCalendarSync` (referenced as lines 29-38 in the spec).
2. Inside that conditional block, add: `services.AddSingleton<IMarketingCategoryMapper, MarketingCategoryMapper>();`. If both branches register `IOutlookCalendarSync` (real and mock), register the mapper in both — it is needed in either mode.
3. Locate the existing `Validate(...)` method. If present, append the new check. If absent, add a `public static void ValidateMarketingCalendarOptions(IServiceProvider sp)` (or similar shape consistent with the codebase) and wire it into the existing startup-validation pipeline used by other modules in `Anela.Heblo.Application`.
4. Validation implementation:
   ```csharp
   var options = sp.GetRequiredService<IOptions<MarketingCalendarOptions>>().Value;
   if (options.OutgoingCategories.Count == 0) return;

   var keys = new HashSet<string>(
       options.CategoryMappings?.Keys?.Select(k => k.Trim()) ?? Enumerable.Empty<string>(),
       StringComparer.OrdinalIgnoreCase);

   var errors = new List<string>();
   foreach (var kv in options.OutgoingCategories)
   {
       var name = (kv.Value ?? string.Empty).Trim();
       if (string.IsNullOrEmpty(name) || !keys.Contains(name))
       {
           errors.Add(
               $"OutgoingCategories[{kv.Key}] = '{kv.Value}' has no matching key in CategoryMappings.");
       }
   }

   if (errors.Count > 0)
   {
       throw new InvalidOperationException(
           "Marketing calendar configuration is invalid: " + string.Join("; ", errors));
   }
   ```
5. Run `dotnet format` and `dotnet build`.

**Tests to write:**
Add a test class `MarketingModuleValidationTests.cs` in `backend/test/Anela.Heblo.Tests/Features/Marketing/`. Each test builds a `MarketingCalendarOptions` instance manually and invokes the validation routine directly (extract a helper method on `MarketingModule` if necessary so the validation can be unit-tested without spinning up the host).

1. `Validate_BothDictionariesEmpty_Passes` — both empty, no exception.
2. `Validate_OnlyCategoryMappingsPopulated_Passes` — `CategoryMappings = {"Sociální sítě": General}`, `OutgoingCategories = {}`, no exception.
3. `Validate_RoundTripValid_Passes` — `CategoryMappings = {"Sociální sítě": General}`, `OutgoingCategories = {General: "Sociální sítě"}`, no exception.
4. `Validate_OutgoingValueMissingFromIncoming_Throws` — `CategoryMappings = {"A": General}`, `OutgoingCategories = {Campaign: "B"}` → `InvalidOperationException` whose message contains `OutgoingCategories[Campaign] = 'B'`.
5. `Validate_CaseInsensitiveMatch_Passes` — `CategoryMappings = {"Sociální Sítě": General}`, `OutgoingCategories = {General: "sociální sítě"}`, no exception.
6. `Validate_TrimmedMatch_Passes` — `CategoryMappings = {"Email": Launch}`, `OutgoingCategories = {Launch: "  Email  "}`, no exception.
7. `Validate_MultipleErrors_AllListedInMessage` — two offending entries → message contains both `OutgoingCategories[X]` and `OutgoingCategories[Y]` substrings.

**Acceptance criteria:**
- `IMarketingCategoryMapper` is resolvable from DI as Singleton in both real-auth and mock-auth modes.
- Startup throws `InvalidOperationException` with a precise message when round-trip is broken.
- Startup succeeds for the empty-config and valid-config cases.
- All 7 validation tests pass.
- Existing `MarketingModule` tests still pass.

---

### task: extend-import-from-outlook-response

**Goal:** Add `UnmappedCategories` field to `ImportFromOutlookResponse` so the API surfaces unmapped names.

**Context:**
`ImportFromOutlookResponse` currently exposes `Created`, `Skipped`, `Failed`. We add a list:

```csharp
public List<string> UnmappedCategories { get; set; } = new();
```

Initialize to empty list (not `null`) so the OpenAPI schema marks it required-non-nullable and the TS client types it as `string[]` rather than `string[] | undefined`. Use class semantics (not record) per CLAUDE.md DTO rules — confirm the existing type is already a class. Use `[JsonPropertyName("unmappedCategories")]` if neighbouring properties on the response use it.

The OpenAPI client regenerates as part of `dotnet build` per `docs/development/api-client-generation.md`; do not hand-edit the generated TypeScript.

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/ImportFromOutlookResponse.cs` — add property.

**Implementation steps:**
1. Open `ImportFromOutlookResponse.cs`. Confirm it is a `class` (not a `record`). If it is a record, convert to a class with public `{ get; set; }` properties — the spec rule in CLAUDE.md (rule 3) forbids records for API DTOs.
2. Add the property after the existing `Failed`:
   ```csharp
   [JsonPropertyName("unmappedCategories")]
   public List<string> UnmappedCategories { get; set; } = new();
   ```
   Match the casing convention used by sibling properties (with or without `[JsonPropertyName]` — be consistent).
3. Add `using System.Collections.Generic;` and `using System.Text.Json.Serialization;` if not already present.
4. Run `dotnet format` and `dotnet build`.

**Tests to write:**
1. `ImportFromOutlookResponse_DefaultsUnmappedCategoriesToEmptyList` — `new ImportFromOutlookResponse().UnmappedCategories` is non-null and `.Count == 0`. Add to existing `ImportFromOutlookResponseTests.cs` if such file exists; otherwise inline this assertion as part of the handler tests in the next task.

**Acceptance criteria:**
- `ImportFromOutlookResponse.UnmappedCategories` is `List<string>` defaulting to empty list.
- `dotnet build` regenerates `frontend/src/api/generated/api-client.ts`; the regenerated file contains `unmappedCategories: string[]` as a required field on the response interface.
- No existing handler/controller tests break.

---

### task: wire-mapper-into-import-handler

**Goal:** Replace the `Enum.TryParse<MarketingActionType>` block in `ImportFromOutlookHandler.BuildAction` with mapper-based resolution and aggregate unmapped category names into the response.

**Context:**
Today `ImportFromOutlookHandler.BuildAction` calls `Enum.TryParse<MarketingActionType>(category)` on the **first** category and silently falls back to `General` (~line 146-149). That collapses every non-enum-named category into `General`.

New behavior (FR-1 through FR-4):
- The handler holds `IMarketingCategoryMapper _mapper` (constructor-injected).
- During the import loop, the handler maintains `var unmappedAccumulator = new HashSet<string>(StringComparer.OrdinalIgnoreCase);`.
- For each event:
  - `var mapping = _mapper.MapToActionType(evt.Categories ?? Array.Empty<string>());`
  - `action.ActionType = mapping.ActionType;`
  - **Rule (FR-4):** Only contribute to `unmappedAccumulator` when the event had at least one non-whitespace category AND `mapping.MatchedCategory is null`. Concretely: `if (mapping.MatchedCategory is null && mapping.UnmappedCategories.Count > 0) { foreach (var n in mapping.UnmappedCategories) unmappedAccumulator.Add(n); }` — the mapper already filtered whitespace.
- After the loop: `response.UnmappedCategories = unmappedAccumulator.ToList();`
- If `unmappedAccumulator.Count > 0`, emit one `Information` log with the batch-aggregated list (NFR-1 amendment 5): `_logger.LogInformation("Marketing import: {Count} unmapped Outlook categories: {Categories}", unmappedAccumulator.Count, string.Join(", ", unmappedAccumulator));`. No per-event logging.

Dry-run path (Open Question 5): mapping happens before persistence, so `UnmappedCategories` is populated identically in dry-run and real runs. If the handler has a `dryRun` branch, ensure it goes through the same `BuildAction` flow.

The first-mapped-category-wins rule and case-insensitive matching are already enforced by the mapper.

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/ImportFromOutlookHandler.cs` — inject mapper, replace mapping block, aggregate unmapped, log, materialize.
- `backend/test/Anela.Heblo.Tests/Features/Marketing/UseCases/ImportFromOutlook/ImportFromOutlookHandlerTests.cs` — extend existing tests.

**Implementation steps:**
1. Open `ImportFromOutlookHandler.cs`. Add a `private readonly IMarketingCategoryMapper _mapper;` field; add the parameter to the constructor and assign.
2. In the `HandleAsync` (or equivalent) method, before the per-event loop, declare `var unmappedAccumulator = new HashSet<string>(StringComparer.OrdinalIgnoreCase);`.
3. Locate `BuildAction` (or wherever the `Enum.TryParse` lives ~line 146-149). Replace:
   ```csharp
   if (!Enum.TryParse<MarketingActionType>(evt.Categories?.FirstOrDefault(), out var actionType))
   {
       actionType = MarketingActionType.General;
   }
   ```
   with:
   ```csharp
   var mapping = _mapper.MapToActionType(evt.Categories ?? Array.Empty<string>());
   var actionType = mapping.ActionType;
   ```
4. Bubble the `mapping` value out of `BuildAction` so the caller (the loop) can update `unmappedAccumulator`. Two options:
   - (a) Change `BuildAction` to return both the action and the mapping.
   - (b) Inline the mapper call into the loop and have `BuildAction` accept `MarketingActionType` as a parameter.
   Pick whichever requires the smaller diff. Either way, the loop must see `mapping.MatchedCategory` and `mapping.UnmappedCategories`.
5. In the loop, after `BuildAction`:
   ```csharp
   if (mapping.MatchedCategory is null && mapping.UnmappedCategories.Count > 0)
   {
       foreach (var name in mapping.UnmappedCategories)
       {
           unmappedAccumulator.Add(name);
       }
   }
   ```
6. After the loop:
   ```csharp
   response.UnmappedCategories = unmappedAccumulator.ToList();
   if (unmappedAccumulator.Count > 0)
   {
       _logger.LogInformation(
           "Marketing import completed with {Count} unmapped Outlook categor{Plural}: {Categories}",
           unmappedAccumulator.Count,
           unmappedAccumulator.Count == 1 ? "y" : "ies",
           string.Join(", ", unmappedAccumulator));
   }
   ```
   (Use the existing `_logger` field on the handler.)
7. Confirm dry-run still flows through the same mapping logic; if the dry-run branch builds a different response object, ensure `UnmappedCategories` is set there too.
8. Run `dotnet format` and `dotnet build`.

**Tests to write:**
Extend `ImportFromOutlookHandlerTests.cs`. Use a hand-rolled `IMarketingCategoryMapper` test double or `Moq`/`NSubstitute` consistent with what the test class already uses.

1. `Handle_EventWithMappedFirstCategory_SetsActionTypeAndDoesNotReportUnmapped` — Graph returns one event with categories `["PR – léto"]`; mapper returns `(Campaign, "PR – léto", [])`. Assert created action's `ActionType == Campaign` and `response.UnmappedCategories.Count == 0`.
2. `Handle_EventWithFirstUnmappedSecondMapped_DoesNotReportFirstAsUnmapped` — categories `["Random", "PR – léto"]`; mapper returns `(Campaign, "PR – léto", [])` (mapper already stops at first match). Assert `response.UnmappedCategories.Count == 0`.
3. `Handle_EventWithAllUnmapped_ReportsAllAsUnmapped` — categories `["Random", "Another"]`; mapper returns `(General, null, ["Random", "Another"])`. Assert `response.UnmappedCategories` equals `["Random", "Another"]` (any order).
4. `Handle_EventWithZeroCategories_DoesNotReportEmptyString` — categories `[]`; mapper returns `(General, null, [])`. Assert `response.UnmappedCategories.Count == 0`.
5. `Handle_BatchAggregatesDistinctUnmappedAcrossEvents` — three events: `["X"]`, `["x"]` (same name different case), `["Y"]`, none mapped. Assert `response.UnmappedCategories.Count == 2` (deduped case-insensitively) and contains both `X` (or `x`) and `Y`.
6. `Handle_MixedEventsBatch_OnlyEventsWithNoMatchContribute` — event A `["PR – léto"]` mapped, event B `["Random"]` unmapped. Assert `response.UnmappedCategories.Count == 1` and contains `"Random"`.
7. `Handle_LogsUnmappedAtInformation_Once` — same setup as test 3; assert exactly one `LogInformation` was emitted referencing both unmapped names. Use `ITestOutputHelper` or a captured logger.
8. `Handle_NoUnmapped_DoesNotEmitInformationLog` — same setup as test 1; assert no `LogInformation` matching the unmapped pattern.
9. `Handle_DryRun_StillPopulatesUnmappedCategories` — if a dry-run flag exists, run with it set; assert `response.UnmappedCategories` is populated identically to the non-dry-run case.
10. `Handle_NullEventCategories_DoesNotThrow` — Graph event whose `Categories` is `null`; handler completes, action is `General`, no unmapped reported.

**Acceptance criteria:**
- Handler no longer references `Enum.TryParse<MarketingActionType>`.
- Handler injects `IMarketingCategoryMapper`.
- All 10 handler tests pass.
- One `Information` log per import run when unmapped non-empty; zero otherwise.
- Existing handler tests for the `created`/`skipped`/`failed` counts still pass unchanged.
- `dotnet format` and `dotnet build` clean.

---

### task: wire-mapper-into-outlook-sync-service

**Goal:** Replace `action.ActionType.ToString()` in `OutlookCalendarSyncService.BuildEventBody` with `_mapper.MapToOutlookCategory(action.ActionType)` so the canonical Outlook category name is written.

**Context:**
Today `OutlookCalendarSyncService.BuildEventBody` (~line 156-182) writes:
```csharp
categories = new[] { action.ActionType.ToString() }
```
That writes raw enum names like `"Campaign"` to Outlook, which do not match the master categories used by the marketing team, so colors are lost on the Outlook side.

New behavior (FR-5):
- Inject `IMarketingCategoryMapper _mapper` (constructor injection alongside existing `IOptionsMonitor<MarketingCalendarOptions>`, Graph client, and logger).
- Replace the `categories = ...` line with:
  ```csharp
  categories = new[] { _mapper.MapToOutlookCategory(action.ActionType) }
  ```
- The mapper falls back to `actionType.ToString()` when no entry exists, preserving exact current behavior in test/dev environments without config (NFR-3).
- `NoOpOutlookCalendarSync` is unchanged — it does not call `BuildEventBody` and does not need the mapper.

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookCalendarSyncService.cs` — inject mapper, update `BuildEventBody`.
- `backend/test/Anela.Heblo.Tests/Features/Marketing/Services/OutlookCalendarSyncServiceTests.cs` — add/modify tests for canonical name.

**Implementation steps:**
1. Open `OutlookCalendarSyncService.cs`. Add `private readonly IMarketingCategoryMapper _mapper;`.
2. Add `IMarketingCategoryMapper mapper` to the constructor parameter list (place it next to other domain-service dependencies). Assign the field.
3. In `BuildEventBody`, replace the `categories = new[] { action.ActionType.ToString() }` expression with `categories = new[] { _mapper.MapToOutlookCategory(action.ActionType) }`.
4. Confirm `NoOpOutlookCalendarSync` is untouched.
5. Update DI registration call sites if the constructor signature change requires it (the DI container resolves automatically once the mapper is registered, but any direct `new OutlookCalendarSyncService(...)` calls in tests must pass a mapper).
6. Run `dotnet format` and `dotnet build`.

**Tests to write:**
Extend `OutlookCalendarSyncServiceTests.cs`. Use a stub `IMarketingCategoryMapper` (hand-rolled or via existing mocking framework).

1. `BuildEventBody_UsesMapperOutlookCategory_WhenConfigured` — mapper returns `"PR – léto"` for `Campaign`. Build body for an action with `ActionType = Campaign`. Assert serialized body contains `"categories": ["PR – léto"]`.
2. `BuildEventBody_FallsBackToToString_WhenMapperReturnsEnumName` — mapper returns `"Campaign"` (the fallback path) for `Campaign`. Build body. Assert serialized body contains `"categories": ["Campaign"]`. (This validates that `BuildEventBody` simply uses whatever the mapper returns, and that backwards compatibility holds when `OutgoingCategories` is empty.)
3. `BuildEventBody_DoesNotCallEnumToStringDirectly` — mapper returns `"FOOBAR"` for any input. Build body for `Campaign`. Assert body contains `"FOOBAR"` (not `"Campaign"`), proving the service routes through the mapper rather than calling `ToString` itself.

**Acceptance criteria:**
- `OutlookCalendarSyncService` no longer calls `actionType.ToString()` for the categories field.
- All 3 new tests pass.
- All existing `OutlookCalendarSyncService` tests continue to pass after constructor signature update.
- `NoOpOutlookCalendarSync` is unmodified.
- `dotnet format` and `dotnet build` clean.

---

### task: add-appsettings-mapping-template

**Goal:** Add the `MarketingCalendar.CategoryMappings` and `MarketingCalendar.OutgoingCategories` template entries to the committed `appsettings.json` so admins have a working reference and validation passes out of the box.

**Context:**
Open Question 1 is resolved by committing category names to `appsettings.{Environment}.json` (non-secret). The committed `appsettings.json` (in `Anela.Heblo.API`) holds the canonical template; environment-specific overrides go in `appsettings.Development.json` etc.

Template (resolves Open Question 2 by including `"Ostatní": "Other"` so the example passes `Validate`):

```json
"MarketingCalendar": {
  "GroupId": "...",
  "PushEnabled": true,
  "CategoryMappings": {
    "Sociální sítě": "General",
    "Ostatní":        "Other",
    "Událost":        "Promotion",
    "Email":          "Launch",
    "PR – léto":      "Campaign",
    "PR – zima":      "Campaign",
    "Fotografie":     "Event"
  },
  "OutgoingCategories": {
    "General":   "Sociální sítě",
    "Other":     "Ostatní",
    "Promotion": "Událost",
    "Launch":    "Email",
    "Campaign":  "PR – léto",
    "Event":     "Fotografie"
  }
}
```

`GroupId` and `PushEnabled` already exist; merge into the existing block — do not duplicate keys.

**Files to create/modify:**
- `backend/src/Anela.Heblo.API/appsettings.json` — add the two dictionary blocks under the existing `MarketingCalendar` section.
- `backend/src/Anela.Heblo.API/appsettings.Development.json` — populate with real Czech category names obtained from the marketing team (per arch-review prerequisite #5). If real names are unavailable at implementation time, copy the same template values so dev startup passes validation; flag in PR description that real names must be substituted before staging rollout.

**Implementation steps:**
1. Open `appsettings.json`. Locate the `MarketingCalendar` section.
2. Add `CategoryMappings` and `OutgoingCategories` properties beside the existing keys, populated with the template values exactly as shown above. Preserve any existing `GroupId` placeholder ("..." or similar).
3. Open `appsettings.Development.json`. If a `MarketingCalendar` section exists, merge the same dictionaries into it. If real Outlook category names from the marketing team are available, substitute them; otherwise commit the template values verbatim.
4. Verify the JSON parses (no trailing commas, valid escaping for `–` em-dash).
5. Run `dotnet build` and confirm startup validation passes (round-trip is consistent: every `OutgoingCategories` value has a matching `CategoryMappings` key).

**Tests to write:**
1. `Configuration_TemplateAppsettings_PassesRoundTripValidation` — load `appsettings.json` (or a copy in test resources) via `ConfigurationBuilder`, bind `MarketingCalendarOptions`, run the same validation routine the module uses; assert no exception. Place this in `MarketingModuleValidationTests.cs`.

**Acceptance criteria:**
- `appsettings.json` and `appsettings.Development.json` both contain `MarketingCalendar.CategoryMappings` and `MarketingCalendar.OutgoingCategories`.
- Application startup with default config does not throw the round-trip validation error.
- Configuration JSON parses without errors.
- The template-validation test passes.

---

### task: regenerate-openapi-client-and-verify-types

**Goal:** Regenerate the TypeScript OpenAPI client so `ImportFromOutlookResponse` exposes a typed `unmappedCategories: string[]` field, and verify the generated file matches.

**Context:**
Per `docs/development/api-client-generation.md`, the TypeScript client at `frontend/src/api/generated/api-client.ts` regenerates as part of `dotnet build`. After the response DTO change, the file must be regenerated and committed. A stale generator can silently produce `unmappedCategories: any` or omit the field.

The generated TypeScript should include (exact name and type may vary slightly with the generator's casing convention; the field MUST be required and non-nullable):

```typescript
interface ImportFromOutlookResponse {
  created: number;
  skipped: number;
  failed: number;
  unmappedCategories: string[];
}
```

**Files to create/modify:**
- `frontend/src/api/generated/api-client.ts` — regenerated (do not hand-edit).

**Implementation steps:**
1. From the repo root run `dotnet build` for the backend solution; this triggers the OpenAPI/NSwag/Swashbuckle pipeline that regenerates `api-client.ts`.
2. Verify `frontend/src/api/generated/api-client.ts` now contains a `unmappedCategories` field on `ImportFromOutlookResponse` typed as `string[]` (required, non-nullable). If the generator produces `string[] | undefined` or `unmappedCategories?: string[]`, that indicates the C# property is nullable or missing initialization — go back to the response DTO task and confirm the field is `List<string>` initialized to `new()` and not annotated `[JsonIgnore]` or similar.
3. Commit the regenerated file (it lives in `frontend/src/api/generated/`).
4. Run `cd frontend && npm run build` to confirm TypeScript still compiles after the regen.

**Tests to write:**
None — the generated file is verified by inspection and by the downstream component (next task) consuming the new typed field. The `npm run build` step provides type-check coverage.

**Acceptance criteria:**
- `frontend/src/api/generated/api-client.ts` contains `unmappedCategories: string[]` on the import response interface as a required, non-nullable field.
- `npm run build` in `frontend/` succeeds with no type errors.
- Backend `dotnet build` succeeds and produces the regen as a side effect.

---

### task: create-unmapped-categories-panel-component

**Goal:** Create the `UnmappedCategoriesPanel` and `CategoryPill` React components co-located with the import modal.

**Context:**
Per the design document, when an import returns unmapped category names the modal renders a warning-level panel listing them as small pills. The panel is local to this feature — not shared. It has no use elsewhere.

Visual treatment (warning level): amber border / light amber background, consistent with the existing design system. Pills are read-only — no click handlers, no copy affordances beyond the user selecting text.

Czech copy (per spec FR-8 + amendment 3 — the subtext does NOT hardcode "Sociální sítě" because `General` may be configured to a different name):
- Heading: `"Nemapované kategorie z Outlooku"`
- Subtext: `"Tyto kategorie nebyly rozpoznány a události byly importovány jako výchozí kategorie (General). Doplňte je do appsettings.json → MarketingCalendar.CategoryMappings."`
- Optional warning icon (e.g. ⚠) to the left of the heading, matching design system iconography.

Component contract:

```typescript
interface UnmappedCategoriesPanelProps {
  categories: string[]; // non-empty; parent guards rendering
}
```

Pill internal contract:

```typescript
interface CategoryPillProps {
  name: string;
}
```

Tailwind / styling rules: follow existing project conventions (CLAUDE.md mentions UI design system in `docs/design/ui_design_document.md`). Use 2-space indentation, single quotes, semicolons (Prettier config).

**Files to create/modify:**
- `frontend/src/components/marketing/detail/UnmappedCategoriesPanel.tsx` — new component.
- `frontend/src/components/marketing/detail/__tests__/UnmappedCategoriesPanel.test.tsx` — new Jest + RTL test file.

**Implementation steps:**
1. Create `UnmappedCategoriesPanel.tsx`. Skeleton:
   ```tsx
   import React from 'react';

   interface UnmappedCategoriesPanelProps {
     categories: string[];
   }

   const CategoryPill: React.FC<{ name: string }> = ({ name }) => (
     <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-amber-100 text-amber-900 border border-amber-300">
       {name}
     </span>
   );

   export const UnmappedCategoriesPanel: React.FC<UnmappedCategoriesPanelProps> = ({
     categories,
   }) => {
     return (
       <div
         role="status"
         aria-label="Nemapované kategorie z Outlooku"
         className="mt-4 p-4 border border-amber-300 bg-amber-50 rounded-md"
       >
         <h3 className="text-sm font-semibold text-amber-900">
           ⚠ Nemapované kategorie z Outlooku
         </h3>
         <p className="text-xs text-amber-800 mt-1">
           Tyto kategorie nebyly rozpoznány a události byly importovány jako výchozí kategorie (General). Doplňte je do appsettings.json → MarketingCalendar.CategoryMappings.
         </p>
         <div className="flex flex-wrap gap-2 mt-3">
           {categories.map((name) => (
             <CategoryPill key={name} name={name} />
           ))}
         </div>
       </div>
     );
   };
   ```
2. Adjust class names if the project uses a non-Tailwind system (check sibling components in `frontend/src/components/marketing/detail/` first).
3. Run `npm run lint`; fix any reported issues.
4. Run `npm run build` to verify type-check passes.

**Tests to write:**
Use Jest + React Testing Library, matching the project's existing test patterns (e.g. `render`, `screen.getByRole`, `screen.getByText`).

1. `renders heading text` — render with `categories=['X']`; assert `screen.getByText(/Nemapované kategorie z Outlooku/)` is present.
2. `renders subtext mentioning appsettings.json` — same render; assert `screen.getByText(/appsettings\.json/)` is present.
3. `renders subtext mentioning General as default` — assert `screen.getByText(/výchozí kategorie \(General\)/)` is present (validates amendment 3 — does NOT hardcode "Sociální sítě").
4. `renders one pill per category` — render with `categories=['A', 'B', 'C']`; assert exactly 3 elements with role/test-id matching the pill (use a `data-testid="category-pill"` if role-based query is fragile; add to the component).
5. `pills display the exact category name` — render with `categories=['PR – léto']`; assert the literal string `'PR – léto'` is rendered (em-dash preserved).
6. `applies warning visual treatment` — assert the root container has the amber border class (e.g., `border-amber-300`); skip if styling assertions are not idiomatic in the project.

**Acceptance criteria:**
- `UnmappedCategoriesPanel.tsx` exports `UnmappedCategoriesPanel` typed with `UnmappedCategoriesPanelProps`.
- All 5–6 component tests pass under `npm test`.
- `npm run build` passes type-check.
- `npm run lint` passes.
- Czech copy matches the spec (no "Sociální sítě" hardcoded in the subtext).

---

### task: wire-unmapped-panel-into-import-modal

**Goal:** Render `UnmappedCategoriesPanel` inside `ImportFromOutlookModal` after the existing created/skipped/failed summary, only when the response contains unmapped names.

**Context:**
`ImportFromOutlookModal.tsx` already shows the import result with `created`, `skipped`, `failed` counts. After this row, it must render the new panel when `(result.unmappedCategories?.length ?? 0) > 0`. No other modal behavior changes — pills are read-only, panel is not independently dismissible (closes with the modal).

Component hierarchy (per design doc):
```
ImportFromOutlookModal
├── ImportSummaryRow              (existing)
└── UnmappedCategoriesPanel       (new, conditional)
```

The OpenAPI-generated typed response now has `unmappedCategories: string[]` (required, non-nullable per the regen task). So `(result.unmappedCategories?.length ?? 0) > 0` simplifies to `result.unmappedCategories.length > 0` once the type is non-nullable, but use the optional chain anyway as a defensive measure for older cached responses.

**Files to create/modify:**
- `frontend/src/components/marketing/detail/ImportFromOutlookModal.tsx` — import and conditionally render the new panel.
- `frontend/src/components/marketing/detail/__tests__/ImportFromOutlookModal.test.tsx` — add tests for the conditional rendering (modify existing file if present; create if not).

**Implementation steps:**
1. Open `ImportFromOutlookModal.tsx`.
2. Add `import { UnmappedCategoriesPanel } from './UnmappedCategoriesPanel';` near the existing imports.
3. Locate the JSX block that renders the result summary (`Vytvořeno`/`Přeskočeno`/`Chyba`).
4. Immediately after that block, add the conditional render:
   ```tsx
   {result && (result.unmappedCategories?.length ?? 0) > 0 && (
     <UnmappedCategoriesPanel categories={result.unmappedCategories!} />
   )}
   ```
   Adjust `result` to whatever the existing variable name is (e.g. `importResult`, `data`, `response`).
5. Run `npm run lint`, `npm run build`, and `npm test` to confirm the modal compiles and existing tests still pass.

**Tests to write:**
Use Jest + RTL. Mock the import API call (use whatever pattern the existing `ImportFromOutlookModal.test.tsx` uses — likely `jest.mock` of the API client).

1. `does not render unmapped panel when response has empty list` — mock import response with `{ created: 5, skipped: 0, failed: 0, unmappedCategories: [] }`; trigger import; assert `screen.queryByText(/Nemapované kategorie z Outlooku/)` is `null`.
2. `does not render unmapped panel before any import has run` — render modal without triggering import; assert no unmapped heading present.
3. `renders unmapped panel with names when response has unmapped` — mock response `{ created: 5, skipped: 0, failed: 0, unmappedCategories: ['PR – jaro', 'Wellness kampaň'] }`; trigger import; assert heading present and both names present.
4. `panel appears after the summary row` — same setup as test 3; query the DOM order — the unmapped panel element comes after the element containing the `created` count. Use `compareDocumentPosition` or `findAllByText` and compare indices.
5. `defensive against undefined unmappedCategories` — mock response without the field at all (cast through `as any`); assert no panel and no crash.

**Acceptance criteria:**
- `ImportFromOutlookModal` imports and renders `UnmappedCategoriesPanel` conditionally.
- All 5 modal tests pass under `npm test`.
- Existing modal tests continue to pass.
- `npm run lint`, `npm run build` clean.
- Manual smoke (recommended, not a CI gate): start `npm start` on port 3000, run an Outlook import that returns at least one unmapped category, confirm the amber panel appears below the summary with the correct Czech copy and pills.