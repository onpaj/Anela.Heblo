Plan saved to `docs/superpowers/plans/2026-06-07-manufacture-group-id-typed-options.md`.

The plan decomposes the migration into 8 tasks:
1. Add `ManufactureGroupId` to `ManufactureErpOptions` (FR-1)
2. Rewrite handler tests using `Options.Create(...)` with whitespace + ctor-null cases (FR-5 + Spec Amendment 3)
3. Refactor handler to `IOptions<ManufactureErpOptions>` with `IsNullOrWhiteSpace` (FR-2) — committed together with Task 2 so the build is never broken between commits
4. Delete `ManufactureConfigurationKeys.cs` (FR-3)
5. Move `appsettings.json` placeholder into the `ManufactureErp` section (FR-4)
6. Drop the top-level `ManufactureGroupId` from `appsettings.Production.json` (FR-4)
7. Full-solution build/format/test validation (NFR-1, NFR-2)
8. Mandatory PR-description deployment-coordination text covering Production + Staging (NFR-3 + Spec Amendment 2)

Spec Amendment 1 is honored: `GetManufactureSettingsEndpointTests` stays untouched (verified — that file doesn't seed config). The endpoint-test file is verified-unchanged in Task 7 Step 2.