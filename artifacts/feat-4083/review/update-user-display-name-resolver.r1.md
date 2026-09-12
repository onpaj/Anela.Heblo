# Code Review: update-user-display-name-resolver

## Summary
The implementation successfully repoints `UserDisplayNameResolver` from the Authorization module's `IAuthorizationRepository`/`AppUser` to the Shared/Users-owned `IUserDirectorySource`/`UserDirectoryEntry` contract, eliminating the cross-module dependency. All behavior is preserved: identifier normalization (case-insensitive, both Entra object id and email), 5-minute caching via IMemoryCache, fallback to email for blank display names, and short-circuit on empty input. All 6 required test cases are present and correctly structured; no references to the old dependencies remain in either file.

## Review Result: PASS

### task: update-user-display-name-resolver
**Status:** PASS

**Verification:**

✓ **Dependency Replacement:**
- Constructor (line 19) now takes `IUserDirectorySource directorySource` and `IMemoryCache cache`
- No references to `IAuthorizationRepository` or `AppUser` in resolver or tests
- Depends only on `IUserDirectorySource` and `UserDirectoryEntry`

✓ **Identifier Normalization:**
- Lines 29–32: `Distinct(StringComparer.OrdinalIgnoreCase)` ensures case-insensitive matching
- `Where(id => !string.IsNullOrWhiteSpace(id))` skips blank identifiers

✓ **Short-circuit on Empty Input:**
- Lines 34–37: Returns empty dictionary without querying `IUserDirectorySource`

✓ **Caching Strategy:**
- Line 13: `CacheKey = "UserDisplayNameResolver:Lookup"`
- Line 14: 5-minute TTL (`TimeSpan.FromMinutes(5)`)
- Lines 53–56: Checks cache before querying
- Line 80: Sets cache with correct TTL

✓ **Lookup Building:**
- Line 58: Calls `_directorySource.GetAllAsync(cancellationToken)`
- Lines 69–72: Maps `EntraObjectId` to display name
- Lines 74–77: Maps `Email` to display name
- Line 63: Falls back to email when display name is blank/whitespace

✓ **All 6 Test Cases Present and Correct:**
1. `ResolveAsync_MapsEntraObjectIdToDisplayName` (lines 30–38) — resolves Entra object id to display name
2. `ResolveAsync_MapsEmailIdentifierToDisplayName` (lines 40–49) — resolves email identifier to display name
3. `ResolveAsync_UnknownIdentifier_ResolvesToNull` (lines 51–60) — unknown identifier returns null
4. `ResolveAsync_FallsBackToEmail_WhenDisplayNameMissing` (lines 62–70) — uses email when display name is blank
5. `ResolveAsync_EmptyInput_DoesNotQueryRepository` (lines 72–79) — empty input skips repository call
6. `ResolveAsync_CachesLookup_AcrossCalls` (lines 81–91) — repeated calls invoke `GetAllAsync` only once

✓ **Interface Preservation:**
- Public `IUserDisplayNameResolver` signature unchanged
- DI registration in `ApplicationModule.cs` remains valid (no direct construction sites)

✓ **Compilation and Testing:**
- All 6 tests pass (as reported)
- `dotnet build` succeeds
- `dotnet format` passes

## Overall Notes
The implementation adheres strictly to the task specification. The contract switch is clean and complete, with no lingering references to the old dependencies. Test coverage is thorough, and the refactoring maintains backward compatibility at the public interface level.
