## Module
GridLayouts

## Finding
`GetGridLayoutHandler` catches `GridLayoutPersistenceException`, logs it, and returns a success response with `Layout = null`:

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs:61-67
catch (GridLayoutPersistenceException ex)
{
    _logger.LogError(ex, "Database error reading GridLayout ...");
    return new GetGridLayoutResponse { Layout = null };  // Success = true, Layout = null
}
```

The controller then returns `Ok(null)` — HTTP 200 with a null body:

```csharp
// backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs:27-28
var response = await _mediator.Send(request);
return Ok(response.Layout);  // no check on response.Success
```

This is **inconsistent with the Save and Reset handlers**, which correctly use `BaseResponse(ErrorCodes.DatabaseError)` and let the controller surface HTTP 500:

```csharp
// SaveGridLayoutHandler.cs:46-49
return new SaveGridLayoutResponse(ErrorCodes.DatabaseError);

// GridLayoutsController.cs:35-37
if (!response.Success)
    return StatusCode(500, response);
```

When the database is unavailable during a GET, the client receives HTTP 200 with `null` layout — identical to the "no saved layout yet" response. The frontend `useGridLayout` hook treats both as equivalent and falls back to `buildDefaultState`, silently resetting the user's visible layout. There is no way for the client to distinguish a transient DB outage from a genuinely empty layout.

## Why it matters
Behavioural inconsistency within the same module: two of three operations correctly propagate DB errors; the third hides them. During a partial DB outage, users see their column layout silently reset to defaults with no error message, then restored on the next successful load — a confusing experience with no actionable feedback. Debugging is also harder because a 500 response would immediately surface the outage, while a 200+null silently passes through monitoring.

## Suggested fix
Apply the same pattern used by Save and Reset. Add an error-code constructor to `GetGridLayoutResponse`, check `response.Success` in the controller, and return HTTP 503 or 500 on DB errors:

```csharp
// GetGridLayoutHandler.cs — DB error branch
return new GetGridLayoutResponse(ErrorCodes.DatabaseError);

// GridLayoutsController.cs
var response = await _mediator.Send(request);
if (!response.Success)
    return StatusCode(503, response);
return Ok(response.Layout);
```

The frontend hook should then treat non-2xx responses as errors (already handled via the catch branch in `useGridLayout.ts:78-80`), keeping the currently-visible layout intact rather than resetting to defaults.

---
_Filed by daily arch-review routine on 2026-06-07._