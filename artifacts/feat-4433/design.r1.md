# Design: ForceRefreshTaskHandlerTests

No UX/UI or schema changes. This design defines the test class structure.

## Test class
`ForceRefreshTaskHandlerTests` in `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/ForceRefreshTaskHandlerTests.cs`.

Helpers:
- `MakeSut()` returns `(ForceRefreshTaskHandler Sut, Mock<IBackgroundRefreshTaskRegistry> Registry, Mock<ILogger<ForceRefreshTaskHandler>> Logger)`.
- `VerifyLogged(logger, LogLevel, Times)` copied from `RunHydrationTierHandlerTests`.

## Test cases
| Test | Arrange | Assert |
|------|---------|--------|
| `Handle_ReturnsSuccess_WhenRefreshCompletes` | `ForceRefreshAsync("task-1", ct)` returns `Task.CompletedTask` | Success true, NotFound false, ErrorMessage null; Verify called once with id and token |
| `Handle_ReturnsNotFound_WhenTaskNotRegistered` | throws `InvalidOperationException("Task with ID 'missing' is not registered")` | NotFound true, ErrorMessage equals message; Warning logged once, Error never |
| `Handle_ReturnsNotFound_WhenSubclassOfInvalidOperationThrown` | throws `ObjectDisposedException("registry")` | NotFound true |
| `Handle_ReturnsGenericError_WhenUnexpectedExceptionThrown` (Theory: `Exception`, `TimeoutException`) | throws the exception with a secret-like message | Success false, NotFound false, ErrorMessage equals fixed generic text, does not contain original message; Error logged once, Warning never |

Do not assert `Success` in NotFound tests (see arch-review).

## Status: COMPLETE
