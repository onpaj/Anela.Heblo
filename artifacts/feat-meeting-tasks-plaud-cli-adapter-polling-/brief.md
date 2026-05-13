# Subtask 2: Plaud CLI Adapter, Polling Job & Ingest Handler

**Parent Epic:** Meeting Task Validation Checkpoint

CRITICAL - This is part of epic, you **MUST** use epic branch - feat/meeting-task-validation-epic as a source for this feature branch and create a PR back to this branch instead of main

> **Architecture change:** The original n8n email-webhook approach is replaced by a Hangfire polling job that pulls recordings directly from the Plaud CLI. Heblo calls `plaud` CLI via `System.Diagnostics.Process`, detects new completed recordings, extracts tasks via Claude (`IChatClient`), and stores them for human review.

## File Structure

### Adapter project (new)
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudOptions.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudClient.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudRecordingSummary.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenBootstrapper.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAdapterServiceCollectionExtensions.cs`

### Tests for adapter
- `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientParserTests.cs`

### Application — Services
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingTaskExtractor.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs`

### Application — Ingest handler
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs`

### Application — Polling job
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs`

### Tests
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs`

### API
- `backend/src/Anela.Heblo.API/Program.cs` — add `AddPlaudAdapter`
- `Dockerfile` — install `plaud` CLI binary

---

## Task 3: Plaud CLI Adapter Project

Follow the layout of `Anela.Heblo.Adapters.Anthropic` for the new adapter project.

- [ ] **Step 1: Create PlaudOptions**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudOptions.cs
namespace Anela.Heblo.Adapters.Plaud;

public class PlaudOptions
{
    public const string SectionKey = "Plaud";

    public string CliExecutablePath { get; set; } = "plaud";
    public string TokensJson { get; set; } = string.Empty;
    public int ProcessTimeoutSeconds { get; set; } = 60;
    public int MaxRecordingAgeDays { get; set; } = 7;
}
```

- [ ] **Step 2: Create PlaudRecordingSummary DTO**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudRecordingSummary.cs
namespace Anela.Heblo.Adapters.Plaud;

