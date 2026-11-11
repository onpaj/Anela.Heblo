### task: add-force-refresh-handler-tests

Create `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/ForceRefreshTaskHandlerTests.cs` (xUnit + Moq + FluentAssertions, style copied from sibling `RunHydrationTierHandlerTests.cs`, including its `VerifyLogged` helper).

Steps:
1. Add `MakeSut()` returning the handler plus `Mock<IBackgroundRefreshTaskRegistry>` and `Mock<ILogger<ForceRefreshTaskHandler>>`.
2. `Handle_ReturnsSuccess_WhenRefreshCompletes`: registry returns `Task.CompletedTask`; assert Success true, NotFound false, ErrorMessage null; verify `ForceRefreshAsync(taskId, ct)` called once with the supplied id and token.
3. `Handle_ReturnsNotFound_WhenTaskNotRegistered`: registry throws `InvalidOperationException("Task with ID 'missing' is not registered")`; assert NotFound true and ErrorMessage equals the message; Warning logged once, Error never. Do NOT assert `Success` on this path (it defaults to true from `BaseResponse`; see spec Open Questions).
4. `Handle_ReturnsNotFound_WhenSubclassOfInvalidOperationThrown`: registry throws `ObjectDisposedException("registry")`; assert NotFound true.
5. `Handle_ReturnsGenericError_WhenUnexpectedExceptionThrown` (Theory over `Exception`, `TimeoutException`, with a secret-like message): assert Success false, NotFound false, ErrorMessage == "An unexpected error occurred during force refresh" and does not contain the original message; Error logged once, Warning never.

Verification:
- `dotnet build backend/Anela.Heblo.sln` and `dotnet format` (verify only the new file changes).
- `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~ForceRefreshTaskHandlerTests` passes.
