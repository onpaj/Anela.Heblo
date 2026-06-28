### task: implement-exception-handlers

**Goal:** Create `ArgumentExceptionHandler` and `ValidationExceptionHandler` following the `UnauthorizedAccessExceptionHandler` pattern, and register both in the DI pipeline.

**Files to create:**
- `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/ArgumentExceptionHandler.cs`
- `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/ValidationExceptionHandler.cs`

**Files to modify:**
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

**Implementation notes:**

`ArgumentExceptionHandler.cs`:
- Namespace: `Anela.Heblo.API.Infrastructure.ExceptionHandling`
- `sealed class ArgumentExceptionHandler : IExceptionHandler`
- Constructor takes `ILogger<ArgumentExceptionHandler>`
- `TryHandleAsync`: return `false` if `exception is not ArgumentException`; otherwise log `LogWarning(ex, "Invalid argument: {Message}", ex.Message)`, set `StatusCodes.Status400BadRequest`, write `ProblemDetails { Status = 400, Title = "Bad Request", Detail = ex.Message, Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1" }`, return `true`
- Include `Detail` set to `ex.Message` — this is a user-facing error (bad input) and preserves the original `{ "message": ex.Message }` contract, now typed as standard ProblemDetails

`ValidationExceptionHandler.cs`:
- Namespace: `Anela.Heblo.API.Infrastructure.ExceptionHandling`
- `sealed class ValidationExceptionHandler : IExceptionHandler`
- Constructor takes `ILogger<ValidationExceptionHandler>`
- `TryHandleAsync`: return `false` if `exception is not FluentValidation.ValidationException`; otherwise log `LogWarning(ex, "Validation failed")`, set `StatusCodes.Status400BadRequest`
- Write `ProblemDetails` with `Status = 400`, `Title = "Validation Failed"`, `Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"`, and add `Extensions["errors"]` containing a list of `{ propertyName, errorMessage }` objects derived from `exception.Errors` (null-coalesce to empty array if `Errors` is null)
- Use `WriteAsJsonAsync` like the existing handler
- Requires `using FluentValidation;`

`ServiceCollectionExtensions.cs` — inside `AddCrossCuttingServices()`, replace:
```csharp
services.AddExceptionHandler<UnauthorizedAccessExceptionHandler>();
services.AddProblemDetails();
```
with:
```csharp
// Handler order is significant — first match wins.
services.AddExceptionHandler<UnauthorizedAccessExceptionHandler>();
services.AddExceptionHandler<ValidationExceptionHandler>();
services.AddExceptionHandler<ArgumentExceptionHandler>();
services.AddProblemDetails();
```

**Success criteria:**
- Both new handler classes compile with `dotnet build`
- `dotnet format` reports no changes
- Registration order in `ServiceCollectionExtensions.cs`: Unauthorized → Validation → Argument → ProblemDetails