public class PlaudRecordingSummary
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public bool HasTranscript { get; set; }
    public bool HasSummary { get; set; }
}
```

- [ ] **Step 3: Create IPlaudClient interface**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudClient.cs
namespace Anela.Heblo.Adapters.Plaud;

public interface IPlaudClient
{
    Task<List<PlaudRecordingSummary>> ListRecentAsync(int days, CancellationToken ct = default);
    Task<string> GetTranscriptAsync(string recordingId, CancellationToken ct = default);
    Task<string> GetSummaryAsync(string recordingId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write parser unit tests (offline — no CLI needed)**

Use snapshot fixture strings that mirror the actual `plaud files` and `plaud transcript` output observed locally.

> **Important:** Run `plaud login` locally first, then capture `plaud files` stdout to use as fixture data. Adjust column format in tests to match actual output.

```csharp
// backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientParserTests.cs
using Anela.Heblo.Adapters.Plaud;
using Xunit;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public class PlaudCliClientParserTests
{
    // Fixture: copy-paste actual `plaud files` output here after manual verification
    private const string SampleFilesOutput = """
        ID          NAME                  CREATED              TRANSCRIPT  SUMMARY
        rec-001     Sprint Planning       2026-05-10 09:00:00  yes         yes
        rec-002     1:1 Alice             2026-05-11 14:30:00  yes         no
        rec-003     Client Call           2026-05-12 10:00:00  no          no
        """;

    [Fact]
    public void ParseFilesOutput_ParsesAllRows()
    {
        var result = PlaudCliClient.ParseFilesOutput(SampleFilesOutput);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ParseFilesOutput_DetectsReadyRecordings()
    {
        var result = PlaudCliClient.ParseFilesOutput(SampleFilesOutput);

        var ready = result.Where(r => r.HasTranscript && r.HasSummary).ToList();
        Assert.Single(ready);
        Assert.Equal("rec-001", ready[0].Id);
        Assert.Equal("Sprint Planning", ready[0].Name);
    }

    [Fact]
    public void ParseFilesOutput_HandlesEmptyOutput()
    {
        var result = PlaudCliClient.ParseFilesOutput(string.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseFilesOutput_HandlesHeaderOnlyOutput()
    {
        var result = PlaudCliClient.ParseFilesOutput("ID  NAME  CREATED  TRANSCRIPT  SUMMARY");
        Assert.Empty(result);
    }
}
```

- [ ] **Step 5: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/`
Expected: FAIL — `PlaudCliClient` does not exist

- [ ] **Step 6: Implement PlaudCliClient**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud;

public class PlaudCliClient : IPlaudClient
{
    private readonly PlaudOptions _options;
    private readonly ILogger<PlaudCliClient> _logger;

    public PlaudCliClient(IOptions<PlaudOptions> options, ILogger<PlaudCliClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<PlaudRecordingSummary>> ListRecentAsync(int days, CancellationToken ct = default)
    {
        var stdout = await RunCommandAsync($"recent --days {days}", ct);
        return ParseFilesOutput(stdout);
    }

    public async Task<string> GetTranscriptAsync(string recordingId, CancellationToken ct = default)
        => await RunCommandAsync($"transcript {recordingId}", ct);

    public async Task<string> GetSummaryAsync(string recordingId, CancellationToken ct = default)
        => await RunCommandAsync($"summary {recordingId}", ct);

    // Internal — public for testability of parser logic
    public static List<PlaudRecordingSummary> ParseFilesOutput(string stdout)
    {
        var results = new List<PlaudRecordingSummary>();
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines.Skip(1)) // skip header
        {
            var parts = line.Split(null as char[], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 6) continue;

            // Format: ID  NAME  DATE  TIME  TRANSCRIPT  SUMMARY
            // NAME may contain spaces — derive by position from end
            var id = parts[0];
            var transcript = parts[^2].Equals("yes", StringComparison.OrdinalIgnoreCase);
            var summary = parts[^1].Equals("yes", StringComparison.OrdinalIgnoreCase);
            var createdStr = $"{parts[^4]} {parts[^3]}";
            _ = DateTime.TryParse(createdStr, out var createdAt);
            var name = string.Join(" ", parts[1..^4]);

            results.Add(new PlaudRecordingSummary
            {
                Id = id,
                Name = name,
                CreatedAt = createdAt,
                HasTranscript = transcript,
                HasSummary = summary
            });
        }

        return results;
    }

    private async Task<string> RunCommandAsync(string arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _options.CliExecutablePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.ProcessTimeoutSeconds));

        await process.WaitForExitAsync(cts.Token);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            _logger.LogError("plaud {Args} exited {Code}: {Stderr}", arguments, process.ExitCode, stderr);
            throw new InvalidOperationException($"plaud {arguments} failed (exit {process.ExitCode}): {stderr}");
        }

        if (!string.IsNullOrEmpty(stderr))
            _logger.LogWarning("plaud {Args} stderr: {Stderr}", arguments, stderr);

        return stdout;
    }
}
```

- [ ] **Step 7: Create PlaudTokenBootstrapper**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenBootstrapper.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud;

public class PlaudTokenBootstrapper : IHostedService
{
    private readonly PlaudOptions _options;
    private readonly ILogger<PlaudTokenBootstrapper> _logger;

    public PlaudTokenBootstrapper(IOptions<PlaudOptions> options, ILogger<PlaudTokenBootstrapper> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.TokensJson))
        {
            _logger.LogWarning("Plaud:TokensJson is not configured. Plaud polling job will be disabled.");
            return Task.CompletedTask;
        }

        var plaudDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".plaud");
        Directory.CreateDirectory(plaudDir);

        var tokenPath = Path.Combine(plaudDir, "tokens.json");
        File.WriteAllText(tokenPath, _options.TokensJson);

        _logger.LogInformation("Plaud tokens written to {Path}", tokenPath);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

- [ ] **Step 8: Create PlaudAdapterServiceCollectionExtensions**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAdapterServiceCollectionExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Plaud;

public static class PlaudAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddPlaudAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaudOptions>(configuration.GetSection(PlaudOptions.SectionKey));
        services.AddSingleton<IPlaudClient, PlaudCliClient>();
        services.AddHostedService<PlaudTokenBootstrapper>();
        return services;
    }
}
```

- [ ] **Step 9: Register in Program.cs**

Add after `builder.Services.AddAnthropicAdapter(...)`:
```csharp
builder.Services.AddPlaudAdapter(builder.Configuration);
```

