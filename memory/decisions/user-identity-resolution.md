# Decision: One Canonical Way to Resolve the Current User

**Date:** 2026-06-05
**Status:** Accepted — see `docs/architecture/development_guidelines.md` ADR-004.

**Decision:** User identity is resolved in exactly one place and one way.

- `ICurrentUserService` **interface** → `Anela.Heblo.Domain/Features/Users/`
- `CurrentUserService` **implementation** (depends on `IHttpContextAccessor`, a web type) → `Anela.Heblo.API/Features/Users/` (outer ring, **not** Application)
- **DI registration** → `Anela.Heblo.API/Features/Users/UsersModule.cs` (`AddUsersModule()`)
- **Resolution happens inside MediatR handlers**, which inject `ICurrentUserService`.

**Rules:**
- Controllers never resolve identity — no `GetCurrentUserId()` helper, no `ICurrentUserService` injection, no stamping `UserId`/`ModifiedBy` onto requests.
- Request DTOs carry no client-settable `UserId`/`ModifiedBy` (spoofing hole).
- Handlers never inject `IHttpContextAccessor` — always go through `ICurrentUserService`.
- GUID audit values: `Guid.TryParse(user.Id, out var id) ? id : null` — nullable, no sentinel GUID. No shared `UserIdResolver` helper unless a real consumer exists. See `CreateNewTransportBoxHandler.cs` and `OpenOrResumeBoxByCodeHandler.cs`, and the related decision [logistics-no-userid-resolver.md](logistics-no-userid-resolver.md).

**Why this exists:** The rule was written down nowhere, so a daily arch-review routine kept re-discovering fragments of the same concern as separate findings — which felt like the code was "moving back and forth." It was not: every step pushed the same direction. ADR-004 + the guidelines section are the single source of truth so the loop stops.

**Convergence history (five arch-review features, all the same direction):**
- `feat-arch-review-users-currentuserservice-liv` — move impl Application → API. *(merged)*
- `feat-arch-review-users-missing-usersmodule-cs` — add `UsersModule.cs`. *(merged)*
- `feat-arch-review-users-gridlayoutscontroller-` — drop unused `ICurrentUserService` from controller. *(merged)*
- `feat-arch-review-users-updatemanufactureorder` — replace direct `IHttpContextAccessor` with `ICurrentUserService`.
- `feat-arch-review-users-baseapicontroller-getc` = **PR #2602** — remove `BaseApiController.GetCurrentUserId()` and migrate the last 3 outlier controllers (`Dashboard`, `CarrierCooling`, `GiftSettings`) to handler-side resolution. The final convergence step.
