## Module
UserManagement

## Finding
`GraphService.GetAppRoleMembersAsync` in `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` (lines 193–384) is approximately 190 lines and executes five sequential Graph API steps inline in a single method body, with no private helper extraction:

1. Acquire token (`AcquireGraphTokenAsync` is already a helper — the rest is not)
2. Resolve service principal ID and collect app roles (`spUrl` fetch, lines 230–247)
3. Find `appRoleId` for the requested role value (loop, lines 249–261)
4. Paginate `appRoleAssignedTo` to collect direct users and groups (while loop, lines 268–306)
5. Expand group members via recursive `GetGroupMembersAsync` call (lines 308–313)
6. Batch-resolve user display names via Graph `$batch` (for loop, lines 317–369)

Each step is 10–40 lines and has an independent failure mode. The previous finding #2630 addressed `GetGroupMembersAsync` (which is now ~88 lines); `GetAppRoleMembersAsync` was not covered.

## Why it matters
A 190-line method with six sequential concerns is hard to unit-test (each step would need the whole method exercised), hard to read (the "what am I looking at" question takes time to answer), and difficult to maintain (a change to step 3 sits 50 lines away from step 6 with no visual separator). This exceeds the project's 50-line guideline for method length.

## Suggested fix
Extract each step as a `private async Task<T>` helper method (e.g. `ResolveServicePrincipalAsync`, `FindAppRoleIdAsync`, `CollectRoleAssigneesAsync`, `BatchResolveUserDtosAsync`). The public method becomes an orchestrating sequence of five calls. No behaviour changes; test surface improves. The recursive `GetGroupMembersAsync` call in step 5 stays as-is.

---
_Filed by daily arch-review routine on 2026-09-17._
