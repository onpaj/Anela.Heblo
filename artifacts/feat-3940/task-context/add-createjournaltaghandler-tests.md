### task: add-createjournaltaghandler-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Journal/CreateJournalTagHandlerTests.cs`
- Test: same file (this is a test-only addition; there is no separate production file to modify)

Reference files read to produce this plan (do not modify):
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/CreateJournalTag/CreateJournalTagHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/CreateJournalTagRequest.cs` (contains both `CreateJournalTagRequest` and `CreateJournalTagResponse`)
- `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalTagRepository.cs`
- `backend/test/Anela.Heblo.Tests/Features/Journal/CreateJournalEntryHandlerTests.cs` (pattern source)

Confirmed signatures used below:
- `CreateJournalTagHandler(IJournalTagRepository tagRepository, ICurrentUserService currentUserService, ILogger<CreateJournalTagHandler> logger)`
- `CreateJournalTagRequest { string Name; string Color = "#6B7280"; }` — implements `IRequest<CreateJournalTagResponse>`
- `CreateJournalTagResponse : BaseResponse { int Id; string Name; string Color; }` with a parameterless ctor and an `(ErrorCodes errorCode, Dictionary<string,string>? parameters = null)` ctor
- `IJournalTagRepository : IRepository<JournalEntryTag, int>` → `Task<JournalEntryTag> AddAsync(JournalEntryTag, CancellationToken)`, `Task<int> SaveChangesAsync(CancellationToken)`
- `JournalEntryTag { int Id; string Name; string Color; DateTime CreatedAt; string CreatedByUserId; }`
- `CurrentUser(string? Id, string? Name, string? Email, bool IsAuthenticated)` — positional record
- `ErrorCodes.UnauthorizedJournalAccess`

---

- [ ] **Step 1: Write the failing test file with all three test cases**

  Create `backend/test/Anela.Heblo.Tests/Features/Journal/CreateJournalTagHandlerTests.cs` with the following exact content:

  ```csharp
  using Anela.Heblo.Application.Features.Journal.Contracts;
  using Anela.Heblo.Application.Features.Journal.UseCases.CreateJournalTag;
  using Anela.Heblo.Application.Shared;
  using Anela.Heblo.Domain.Features.Journal;
  using Anela.Heblo.Domain.Features.Users;
  using FluentAssertions;
  using Microsoft.Extensions.Logging;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Features.Journal;

  public class CreateJournalTagHandlerTests
  {
      private readonly Mock<IJournalTagRepository> _tagRepositoryMock;
      private readonly Mock<ICurrentUserService> _currentUserServiceMock;
      private readonly Mock<ILogger<CreateJournalTagHandler>> _loggerMock;
      private readonly CreateJournalTagHandler _handler;

      public CreateJournalTagHandlerTests()
      {
          _tagRepositoryMock = new Mock<IJournalTagRepository>();
          _currentUserServiceMock = new Mock<ICurrentUserService>();
          _loggerMock = new Mock<ILogger<CreateJournalTagHandler>>();
          _handler = new CreateJournalTagHandler(
              _tagRepositoryMock.Object,
              _currentUserServiceMock.Object,
              _loggerMock.Object);
      }

      [Fact]
      public async Task Handle_WhenUserNotAuthenticated_ShouldReturnUnauthorizedError()
      {
          // Arrange
          var request = new CreateJournalTagRequest
          {
              Name = "Urgent",
              Color = "#FF0000"
          };

          var currentUser = new CurrentUser(
              Id: null,
              Name: null,
              Email: null,
              IsAuthenticated: false
          );

          _currentUserServiceMock
              .Setup(x => x.GetCurrentUser())
              .Returns(currentUser);

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Should().NotBeNull();
          result.Success.Should().BeFalse();
          result.ErrorCode.Should().Be(ErrorCodes.UnauthorizedJournalAccess);
          result.Params.Should().ContainKey("resource");
          result.Params!["resource"].Should().Be("journal_tag");

          _tagRepositoryMock.Verify(
              x => x.AddAsync(It.IsAny<JournalEntryTag>(), It.IsAny<CancellationToken>()),
              Times.Never);
          _tagRepositoryMock.Verify(
              x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
              Times.Never);
      }

      [Fact]
      public async Task Handle_WhenUserIdIsEmpty_ShouldReturnUnauthorizedError()
      {
          // Arrange
          var request = new CreateJournalTagRequest
          {
              Name = "Urgent",
              Color = "#FF0000"
          };

          var currentUser = new CurrentUser(
              Id: string.Empty,
              Name: null,
              Email: null,
              IsAuthenticated: true
          );

          _currentUserServiceMock
              .Setup(x => x.GetCurrentUser())
              .Returns(currentUser);

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Should().NotBeNull();
          result.Success.Should().BeFalse();
          result.ErrorCode.Should().Be(ErrorCodes.UnauthorizedJournalAccess);
          result.Params.Should().ContainKey("resource");
          result.Params!["resource"].Should().Be("journal_tag");

          _tagRepositoryMock.Verify(
              x => x.AddAsync(It.IsAny<JournalEntryTag>(), It.IsAny<CancellationToken>()),
              Times.Never);
          _tagRepositoryMock.Verify(
              x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
              Times.Never);
      }

      [Fact]
      public async Task Handle_WhenValidRequest_ShouldCreateJournalTagSuccessfully()
      {
          // Arrange
          var request = new CreateJournalTagRequest
          {
              Name = "  Urgent  ",
              Color = "#FF0000"
          };

          var currentUser = new CurrentUser(
              Id: "user123",
              Name: "Test User",
              Email: "test@example.com",
              IsAuthenticated: true
          );

          var createdTag = new JournalEntryTag
          {
              Id = 42,
              Name = "Urgent",
              Color = request.Color,
              CreatedAt = DateTime.UtcNow,
              CreatedByUserId = currentUser.Id
          };

          _currentUserServiceMock
              .Setup(x => x.GetCurrentUser())
              .Returns(currentUser);

          _tagRepositoryMock
              .Setup(x => x.AddAsync(It.IsAny<JournalEntryTag>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(createdTag);

          _tagRepositoryMock
              .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(1);

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Should().NotBeNull();
          result.Success.Should().BeTrue();
          result.ErrorCode.Should().BeNull();
          result.Id.Should().Be(createdTag.Id);
          result.Name.Should().Be(createdTag.Name);
          result.Color.Should().Be(createdTag.Color);

          _tagRepositoryMock.Verify(x => x.AddAsync(
              It.Is<JournalEntryTag>(t =>
                  t.Name == "Urgent" &&
                  t.CreatedByUserId == currentUser.Id &&
                  t.Color == request.Color),
              It.IsAny<CancellationToken>()), Times.Once);

          _tagRepositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
      }
  }
  ```

- [ ] **Step 2: Run the new tests to verify they fail before any prerequisite issue is ruled out, then confirm they pass**

  Since no production code changes are required (the handler already implements the behavior under test), run the test project directly — this doubles as both the "verify it fails without the file" check (the file did not exist / did not compile before Step 1) and the "verify it passes" check once Step 1 is saved.

  From the repository root:

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CreateJournalTagHandlerTests"
  ```

  Expected output: `Passed!` with a summary showing 3 total tests, 3 passed, 0 failed, 0 skipped:
  - `Handle_WhenUserNotAuthenticated_ShouldReturnUnauthorizedError`
  - `Handle_WhenUserIdIsEmpty_ShouldReturnUnauthorizedError`
  - `Handle_WhenValidRequest_ShouldCreateJournalTagSuccessfully`

  If any test fails, re-check the assertion against `CreateJournalTagHandler.cs` (read in this plan's grounding step) rather than altering the failure response shape — the handler is out of scope for changes.

- [ ] **Step 3: Run the full Journal test folder to confirm no regressions among sibling tests**

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Journal"
  ```

  Expected: all existing Journal tests (`CreateJournalEntryHandlerTests`, `JournalEntryTests`, `JournalEntryMapperTests`, `GetJournalEntryHandlerTests`, `SearchJournalEntriesHandlerTests`, `UpdateJournalEntryHandlerTests`, `DeleteJournalEntryHandlerTests`, `JournalRepositoryIntegrationTests`) plus the new `CreateJournalTagHandlerTests` all pass, with no unrelated failures introduced.

- [ ] **Step 4: Run `dotnet format` to confirm formatting compliance**

  ```bash
  cd backend
  dotnet format --verify-no-changes --include test/Anela.Heblo.Tests/Features/Journal/CreateJournalTagHandlerTests.cs
  ```

  If this reports changes needed, run `dotnet format --include test/Anela.Heblo.Tests/Features/Journal/CreateJournalTagHandlerTests.cs` (without `--verify-no-changes`) to apply them, then re-run Step 2 to confirm the tests still pass after formatting.

- [ ] **Step 5: Commit**

  Stage only the new test file and commit with a message describing the coverage-gap fix:

  ```bash
  git add backend/test/Anela.Heblo.Tests/Features/Journal/CreateJournalTagHandlerTests.cs
  git commit -m "test(journal): add unit tests for CreateJournalTagHandler

  Covers the unauthenticated/missing-Id rejection branch and the
  authenticated success/persistence branch (name trimming, CreatedByUserId
  attribution), mirroring CreateJournalEntryHandlerTests. Closes the
  coverage gap on CreateJournalTagHandler (24.3% -> full branch coverage).

  No production code changes."
  ```

  Verify the commit succeeded and only the intended file was included:

  ```bash
  git show --stat HEAD
  ```

  Expected: a single file, `backend/test/Anela.Heblo.Tests/Features/Journal/CreateJournalTagHandlerTests.cs`, listed as added.
