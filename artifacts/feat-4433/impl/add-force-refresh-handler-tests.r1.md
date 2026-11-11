# Implementation: add-force-refresh-handler-tests r1

Added `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/ForceRefreshTaskHandlerTests.cs` (5 test cases: success, not-registered -> NotFound, InvalidOperation subclass -> NotFound, generic error theory over Exception/TimeoutException).
Style mirrors RunHydrationTierHandlerTests (MakeSut, VerifyLogged). No production code changed.
Verification: `dotnet test --filter FullyQualifiedName~ForceRefreshTaskHandlerTests` -> Passed 5/5. Build succeeded. dotnet format run scoped to the new file; no other files changed.
