# User-aware Meeting Task Extraction + Transcript Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the meeting-task extraction LLM a static user directory so it assigns tasks to canonical organisation emails, and surface the already-stored raw transcript in the UI.

**Architecture:** A static JSON file (`meeting-users.json`) loaded once into a cached `IMeetingUserDirectory` singleton. The directory is injected into the extraction prompt; the LLM emits an `assigneeEmail` per task. A nullable `AssigneeEmail` column is added to `ProposedTask`. Submission resolves users by email instead of fuzzy display-name match; unresolved tasks are skipped and reported. The frontend gains an assignee dropdown, an unknown-user warning badge, and a collapsible transcript section.

**Tech Stack:** .NET 8, MediatR, EF Core (PostgreSQL), MVC controllers, React 18 + TypeScript, TanStack Query, Tailwind.

---

## File Structure

**Backend — new files:**
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingUser.cs` — directory record
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingUserDirectory.cs` — directory interface
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingUserDirectory.cs` — JSON-backed implementation
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingUserDto.cs` — API DTO
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetMeetingUsers/GetMeetingUsersRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetMeetingUsers/GetMeetingUsersResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetMeetingUsers/GetMeetingUsersHandler.cs`
- `backend/src/Anela.Heblo.API/meeting-users.json` — the directory data

**Backend — modified files:**
- `ProposedTask.cs`, `ProposedTaskConfiguration.cs` (+ EF migration)
- `MeetingTasksOptions.cs`, `MeetingTasksModule.cs`, `appsettings.json`, `Anela.Heblo.API.csproj`
- `ExtractedTask` / `IMeetingTaskExtractor.cs`, `ClaudeMeetingTaskExtractor.cs`
- `IngestPlaudRecordingHandler.cs`
- `MeetingTranscriptDto.cs`, `ProposedTaskDto.cs`, `GetTranscriptDetailHandler.cs`
- `AddProposedTaskRequest.cs` / `AddProposedTaskHandler.cs`, `UpdateProposedTaskRequest.cs` / `UpdateProposedTaskHandler.cs`
- `IGraphTodoService.cs`, `GraphTodoService.cs`, `NoOpGraphTodoService.cs`, `SubmitToTodoHandler.cs`
- `MeetingTasksController.cs`

**Frontend — modified files:**
- `frontend/src/api/hooks/useMeetingTasks.ts`
- `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`

---

## Task 1: User directory service

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingUser.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingUserDirectory.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingUserDirectory.cs`
- Create: `backend/src/Anela.Heblo.API/meeting-users.json`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (lines 518-520)
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingUserDirectoryTests.cs`

- [ ] **Step 1: Create the `MeetingUser` record**

`MeetingUser.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

/// <summary>
/// A known organisation user the extraction LLM can assign tasks to.
/// Internal domain type — may be a record (not exposed via OpenAPI).
/// </summary>
public sealed record MeetingUser(string Email, string DisplayName, IReadOnlyList<string> Aliases);
```

- [ ] **Step 2: Create the `IMeetingUserDirectory` interface**

`IMeetingUserDirectory.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public interface IMeetingUserDirectory
{
    /// <summary>All known users from the static directory file.</summary>
    IReadOnlyList<MeetingUser> GetAll();

    /// <summary>
    /// Resolve a free-form name or alias (case-insensitive) to a known user.
    /// Returns null when no display name or alias matches.
    /// </summary>
    MeetingUser? Resolve(string nameOrAlias);
}
```

- [ ] **Step 3: Add `UserDirectoryPath` to `MeetingTasksOptions`**

Replace the body of `MeetingTasksOptions.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks;

public class MeetingTasksOptions
{
    public const string SectionName = "MeetingTasks";

    public string TodoListName { get; set; } = "Meeting Actions";

    /// <summary>
    /// Path to the static user-directory JSON file. Relative paths are resolved
    /// against the application base directory.
    /// </summary>
    public string UserDirectoryPath { get; set; } = "meeting-users.json";
}
```

- [ ] **Step 4: Write the failing test**

`MeetingUserDirectoryTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class MeetingUserDirectoryTests : IDisposable
{
    private readonly string _tempFile;

    public MeetingUserDirectoryTests()
    {
        _tempFile = Path.GetTempFileName();
        File.WriteAllText(_tempFile, """
            [
              { "email": "andrea@anela.cz", "displayName": "Andrea Nováková", "aliases": ["Andy", "Andrea"] },
              { "email": "petr@anela.cz", "displayName": "Petr Svoboda", "aliases": [] }
            ]
            """);
    }

    private MeetingUserDirectory CreateDirectory(string path)
    {
        var options = Options.Create(new MeetingTasksOptions { UserDirectoryPath = path });
        return new MeetingUserDirectory(options, Mock.Of<ILogger<MeetingUserDirectory>>());
    }

    [Fact]
    public void GetAll_ReturnsAllUsersFromFile()
    {
        var directory = CreateDirectory(_tempFile);

        directory.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void Resolve_MatchesAliasCaseInsensitively()
    {
        var directory = CreateDirectory(_tempFile);

        var user = directory.Resolve("andy");

        user.Should().NotBeNull();
        user!.Email.Should().Be("andrea@anela.cz");
    }

    [Fact]
    public void Resolve_MatchesDisplayName()
    {
        var directory = CreateDirectory(_tempFile);

        directory.Resolve("Petr Svoboda")!.Email.Should().Be("petr@anela.cz");
    }

    [Fact]
    public void Resolve_ReturnsNullForUnknownName()
    {
        var directory = CreateDirectory(_tempFile);

        directory.Resolve("Nobody").Should().BeNull();
    }

    [Fact]
    public void GetAll_ReturnsEmptyWhenFileMissing()
    {
        var directory = CreateDirectory(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".json"));

        directory.GetAll().Should().BeEmpty();
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }
}
```

- [ ] **Step 5: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter MeetingUserDirectoryTests`
Expected: FAIL — `MeetingUserDirectory` does not exist (compile error).

- [ ] **Step 6: Implement `MeetingUserDirectory`**

