# Design: Reconcile frontend feature-flag mirror with backend registry

No UI/UX section — this task has no user-facing surface. It touches a
constants file, a config file, and adds a backend unit test; no component
renders differently and no screen changes.

## Component design

Three touch points, no new components or modules. Each is described with its
responsibility before/after and its interface (inputs/outputs) so the
boundary between "source of truth" and "mirror" stays explicit.

### 1. `FeatureFlagRegistry.cs` (backend, source of truth) — unchanged

Responsibility: enumerate every real flag, its description, and its default.
This is the design's anchor — nothing here changes. Referenced by name only
because the other two components are defined *relative to* it.

Interface (unchanged):
```csharp
public static class FeatureFlagRegistry {
    public static readonly IReadOnlyList<FeatureFlagDefinition> All;      // 3 entries
    public static readonly IReadOnlyDictionary<string, FeatureFlagDefinition> ByKey;
}
```
Current `All` keys: `is-delivered-order-completion-enabled`,
`is-delivered-order-completion-test-source-enabled`,
`is-label-printing-enabled`.

### 2. `frontend/src/features/feature-flags/featureFlags.ts` (frontend mirror)

Responsibility narrows to exactly what it should have been: expose only flag
keys that (a) exist in the backend registry and (b) have a UI consumer.
Today that's one key.

Before:
```ts
export const FeatureFlagKeys = {
  TransportBoxTracking: "is-transport-box-tracking-enabled",
  StockTaking: "is-stock-taking-enabled",
  BackgroundRefresh: "is-background-refresh-enabled",
  LabelPrinting: "is-label-printing-enabled",
} as const;
```

After:
```ts
export const FeatureFlagKeys = {
  LabelPrinting: "is-label-printing-enabled",
} as const;

export type FeatureFlagKey = (typeof FeatureFlagKeys)[keyof typeof FeatureFlagKeys];
```
(`FeatureFlagKey` type line is unchanged, kept for completeness of the
diff boundary.)

No change to consumers: `grep` across `frontend/src` for
`FeatureFlagKeys.TransportBoxTracking|StockTaking|BackgroundRefresh` must
stay at zero matches (already verified in the architecture step) — this is
the precondition that makes the deletion safe, not something this step
decides.

`DeliveredOrderCompletion` / `DeliveredOrderCompletionTestSource` are
deliberately **not** mirrored here — they gate a server-side job with no UI
control surface. Adding unused constants for them would recreate the exact
defect this task fixes (mirror entries with nothing behind them, just in the
other direction: mirror entries with nothing *in front* of them). If a future
task adds an admin toggle for either job flag, the mirror entry gets added
then, next to the UI code that uses it.

### 3. `backend/src/Anela.Heblo.API/appsettings.json` — `FeatureManagement` section

Responsibility: hold the default value ASP.NET's `IFeatureManager` reads for
each registry key. Must contain exactly the registry's key set — a key here
with no registry entry is inert config; a registry key with no entry here
falls back to `FeatureFlagDefinition.DefaultValue` in code, so the risk of a
missing line is lower than the risk of an orphaned one.

Before:
```json
"FeatureManagement": {
  "is-transport-box-tracking-enabled": false,
  "is-stock-taking-enabled": false,
  "is-background-refresh-enabled": true,
  "is-delivered-order-completion-enabled": false,
  "is-delivered-order-completion-test-source-enabled": false,
  "is-label-printing-enabled": true
}
```

After:
```json
"FeatureManagement": {
  "is-delivered-order-completion-enabled": false,
  "is-delivered-order-completion-test-source-enabled": false,
  "is-label-printing-enabled": true
}
```

`appsettings.Staging.json` is untouched — it already carries only
`is-label-printing-enabled` (an intentional environment override, not a full
mirror of the base file; confirmed no orphaned keys live there).

### 4. New backend test — drift guard (FR-4)

Responsibility: assert, as an automated build gate, the invariant this whole
task manually restores by hand: *every frontend flag key value has a matching
entry in `FeatureFlagRegistry.All`*. This is a one-directional subset check
(frontend ⊆ backend), not an exact-match check — the two `DeliveredOrderCompletion`
keys are legitimately backend-only (see component 2), so the test must not
require the reverse direction.

