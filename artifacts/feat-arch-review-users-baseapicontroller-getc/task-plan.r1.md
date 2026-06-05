Plan saved to `docs/superpowers/plans/2026-06-04-consolidate-getcurrentuserid-via-icurrentuserservice.md`.

## Summary

Created a 10-task implementation plan that incrementally migrates user identity resolution from `BaseApiController.GetCurrentUserId()` into MediatR handlers via `ICurrentUserService`:

- **Task 1** — Add 3 missing claim priority-chain tests to existing `CurrentUserServiceTests`.
- **Tasks 2–3** — Migrate `SetCarrierCoolingHandler` and `SetGiftSettingHandler` with `ErrorCodes.Unauthorized` (HTTP 401) defense-in-depth, mirroring `CreateMarketingActionHandler`. GiftSettings controller gets an explicit `Unauthorized` branch to preserve its 204 success contract.
- **Tasks 4–8** — Migrate the 5 Dashboard handlers (one per task) preserving the existing `"anonymous"` fallback. Mutator keeps its existing signature; handlers resolve identity then pass it in.
- **Task 9** — Delete `BaseApiController.GetCurrentUserId()`, `BaseApiControllerTests.cs`, and trim stale `DashboardControllerTests` assertions.
- **Task 10** — Regenerate the OpenAPI TypeScript client and verify frontend build/lint.

Each task is bite-sized with explicit test code, exact file paths, complete code blocks (no placeholders), commit commands, and verification steps. Task ordering keeps the build green between commits — `GetCurrentUserId()` stays available until all controller call-sites have been migrated.