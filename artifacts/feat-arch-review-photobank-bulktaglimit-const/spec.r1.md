# Specification: Extract `BulkTagLimit` to Shared `PhotobankConstants`

## Summary
The `BulkTagLimit` constant (5,000) is currently duplicated as a `private const int` in two Photobank handlers that enforce the same bulk-tag business rule. This refactor extracts it into a single shared `PhotobankConstants` static class so future changes to the limit (or addition of other Photobank-wide constants) are made in one place. Behavior is unchanged.

## Background
Two MediatR handlers in the Photobank module enforce a hard upper bound on how many photos can be tagged in a single bulk operation:

- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/BulkAddPhotoTag/BulkAddPhotoTagHandler.cs` (line 15) — applies to a filter-based bulk-tag operation, comparing the total filtered count to the limit.
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/BulkAddPhotoTagByIds/BulkAddPhotoTagByIdsHandler.cs` (line 15) — applies to an explicit-IDs bulk-tag operation, comparing the supplied list size to the limit.

Both handlers:
- Declare `private const int BulkTagLimit = 5_000;` with identical values.
- Return `ErrorCodes.BulkTagLimitExceeded` with the same `Params` shape (`Count`, `Limit`).

A maintainer wishing to change the limit must remember to update both files; this is a DRY violation and an easy miss. The repository already establishes a clear convention for per-feature constants at the feature root (e.g. `CatalogConstants.cs`, `ManufactureConstants.cs`, `MeetingTasksConstants.cs`, `InventoryConstants.cs`). The Photobank feature has no such file yet.

## Functional Requirements

### FR-1: Introduce `PhotobankConstants` static class
Create a new file `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankConstants.cs` containing a `public static class PhotobankConstants` in the namespace `Anela.Heblo.Application.Features.Photobank`. Expose `public const int BulkTagLimit = 5_000;` on it.

**Acceptance criteria:**
- File exists at the exact path above.
- Class is `public static` and named `PhotobankConstants`.
- Namespace matches the directory: `Anela.Heblo.Application.Features.Photobank`.
- Constant is declared exactly as `public const int BulkTagLimit = 5_000;` (preserving the digit-group separator for readability and consistency with the original declarations).
- File follows the same minimal style as `CatalogConstants.cs` (file-scoped namespace, no extra `using` directives needed).

### FR-2: Replace local constant in `BulkAddPhotoTagHandler`
Remove the `private const int BulkTagLimit = 5_000;` declaration on line 15 of `BulkAddPhotoTagHandler.cs` and update the two references inside `Handle(...)` (the `if (total > BulkTagLimit)` check and the `BulkTagLimit.ToString()` call in the error `Params`) to use `PhotobankConstants.BulkTagLimit`.

**Acceptance criteria:**
- `BulkAddPhotoTagHandler.cs` no longer declares `BulkTagLimit`.
- Both usages reference `PhotobankConstants.BulkTagLimit`.
- No new `using` directive is required — the constants class lives in the same `Anela.Heblo.Application.Features.Photobank` namespace tree; only confirm the file compiles without adding an explicit import.
- Behavior of the handler is unchanged: still returns `BulkAddPhotoTagResponse(ErrorCodes.BulkTagLimitExceeded)` with `Params["Count"]` and `Params["Limit"]` populated identically.

### FR-3: Replace local constant in `BulkAddPhotoTagByIdsHandler`
Remove the `private const int BulkTagLimit = 5_000;` declaration on line 15 of `BulkAddPhotoTagByIdsHandler.cs` and update the two references inside `Handle(...)` to use `PhotobankConstants.BulkTagLimit`.

**Acceptance criteria:**
- `BulkAddPhotoTagByIdsHandler.cs` no longer declares `BulkTagLimit`.
- Both usages reference `PhotobankConstants.BulkTagLimit`.
- Behavior is unchanged: still returns `BulkAddPhotoTagByIdsResponse(ErrorCodes.BulkTagLimitExceeded)` with `Params["Count"]` and `Params["Limit"]` populated identically.

### FR-4: Preserve existing tests
The existing handler tests assert on the string `"5000"` for the `Limit` parameter:
- `backend/test/Anela.Heblo.Tests/Features/Photobank/BulkAddPhotoTagHandlerTests.cs` line 99
- `backend/test/Anela.Heblo.Tests/Features/Photobank/BulkAddPhotoTagByIdsHandlerTests.cs` line 87

These tests must continue to pass without modification because the runtime value of the constant remains 5,000 and `(5_000).ToString()` yields `"5000"`.

**Acceptance criteria:**
- No edits to existing test files are required.
- `dotnet test` for `Anela.Heblo.Tests` passes with no regressions in the Photobank suite.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact. A `public const int` is inlined at compile time, identical to the prior `private const int`.

### NFR-2: Security
No security impact. No data, auth, or API surface changes.

### NFR-3: Backwards compatibility
No public API, DTO, error code, response shape, or behavioral change. The HTTP responses produced by both endpoints remain byte-identical.

### NFR-4: Style and conventions
The new file must follow project conventions used by sibling constants files (see `CatalogConstants.cs`): file-scoped namespace, single static class, public const members. After changes, `dotnet build` and `dotnet format` must complete with no warnings or formatting drift in the touched files.

## Data Model
No data model changes.

## API / Interface Design
No public API changes.

The internal interface change is the introduction of a single new symbol:

```csharp
namespace Anela.Heblo.Application.Features.Photobank;

public static class PhotobankConstants
{
    public const int BulkTagLimit = 5_000;
}
```

Consumers in this PR: `BulkAddPhotoTagHandler`, `BulkAddPhotoTagByIdsHandler`.

## Dependencies
None. The change is internal to the `Anela.Heblo.Application` project. No new NuGet packages, no cross-project references, no infrastructure changes.

## Out of Scope
- Changing the limit value (still 5,000).
- Introducing configuration-driven (e.g., `IOptions<...>`) overrides for the limit.
- Refactoring the bulk-tag error-response shape or error codes.
- Extracting other duplicated literals in the Photobank module (e.g., other potentially shared limits or magic strings) — only `BulkTagLimit` is in scope for this brief.
- Updating tests to reference `PhotobankConstants.BulkTagLimit` instead of the literal `"5000"`. While arguably an improvement, the brief asks for a minimal surgical refactor; test edits are unnecessary for correctness and would expand the change set.
- Moving the constant into the `Anela.Heblo.Domain` project. The duplication is in the Application layer and the convention `*Constants.cs` is present in both layers; keeping it adjacent to the handlers (Application layer) matches the brief's explicit path suggestion and the location of `CatalogConstants.cs`.

## Open Questions
None.

## Status: COMPLETE