`MeetingUserDirectory.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

/// <summary>
/// Loads the static user directory from a JSON file once at construction.
/// Registered as a singleton — the file is read a single time per process.
/// A missing or malformed file degrades gracefully to an empty directory.
/// </summary>
public sealed class MeetingUserDirectory : IMeetingUserDirectory
{
    private readonly IReadOnlyList<MeetingUser> _users;

    public MeetingUserDirectory(IOptions<MeetingTasksOptions> options, ILogger<MeetingUserDirectory> logger)
    {
        _users = Load(options.Value.UserDirectoryPath, logger);
    }

    public IReadOnlyList<MeetingUser> GetAll() => _users;

    public MeetingUser? Resolve(string nameOrAlias)
    {
        if (string.IsNullOrWhiteSpace(nameOrAlias))
            return null;

        return _users.FirstOrDefault(u =>
            string.Equals(u.DisplayName, nameOrAlias, StringComparison.OrdinalIgnoreCase) ||
            u.Aliases.Any(a => string.Equals(a, nameOrAlias, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<MeetingUser> Load(string path, ILogger logger)
    {
        var fullPath = Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);

        if (!File.Exists(fullPath))
        {
            logger.LogError("Meeting user directory file not found at {Path}; using empty directory", fullPath);
            return Array.Empty<MeetingUser>();
        }

        try
        {
            var json = File.ReadAllText(fullPath);
            var entries = JsonSerializer.Deserialize<List<DirectoryEntry>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (entries is null)
                return Array.Empty<MeetingUser>();

            return entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Email))
                .Select(e => new MeetingUser(
                    e.Email,
                    e.DisplayName ?? string.Empty,
                    e.Aliases ?? new List<string>()))
                .ToList();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse meeting user directory at {Path}; using empty directory", fullPath);
            return Array.Empty<MeetingUser>();
        }
    }

    private sealed class DirectoryEntry
    {
        [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("aliases")] public List<string>? Aliases { get; set; }
    }
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter MeetingUserDirectoryTests`
Expected: PASS — 5 tests pass.

- [ ] **Step 8: Create the `meeting-users.json` data file**

`backend/src/Anela.Heblo.API/meeting-users.json` (placeholder content — the user maintains real entries):

```json
[
  {
    "email": "ondra@anela.cz",
    "displayName": "Ondřej Pajgrt",
    "aliases": ["Ondra", "Ondřej"]
  }
]
```

- [ ] **Step 9: Bundle the JSON file into the API build output**

In `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`, add a new `ItemGroup` next to the existing `.dockerignore` `Content` group (after line 45):

```xml
    <ItemGroup>
      <Content Include="meeting-users.json">
        <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      </Content>
    </ItemGroup>
```

- [ ] **Step 10: Register the directory in DI**

In `MeetingTasksModule.cs`, add inside `AddMeetingTasksModule`, immediately before the `return services;` line:

```csharp
        services.AddSingleton<IMeetingUserDirectory, MeetingUserDirectory>();
```

- [ ] **Step 11: Add config key to `appsettings.json`**

Replace the `MeetingTasks` section (lines 518-520) with:

```json
  "MeetingTasks": {
    "TodoListName": "Meeting Actions",
    "UserDirectoryPath": "meeting-users.json"
  },
```

- [ ] **Step 12: Verify build**

Run: `cd backend && dotnet build`
Expected: Build succeeds.

- [ ] **Step 13: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingUser.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingUserDirectory.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingUserDirectory.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs \
        backend/src/Anela.Heblo.API/meeting-users.json \
        backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj \
        backend/src/Anela.Heblo.API/appsettings.json \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingUserDirectoryTests.cs
git commit -m "feat(meeting-tasks): add static user directory service"
```

---

## Task 2: Add `AssigneeEmail` to the domain and database

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTask.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/MeetingTasks/ProposedTaskConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingTaskExtractor.cs`
- Create: EF migration under `backend/src/Anela.Heblo.Persistence/Migrations/`

- [ ] **Step 1: Add `AssigneeEmail` to `ProposedTask`**

In `ProposedTask.cs`, add this property immediately after the `Assignee` property:

```csharp
    /// <summary>Resolved canonical email of the assignee, or null when no known user matched.</summary>
    public string? AssigneeEmail { get; set; }
```

- [ ] **Step 2: Configure the column**

In `ProposedTaskConfiguration.cs`, add after the `Assignee` property block:

```csharp
        builder.Property(x => x.AssigneeEmail)
            .HasMaxLength(320)
            .IsRequired(false);
```

- [ ] **Step 3: Add `AssigneeEmail` to the `ExtractedTask` record**

In `IMeetingTaskExtractor.cs`, replace the `ExtractedTask` record declaration with:

```csharp
public record ExtractedTask(
    string Title,
    string Description,
    string Assignee,
    DateTime? DueDate,
    string? AssigneeEmail = null);
```

(The optional parameter keeps existing positional constructions valid.)

- [ ] **Step 4: Generate the EF migration**

Run: `cd backend && dotnet ef migrations add AddProposedTaskAssigneeEmail --project src/Anela.Heblo.Persistence --startup-project src/Anela.Heblo.API`
Expected: A new `*_AddProposedTaskAssigneeEmail.cs` + `.Designer.cs` pair appears in `Migrations/`, and `ApplicationDbContextModelSnapshot.cs` is updated. Verify the migration's `Up()` adds a nullable `AssigneeEmail` column to `ProposedTasks`.

- [ ] **Step 5: Verify build**

Run: `cd backend && dotnet build`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTask.cs \
        backend/src/Anela.Heblo.Persistence/MeetingTasks/ProposedTaskConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/Migrations/ \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingTaskExtractor.cs
git commit -m "feat(meeting-tasks): add AssigneeEmail column to ProposedTask"
```

> **Note:** migrations are applied manually per project convention. Apply with
> `dotnet ef database update --project src/Anela.Heblo.Persistence --startup-project src/Anela.Heblo.API` when ready.

---

## Task 3: Inject the user directory into the extraction LLM

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs`

