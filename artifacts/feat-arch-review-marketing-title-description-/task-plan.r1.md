Plan saved to `artifacts/feat-arch-review-marketing-title-description-/plan.r1.md`.

The 13-task TDD plan covers:

- **Task 1** — Introduce `MarketingActionTestBuilder` and migrate 7 existing test files to absorb the upcoming ctor ripple.
- **Tasks 2–3** — Add `UpdateDetails` and the new public constructor on `MarketingAction`, both delegating to private `NormalizeTitle` / `NormalizeDescription` helpers (TDD).
- **Tasks 4–7** — Refactor the three call sites (`UpdateMarketingActionHandler`, `CreateMarketingActionHandler`, `OutlookEventImportMapper.BuildAction`, `OutlookEventImportMapper.ApplyChanges`). Outlook paths guard `currentUser.Id` per SA-2.
- **Tasks 8–9** — Red/green pair for SA-1: fix `HasChanges` to compare normalized values so re-importing whitespace-titled events isn't reported as `Updated`.
- **Tasks 10–11** — Integration tests for FR-7 (new event and update event trim behavior).
- **Task 12** — Tighten setters to `private set`, narrow the parameterless ctor to `private` for EF Core, and switch the test builder over to the new ctor.
- **Task 13** — Final `dotnet build` / `dotnet format` / full test suite gates plus a grep check that no direct setter access survives in `Application/`.

All five arch-review amendments (SA-1 through SA-5) are mapped to specific tasks. Each task ends with a discrete commit so a failure point is easy to bisect.