- [ ] **Step 10: Run parser tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/`
Expected: All tests PASS

> **Manual verification:** Run `plaud login` locally, then `plaud recent --days 7` and `plaud transcript <id>`. Adjust fixture strings in parser tests to match actual CLI output format. Commit updated fixtures.

- [ ] **Step 11: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/
git add backend/src/Anela.Heblo.API/Program.cs
git commit -m "feat(meeting-tasks): add Plaud CLI adapter with token bootstrapper"
```

---

## Task 4: Claude Task Extractor

- [ ] **Step 1: Create IMeetingTaskExtractor interface**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingTaskExtractor.cs
namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public record ExtractedTask(string Title, string Description, string Assignee, DateTime? DueDate);

public interface IMeetingTaskExtractor
{
    Task<List<ExtractedTask>> ExtractAsync(string summary, string transcript, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write extractor tests**

```csharp
// backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class ClaudeMeetingTaskExtractorTests
{
    private readonly Mock<IChatClient> _chatClientMock = new();

    [Fact]
    public async Task ExtractAsync_ReturnsParsedTasks()
    {
        var json = """[{"title":"Write specs","description":"For feature X","assignee":"Petr","dueDate":null}]""";
        _chatClientMock
            .Setup(c => c.GetResponseAsync<List<ExtractedTask>>(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse<List<ExtractedTask>>(
                new List<ExtractedTask> { new("Write specs", "For feature X", "Petr", null) }));

        var extractor = new ClaudeMeetingTaskExtractor(_chatClientMock.Object);
        var result = await extractor.ExtractAsync("Summary text", "Transcript text");

        Assert.Single(result);
        Assert.Equal("Write specs", result[0].Title);
        Assert.Equal("Petr", result[0].Assignee);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsEmptyListOnError()
    {
        _chatClientMock
            .Setup(c => c.GetResponseAsync<List<ExtractedTask>>(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API error"));

        var extractor = new ClaudeMeetingTaskExtractor(_chatClientMock.Object);
        var result = await extractor.ExtractAsync("Summary", "Transcript");

        Assert.Empty(result);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Expected: FAIL — `ClaudeMeetingTaskExtractor` does not exist

- [ ] **Step 4: Implement ClaudeMeetingTaskExtractor**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public class ClaudeMeetingTaskExtractor : IMeetingTaskExtractor
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<ClaudeMeetingTaskExtractor> _logger;

    public ClaudeMeetingTaskExtractor(IChatClient chatClient, ILogger<ClaudeMeetingTaskExtractor> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<List<ExtractedTask>> ExtractAsync(string summary, string transcript, CancellationToken ct = default)
    {
        try
        {
            var prompt = $"""
                Ze záznamu meetingu extrahuj akční úkoly jako JSON pole.
                Každý úkol musí mít: title (stručný název), description (podrobnosti), assignee (jméno osoby z transkriptu), dueDate (ISO datum nebo null).
                Vrať POUZE JSON pole, žádný jiný text.

                === SHRNUTÍ ===
                {summary}

                === TRANSKRIPT ===
                {transcript}
                """;

            var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
            var response = await _chatClient.GetResponseAsync<List<ExtractedTask>>(messages, cancellationToken: ct);
            return response.Result ?? new List<ExtractedTask>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract tasks via Claude — transcript will be imported without tasks");
            return new List<ExtractedTask>();
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Expected: All tests PASS

- [ ] **Step 6: Register in MeetingTasksModule**

Add to `MeetingTasksModule.cs`:
```csharp
services.AddScoped<IMeetingTaskExtractor, ClaudeMeetingTaskExtractor>();
```

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/
git add backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs
git commit -m "feat(meeting-tasks): add Claude task extractor for structured action item extraction"
```

---

## Task 5: IngestPlaudRecording Handler

- [ ] **Step 1: Create request**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;

public class IngestPlaudRecordingRequest : IRequest<IngestPlaudRecordingResponse>
{
    public string PlaudRecordingId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DateTime PlaudCreatedAt { get; set; }
}

public class IngestPlaudRecordingResponse
{
    public bool Success { get; set; } = true;
    public bool Skipped { get; set; }
    public Guid? TranscriptId { get; set; }
}
```

- [ ] **Step 2: Write handler tests**

```csharp
// backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs
using Anela.Heblo.Adapters.Plaud;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class IngestPlaudRecordingHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repoMock = new();
    private readonly Mock<IPlaudClient> _plaudMock = new();
    private readonly Mock<IMeetingTaskExtractor> _extractorMock = new();
    private readonly Mock<ILogger<IngestPlaudRecordingHandler>> _loggerMock = new();

    private IngestPlaudRecordingHandler CreateHandler() =>
        new(_repoMock.Object, _plaudMock.Object, _extractorMock.Object, _loggerMock.Object);

    [Fact]
    public async Task Handle_NewRecording_CreatesTranscriptWithTasks()
    {
        _repoMock.Setup(r => r.ExistsByPlaudIdAsync("rec-001", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _plaudMock.Setup(p => p.GetTranscriptAsync("rec-001", It.IsAny<CancellationToken>())).ReturnsAsync("Full transcript...");
        _plaudMock.Setup(p => p.GetSummaryAsync("rec-001", It.IsAny<CancellationToken>())).ReturnsAsync("Meeting summary...");
        _extractorMock.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask> { new("Buy milk", "Urgent", "Petr", null) });

        MeetingTranscript? saved = null;
        _repoMock.Setup(r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .Callback<MeetingTranscript, CancellationToken>((t, _) => saved = t);

        var result = await CreateHandler().Handle(new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = "rec-001",
            Name = "Sprint Planning",
            PlaudCreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.Skipped);
        Assert.NotNull(saved);
        Assert.Equal("rec-001", saved!.PlaudRecordingId);
        Assert.Equal(MeetingTranscriptStatus.PendingReview, saved.Status);
        Assert.Single(saved.Tasks);
        Assert.Equal("Buy milk", saved.Tasks[0].Title);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyIngested_SkipsWithoutSaving()
    {
        _repoMock.Setup(r => r.ExistsByPlaudIdAsync("rec-001", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateHandler().Handle(new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = "rec-001",
            Name = "Sprint Planning",
            PlaudCreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        Assert.True(result.Skipped);
        _repoMock.Verify(r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExtractorFails_SavesTranscriptWithoutTasks()
    {
        _repoMock.Setup(r => r.ExistsByPlaudIdAsync("rec-001", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _plaudMock.Setup(p => p.GetTranscriptAsync("rec-001", It.IsAny<CancellationToken>())).ReturnsAsync("Transcript...");
        _plaudMock.Setup(p => p.GetSummaryAsync("rec-001", It.IsAny<CancellationToken>())).ReturnsAsync("Summary...");
        _extractorMock.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>());

        MeetingTranscript? saved = null;
        _repoMock.Setup(r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .Callback<MeetingTranscript, CancellationToken>((t, _) => saved = t);

        var result = await CreateHandler().Handle(new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = "rec-001", Name = "Meeting", PlaudCreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(saved);
        Assert.Empty(saved!.Tasks);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Expected: FAIL — `IngestPlaudRecordingHandler` does not exist

- [ ] **Step 4: Implement handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs
using Anela.Heblo.Adapters.Plaud;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;

public class IngestPlaudRecordingHandler : IRequestHandler<IngestPlaudRecordingRequest, IngestPlaudRecordingResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IPlaudClient _plaudClient;
    private readonly IMeetingTaskExtractor _extractor;
    private readonly ILogger<IngestPlaudRecordingHandler> _logger;

    public IngestPlaudRecordingHandler(
        IMeetingTranscriptRepository repository,
        IPlaudClient plaudClient,
        IMeetingTaskExtractor extractor,
        ILogger<IngestPlaudRecordingHandler> logger)
    {
        _repository = repository;
        _plaudClient = plaudClient;
        _extractor = extractor;
        _logger = logger;
    }

    public async Task<IngestPlaudRecordingResponse> Handle(
        IngestPlaudRecordingRequest request, CancellationToken cancellationToken)
    {
        if (await _repository.ExistsByPlaudIdAsync(request.PlaudRecordingId, cancellationToken))
        {
            _logger.LogDebug("Recording {Id} already ingested, skipping", request.PlaudRecordingId);
            return new IngestPlaudRecordingResponse { Skipped = true };
        }

        var transcript = await _plaudClient.GetTranscriptAsync(request.PlaudRecordingId, cancellationToken);
        var summary = await _plaudClient.GetSummaryAsync(request.PlaudRecordingId, cancellationToken);
        var tasks = await _extractor.ExtractAsync(summary, transcript, cancellationToken);

        var entity = new MeetingTranscript
        {
            Id = Guid.NewGuid(),
            PlaudRecordingId = request.PlaudRecordingId,
            PlaudCreatedAt = request.PlaudCreatedAt,
            Subject = request.Name,
            Summary = summary,
            RawTranscript = transcript,
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = tasks.Select(t => new ProposedTask
            {
                Id = Guid.NewGuid(),
                Title = t.Title,
                Description = t.Description,
                Assignee = t.Assignee,
                DueDate = t.DueDate,
                Status = ProposedTaskStatus.Pending,
                IsManuallyAdded = false
            }).ToList()
        };

        await _repository.AddAsync(entity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Ingested recording {Id} '{Name}' with {Count} proposed tasks",
            request.PlaudRecordingId, request.Name, entity.Tasks.Count);

        return new IngestPlaudRecordingResponse { TranscriptId = entity.Id };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Expected: All 3 tests PASS

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/
git add backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs
git commit -m "feat(meeting-tasks): add IngestPlaudRecording handler with dedup and task extraction"
```

---

## Task 6: Hangfire Polling Job

Follows the `DailyConsumptionJob` pattern (`IRecurringJob`, `_statusChecker` gate, MediatR dispatch).

- [ ] **Step 1: Create PlaudPollingJob**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs
using Anela.Heblo.Adapters.Plaud;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MeetingTasks.Infrastructure.Jobs;

public class PlaudPollingJob : IRecurringJob
{
    public RecurringJobMetadata Metadata => new(
        JobName: "MeetingTasks.PlaudPolling",
        DisplayName: "Plaud — pull meeting transcripts",
        Cron: "*/5 * * * *",
        DefaultIsEnabled: false);

    private readonly IMediator _mediator;
    private readonly IPlaudClient _plaudClient;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly PlaudOptions _options;
    private readonly ILogger<PlaudPollingJob> _logger;

    public PlaudPollingJob(
        IMediator mediator,
        IPlaudClient plaudClient,
        IRecurringJobStatusChecker statusChecker,
        IOptions<PlaudOptions> options,
        ILogger<PlaudPollingJob> logger)
    {
        _mediator = mediator;
        _plaudClient = plaudClient;
        _statusChecker = statusChecker;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
            return;

        var recordings = await _plaudClient.ListRecentAsync(_options.MaxRecordingAgeDays, cancellationToken);
        var ready = recordings.Where(r => r.HasTranscript && r.HasSummary).ToList();

        _logger.LogInformation("Plaud poll: {Total} recordings found, {Ready} ready for ingestion",
            recordings.Count, ready.Count);

        var ingested = 0;
        var skipped = 0;

        foreach (var recording in ready)
        {
            var response = await _mediator.Send(new IngestPlaudRecordingRequest
            {
                PlaudRecordingId = recording.Id,
                Name = recording.Name,
                PlaudCreatedAt = recording.CreatedAt
            }, cancellationToken);

            if (response.Skipped) skipped++;
            else if (response.Success) ingested++;
        }

        _logger.LogInformation("Plaud poll complete: {Ingested} new, {Skipped} already known", ingested, skipped);
    }
}
```

- [ ] **Step 2: Register job in MeetingTasksModule**

```csharp
services.AddTransient<PlaudPollingJob>();
```

The job is auto-discovered by `RecurringJobDiscoveryService` as long as it implements `IRecurringJob` and is registered in DI.

- [ ] **Step 3: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.Application/`
Expected: Build succeeded

- [ ] **Step 4: Manual end-to-end verification**

1. Run `plaud login` locally
2. Copy `~/.plaud/tokens.json` content → set as `Plaud:TokensJson` in `secrets.json`
3. Start backend locally
4. Open Hangfire UI → `/hangfire` → trigger `MeetingTasks.PlaudPolling` manually
5. Verify new `MeetingTranscripts` rows appear in DB with `PlaudRecordingId`, summary, transcript, and extracted tasks

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs
git commit -m "feat(meeting-tasks): add Hangfire polling job to pull Plaud recordings every 5 minutes"
```

---

## Config reference

`appsettings.json`:
```json
"Plaud": {
  "CliExecutablePath": "plaud",
  "ProcessTimeoutSeconds": 60,
  "MaxRecordingAgeDays": 7
}
```

Azure App Service / `secrets.json`:
```
Plaud__TokensJson = <raw content of ~/.plaud/tokens.json>
```

The polling job is **disabled by default**. Enable via Hangfire UI or `IRecurringJobStatusChecker` after `Plaud__TokensJson` is configured.

---

> **Integration:** Create your feature branch from `feat/meeting-task-validation-epic`. When done, open a PR targeting `feat/meeting-task-validation-epic` (not `main`).