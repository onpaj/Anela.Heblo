# Specification: Runtime Hot-Reload for Marketing `PushEnabled` Kill-Switch

## Summary
Convert the marketing action write handlers to read `PushEnabled` via `IOptionsMonitor<MarketingCalendarOptions>` so the Outlook calendar sync kill-switch responds to runtime configuration changes (e.g., Azure App Configuration) without requiring an application restart. This aligns the handlers with the existing `MarketingCategoryMapper` hot-reload pattern already established in the module.

## Background
The Marketing module integrates with Microsoft Graph to push marketing actions into an Outlook calendar. The `PushEnabled` flag in `MarketingCalendarOptions` is the single kill-switch that disables these outbound Graph calls. Today, two write handlers — `CreateMarketingActionHandler` and `UpdateMarketingActionHandler` — inject `IOptions<MarketingCalendarOptions>`, which captures a startup snapshot. As a result, toggling `PushEnabled` at runtime via Azure App Configuration (or any other dynamic config source) has no effect on the handlers; an application restart is required to honor the change.

This is operationally unsafe. During an incident — Graph API degradation, expired/rotated credentials, runaway sync producing duplicate calendar entries, or any need to halt outbound calls quickly — the operator cannot stop the sync without a deployment-equivalent action (restart). The `MarketingCategoryMapper` singleton in the same module already uses `IOptionsMonitor<MarketingCalendarOptions>` with a documented snapshot-on-failure strategy, demonstrating that runtime config refresh is an intentional design goal for this module. The handlers' continued use of `IOptions<T>` is an inconsistency, and because `PushEnabled` is a kill-switch (not a tunable), it is the higher-impact omission.

Affected code:
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs` (lines 25, 70)
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs` (lines 25, 70)

## Functional Requirements

### FR-1: `CreateMarketingActionHandler` reads `PushEnabled` at runtime
The `CreateMarketingActionHandler` must evaluate the current value of `MarketingCalendarOptions.PushEnabled` at the time the handler executes, not at the time it (or the host) was constructed.

**Acceptance criteria:**
- The handler's constructor injects `IOptionsMonitor<MarketingCalendarOptions>` in place of `IOptions<MarketingCalendarOptions>`.
- The kill-switch check at line 70 reads `_options.CurrentValue.PushEnabled` (or equivalent live access) rather than `_options.Value.PushEnabled`.
- After the application is running, setting `PushEnabled = false` in the configuration source causes the next invocation of the handler to skip the Outlook push, without restart.
- After the application is running, setting `PushEnabled = true` causes the next invocation to perform the Outlook push, without restart.
- No behavioral change occurs when `PushEnabled` is unchanged.

### FR-2: `UpdateMarketingActionHandler` reads `PushEnabled` at runtime
The `UpdateMarketingActionHandler` must evaluate `PushEnabled` at handler execution time, mirroring FR-1.

**Acceptance criteria:**
- The handler's constructor injects `IOptionsMonitor<MarketingCalendarOptions>` in place of `IOptions<MarketingCalendarOptions>`.
- The kill-switch check at line 70 reads `_options.CurrentValue.PushEnabled`.
- Runtime toggling of `PushEnabled` is honored on the next handler invocation, without restart.

### FR-3: Consistent pattern with existing hot-reload usage
The implementation must match the conventions already established by `MarketingCategoryMapper`, so that future readers see a single, consistent pattern within the Marketing module.

**Acceptance criteria:**
- The field name and access pattern follow the conventions used in `MarketingCategoryMapper` (e.g., field type `IOptionsMonitor<MarketingCalendarOptions>`, access via `.CurrentValue`).
- No additional caching of `PushEnabled` is introduced at the handler level that would re-establish the snapshot problem.
- If `MarketingCategoryMapper` uses a specific failure/fallback strategy when reading options, this is intentionally **not** copied for the simple boolean `PushEnabled` — the snapshot-on-failure strategy is specific to mapping data, not to a primitive flag.

### FR-4: No regression in non-push code paths
All existing functionality of both handlers — request validation, persistence, response shape, error handling — must remain identical. The change is strictly an option-access mechanism.

**Acceptance criteria:**
- All existing unit/integration tests for `CreateMarketingActionHandler` and `UpdateMarketingActionHandler` pass without modification, except where they construct the handler and must be updated to supply an `IOptionsMonitor<T>` test double.
- Behavior when `PushEnabled = true` and when `PushEnabled = false` is observationally identical to current behavior at the moment of handler invocation.

### FR-5: Test coverage for runtime toggling
The hot-reload behavior must be covered by automated tests so the kill-switch cannot silently regress to a snapshot read.

