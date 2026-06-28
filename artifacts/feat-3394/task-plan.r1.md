# Task Plan: feat-3394

## Overview
Replace inline try/catch blocks in `BankStatementsController` with two new global `IExceptionHandler` implementations (`ArgumentExceptionHandler` and `ValidationExceptionHandler`), registered in the DI pipeline after the existing `UnauthorizedAccessExceptionHandler`. Unit tests mirror the pattern established by `UnauthorizedAccessExceptionHandlerTests`.

---

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

---

### task: implement-controller-cleanup

**Goal:** Remove the try/catch blocks from `ImportStatements` and `GetBankStatements` in `BankStatementsController`, retaining only the `_logger.LogInformation(...)` calls and the happy-path logic.

**Files to modify:**
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`

**Implementation notes:**

`ImportStatements`:
- Keep `_logger.LogInformation("Importing bank statements for account {AccountName} from {DateFrom} to {DateTo}", ...)` at the top of the method body
- Remove the `try { ... } catch (ArgumentException ...) { ... } catch (Exception ...) { ... }` wrapper entirely
- The method body becomes: log → build `importRequest` → `await _mediator.Send(importRequest)` → build `result` → return `Ok(result)`

`GetBankStatements`:
- Keep `_logger.LogInformation("Getting bank statements with Skip={Skip}, Take={Take}", skip, take)` at the top of the method body
- Remove the `try { ... } catch (FluentValidation.ValidationException ...) { ... } catch (Exception ...) { ... }` wrapper entirely
- The method body becomes: log → build `request` → `await _mediator.Send(request)` → return `Ok(response)`
- Remove the `using FluentValidation;` using if it becomes unused (verify — no explicit FluentValidation using exists at the top of the controller file, only a catch-block reference)

**Success criteria:**
- Neither method contains `try`, `catch`, `return BadRequest(...)`, or `return StatusCode(500, ...)`
- `_logger.LogInformation(...)` is still present in both methods
- `dotnet build` succeeds
- `dotnet format` reports no changes

---

### task: implement-tests

**Goal:** Add unit tests for `ArgumentExceptionHandler` and `ValidationExceptionHandler` following the exact pattern from `UnauthorizedAccessExceptionHandlerTests.cs`.

**Files to create:**
- `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/ArgumentExceptionHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/ValidationExceptionHandlerTests.cs`

**Implementation notes:**

Both test files use:
- Namespace: `Anela.Heblo.Tests.Infrastructure.ExceptionHandling`
- Usings: `System.Text.Json`, `Anela.Heblo.API.Infrastructure.ExceptionHandling`, `FluentAssertions`, `Microsoft.AspNetCore.Http`, `Microsoft.Extensions.Logging.Abstractions`, `Xunit`
- A `private static CreateSut()` helper that constructs the handler with `NullLogger<T>.Instance`, a `MemoryStream` body, and a `DefaultHttpContext` with that body wired to `context.Response.Body`

`ArgumentExceptionHandlerTests` — three tests:
1. `TryHandleAsync_WhenArgumentException_Returns400WithProblemDetails` — pass `new ArgumentException("Unknown account name: TestAccount")`, assert `handled == true`, `StatusCode == 400`, JSON body has `status == 400`, `title == "Bad Request"`, `detail == "Unknown account name: TestAccount"`
2. `TryHandleAsync_WhenArgumentNullException_Returns400` — pass `new ArgumentNullException("param")`, assert `handled == true`, `StatusCode == 400` (validates subclass handling)
3. `TryHandleAsync_WhenOtherException_ReturnsFalseAndDoesNotWriteBody` — pass `new InvalidOperationException("unrelated")`, assert `handled == false`, `body.Length == 0`, `StatusCode == 200` (unchanged)

`ValidationExceptionHandlerTests` — three tests:
1. `TryHandleAsync_WhenValidationException_Returns400WithProblemDetails` — pass `new ValidationException(new[] { new ValidationFailure("Amount", "Must be greater than zero") })`, assert `handled == true`, `StatusCode == 400`, JSON body has `status == 400`, `title == "Validation Failed"`
2. `TryHandleAsync_WhenValidationException_ExposesErrorsInExtensions` — same setup, assert JSON body has `errors` array with one entry where `propertyName == "Amount"` and `errorMessage == "Must be greater than zero"`
3. `TryHandleAsync_WhenOtherException_ReturnsFalseAndDoesNotWriteBody` — pass `new InvalidOperationException("unrelated")`, assert `handled == false`, `body.Length == 0`, `StatusCode == 200` (unchanged)

For `ValidationException` construction: `FluentValidation.ValidationException` has a constructor that accepts `IEnumerable<ValidationFailure>`. Use `new ValidationException(new[] { new ValidationFailure("Amount", "Must be greater than zero") })`.

**Success criteria:**
- `dotnet build` succeeds for the test project
- All six new tests pass (`dotnet test --filter "FullyQualifiedName~ExceptionHandling"`)
- No test uses `Skip` or `[Ignore]`
