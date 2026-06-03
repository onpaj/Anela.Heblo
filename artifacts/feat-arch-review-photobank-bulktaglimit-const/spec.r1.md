# Specification: Extract `BulkTagLimit` Constant to Shared `PhotobankConstants`

## Summary
The `BulkTagLimit` value (5,000) is duplicated as a private constant in two Photobank bulk-tag handlers. This spec consolidates the constant into a single shared `PhotobankConstants` class to eliminate drift risk and align with DRY. No behavioral change is intended — the literal value, error code, and error payload remain identical.

## Background
The Photobank module enforces a maximum of 5,000 photos per bulk-tag operation. Two handlers each define `private const int BulkTagLimit = 5_000;`:

- `backend/src/Anela.Heblo.Application/Features/Photobank/.../BulkAddPhotoTagHandler.cs:15`
- `backend/src/Anela.Heblo.Application/Features/Photobank/.../BulkAddPhotoTagByIdsHandler.cs:15`

Both handlers reject requests exceeding the limit with the same `ErrorCodes.BulkTagLimitExceeded` and identical error payload shape (`Params["Count"]`, `Params["Limit"]`). The duplication was flagged by the daily architecture-review routine on 2026-05-27. Because the two handlers are owned by the same module and share the same business rule, a single canonical source removes the risk that a future limit change updates one site and silently leaves the other behind.

## Functional Requirements

### FR-1: Introduce `PhotobankConstants`
Create a new file `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankConstants.cs` (if it does not exist) containing a static class `PhotobankConstants` with a single public constant:

```csharp
public static class PhotobankConstants
{
    public const int BulkTagLimit = 5_000;
}
```

The class must reside in the `Anela.Heblo.Application.Features.Photobank` namespace (matching the existing Photobank feature folder conventions defined in `docs/architecture/filesystem.md`).

**Acceptance criteria:**
- File exists at the specified path.
- `PhotobankConstants` is `public static` and `BulkTagLimit` is `public const int` with value `5_000`.
- Namespace matches the Photobank feature folder.
- No additional constants are introduced beyond `BulkTagLimit` (scope is limited to the duplication identified in the brief).

### FR-2: Remove duplicated constant from `BulkAddPhotoTagHandler`
Delete the `private const int BulkTagLimit = 5_000;` line from `BulkAddPhotoTagHandler.cs`. Replace every in-file reference (the guard comparison `if (total > BulkTagLimit)` and any error-payload binding such as `Params["Limit"] = BulkTagLimit`) with `PhotobankConstants.BulkTagLimit`.

**Acceptance criteria:**
- The private constant declaration is removed.
- All references in the file compile and resolve to `PhotobankConstants.BulkTagLimit`.
- The handler's behavior (limit check, error code, error payload values) is byte-for-byte equivalent to the prior implementation.

### FR-3: Remove duplicated constant from `BulkAddPhotoTagByIdsHandler`
Apply the same change as FR-2 to `BulkAddPhotoTagByIdsHandler.cs`.

**Acceptance criteria:**
- The private constant declaration is removed.
- All references resolve to `PhotobankConstants.BulkTagLimit`.
- Handler behavior is unchanged.

### FR-4: Preserve error contract
The HTTP/MediatR response for over-limit requests must remain identical:
- Error code: `ErrorCodes.BulkTagLimitExceeded` (unchanged).
- Error params: `Count` = the actual photo count from the request; `Limit` = `5000`.
- HTTP status code (or equivalent failure mapping) emitted by the existing pipeline is unchanged.

**Acceptance criteria:**
- An over-limit request to either bulk-tag endpoint returns the same error code, message template, and params as before the refactor.
- Existing tests asserting on `ErrorCodes.BulkTagLimitExceeded` and the `Count`/`Limit` params continue to pass without modification.

### FR-5: Test coverage for shared constant
Existing unit/integration tests that exercise the 5,000-photo boundary on either handler must continue to pass without modification. If either handler currently lacks a test asserting the limit guard, add one that:
- Submits a bulk-tag request with 5,001 items.
- Asserts the result is a failure with `ErrorCodes.BulkTagLimitExceeded`.
- Asserts `Params["Limit"]` equals `PhotobankConstants.BulkTagLimit`.

**Acceptance criteria:**
- Both handlers have at least one test covering the over-limit boundary that references `PhotobankConstants.BulkTagLimit` (not a magic number) in its assertion.
- All existing Photobank tests pass.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact. The constant is resolved at compile time; `public const int` values are inlined by the C# compiler at call sites. No allocations, no startup cost.

### NFR-2: Security
None. This is a code-organization refactor with no change to authorization, input validation, or data handling.

### NFR-3: Maintainability
A single canonical source for `BulkTagLimit` eliminates the drift risk identified by the architecture review. Future limit changes touch exactly one line.

### NFR-4: Backwards compatibility
None to preserve in terms of public API surface — `PhotobankConstants` is a new internal-to-the-module utility and the private constants being removed were never part of any public contract. The HTTP/MediatR error contract (FR-4) is preserved exactly.

### NFR-5: Build and formatting
- `dotnet build` must succeed with no new warnings.
- `dotnet format` must produce no diff after the change.

## Data Model
No data-model changes. No database migrations, no DTO changes, no entity changes.

## API / Interface Design
No public API changes. No new endpoints, no changed endpoints, no changed request/response shapes. The two existing bulk-tag endpoints continue to behave identically from a client's perspective.

Internal interface change (intra-module only):
- New type: `Anela.Heblo.Application.Features.Photobank.PhotobankConstants` (static class, single `public const int BulkTagLimit`).
- Removed members: `BulkAddPhotoTagHandler.BulkTagLimit` and `BulkAddPhotoTagByIdsHandler.BulkTagLimit` (both `private const`, no external visibility).

## Dependencies
- No new NuGet packages.
- No new external services.
- No dependency on other in-flight features.
- Depends only on the existing Photobank feature folder structure and the existing `ErrorCodes.BulkTagLimitExceeded` enum value.

## Out of Scope
- Changing the value of `BulkTagLimit` (stays at 5,000).
- Making the limit configurable via `appsettings.json`, environment variables, or a feature flag.
- Extracting any other duplicated constants or magic numbers elsewhere in the Photobank module or codebase.
- Adding additional constants to `PhotobankConstants` beyond `BulkTagLimit`.
- Changing the error code, error message template, or error param keys.
- Refactoring the handlers' structure, validation flow, or any other logic.
- Frontend changes — the limit is enforced server-side and the client receives the same error contract.

## Open Questions
None.

## Status: COMPLETE