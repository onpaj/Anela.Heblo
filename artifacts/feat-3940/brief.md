## Module / File
`backend/src/Anela.Heblo.Application/Features/Journal/UseCases/CreateJournalTag/CreateJournalTagHandler.cs`

## Coverage
Line coverage: 24.3% (filter threshold: 60%)

## What's not tested
1. **Unauthenticated path** — when `currentUser.IsAuthenticated == false` or `currentUser.Id` is null/empty, the handler returns `ErrorCodes.UnauthorizedJournalAccess` with a structured params dict. No test verifies this guard fires, that the correct error code is returned, or that the repository `AddAsync` is never called.
2. **Success path** — the happy path (authenticated user, tag created, response with Id/Name/Color) is not covered. No test verifies that the tag's `Name` is trimmed before persistence, or that `CreatedByUserId` is set from the authenticated user's ID.

## Why it matters
Journal tags are user-scoped. If the auth guard is accidentally removed, any call without a valid session can create tags attributed to an empty user ID, silently corrupting ownership records. The `Name.Trim()` call is a normalization rule — if it regresses, leading/trailing spaces produce duplicate logical tags.

## Suggested approach
Unit test with mocked `IJournalTagRepository` and `ICurrentUserService`:
- Case: unauthenticated user → ErrorCode == UnauthorizedJournalAccess, AddAsync never called
- Case: authenticated user → AddAsync called with trimmed Name and correct CreatedByUserId, response contains the persisted tag's Id
~30 min effort.

---
_Filed by weekly coverage-gap routine on 2026-08-17. Based on CI run #31804633307 (6f781d410eb84616c8decb088d6d18cd1de01fb8)._