**Acceptance criteria:**
- A unit test exists for each handler that:
  1. Constructs the handler with an `IOptionsMonitor<MarketingCalendarOptions>` test double initialized with `PushEnabled = true`.
  2. Invokes the handler and asserts the push path executed.
  3. Mutates the monitor's current value to `PushEnabled = false`.
  4. Invokes the handler again with the same instance and asserts the push path did **not** execute.
- A symmetric test covers the `false → true` transition.
- Tests use the standard test-double approach already used elsewhere in the codebase for `IOptionsMonitor<T>` (or introduce a minimal helper if none exists).

## Non-Functional Requirements

### NFR-1: Performance
`IOptionsMonitor<T>.CurrentValue` is a fast property read backed by a cached value updated by change tokens. The overhead per handler invocation must remain negligible (sub-microsecond) and must not introduce any measurable latency vs. the current `IOptions<T>.Value` access.

### NFR-2: Security
No change to authentication, authorization, or data sensitivity. The kill-switch itself becomes more responsive, which is a security/operations improvement: operators can disable outbound Graph traffic immediately during an incident without a deployment.

### NFR-3: Operability
After deployment, an operator must be able to toggle `PushEnabled` via the configured runtime configuration source (e.g., Azure App Configuration) and observe the change taking effect on subsequent handler invocations within the refresh interval already configured for that source. No new configuration source, polling mechanism, or refresh wiring is introduced by this change — it relies on whatever is already in place for `MarketingCategoryMapper`.

### NFR-4: Reliability
Reading `CurrentValue` must not throw under normal operation. If the underlying `IOptionsMonitor<T>` ever fails to produce a value, the handler should fail closed — i.e., behave as if `PushEnabled = false` and skip the push — rather than throwing and failing the whole create/update operation. (See Open Questions if this contradicts existing module conventions.)

### NFR-5: Backwards compatibility
The public contract of `CreateMarketingActionHandler` and `UpdateMarketingActionHandler` (their MediatR request/response types) does not change. Only the DI signature changes, which is internal to composition.

## Data Model
No data model changes. `MarketingCalendarOptions` remains as-is. The only entities involved are:

- `MarketingCalendarOptions` — existing options class containing `PushEnabled` (bool), `GroupId`, and other Graph/calendar settings.
- `CreateMarketingActionHandler` / `UpdateMarketingActionHandler` — MediatR handlers whose constructor signatures change.

## API / Interface Design
No external API surface changes. The change is confined to:

1. **Constructor parameter type change** in both handlers:
   - Before: `IOptions<MarketingCalendarOptions> options`
   - After: `IOptionsMonitor<MarketingCalendarOptions> options`

2. **Field type change** in both handlers:
   - Before: `private readonly IOptions<MarketingCalendarOptions> _options;`
   - After: `private readonly IOptionsMonitor<MarketingCalendarOptions> _options;`

3. **Access expression change** at the `PushEnabled` check site (line 70 in both files):
   - Before: `if (_options.Value.PushEnabled)`
   - After: `if (_options.CurrentValue.PushEnabled)`

DI registration of `MarketingCalendarOptions` does not change — `IOptionsMonitor<T>` is available automatically whenever `IOptions<T>` is registered via `services.Configure<T>(...)`.

## Dependencies
- **Microsoft.Extensions.Options** — `IOptionsMonitor<T>` is part of the standard options abstraction; no new package is required.
- **Existing Azure App Configuration / change-token plumbing** — whatever already powers `MarketingCategoryMapper`'s hot-reload is reused as-is. This work depends on that infrastructure being functional but does not modify it.
- **Existing test infrastructure** — `Moq`/`NSubstitute`/manual stub patterns already used in the handler test suites.

## Out of Scope
- **`OutlookCalendarSyncService`** (`options.Value` cached at construction, line 43). The brief explicitly deprioritizes this: it is scoped, and its options (`GroupId`, `PushEnabled`) are unlikely to change independently of its lifetime. Not touched in this work.
- **`MarketingCategoryMapper`** — already correct; no changes.
- **Changes to `MarketingCalendarOptions`** itself (no new fields, no defaults changed).
- **Changes to Azure App Configuration wiring, refresh intervals, or sentinel keys.**
- **A general audit of `IOptions<T>` vs. `IOptionsMonitor<T>` across the rest of the codebase.** That is worthwhile but separate from this kill-switch fix.
- **Adding telemetry, alerting, or audit logging around `PushEnabled` transitions.** Worth considering but out of scope here.
- **UI or admin endpoint for toggling `PushEnabled`.** The toggle remains a configuration-source concern.

## Open Questions
None.

## Status: COMPLETE