Commit `76963665` is on the branch, diff is confined to exactly the two files required by FR-5.

---

# Implementation: Graceful Handling of Malformed LayoutJson in GetGridLayoutHandler

## What was implemented

Added an inner `try/catch (JsonException)` around `JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson)` in `GetGridLayoutHandler.Handle`. When deserialization fails (malformed JSON or empty string), the handler logs a `Warning` with `UserId` + `GridKey` and returns `{ Layout = null }`. A legal JSON `"null"` deserialization result (which `JsonSerializer` returns without throwing) is also folded into the same `null` fallback — but without logging, since it is a degenerate-but-valid value rather than a corruption event. The `?? new GridLayoutDto()` fallback is removed and replaced by an explicit `if (dto is null)` guard. All existing paths (missing row, valid JSON, DB error, missing user claim) are unchanged.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` — inner try/catch for JsonException + null guard after deserialization
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` — three new [Fact] tests for the malformed, empty, and literal-null cases

## Tests

**`GetGridLayoutHandlerTests.cs`** — 6 tests total (3 existing, 3 new):
- `Handle_WhenLayoutJsonIsMalformed_ReturnsNullLayoutAndLogsWarning` — corrupt JSON (`{not json`) returns null layout, Warning logged with JsonException
- `Handle_WhenLayoutJsonIsEmpty_ReturnsNullLayoutAndLogsWarning` — empty string returns null layout, Warning logged with JsonException
- `Handle_WhenLayoutJsonIsLiteralNull_ReturnsNullLayoutAndDoesNotLog` — literal JSON `"null"` returns null layout, no warning logged

All 6 tests pass. Build: 0 errors.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetGridLayoutHandlerTests"
# Expected: 6 passed, 0 failed
```

## Notes

No deviations from spec. The arch-review amendment (explicit `if (dto is null)` branch with no log; log message matches the brief's exact template verbatim) is implemented as specified.

## PR Summary

Gracefully handles malformed `LayoutJson` in `GetGridLayoutHandler` so a corrupt user-preference row falls back to "no saved layout" instead of surfacing a 500. Previously, `JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson)` would let `JsonException` escape through the outer DB-error catch filter, reaching the unhandled-exception middleware. The fix wraps only the deserialize call in a typed `catch (JsonException)`, logs a `Warning` with `UserId` + `GridKey`, and returns `{ Layout = null }` — the same shape the frontend already handles for the "no saved layout" case.

A legal JSON `"null"` value (which deserializes to `null` without throwing) is also folded into the null-layout fallback via an explicit `if (dto is null)` guard, replacing the previous `?? new GridLayoutDto()` which would have silently emitted a DTO with zero columns.

### Changes
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` — inner try/catch (JsonException) around deserialization + null guard; removes `?? new GridLayoutDto()` fallback
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` — three new xUnit tests covering malformed JSON, empty string, and literal-null JSON payloads

## Status
DONE