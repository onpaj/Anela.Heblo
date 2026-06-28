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