Location: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/FeatureFlagRegistryFrontendMirrorTests.cs`
(new file, sibling to the existing `FeatureFlagsControllerLintTests.cs` in
the same folder).

Design follows the precedent already in this codebase —
`LocalizationCoverageTests.cs` walks up from the test assembly's
`AppContext.BaseDirectory`/`Assembly.GetExecutingAssembly().Location` to find
the repo root, then reads a frontend file as plain text and regexes the
values out (no TS parser / no npm dependency needed, keeps the test running
under plain `dotnet test`). This test reuses that exact walk-up-to-repo-root
technique rather than the fixed `../../../../../..` relative path style used
in `AccessMatrixJsonTests.cs`, since both are already-proven patterns in this
repo and the walk-up variant is more resilient to build output path changes.

Interface / shape:
```csharp
public class FeatureFlagRegistryFrontendMirrorTests
{
    // Locates <repoRoot>/frontend/src/features/feature-flags/featureFlags.ts
    // by walking up from the test assembly location (same technique as
    // LocalizationCoverageTests.FrontendI18n_ShouldHaveTranslationsForAllErrorCodes).
    private static string LoadFeatureFlagsTs();

    // Extracts the quoted string values assigned in the `FeatureFlagKeys`
    // object literal, e.g. via Regex @"""([\w-]+)""\s*,?" restricted to the
    // block between `FeatureFlagKeys = {` and the closing `} as const;`.
    private static IReadOnlyList<string> ExtractFrontendFlagValues(string tsSource);

    [Fact]
    public void FrontendMirror_AllKeys_ExistInBackendRegistry()
    {
        var frontendValues = ExtractFrontendFlagValues(LoadFeatureFlagsTs());
        var backendKeys = FeatureFlagRegistry.All.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);

        var orphaned = frontendValues.Where(v => !backendKeys.Contains(v)).ToList();

        orphaned.Should().BeEmpty(
            "frontend/src/features/feature-flags/featureFlags.ts declares flag keys with no " +
            "FeatureFlagRegistry.cs entry — delete the orphaned mirror or add the missing " +
            "registry entry (see docs/development/feature-flags.md)");
    }
}
```

Failure mode this catches going forward: someone adds a frontend constant
without a registry entry (the exact drift this task fixes), or removes a
registry entry without deleting its frontend mirror. It does **not** catch
the reverse (backend flag with no frontend mirror) — that's fine per FR-3,
since backend-only flags are a valid, expected steady state.

FR-5 (appsettings ⊆ registry exact-match check) is deferred, not designed
here: it would need its own small test reading `appsettings.json`'s
`FeatureManagement` section (`System.Text.Json`, straightforward — no
walk-up-and-regex needed since it's valid JSON) and diffing keys against
`FeatureFlagRegistry.All` both ways (exact match, since the base
`appsettings.json` is meant to be complete, unlike the Staging override
file). Worth adding in the same PR if FR-4 lands without friction, but not
required — noted as a nice-to-have, not a blocking design decision.

## Data schemas

No wire/API schema changes — `GET /api/feature-flags` response shape
(`EvaluateFlagsForClientResponse.Flags: IDictionary<string,bool>`) is
unaffected, since it was already built purely from `FeatureFlagRegistry.All`
and never read the frontend file or the orphaned appsettings keys.

Two config-shaped artifacts change membership only, not shape:

- `FeatureFlagKeys` (frontend, `frontend/src/features/feature-flags/featureFlags.ts`):
  `Record<string, string>`-shaped `const` object, `as const`-narrowed to a
  literal-string union via `FeatureFlagKey`. Shape unchanged; goes from 4 keys
  to 1.
- `FeatureManagement` (backend, `appsettings.json`): flat
  `Record<string, bool>` JSON object under the standard
  `Microsoft.FeatureManagement` config section. Shape unchanged; goes from 6
  keys to 3.

No DB schema involved — `FeatureFlagOverrides` (if any admin-set override
rows exist for the three removed keys) are addressed operationally in the
development step (check dev/staging DB), not through a migration, since
orphaned override rows for keys absent from `FeatureFlagRegistry.ByKey` are
already inert (nothing reads them).
