# In-app permissions — cutover runbook

## One-time Entra change
Add a `super_user` **app role** (value `super_user`) to the API app registration and assign
it to the operator account(s). This is the break-glass/admin bootstrap. Per-feature app roles
remain in the manifest but become unused (optional cleanup later).

## Deploy
1. Apply the DB migration `AddAuthorizationTables` (migrations are manual — see CLAUDE.md).
   Startup also runs `AuthorizationSeeder` to create/sync the 10 system groups.
2. Deploy the build. Enforcement now flows through the claims transformation.

## Immediately after deploy (the access-gap mitigation)
Non-super users have **no access until assigned** (decision #4). To minimize the window:
1. Pre-write the assignment list: each staff email → the in-app group matching their old
   Entra role (names align 1:1: Marketer, Skladnik, Ucetni, …).
2. A `super_user` logs in → `/admin/access` → Users tab → assign each user their group(s).
   Users appear in the list after their first login (AppUser is materialized on login); for
   users who haven't logged in yet, assignment can be done as soon as they do.
3. Verify with the per-user "effective permissions" view.

## Rollback
Remove the deploy; the additive tables are harmless if unused. (No feature flag by decision #7.)

## Notes
- Disabling a user (Users tab) takes effect within ~5 min (cache TTL).
- System groups are read-only and re-synced from `AccessMatrix` on every startup.