- [ ] **Step 1: Write the failing tests**

In `ClaudeMeetingTaskExtractorTests.cs`, replace the whole file with:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class ClaudeMeetingTaskExtractorTests
{
    private readonly Mock<IChatClient> _mockChatClient;
    private readonly Mock<ILogger<ClaudeMeetingTaskExtractor>> _mockLogger;
    private readonly Mock<IMeetingUserDirectory> _mockDirectory;
    private readonly ClaudeMeetingTaskExtractor _extractor;

    public ClaudeMeetingTaskExtractorTests()
    {
        _mockChatClient = new Mock<IChatClient>();
        _mockLogger = new Mock<ILogger<ClaudeMeetingTaskExtractor>>();
        _mockDirectory = new Mock<IMeetingUserDirectory>();
        _mockDirectory.Setup(d => d.GetAll()).Returns(new List<MeetingUser>
        {
            new("andrea@anela.cz", "Andrea Nováková", new[] { "Andy" }),
        });
        _extractor = new ClaudeMeetingTaskExtractor(
            _mockChatClient.Object, _mockDirectory.Object, _mockLogger.Object);
    }

    private void SetupResponse(string json)
    {
        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, json)]);
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);
    }

    [Fact]
    public async Task ExtractAsync_WithValidJsonResponse_ReturnsParsedTasks()
    {
        SetupResponse("""[{"title":"Meeting Action","description":"Follow up","assignee":"John","assigneeEmail":null,"dueDate":"2026-06-01"}]""");

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Meeting Action");
        result[0].Assignee.Should().Be("John");
        result[0].AssigneeEmail.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_ParsesAssigneeEmailWhenLlmMatchesUser()
    {
        SetupResponse("""[{"title":"T","description":"D","assignee":"Andrea Nováková","assigneeEmail":"andrea@anela.cz","dueDate":null}]""");

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result[0].AssigneeEmail.Should().Be("andrea@anela.cz");
    }

    [Fact]
    public async Task ExtractAsync_IncludesDirectoryUsersInPrompt()
    {
        SetupResponse("[]");

        await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        _mockChatClient.Verify(x => x.GetResponseAsync(
            It.Is<IEnumerable<ChatMessage>>(msgs =>
                msgs.Any(m => m.Role == ChatRole.System && m.Text!.Contains("andrea@anela.cz"))),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_WhenChatClientThrows_ReturnsEmptyList()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API error"));

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_WithMarkdownWrappedJson_StripsFenceAndParses()
    {
        SetupResponse("```json\n[{\"title\":\"Action\",\"description\":\"Do it\",\"assignee\":\"Bob\",\"assigneeEmail\":null,\"dueDate\":null}]\n```");

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Action");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter ClaudeMeetingTaskExtractorTests`
Expected: FAIL — `ClaudeMeetingTaskExtractor` constructor does not take `IMeetingUserDirectory` (compile error).

- [ ] **Step 3: Update `ClaudeMeetingTaskExtractor`**

Replace `ClaudeMeetingTaskExtractor.cs` with:

```csharp
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public sealed class ClaudeMeetingTaskExtractor : IMeetingTaskExtractor
{
    private const string BasePrompt = """
        Jsi asistent, který z transkriptu schůzky extrahuje akční položky.
        Z dodaného souhrnu a transkriptu schůzky extrahuj všechny akční položky.
        Vrať POUZE JSON pole (bez dalšího textu) obsahující objekty s těmito poli:
        - title: stručný název úkolu
        - description: podrobný popis úkolu
        - assignee: jméno osoby odpovědné za splnění (nebo prázdný řetězec)
        - assigneeEmail: e-mail osoby ze seznamu známých uživatelů níže, pokud
          jméno nebo přezdívku v transkriptu dokážeš spolehlivě přiřadit ke
          konkrétnímu uživateli; jinak null
        - dueDate: datum splnění ve formátu ISO 8601 (nebo null)
        """;

    private const string NoUsersNote =
        "\n\nSeznam známých uživatelů je prázdný — assigneeEmail vždy nastav na null.";

    private readonly IChatClient _chatClient;
    private readonly IMeetingUserDirectory _userDirectory;
    private readonly ILogger<ClaudeMeetingTaskExtractor> _logger;

    public ClaudeMeetingTaskExtractor(
        IChatClient chatClient,
        IMeetingUserDirectory userDirectory,
        ILogger<ClaudeMeetingTaskExtractor> logger)
    {
        _chatClient = chatClient;
        _userDirectory = userDirectory;
        _logger = logger;
    }

    public async Task<List<ExtractedTask>> ExtractAsync(
        string summary,
        string transcript,
        CancellationToken ct = default)
    {
        try
        {
            var messages = new[]
            {
                new ChatMessage(ChatRole.System, BuildSystemPrompt()),
                new ChatMessage(ChatRole.User, $"Souhrn: {summary}\n\nTranskript: {transcript}")
            };

            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var text = StripMarkdownCodeFence(response.Text ?? string.Empty);

            var result = JsonSerializer.Deserialize<List<ExtractedTask>>(
                text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result ?? new List<ExtractedTask>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to extract tasks via Claude — transcript will be imported without tasks");
            return new List<ExtractedTask>();
        }
    }

    private string BuildSystemPrompt()
    {
        var users = _userDirectory.GetAll();
        if (users.Count == 0)
            return BasePrompt + NoUsersNote;

        var sb = new StringBuilder(BasePrompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Seznam známých uživatelů (assigneeEmail vybírej pouze z tohoto seznamu):");
        foreach (var user in users)
        {
            var aliases = user.Aliases.Count > 0 ? $" (přezdívky: {string.Join(", ", user.Aliases)})" : string.Empty;
            sb.AppendLine($"- {user.DisplayName}{aliases} → {user.Email}");
        }
        return sb.ToString();
    }

    private static string StripMarkdownCodeFence(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.StartsWith("```json"))
        {
            trimmed = trimmed["```json".Length..];
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed["```".Length..];
        }

        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed[..^"```".Length];
        }

        return trimmed.Trim();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter ClaudeMeetingTaskExtractorTests`
Expected: PASS — 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs
git commit -m "feat(meeting-tasks): feed user directory into extraction prompt"
```

---

## Task 4: Map `AssigneeEmail` in the ingest handler with a safety net

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

In `IngestPlaudRecordingHandlerTests.cs`, update the constructor to inject the directory mock and add a safety-net test. Add a field and update the handler construction:

```csharp
    private readonly Mock<IMeetingUserDirectory> _mockDirectory;
```

In the test class constructor, after `_mockLogger` is created:

```csharp
        _mockDirectory = new Mock<IMeetingUserDirectory>();
        _handler = new IngestPlaudRecordingHandler(
            _mockRepository.Object,
            _mockPlaudClient.Object,
            _mockExtractor.Object,
            _mockDirectory.Object,
            _mockLogger.Object);
```

Then add this test method:

```csharp
    [Fact]
    public async Task Handle_WhenLlmReturnsNameWithoutEmail_FillsEmailFromDirectory()
    {
        // Arrange
        var request = new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = "rec_safety",
            Name = "Meeting",
            PlaudCreatedAt = DateTime.UtcNow
        };

        _mockRepository
            .Setup(r => r.ExistsByPlaudIdAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("transcript");
        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("Headline", "summary"));
        _mockExtractor
            .Setup(e => e.ExtractAsync("summary", "transcript", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>
            {
                new("Task", "Desc", "Andy", null, AssigneeEmail: null)
            });
        _mockDirectory
            .Setup(d => d.Resolve("Andy"))
            .Returns(new MeetingUser("andrea@anela.cz", "Andrea Nováková", new[] { "Andy" }));

        MeetingTranscript? saved = null;
        _mockRepository
            .Setup(r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .Callback<MeetingTranscript, CancellationToken>((t, _) => saved = t);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        saved.Should().NotBeNull();
        saved!.Tasks.Single().AssigneeEmail.Should().Be("andrea@anela.cz");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter IngestPlaudRecordingHandlerTests`
Expected: FAIL — `IngestPlaudRecordingHandler` constructor has 4 parameters, not 5 (compile error).

- [ ] **Step 3: Update `IngestPlaudRecordingHandler`**

In `IngestPlaudRecordingHandler.cs`:

Add the field and constructor parameter. Replace the fields block and constructor with:

```csharp
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IPlaudClient _plaudClient;
    private readonly IMeetingTaskExtractor _extractor;
    private readonly IMeetingUserDirectory _userDirectory;
    private readonly ILogger<IngestPlaudRecordingHandler> _logger;

    public IngestPlaudRecordingHandler(
        IMeetingTranscriptRepository repository,
        IPlaudClient plaudClient,
        IMeetingTaskExtractor extractor,
        IMeetingUserDirectory userDirectory,
        ILogger<IngestPlaudRecordingHandler> logger)
    {
        _repository = repository;
        _plaudClient = plaudClient;
        _extractor = extractor;
        _userDirectory = userDirectory;
        _logger = logger;
    }
```

In the `Tasks = extractedTasks.Select(...)` projection, replace the lambda body with:

```csharp
                .Select(t => new ProposedTask
                {
                    Id = Guid.NewGuid(),
                    MeetingTranscriptId = transcriptId,
                    Title = t.Title,
                    Description = t.Description,
                    Assignee = t.Assignee,
                    AssigneeEmail = ResolveAssigneeEmail(t),
                    DueDate = t.DueDate,
                    Status = ProposedTaskStatus.Pending,
                    IsManuallyAdded = false
                })
                .ToList()
```

Add this private method to the class (after the `Handle` method):

```csharp
    /// <summary>
    /// Use the email the LLM resolved; if it returned only a name, try a
    /// direct directory lookup as a safety net before persisting.
    /// </summary>
    private string? ResolveAssigneeEmail(ExtractedTask task)
    {
        if (!string.IsNullOrWhiteSpace(task.AssigneeEmail))
            return task.AssigneeEmail;

        return _userDirectory.Resolve(task.Assignee)?.Email;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter IngestPlaudRecordingHandlerTests`
Expected: PASS — all tests pass (existing tests still pass because `Resolve` on an unconfigured mock returns null).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs
git commit -m "feat(meeting-tasks): persist resolved AssigneeEmail on ingest"
```

---

## Task 5: Expose `RawTranscript` and `AssigneeEmail` through the API

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/ProposedTaskDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/AddProposedTaskHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/UpdateProposedTaskHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptDetailHandlerTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs`

- [ ] **Step 1: Write the failing test for transcript passthrough**

In `GetTranscriptDetailHandlerTests.cs`, add a test that asserts `RawTranscript` and `AssigneeEmail` flow into the DTO. Add this test method (adapt the arrange block to match the existing test setup in the file — build a `MeetingTranscript` with `RawTranscript = "raw text"` and one `ProposedTask` with `AssigneeEmail = "x@anela.cz"`, set the repository mock to return it):

```csharp
    [Fact]
    public async Task Handle_MapsRawTranscriptAndAssigneeEmail()
    {
        // Arrange
        var transcriptId = Guid.NewGuid();
        var transcript = new MeetingTranscript
        {
            Id = transcriptId,
            PlaudRecordingId = "rec_1",
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "Subject",
            Summary = "Summary",
            RawTranscript = "raw transcript text",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = new List<ProposedTask>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    MeetingTranscriptId = transcriptId,
                    Title = "T", Description = "D",
                    Assignee = "Andrea Nováková",
                    AssigneeEmail = "andrea@anela.cz",
                    Status = ProposedTaskStatus.Pending
                }
            }
        };
        _mockRepository
            .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        // Act
        var result = await _handler.Handle(
            new GetTranscriptDetailRequest { Id = transcriptId }, CancellationToken.None);

        // Assert
        result.Transcript.RawTranscript.Should().Be("raw transcript text");
        result.Transcript.Tasks.Single().AssigneeEmail.Should().Be("andrea@anela.cz");
    }
```

> If the test file's existing helpers already build a transcript, reuse them instead of duplicating; the asserted fields are what matters.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter GetTranscriptDetailHandlerTests`
Expected: FAIL — `MeetingTranscriptDto` has no `RawTranscript`, `ProposedTaskDto` has no `AssigneeEmail` (compile error).

- [ ] **Step 3: Add `RawTranscript` to `MeetingTranscriptDto`**

In `MeetingTranscriptDto.cs`, add after the `Summary` property:

```csharp
    public string RawTranscript { get; set; } = null!;
```

- [ ] **Step 4: Add `AssigneeEmail` to `ProposedTaskDto`**

In `ProposedTaskDto.cs`, add after the `Assignee` property:

```csharp
    public string? AssigneeEmail { get; set; }
```

- [ ] **Step 5: Map both fields in `GetTranscriptDetailHandler`**

In `GetTranscriptDetailHandler.cs`, in the `new MeetingTranscriptDto { ... }` initializer add after `Summary = transcript.Summary,`:

```csharp
            RawTranscript = transcript.RawTranscript,
```

In the `Tasks = transcript.Tasks.Select(t => new ProposedTaskDto { ... })` projection add after `Assignee = t.Assignee,`:

```csharp
                AssigneeEmail = t.AssigneeEmail,
```

- [ ] **Step 6: Add `AssigneeEmail` to the add/update request contracts**

In `AddProposedTaskRequest.cs`, add after the `Assignee` property:

```csharp
    public string? AssigneeEmail { get; set; }
```

In `UpdateProposedTaskRequest.cs`, add after the `Assignee` property:

```csharp
    public string? AssigneeEmail { get; set; }
```

- [ ] **Step 7: Map `AssigneeEmail` in the add/update handlers**

In `AddProposedTaskHandler.cs`, in the `new ProposedTask { ... }` initializer add after `Assignee = request.Assignee,`:

```csharp
            AssigneeEmail = request.AssigneeEmail,
```

And in the response's `new ProposedTaskDto { ... }` initializer add after `Assignee = task.Assignee,`:

```csharp
                AssigneeEmail = task.AssigneeEmail,
```

In `UpdateProposedTaskHandler.cs`, after the line `task.Assignee = request.Assignee;` add:

```csharp
        task.AssigneeEmail = request.AssigneeEmail;
```

- [ ] **Step 8: Add an update-handler test for `AssigneeEmail`**

In `UpdateProposedTaskHandlerTests.cs`, add a test that calls the handler with an `UpdateProposedTaskRequest` whose `AssigneeEmail = "andrea@anela.cz"` and asserts the task on the transcript has `AssigneeEmail == "andrea@anela.cz"` after handling. Follow the existing arrange pattern in that file (it already builds a transcript with a task and a repository mock):

```csharp
    [Fact]
    public async Task Handle_UpdatesAssigneeEmail()
    {
        // Arrange — reuse the file's existing transcript/task/repository setup,
        // then issue the update with an email.
        var request = new UpdateProposedTaskRequest
        {
            TranscriptId = TranscriptId,   // existing test constant/field
            TaskId = TaskId,               // existing test constant/field
            Title = "Updated",
            Description = "Updated desc",
            Assignee = "Andrea Nováková",
            AssigneeEmail = "andrea@anela.cz",
            DueDate = null
        };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        Transcript.Tasks.Single().AssigneeEmail.Should().Be("andrea@anela.cz");
    }
```

> Adapt `TranscriptId`, `TaskId`, and `Transcript` to the actual identifiers used by the existing tests in that file.

- [ ] **Step 9: Run tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "GetTranscriptDetailHandlerTests|UpdateProposedTaskHandlerTests"`
Expected: PASS — all tests pass.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/ \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailHandler.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/AddProposedTask/ \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/UpdateProposedTask/ \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptDetailHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UpdateProposedTaskHandlerTests.cs
git commit -m "feat(meeting-tasks): expose RawTranscript and AssigneeEmail via API"
```

---

## Task 6: `GET /api/meeting-tasks/users` endpoint

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingUserDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetMeetingUsers/GetMeetingUsersRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetMeetingUsers/GetMeetingUsersResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetMeetingUsers/GetMeetingUsersHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetMeetingUsersHandlerTests.cs`

- [ ] **Step 1: Create the DTO (class, not record — OpenAPI rule)**

`MeetingUserDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Contracts;

public class MeetingUserDto
{
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public List<string> Aliases { get; set; } = new();
}
```

- [ ] **Step 2: Create the request**

`GetMeetingUsersRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetMeetingUsers;

public class GetMeetingUsersRequest : IRequest<GetMeetingUsersResponse>
{
}
```

- [ ] **Step 3: Create the response**

`GetMeetingUsersResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetMeetingUsers;

public class GetMeetingUsersResponse : BaseResponse
{
    public GetMeetingUsersResponse() { }
    public GetMeetingUsersResponse(ErrorCodes errorCode) : base(errorCode) { }

    public List<MeetingUserDto> Users { get; set; } = new();
}
```

- [ ] **Step 4: Write the failing test**

`GetMeetingUsersHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetMeetingUsers;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class GetMeetingUsersHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsAllDirectoryUsers()
    {
        // Arrange
        var directory = new Mock<IMeetingUserDirectory>();
        directory.Setup(d => d.GetAll()).Returns(new List<MeetingUser>
        {
            new("andrea@anela.cz", "Andrea Nováková", new[] { "Andy" }),
            new("petr@anela.cz", "Petr Svoboda", Array.Empty<string>()),
        });
        var handler = new GetMeetingUsersHandler(directory.Object);

        // Act
        var result = await handler.Handle(new GetMeetingUsersRequest(), CancellationToken.None);

        // Assert
        result.Users.Should().HaveCount(2);
        result.Users[0].Email.Should().Be("andrea@anela.cz");
        result.Users[0].Aliases.Should().ContainSingle().Which.Should().Be("Andy");
    }
}
```

- [ ] **Step 5: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter GetMeetingUsersHandlerTests`
Expected: FAIL — `GetMeetingUsersHandler` does not exist (compile error).

- [ ] **Step 6: Create the handler**

`GetMeetingUsersHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetMeetingUsers;

public class GetMeetingUsersHandler : IRequestHandler<GetMeetingUsersRequest, GetMeetingUsersResponse>
{
    private readonly IMeetingUserDirectory _userDirectory;

    public GetMeetingUsersHandler(IMeetingUserDirectory userDirectory)
    {
        _userDirectory = userDirectory;
    }

    public Task<GetMeetingUsersResponse> Handle(
        GetMeetingUsersRequest request,
        CancellationToken cancellationToken)
    {
        var users = _userDirectory.GetAll()
            .Select(u => new MeetingUserDto
            {
                Email = u.Email,
                DisplayName = u.DisplayName,
                Aliases = u.Aliases.ToList()
            })
            .ToList();

        return Task.FromResult(new GetMeetingUsersResponse { Users = users });
    }
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter GetMeetingUsersHandlerTests`
Expected: PASS — 1 test passes.

- [ ] **Step 8: Add the controller action**

In `MeetingTasksController.cs`, add the using:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetMeetingUsers;
```

And add this action after the `List` action:

```csharp
    [HttpGet("users")]
    public async Task<ActionResult<GetMeetingUsersResponse>> Users(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMeetingUsersRequest(), ct);
        return HandleResponse(result);
    }
```

> Place `users` before the `{id:guid}` route so the literal segment is matched first; ASP.NET Core route precedence already favours literal over `{id:guid}`, but ordering keeps it readable.

- [ ] **Step 9: Verify build**

Run: `cd backend && dotnet build`
Expected: Build succeeds.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingUserDto.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetMeetingUsers/ \
        backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetMeetingUsersHandlerTests.cs
git commit -m "feat(meeting-tasks): add GET /meeting-tasks/users endpoint"
```

---

## Task 7: Resolve TODO submission by email

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IGraphTodoService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/NoOpGraphTodoService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/SubmitToTodoHandlerTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs`

- [ ] **Step 1: Write the failing test for the handler**

In `SubmitToTodoHandlerTests.cs`, add tests covering the new behaviour. The existing tests mock `IGraphTodoService.ResolveUserIdAsync`; they will be updated to `ResolveUserIdByEmailAsync`. Add:

```csharp
    [Fact]
    public async Task Handle_SkipsAndReportsTaskWithNoAssigneeEmail()
    {
        // Arrange — transcript with one approved task that has AssigneeEmail = null
        var transcript = BuildTranscriptWithApprovedTask(assigneeEmail: null);
        _mockRepository
            .Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        // Act
        var result = await _handler.Handle(
            new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        // Assert
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("no resolved user");
        _mockTodoService.Verify(
            s => s.ResolveUserIdByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SubmitsTaskWithResolvedAssigneeEmail()
    {
        // Arrange
        var transcript = BuildTranscriptWithApprovedTask(assigneeEmail: "andrea@anela.cz");
        _mockRepository
            .Setup(r => r.GetByIdAsync(transcript.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);
        _mockTodoService
            .Setup(s => s.ResolveUserIdByEmailAsync("andrea@anela.cz", It.IsAny<CancellationToken>()))
            .ReturnsAsync("graph-user-id");
        _mockTodoService
            .Setup(s => s.CreateTodoTaskAsync(
                "graph-user-id", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoTaskResult(true, "ext-1", null));

        // Act
        var result = await _handler.Handle(
            new SubmitToTodoRequest { TranscriptId = transcript.Id }, CancellationToken.None);

        // Assert
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
    }
```

Add this helper to the test class (adapt to the file's existing constants/fields if a similar builder already exists):

```csharp
    private static MeetingTranscript BuildTranscriptWithApprovedTask(string? assigneeEmail)
    {
        var id = Guid.NewGuid();
        return new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec",
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "S", Summary = "Sum", RawTranscript = "raw",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = new List<ProposedTask>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    MeetingTranscriptId = id,
                    Title = "Task", Description = "Desc",
                    Assignee = "Andrea Nováková",
                    AssigneeEmail = assigneeEmail,
                    Status = ProposedTaskStatus.Approved,
                    ExternalTaskId = null
                }
            }
        };
    }
```

> Update any existing `SubmitToTodoHandlerTests` that call `ResolveUserIdAsync` so they call `ResolveUserIdByEmailAsync` and set `AssigneeEmail` on their approved tasks — otherwise those tasks now skip with "no resolved user".

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter SubmitToTodoHandlerTests`
Expected: FAIL — `ResolveUserIdByEmailAsync` is not a member of `IGraphTodoService` (compile error).

- [ ] **Step 3: Update `IGraphTodoService`**

In `IGraphTodoService.cs`, replace the `ResolveUserIdAsync` declaration with:

```csharp
    /// <summary>Resolve a Graph user id from a canonical email address.</summary>
    Task<string?> ResolveUserIdByEmailAsync(string email, CancellationToken ct = default);
```

- [ ] **Step 4: Update `GraphTodoService`**

In `GraphTodoService.cs`, replace the entire `ResolveUserIdAsync` method with:

```csharp
    public async Task<string?> ResolveUserIdByEmailAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        try
        {
            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphApiHelpers.GraphScope);
            using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

            // OData v4 string-literal rule: single quotes inside the literal are doubled,
            // then the whole literal is URL-encoded.
            var doubledQuotes = email.Replace("'", "''");
            var filter = Uri.EscapeDataString($"mail eq '{doubledQuotes}'");
            var url = $"{GraphApiHelpers.GraphBaseUrl}/users?$filter={filter}&$select=id,displayName";

            var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, url, token);
            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Graph user lookup for '{Email}' returned {Status}", email, response.StatusCode);
                return null;
            }

            var result = await GraphApiHelpers.DeserializeAsync<GraphUserCollection>(response, ct);

            if (result.Value.Count == 0)
                return null;

            if (result.Value.Count > 1)
                _logger.LogInformation(
                    "Graph user lookup for '{Email}' matched {Count} users; returning first id",
                    email, result.Value.Count);

            return result.Value[0].Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve Graph user id for '{Email}'", email);
            return null;
        }
    }
```

- [ ] **Step 5: Update `NoOpGraphTodoService`**

In `NoOpGraphTodoService.cs`, replace the `ResolveUserIdAsync` method with:

```csharp
    public Task<string?> ResolveUserIdByEmailAsync(string email, CancellationToken ct = default)
    {
        _logger.LogWarning("Graph Todo disabled (mock auth active) — skipping ResolveUserIdByEmail for '{Email}'", email);
        return Task.FromResult<string?>(null);
    }
```

- [ ] **Step 6: Update `SubmitToTodoHandler`**

In `SubmitToTodoHandler.cs`, replace the `foreach (var task in toSubmit)` loop body up to (and including) the `ResolveUserIdAsync` call. Replace:

```csharp
        foreach (var task in toSubmit)
        {
            var userId = await _todoService.ResolveUserIdAsync(task.Assignee, cancellationToken);
            if (userId is null)
            {
                response.FailedCount++;
                response.Errors.Add($"Could not resolve assignee '{task.Assignee}' for task '{task.Title}'.");
                continue;
            }
```

with:

```csharp
        foreach (var task in toSubmit)
        {
            if (string.IsNullOrWhiteSpace(task.AssigneeEmail))
            {
                response.FailedCount++;
                response.Errors.Add(
                    $"Task '{task.Title}' has no resolved user — assign a known user before submitting.");
                continue;
            }

            var userId = await _todoService.ResolveUserIdByEmailAsync(task.AssigneeEmail, cancellationToken);
            if (userId is null)
            {
                response.FailedCount++;
                response.Errors.Add(
                    $"Could not resolve user '{task.AssigneeEmail}' for task '{task.Title}'.");
                continue;
            }
```

- [ ] **Step 7: Update `GraphTodoServiceTests`**

In `GraphTodoServiceTests.cs`, rename every test that exercises `ResolveUserIdAsync` to call `ResolveUserIdByEmailAsync` and pass an email argument. The behaviour (HTTP filter query, null on no match, null on error) is unchanged — only the method name, parameter semantics (email instead of display name), and the OData filter field (`mail eq` instead of `displayName eq`) differ. Update any assertion that inspects the request URL to expect `mail eq` instead of `displayName eq`.

- [ ] **Step 8: Run tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "SubmitToTodoHandlerTests|GraphTodoServiceTests"`
Expected: PASS — all tests pass.

- [ ] **Step 9: Verify full backend build and test suite**

Run: `cd backend && dotnet build && dotnet test test/Anela.Heblo.Tests --filter MeetingTasks`
Expected: Build succeeds; all MeetingTasks tests pass.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IGraphTodoService.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoService.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/NoOpGraphTodoService.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/SubmitToTodoHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GraphTodoServiceTests.cs
git commit -m "feat(meeting-tasks): resolve TODO submission by AssigneeEmail"
```

---

## Task 8: Frontend data layer

**Files:**
- Modify: `frontend/src/api/hooks/useMeetingTasks.ts`

- [ ] **Step 1: Add `assigneeEmail` to `ProposedTaskDto`**

In `useMeetingTasks.ts`, in the `ProposedTaskDto` interface add after `assignee: string;`:

```typescript
  assigneeEmail: string | null;
```

- [ ] **Step 2: Add `rawTranscript` to `MeetingTranscriptDto`**

In the `MeetingTranscriptDto` interface, add after `summary: string;`:

```typescript
  rawTranscript: string;
```

- [ ] **Step 3: Add `assigneeEmail` to `TaskFormData`**

In the `TaskFormData` interface, add after `assignee: string;`:

```typescript
  assigneeEmail: string | null;
```

- [ ] **Step 4: Add the `MeetingUserDto` interface and `useMeetingUsers` query**

After the `TaskFormData` interface, add:

```typescript
export interface MeetingUserDto {
  email: string;
  displayName: string;
  aliases: string[];
}

interface MeetingUsersResponse {
  success: boolean;
  users: MeetingUserDto[];
}
```

After the `useMeetingTaskDetail` query function, add:

```typescript
export function useMeetingUsers() {
  return useQuery<MeetingUserDto[]>({
    queryKey: ["meetingTasks", "users"],
    staleTime: 10 * 60 * 1000,
    queryFn: async () => {
      const response = await fetchJson<MeetingUsersResponse>(
        `/api/meeting-tasks/users`,
        { method: "GET", headers: { Accept: "application/json" } },
      );
      return response.users;
    },
  });
}
```

- [ ] **Step 5: Send `assigneeEmail` in the update and add mutations**

In `useUpdateProposedTask`, in the `JSON.stringify({ ... })` body add after `assignee: input.data.assignee,`:

```typescript
            assigneeEmail: input.data.assigneeEmail || null,
```

In `useAddProposedTask`, in the `JSON.stringify({ ... })` body add after `assignee: input.data.assignee,`:

```typescript
            assigneeEmail: input.data.assigneeEmail || null,
```

- [ ] **Step 6: Verify build**

Run: `cd frontend && npm run build`
Expected: Build succeeds (no TypeScript errors).

- [ ] **Step 7: Commit**

```bash
git add frontend/src/api/hooks/useMeetingTasks.ts
git commit -m "feat(meeting-tasks): add user directory + transcript to FE data layer"
```

---

## Task 9: Frontend detail page — transcript, dropdown, warning badge

**Files:**
- Modify: `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`

- [ ] **Step 1: Import the new hook and icons**

In `MeetingTaskDetailPage.tsx`, update the `lucide-react` import to add `ChevronDown`, `ChevronRight`, and `AlertTriangle`:

```tsx
import {
  ArrowLeft, Check, X, Plus, Send, CheckCheck, Clock, CheckCircle, CheckCircle2,
  ChevronDown, ChevronRight, AlertTriangle,
} from "lucide-react";
```

Add `useMeetingUsers` to the `useMeetingTasks` import list, and add `MeetingUserDto` if needed for typing.

- [ ] **Step 2: Update `EMPTY_FORM` and form state for `assigneeEmail`**

Replace the `EMPTY_FORM` constant:

```tsx
const EMPTY_FORM: TaskFormData = { title: "", description: "", assignee: "", assigneeEmail: null, dueDate: null };
```

- [ ] **Step 3: Load the user directory and add transcript toggle state**

Inside the component, after the existing `useState` declarations, add:

```tsx
  const users = useMeetingUsers();
  const [transcriptOpen, setTranscriptOpen] = useState(false);
```

- [ ] **Step 4: Update `beginEdit` to carry `assigneeEmail`**

In `beginEdit`, replace the `setEditForm({ ... })` call with:

```tsx
    setEditForm({
      title: t.title,
      description: t.description,
      assignee: t.assignee,
      assigneeEmail: t.assigneeEmail,
      dueDate: t.dueDate,
    });
```

- [ ] **Step 5: Add an assignee `<select>` helper component**

Above the `MeetingTaskDetailPage` component, add a small reusable assignee picker:

```tsx
interface AssigneePickerProps {
  users: { email: string; displayName: string }[];
  value: string | null;
  onChange: (displayName: string, email: string | null) => void;
}

function AssigneePicker({ users, value, onChange }: AssigneePickerProps) {
  return (
    <select
      value={value ?? ""}
      onChange={(e) => {
        const email = e.target.value || null;
        const user = users.find((u) => u.email === email);
        onChange(user?.displayName ?? "", email);
      }}
      className="flex-1 border border-gray-300 rounded-md px-2 py-1 text-sm"
    >
      <option value="">— vyberte řešitele —</option>
      {users.map((u) => (
        <option key={u.email} value={u.email}>
          {u.displayName}
        </option>
      ))}
    </select>
  );
}
```

- [ ] **Step 6: Replace the assignee text input in the add-task form**

In the `addingTask` block, replace the assignee `<input type="text" ... />` with:

```tsx
              <AssigneePicker
                users={users.data ?? []}
                value={addForm.assigneeEmail}
                onChange={(displayName, email) =>
                  setAddForm({ ...addForm, assignee: displayName, assigneeEmail: email })
                }
              />
```

- [ ] **Step 7: Replace the assignee text input in the edit-task form**

In the `isEditing` block, replace the assignee `<input type="text" ... />` with:

```tsx
                    <AssigneePicker
                      users={users.data ?? []}
                      value={editForm.assigneeEmail}
                      onChange={(displayName, email) =>
                        setEditForm({ ...editForm, assignee: displayName, assigneeEmail: email })
                      }
                    />
```

- [ ] **Step 8: Show the unknown-user warning badge on task cards**

In the non-editing task card, in the `<div className="text-xs text-gray-500 mt-1">` assignee line, append a warning when `assigneeEmail` is missing. Replace that line with:

```tsx
                    <div className="text-xs text-gray-500 mt-1 flex items-center gap-1">
                      <span>
                        {t.assignee}{t.dueDate ? ` · ${new Date(t.dueDate).toLocaleDateString("cs-CZ")}` : ""}
                      </span>
                      {!t.assigneeEmail && (
                        <span className="inline-flex items-center text-amber-700 bg-amber-100 rounded px-1.5 py-0.5">
                          <AlertTriangle className="w-3 h-3 mr-1" /> neznámý uživatel
                        </span>
                      )}
                    </div>
```

- [ ] **Step 9: Add the collapsible transcript section**

After the summary `<div className="px-4 sm:px-6 lg:px-8 mt-4">...</div>` block, add:

```tsx
      <div className="px-4 sm:px-6 lg:px-8 mt-3">
        <button
          type="button"
          onClick={() => setTranscriptOpen((v) => !v)}
          className="inline-flex items-center text-sm font-medium text-gray-700 hover:text-gray-900"
        >
          {transcriptOpen ? (
            <ChevronDown className="w-4 h-4 mr-1" />
          ) : (
            <ChevronRight className="w-4 h-4 mr-1" />
          )}
          Přepis schůzky
        </button>
        {transcriptOpen && (
          <div className="mt-2 rounded-md border border-gray-200 bg-gray-50 p-3 text-sm text-gray-800 whitespace-pre-wrap max-h-96 overflow-auto">
            {transcript.rawTranscript}
          </div>
        )}
      </div>
```

- [ ] **Step 10: Verify build and lint**

Run: `cd frontend && npm run build && npm run lint`
Expected: Build succeeds; lint passes with no new errors.

- [ ] **Step 11: Commit**

```bash
git add frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx
git commit -m "feat(meeting-tasks): transcript section, assignee dropdown, unknown-user badge"
```

---

## Final Verification

- [ ] **Step 1: Backend build + format**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes`
Expected: Build succeeds; formatting clean (run `dotnet format` if not).

- [ ] **Step 2: Full MeetingTasks test suite**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter MeetingTasks`
Expected: All tests pass.

- [ ] **Step 3: Frontend build + lint**

Run: `cd frontend && npm run build && npm run lint`
Expected: Both succeed.

- [ ] **Step 4: Manual smoke check**

- Apply the migration: `cd backend && dotnet ef database update --project src/Anela.Heblo.Persistence --startup-project src/Anela.Heblo.API`
- Open a meeting task detail page; confirm the transcript section toggles, the assignee field is a dropdown, and a task with no resolved user shows the warning badge.
- Confirm `GET /api/meeting-tasks/users` returns the directory entries.

---

## Notes for the implementer

- **DTOs are classes, never records** — `MeetingUserDto` is a class (OpenAPI generator constraint). `MeetingUser` and `ExtractedTask` are internal domain types and stay records.
- **Migrations are applied manually** — generating the migration (Task 2) is part of the plan; applying it to a real database is a deploy-time step.
- The raw transcript is **plain speaker-labeled text, not markdown** — render it with `whitespace-pre-wrap`, not `ReactMarkdown`.
- The summary markdown rendering was already fixed in a prior commit; do not change it.
</content>
