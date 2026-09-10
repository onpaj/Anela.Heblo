### task: verify-full-suite

**Files:** none (verification only)

**Depends on:** add-user-directory-contract, add-authorization-directory-adapter, update-user-display-name-resolver, add-module-boundary-rule

- [ ] **Step 1: Full backend build**

Run: `cd backend && dotnet build`
Expected: `Build succeeded.` with 0 errors, 0 new warnings attributable to this change.

- [ ] **Step 2: Full backend test run**

Run: `cd backend && dotnet test`
Expected: all tests pass, including `UserDisplayNameResolverTests` (6/6) and `ModuleBoundariesTests`
(all `[Theory]` cases including the new `"Shared.Users -> Authorization"` entry, plus the unrelated
`Application_types_should_not_reference_AspNetCore_namespaces` fact, all green).

- [ ] **Step 3: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: no formatting diffs. If it reports changes, run `dotnet format` (no `--verify-no-changes`) and re-stage/commit the formatting fix as its own commit.

- [ ] **Step 4: Confirm no consumer module needed a code change**

Run: `git diff --stat main...HEAD -- backend/src/Anela.Heblo.Application/Features/KnowledgeBase backend/src/Anela.Heblo.Application/Features/Article backend/src/Anela.Heblo.Application/Features/Leaflet backend/src/Anela.Heblo.Application/Features/Smartsupp`
Expected: empty output — none of the four consumer feature modules were touched, confirming
`IUserDisplayNameResolver`'s public contract stayed stable (NFR-3).

- [ ] **Step 5: Grep for any remaining direct reference (belt-and-suspenders manual check)**

Run: `grep -rn "IAuthorizationRepository\|Authorization.Entities" backend/src/Anela.Heblo.Application/Shared/Users/`
Expected: no output (empty match) — confirms FR-4's acceptance criterion by direct inspection, not just by the automated rule.

- [ ] **Step 6: Final commit (if any formatting fix was needed) and push**

```bash
git push
```
