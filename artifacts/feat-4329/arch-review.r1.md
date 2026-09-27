# Architecture Review: GetExpeditionListsByDateResponse.Fail() factory method

## Skip Design: true

## Architectural Fit Assessment
This is a pure, backend-only refactor confined to a single Vertical Slice module (`Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate`). It touches no controller, no API contract shape, no DTO field, and no cross-module boundary. The change moves the construction of a failure response from the MediatR handler into a static factory on the response DTO itself — exactly mirroring the pattern already established by the two sibling response types in the same module:

- `DownloadExpeditionListResponse.Fail()` (`.../DownloadExpeditionList/DownloadExpeditionListResponse.cs:11`)
- `ReprintExpeditionListResponse.Fail()` (`.../ReprintExpeditionList/ReprintExpeditionListResponse.cs:7`)

I read both sibling files and the target handler/response/request files directly (not just the issue's line references) to confirm the pattern and the exact fields involved. `BaseResponse` (`Shared/BaseResponse.cs`) confirms `Success`, `ErrorCode` (`ErrorCodes?`), and `Params` (`Dictionary<string, string>?`) are all inherited, so the new factory only needs to set values, not add members. `docs/architecture/development_guidelines.md` confirms response DTOs are classes owned by their module's use-case folder (not shared/global) — consistent with keeping this factory local to `GetExpeditionListsByDateResponse` rather than introducing a shared abstraction. No architectural amendment to the spec is needed; the spec's FR-1/FR-2 already describe exactly this move.

## Proposed Architecture

### Component Overview
```
GetExpeditionListsByDateHandler.Handle(request)
        │
        │  date invalid?
        ▼
GetExpeditionListsByDateResponse.InvalidDate()   <-- NEW static factory
        │  (Success=false, ErrorCode=InvalidFormat, Params={Field,ExpectedFormat})
        ▼
   returned to caller (MediatR pipeline → controller → HTTP response)
```
No new components, no new files beyond the two already-existing ones being edited. No change to `GetExpeditionListsByDateRequest`, `IExpeditionListArchiveBlobStore`, or the success path.

### Key Design Decisions

#### Decision 1: Factory method name — `InvalidDate()` vs. generic `Fail()`
**Options considered:**
- (a) `Fail()` with no parameters, matching the two sibling response types verbatim.
- (b) `InvalidDate()`, named after the specific failure condition, as proposed in the issue.
- (c) `Fail(ErrorCodes code, Dictionary<string,string> params)` — a generic parameterized factory.

**Chosen approach:** (b) `InvalidDate()`.

**Rationale:** `DownloadExpeditionListResponse.Fail()` and `ReprintExpeditionListResponse.Fail()` are parameterless because each of those response types has exactly one possible failure mode (`InvalidBlobPath`), so `Fail()` is unambiguous. `GetExpeditionListsByDateResponse` currently also has exactly one failure mode (invalid date format), so a parameterless factory is equally appropriate here — but naming it `Fail()` would be a false-symmetry trap: if a second failure mode is ever added to this response type (e.g., a storage error), a bare `Fail()` name would force an awkward parameter list or a misleading name reuse. Naming it `InvalidDate()` now costs nothing and avoids that trap; it also matches the issue's own suggested fix verbatim, so there is no reason to diverge. Option (c) is over-engineering for a single call site with a single failure shape — reject it; do not add parameters that have no second caller today.

#### Decision 2: Where the factory lives
**Options considered:**
- (a) Static method directly on `GetExpeditionListsByDateResponse` (matches sibling pattern).
- (b) A shared base-class helper (e.g. `BaseResponse.Fail<T>(...)`) usable by all three response types.

**Chosen approach:** (a).

**Rationale:** The issue explicitly scopes the fix to this one response type, and the spec marks any shared base-factory abstraction as Out of Scope. The two existing siblings already duplicate the "static factory on the concrete response type" pattern rather than sharing one — introducing a shared base method now would be an unrequested, unscoped architectural change (and would touch two files that are not part of this issue). Stay consistent with the established, if slightly duplicative, per-type factory convention.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Edit exactly two existing files:
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateResponse.cs` — add the `InvalidDate()` static factory.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs` — replace the inline construction (lines 21–33) with `return GetExpeditionListsByDateResponse.InvalidDate();`.

### Interfaces and Contracts
```csharp
// GetExpeditionListsByDateResponse.cs
public class GetExpeditionListsByDateResponse : BaseResponse
{
    public List<ExpeditionListItemDto> Items { get; set; } = new();

    public static GetExpeditionListsByDateResponse InvalidDate() =>
        new()
        {
            Success = false,
            ErrorCode = ErrorCodes.InvalidFormat,
            Params = new Dictionary<string, string>
            {
                { "Field", "Date" },
                { "ExpectedFormat", "yyyy-MM-dd" }
            }
        };
}
```
```csharp
// GetExpeditionListsByDateHandler.cs — inside Handle(...)
if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
{
    return GetExpeditionListsByDateResponse.InvalidDate();
}
```
No other public surface changes. `ErrorCodes` and `Dictionary<string,string>` usage moves from the handler's `using Anela.Heblo.Application.Shared;` scope into the response file, which already has that `using` (confirmed present at line 2 of the response file). The developer should check, after the edit, whether the handler's `using Anela.Heblo.Application.Shared;` is now unused (it currently also isn't referenced elsewhere in the handler beyond `ErrorCodes`/nothing else) and remove it only if the compiler/analyzer flags it — do not remove it speculatively if something else in the file still needs the namespace.

### Data Flow
Unchanged. Invalid date in `request.Date` → `DateOnly.TryParseExact` fails → handler now delegates to `GetExpeditionListsByDateResponse.InvalidDate()` instead of building the object inline → same response object shape flows back through the MediatR pipeline to the controller exactly as before. Valid date → blob listing/filtering path is completely untouched.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing test `GetExpeditionListsByDateHandlerTests.Handle_ReturnsFailure_WhenDateIsInvalid` breaks | Low | Test asserts on `result.Success`, `result.ErrorCode`, `result.Params` values only, not on construction call sites — verified by reading the test file directly. Values are unchanged by this refactor, so the test should pass unmodified. Run it to confirm. |
| Unused `using` directive left in handler causing a lint/format warning | Low | `dotnet format` / build warnings will surface it; remove only if flagged, per CLAUDE.md's "surgical changes" rule (don't touch what isn't required). |
| Scope creep into the two sibling `Fail()` methods or into a shared base factory | Low | Explicitly Out of Scope per spec; this review reaffirms — do not touch `DownloadExpeditionListResponse` or `ReprintExpeditionListResponse`. |

## Specification Amendments
None. The spec (`spec.r1.md`) FR-1 and FR-2 already fully and correctly describe this change; no amendment needed.

## Prerequisites
None. No migrations, no config, no infrastructure changes. This can be implemented and merged independently of any other work.
