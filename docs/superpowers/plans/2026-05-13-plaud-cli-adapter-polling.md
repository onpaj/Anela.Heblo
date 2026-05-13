# Plaud CLI Adapter, Polling Job & Ingest Handler — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an autonomous server-driven pipeline that polls the Plaud CLI every 5 minutes, fetches completed meeting recordings, extracts action items via Claude, and persists them as `MeetingTranscript` rows in `PendingReview` state — replacing the rejected n8n email-webhook approach.

**Architecture:** A new adapter project `Anela.Heblo.Adapters.Plaud` wraps the `plaud` CLI binary as `IPlaudClient` (process invocations with `ArgumentList`, explicit kill-on-timeout, parser exposed `public static` for tests). A Hangfire `IRecurringJob` (`PlaudPollingJob`, kebab-case `plaud-polling`, disabled by default, auto-discovered via `AddRecurringJobs` assembly scan) dispatches `IngestPlaudRecordingRequest` per ready recording. The MediatR handler dedupes on `PlaudRecordingId`, fetches transcript + summary, calls `ClaudeMeetingTaskExtractor` (plain-text Claude response → strip code-fence → `JsonSerializer.Deserialize`, never fails ingestion), and persists via the epic-branch `IMeetingTranscriptRepository`. Per-recording try/catch isolates failures inside the polling loop.

**Tech Stack:** .NET 8, C# 12, MediatR, Hangfire (`AddOrUpdate` via existing `RecurringJobDiscoveryService`), `Microsoft.Extensions.AI` (`IChatClient` from existing Anthropic adapter), EF Core (consumed via `IMeetingTranscriptRepository`), xUnit + Moq + FluentAssertions, `System.Diagnostics.Process`.

---

## File Structure

### New project: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/`
| File | Responsibility |
|---|---|
| `Anela.Heblo.Adapters.Plaud.csproj` | Project file, references `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`. No project references (adapter stays independent). |
| `PlaudOptions.cs` | Options class bound to `"Plaud"` configuration section. |
| `PlaudRecordingSummary.cs` | DTO returned by `IPlaudClient.ListRecentAsync`. Class (not record) so callers can read by property name. |
| `IPlaudClient.cs` | Contract: `ListRecentAsync`, `GetTranscriptAsync`, `GetSummaryAsync`. |
| `PlaudCliClient.cs` | `IPlaudClient` implementation; subprocess invocation via `ProcessStartInfo.ArgumentList`; `public static ParseFilesOutput` exposed for tests. |
| `PlaudTokenBootstrapper.cs` | `IHostedService` that writes `~/.plaud/tokens.json` on startup (mode `0600` on Unix). |
| `PlaudAdapterServiceCollectionExtensions.cs` | `AddPlaudAdapter(IConfiguration)` registers options, singleton `IPlaudClient → PlaudCliClient`, hosted-service bootstrapper. |

### New test project: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/`
| File | Responsibility |
|---|---|
| `Anela.Heblo.Adapters.Plaud.Tests.csproj` | xUnit + Moq + FluentAssertions; project reference to `Anela.Heblo.Adapters.Plaud`. |
| `PlaudCliClientParserTests.cs` | All parser tests; load fixture from `Fixtures/plaud_recent_sample.txt`. |
| `Fixtures/plaud_recent_sample.txt` | Verbatim `plaud recent --days 7` output captured against a live CLI. Committed to repo. |
| `Fixtures/plaud_recent_header_only.txt` | Header row + blank body (CI fixture for empty case). |

### New feature folder: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/`
| File | Responsibility |
|---|---|
| `MeetingTasksModule.cs` | `AddMeetingTasksModule(IConfiguration)`; registers `IMeetingTaskExtractor`. `PlaudPollingJob` is auto-discovered by `AddRecurringJobs()` — no manual registration. |
| `Services/IMeetingTaskExtractor.cs` | Contract returning `Task<List<ExtractedTask>>`. |
| `Services/ExtractedTask.cs` | `record ExtractedTask(string Title, string Description, string Assignee, DateTime? DueDate)`. Internal domain DTO; not exposed via OpenAPI, so record is fine. |
| `Services/ClaudeMeetingTaskExtractor.cs` | Implementation using plain-text `IChatClient.GetResponseAsync` + Markdown code-fence stripping + `JsonSerializer.Deserialize`. Swallows all exceptions → empty list. |
| `UseCases/IngestPlaudRecording/IngestPlaudRecordingRequest.cs` | Class (not record — DTO rule). MediatR request. |
| `UseCases/IngestPlaudRecording/IngestPlaudRecordingResponse.cs` | Class. `Success`, `Skipped`, `TranscriptId`. |
| `UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs` | MediatR handler: dedupe → fetch transcript+summary → extract → save. |
| `Infrastructure/Jobs/PlaudPollingJob.cs` | `IRecurringJob` + `[DisableConcurrentExecution(60)]`. Kebab-case `JobName = "plaud-polling"`, `DefaultIsEnabled = false`. |

### New test folder: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/`
| File | Responsibility |
|---|---|
| `Services/ClaudeMeetingTaskExtractorTests.cs` | Stubbed `IChatClient`; success path + exception path. |
| `UseCases/IngestPlaudRecording/IngestPlaudRecordingHandlerTests.cs` | New / already-exists / empty-extraction paths. |
| `Infrastructure/Jobs/PlaudPollingJobTests.cs` | Disabled-gate / filter `HasTranscript && HasSummary` / per-recording try-catch / count logging. |

### Modified
- `Anela.Heblo.sln` — register two new projects.
- `backend/src/Anela.Heblo.API/Program.cs` (line 74 area) — add `AddPlaudAdapter`.
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (after `AddLeafletModule`) — add `AddMeetingTasksModule`.
- `backend/src/Anela.Heblo.API/appsettings.json` — add `"Plaud"` section (non-secret defaults).
- `Dockerfile` — install pinned `plaud` CLI binary.
- `docs/integrations/plaud-cli.md` — install instructions, version pin, `plaud login` flow.

---

## Conventions used throughout this plan

- **Commit prefix:** `feat:`, `test:`, `chore:` per `~/.claude/rules/git-workflow.md`.
- **DTO rule:** `IngestPlaudRecording{Request,Response}` are classes. Internal records (`ExtractedTask`) are fine.
- **Job naming:** kebab-case `plaud-polling`; required `Description` field; `DefaultIsEnabled = false`.
- **Per-recording isolation:** every per-iteration call wrapped in `try { … } catch (Exception ex) { _logger.LogError(ex, "…", id); }`.
- **No mocking of `Process`:** the parser is the only seam in the adapter that gets tested in-proc; CLI invocation is verified manually.

---

## Prerequisite Tasks

These must be resolved before any implementation work starts. They are documented as discrete tasks so the executing agent can verify each one independently.

### Task P1: Rebase onto epic branch

**Files:**
- N/A — git operation only.

- [ ] **Step 1: Verify current branch and remote epic branch exist**

Run:
```bash
git status
git fetch origin
git rev-parse --verify origin/feat/meeting-task-validation-epic
```
Expected: clean working tree, hash printed for the epic ref.

- [ ] **Step 2: Rebase onto the epic branch**

Run:
```bash
git rebase origin/feat/meeting-task-validation-epic
```
Expected: "Successfully rebased" — no conflicts (working tree currently only has artifacts).

- [ ] **Step 3: Verify epic-branch types are now present locally**

Run:
```bash
ls backend/src/Anela.Heblo.Domain/Features/MeetingTasks/
```
Expected output includes: `IMeetingTranscriptRepository.cs`, `MeetingTranscript.cs`, `MeetingTranscriptStatus.cs`, `ProposedTask.cs`, `ProposedTaskStatus.cs`.

- [ ] **Step 4: Verify solution still builds**

Run:
```bash
dotnet build Anela.Heblo.sln
```
Expected: build succeeds with zero errors. (Warnings acceptable.)

### Task P2: Pick the Plaud CLI distribution channel and version pin

**Files:**
- Create: `docs/integrations/plaud-cli.md`

- [ ] **Step 1: Decide channel**

Document the chosen distribution channel (pip / npm / Go install / curl tarball) and pin a specific version. The executing agent SHOULD ask the user to confirm the channel before writing the doc. If the user is unavailable, default to **pip** with version pinned, since the `plaud` reference CLI is Python-based and `pip install --upgrade --user "plaud-cli==X.Y.Z"` is the most portable inside a Debian `mcr.microsoft.com/dotnet/aspnet:8.0` image.

- [ ] **Step 2: Write the integration doc**

Create `docs/integrations/plaud-cli.md`:

```markdown
# Plaud CLI

## Install

Install pinned version inside the runtime container (Debian-based aspnet:8.0):

```
RUN apt-get update && apt-get install -y python3-pip ca-certificates \
  && pip3 install --no-cache-dir "plaud-cli==X.Y.Z" \
  && apt-get clean && rm -rf /var/lib/apt/lists/*
```

Pinned version: **X.Y.Z**. Bump deliberately; never use a floating tag.

## Authentication

The CLI requires OAuth tokens at `~/.plaud/tokens.json`. In container deployments we materialize this file from the `Plaud__TokensJson` configuration value via `PlaudTokenBootstrapper` (see `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenBootstrapper.cs`).

To obtain tokens locally:

```
plaud login
cat ~/.plaud/tokens.json   # paste into dotnet user-secrets as Plaud:TokensJson
```

## Subcommands used by Heblo

| Command | Output | Consumer |
|---|---|---|
| `plaud recent --days 7` | Tabular file list | `PlaudCliClient.ListRecentAsync` |
| `plaud transcript {id}` | Plain text | `PlaudCliClient.GetTranscriptAsync` |
| `plaud summary {id}` | Plain text | `PlaudCliClient.GetSummaryAsync` |

## Smoke test

```
plaud --version          # expect X.Y.Z
plaud recent --days 1    # expect tabular output, headers preserved
```
```

- [ ] **Step 3: Commit**

```bash
git add docs/integrations/plaud-cli.md
git commit -m "docs: document Plaud CLI distribution channel and version pin"
```

### Task P3: Capture real CLI output fixture

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/Fixtures/plaud_recent_sample.txt`
- Create: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/Fixtures/plaud_recent_header_only.txt`

(The csproj for this test project is created in Task 2 below; the fixtures live alongside it.)

- [ ] **Step 1: Capture verbatim output**

On a machine with a logged-in CLI, run `plaud recent --days 7 > plaud_recent_sample.txt`. Strip nothing. Save the file as the fixture, committed to the repo. Format **must** match what the parser will receive at runtime.

If the executing agent does not have CLI access, **fail this task explicitly** and ask the user. Do NOT invent a hypothetical format — the spec amendment in `arch-review.r1.md` (Decision 1, FR-2 amendment) forbids inline-string fixtures.

- [ ] **Step 2: Create the empty/header-only fixture**

Save only the header line from `plaud_recent_sample.txt` to `plaud_recent_header_only.txt` (single line, no body). This is used to verify empty parsing.

- [ ] **Step 3: Commit (after Task 2 has created the project)**

Defer commit until Task 2.5 below where the csproj exists; otherwise these files are stranded. Once the test csproj exists:

```bash
git add backend/test/Anela.Heblo.Adapters.Plaud.Tests/Fixtures/
git commit -m "test: add Plaud CLI output fixtures captured from live CLI"
```

---

## Task 1: Create the `Anela.Heblo.Adapters.Plaud` project

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj`

- [ ] **Step 1: Create the directory and csproj**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj` with the following content:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net8.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <RootNamespace>Anela.Heblo.Adapters.Plaud</RootNamespace>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="8.0.0" />
        <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
        <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0" />
    </ItemGroup>

</Project>
```

- [ ] **Step 2: Register the project in the solution**

Use `dotnet sln` so the GUIDs are unique:

```bash
dotnet sln Anela.Heblo.sln add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj
```

Expected: "Project … added to the solution."

- [ ] **Step 3: Move the project under the `Adapters` solution folder**

The `dotnet sln add` command places the project at the root of the solution. The repo's `.sln` has a virtual `Adapters` folder (GUID `{4B6F17C3-0A57-487A-BE8C-1808B40EC604}`); other adapters live under it.

Open `Anela.Heblo.sln`, find the new `Anela.Heblo.Adapters.Plaud` entry and the existing `GlobalSection(NestedProjects) = preSolution` block, and add a line nesting the new project's GUID under the `Adapters` folder GUID:

```
{<new-plaud-guid>} = {4B6F17C3-0A57-487A-BE8C-1808B40EC604}
```

Verify by searching for other adapter GUID nestings (e.g. `Anela.Heblo.Adapters.Anthropic`'s line) and following the same pattern.

- [ ] **Step 4: Verify solution restores**

Run:
```bash
dotnet restore Anela.Heblo.sln
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj
```
Expected: both succeed with zero errors.

- [ ] **Step 5: Commit**

```bash
git add Anela.Heblo.sln backend/src/Adapters/Anela.Heblo.Adapters.Plaud/
git commit -m "feat: scaffold Anela.Heblo.Adapters.Plaud project"
```

---

## Task 2: Create the test project skeleton + fixture commit

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj`

- [ ] **Step 1: Create the csproj**

Create `backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net8.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <IsPackable>false</IsPackable>
        <IsTestProject>true</IsTestProject>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="coverlet.collector" Version="6.0.0" />
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
        <PackageReference Include="xunit" Version="2.5.3" />
        <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
        <PackageReference Include="Moq" Version="4.20.70" />
        <PackageReference Include="FluentAssertions" Version="6.12.0" />
    </ItemGroup>

    <ItemGroup>
        <Using Include="Xunit" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.Plaud\Anela.Heblo.Adapters.Plaud.csproj" />
    </ItemGroup>

    <ItemGroup>
        <None Update="Fixtures\plaud_recent_sample.txt">
            <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        </None>
        <None Update="Fixtures\plaud_recent_header_only.txt">
            <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        </None>
    </ItemGroup>

</Project>
```

- [ ] **Step 2: Add the project to the solution under the `test` folder**

Run:
```bash
dotnet sln Anela.Heblo.sln add backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj
```

Then nest its GUID under the `test` solution folder (find the existing `Anela.Heblo.Adapters.Flexi.Tests` nesting in the `NestedProjects` section as a model: `{<new-test-guid>} = {23FE24B3-CD9D-4576-A7C8-85D5B012F43D}`).

- [ ] **Step 3: Add the fixtures from Task P3 (now that the csproj exists)**

Run:
```bash
ls backend/test/Anela.Heblo.Adapters.Plaud.Tests/Fixtures/
```
Expected: `plaud_recent_header_only.txt`, `plaud_recent_sample.txt`. If either is missing, **stop** and complete Task P3.

- [ ] **Step 4: Verify the test project compiles**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj
```
Expected: zero errors. (Project has no test code yet — empty test suite is fine.)

- [ ] **Step 5: Commit**

```bash
git add Anela.Heblo.sln backend/test/Anela.Heblo.Adapters.Plaud.Tests/
git commit -m "test: scaffold Anela.Heblo.Adapters.Plaud.Tests project with CLI fixtures"
```

---

## Task 3: `PlaudOptions`

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudOptions.cs`

- [ ] **Step 1: Write the options class**

```csharp
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

- [ ] **Step 2: Build**

Run:
```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj
```
Expected: zero errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudOptions.cs
git commit -m "feat: add PlaudOptions with CLI path, tokens, timeout, age window"
```

---

## Task 4: `PlaudRecordingSummary` DTO

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudRecordingSummary.cs`

- [ ] **Step 1: Write the DTO**

```csharp
namespace Anela.Heblo.Adapters.Plaud;

public class PlaudRecordingSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool HasTranscript { get; set; }
    public bool HasSummary { get; set; }
}
```

- [ ] **Step 2: Build**

Run:
```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj
```
Expected: zero errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudRecordingSummary.cs
git commit -m "feat: add PlaudRecordingSummary DTO"
```

---

## Task 5: `IPlaudClient` contract

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudClient.cs`

- [ ] **Step 1: Write the interface**

```csharp
namespace Anela.Heblo.Adapters.Plaud;

public interface IPlaudClient
{
    Task<List<PlaudRecordingSummary>> ListRecentAsync(int days, CancellationToken ct = default);

    Task<string> GetTranscriptAsync(string recordingId, CancellationToken ct = default);

    Task<string> GetSummaryAsync(string recordingId, CancellationToken ct = default);
}
```

- [ ] **Step 2: Build**

Run:
```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj
```
Expected: zero errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudClient.cs
git commit -m "feat: add IPlaudClient contract"
```

---

## Task 6: `PlaudCliClient.ParseFilesOutput` — TDD

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientParserTests.cs`

The parser is the only piece of the adapter that runs in-proc during tests. Build it first with TDD before any subprocess code.

- [ ] **Step 1: Write the failing parser tests**

Create `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientParserTests.cs`:

```csharp
using Anela.Heblo.Adapters.Plaud;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public class PlaudCliClientParserTests
{
    private static string LoadFixture(string filename) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", filename));

    [Fact]
    public void ParseFilesOutput_returns_empty_list_for_empty_input()
    {
        var result = PlaudCliClient.ParseFilesOutput(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseFilesOutput_returns_empty_list_for_header_only_input()
    {
        var headerOnly = LoadFixture("plaud_recent_header_only.txt");

        var result = PlaudCliClient.ParseFilesOutput(headerOnly);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseFilesOutput_parses_multi_row_fixture()
    {
        var sample = LoadFixture("plaud_recent_sample.txt");

        var result = PlaudCliClient.ParseFilesOutput(sample);

        result.Should().NotBeEmpty("the fixture must contain at least one recording row");
        result.Should().AllSatisfy(r =>
        {
            r.Id.Should().NotBeNullOrWhiteSpace();
            r.Name.Should().NotBeNullOrWhiteSpace();
        });
    }

    [Fact]
    public void ParseFilesOutput_detects_ready_recordings()
    {
        var sample = LoadFixture("plaud_recent_sample.txt");

        var result = PlaudCliClient.ParseFilesOutput(sample);

        result.Should().Contain(r => r.HasTranscript && r.HasSummary,
            "the captured fixture is expected to include at least one fully-ready recording");
    }

    [Fact]
    public void ParseFilesOutput_skips_rows_with_fewer_than_six_tokens()
    {
        var malformed = "ID NAME DATE TIME TRANSCRIPT SUMMARY\nbad-row\n";

        var result = PlaudCliClient.ParseFilesOutput(malformed);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseFilesOutput_treats_non_yes_flags_as_false()
    {
        var input =
            "ID NAME DATE TIME TRANSCRIPT SUMMARY\n" +
            "rec-001 My Meeting Name 2026-05-10 09:00 no no\n";

        var result = PlaudCliClient.ParseFilesOutput(input);

        result.Should().HaveCount(1);
        result[0].HasTranscript.Should().BeFalse();
        result[0].HasSummary.Should().BeFalse();
    }

    [Fact]
    public void ParseFilesOutput_treats_yes_flags_case_insensitively()
    {
        var input =
            "ID NAME DATE TIME TRANSCRIPT SUMMARY\n" +
            "rec-002 Other Meeting 2026-05-11 10:30 YES Yes\n";

        var result = PlaudCliClient.ParseFilesOutput(input);

        result.Should().HaveCount(1);
        result[0].HasTranscript.Should().BeTrue();
        result[0].HasSummary.Should().BeTrue();
    }

    [Fact]
    public void ParseFilesOutput_reconstructs_multi_word_names()
    {
        var input =
            "ID NAME DATE TIME TRANSCRIPT SUMMARY\n" +
            "rec-003 Quarterly Planning Sync 2026-05-12 14:00 yes yes\n";

        var result = PlaudCliClient.ParseFilesOutput(input);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("rec-003");
        result[0].Name.Should().Be("Quarterly Planning Sync");
    }

    [Fact]
    public void ParseFilesOutput_returns_default_dt_on_unparseable_date()
    {
        var input =
            "ID NAME DATE TIME TRANSCRIPT SUMMARY\n" +
            "rec-004 Meeting NOT-A-DATE 99:99 yes yes\n";

        var result = PlaudCliClient.ParseFilesOutput(input);

        result.Should().HaveCount(1);
        result[0].CreatedAt.Should().Be(default(DateTime));
    }
}
```

- [ ] **Step 2: Verify tests fail (parser not defined yet)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj
```
Expected: compile failure ("`PlaudCliClient` does not exist" or "`ParseFilesOutput` not found").

- [ ] **Step 3: Implement the parser only (leave subprocess methods for later)**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs` with **only** the parser. The full `IPlaudClient` implementation lands in Task 7.

```csharp
using System.Globalization;

namespace Anela.Heblo.Adapters.Plaud;

public sealed partial class PlaudCliClient
{
    public static List<PlaudRecordingSummary> ParseFilesOutput(string stdout)
    {
        var results = new List<PlaudRecordingSummary>();
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return results;
        }

        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // First line is the header; skip it.
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 6)
            {
                continue;
            }

            // Last four tokens are: date, time, transcript flag, summary flag.
            var summaryFlag = tokens[^1];
            var transcriptFlag = tokens[^2];
            var timeToken = tokens[^3];
            var dateToken = tokens[^4];

            var id = tokens[0];
            var nameTokens = tokens[1..^4];
            var name = string.Join(' ', nameTokens);

            DateTime createdAt;
            if (!DateTime.TryParse($"{dateToken} {timeToken}", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out createdAt))
            {
                createdAt = default;
            }

            results.Add(new PlaudRecordingSummary
            {
                Id = id,
                Name = name,
                CreatedAt = createdAt,
                HasTranscript = string.Equals(transcriptFlag, "yes", StringComparison.OrdinalIgnoreCase),
                HasSummary = string.Equals(summaryFlag, "yes", StringComparison.OrdinalIgnoreCase),
            });
        }

        return results;
    }
}
```

The class is `partial` so subprocess methods can land in Task 7 in the same physical file without disturbing this content.

- [ ] **Step 4: Verify tests pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj
```
Expected: all parser tests pass. If `ParseFilesOutput_parses_multi_row_fixture` or `ParseFilesOutput_detects_ready_recordings` fails, the captured fixture in Task P3 does not match the expected column structure — investigate the fixture, **do not loosen the parser**.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientParserTests.cs
git commit -m "feat: add PlaudCliClient.ParseFilesOutput with TDD coverage"
```

---

## Task 7: `PlaudCliClient` subprocess invocation (no unit tests — manual verification only)

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs`

Process invocation is not unit-tested (`Process` is hard to fake safely and the codebase doesn't mock the OS). Tests for the parser (Task 6) and integration smoke (Task P2's `plaud --version` step in the Dockerfile) cover the seams that matter.

- [ ] **Step 1: Add the instance portion to `PlaudCliClient.cs`**

Append to the existing file:

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud;

public sealed partial class PlaudCliClient : IPlaudClient
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
        var stdout = await RunAsync(["recent", "--days", days.ToString()], ct);
        return ParseFilesOutput(stdout);
    }

    public Task<string> GetTranscriptAsync(string recordingId, CancellationToken ct = default) =>
        RunAsync(["transcript", recordingId], ct);

    public Task<string> GetSummaryAsync(string recordingId, CancellationToken ct = default) =>
        RunAsync(["summary", recordingId], ct);

    private async Task<string> RunAsync(string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _options.CliExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = new Process { StartInfo = psi };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start '{_options.CliExecutablePath}'.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Could not start CLI '{_options.CliExecutablePath}'. Is it installed and on PATH?", ex);
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ProcessTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token).AsTask();
        var stderrTask = process.StandardError.ReadToEndAsync(linked.Token).AsTask();

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"Plaud CLI invocation timed out after {_options.ProcessTimeoutSeconds}s: {_options.CliExecutablePath} {string.Join(' ', args)}");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            _logger.LogError(
                "Plaud CLI exited {ExitCode}. Args: {Args}. Stderr: {Stderr}",
                process.ExitCode, string.Join(' ', args), stderr);
            throw new InvalidOperationException(
                $"Plaud CLI exited with code {process.ExitCode}. See logs for stderr.");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _logger.LogWarning("Plaud CLI emitted stderr: {Stderr}", stderr);
        }

        return stdout;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup; ignore.
        }
    }
}
```

- [ ] **Step 2: Build**

Run:
```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj
```
Expected: zero errors.

- [ ] **Step 3: Re-run parser tests (no regressions)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj
```
Expected: parser tests still pass.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs
git commit -m "feat: implement PlaudCliClient subprocess invocation with kill-on-timeout"
```

---

## Task 8: `PlaudTokenBootstrapper`

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenBootstrapper.cs`

- [ ] **Step 1: Write the hosted service**

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudTokenBootstrapper : IHostedService
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
            _logger.LogWarning(
                "Plaud:TokensJson is not configured. The Plaud CLI will not be authenticated. " +
                "The plaud-polling job is disabled by default; enable it only after tokens are supplied.");
            return Task.CompletedTask;
        }

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var plaudDir = Path.Combine(homeDir, ".plaud");
        Directory.CreateDirectory(plaudDir);

        var tokensPath = Path.Combine(plaudDir, "tokens.json");
        File.WriteAllText(tokensPath, _options.TokensJson);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(tokensPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not set 0600 permissions on {Path}", tokensPath);
            }
        }

        _logger.LogInformation("Wrote Plaud tokens to {Path}", tokensPath);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

- [ ] **Step 2: Build**

Run:
```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj
```
Expected: zero errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenBootstrapper.cs
git commit -m "feat: add PlaudTokenBootstrapper hosted service with 0600 file mode"
```

---

## Task 9: `AddPlaudAdapter` DI extension

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAdapterServiceCollectionExtensions.cs`

- [ ] **Step 1: Write the extension**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Adapters.Plaud;

public static class PlaudAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddPlaudAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PlaudOptions>()
            .Bind(configuration.GetSection(PlaudOptions.SectionKey));

        services.AddSingleton<IPlaudClient, PlaudCliClient>();
        services.AddHostedService<PlaudTokenBootstrapper>();

        return services;
    }
}
```

- [ ] **Step 2: Build**

Run:
```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj
```
Expected: zero errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAdapterServiceCollectionExtensions.cs
git commit -m "feat: add AddPlaudAdapter DI extension"
```

---

## Task 10: Wire `AddPlaudAdapter` into `Program.cs` and `appsettings.json`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
- Modify: `backend/src/Anela.Heblo.API/Program.cs` (around line 74)
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (after the `"Anthropic"` block)

- [ ] **Step 1: Add a `ProjectReference` to `Anela.Heblo.Adapters.Plaud` in the API csproj**

In `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`, find the block where other adapter `ProjectReference` entries live (search for `Anela.Heblo.Adapters.Anthropic`) and add adjacent to it:

```xml
<ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.Plaud\Anela.Heblo.Adapters.Plaud.csproj" />
```

- [ ] **Step 2: Add the using and the registration in `Program.cs`**

After line `using Anela.Heblo.Adapters.Anthropic;` add:
```csharp
using Anela.Heblo.Adapters.Plaud;
```

After line `builder.Services.AddAnthropicAdapter(builder.Configuration);` add:
```csharp
        builder.Services.AddPlaudAdapter(builder.Configuration);
```

- [ ] **Step 3: Add the `"Plaud"` section to `appsettings.json`**

After the `"Anthropic"` block, add:

```json
  "Plaud": {
    "CliExecutablePath": "plaud",
    "ProcessTimeoutSeconds": 60,
    "MaxRecordingAgeDays": 7
  },
```

(Note: `TokensJson` is intentionally omitted — it is supplied only via user-secrets / App Service config.)

- [ ] **Step 4: Build the whole solution**

Run:
```bash
dotnet build Anela.Heblo.sln
```
Expected: zero errors. (`MeetingTranscript` types from the epic rebase must be present.)

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj backend/src/Anela.Heblo.API/Program.cs backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat: wire AddPlaudAdapter into API host and add non-secret defaults"
```

---

## Task 11: `ExtractedTask` and `IMeetingTaskExtractor`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ExtractedTask.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingTaskExtractor.cs`

- [ ] **Step 1: Write the record**

`ExtractedTask.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public record ExtractedTask(string Title, string Description, string Assignee, DateTime? DueDate);
```

- [ ] **Step 2: Write the interface**

`IMeetingTaskExtractor.cs`:
```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public interface IMeetingTaskExtractor
{
    Task<List<ExtractedTask>> ExtractAsync(string summary, string transcript, CancellationToken ct = default);
}
```

- [ ] **Step 3: Build**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: zero errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/
git commit -m "feat: add IMeetingTaskExtractor contract and ExtractedTask DTO"
```

---

## Task 12: `ClaudeMeetingTaskExtractor` — TDD

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractorTests.cs`

The extractor takes the plain-text Claude response, strips Markdown code fences, and deserializes the JSON array. Failures (network, JSON, anything) become empty lists per FR-4.

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks.Services;

public class ClaudeMeetingTaskExtractorTests
{
    private readonly Mock<IChatClient> _chatClient = new();
    private readonly Mock<ILogger<ClaudeMeetingTaskExtractor>> _logger = new();

    private ClaudeMeetingTaskExtractor CreateExtractor() =>
        new(_chatClient.Object, _logger.Object);

    private void SetupChat(string responseText)
    {
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, responseText)]));
    }

    [Fact]
    public async Task ExtractAsync_returns_parsed_tasks_on_success()
    {
        SetupChat(
            """
            [
              { "title": "Send proposal", "description": "Send Q3 proposal to ACME", "assignee": "Jana", "dueDate": "2026-06-01" },
              { "title": "Book venue", "description": "Find venue for kickoff", "assignee": "Pavel", "dueDate": null }
            ]
            """);

        var result = await CreateExtractor().ExtractAsync("summary", "transcript");

        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Send proposal");
        result[0].Assignee.Should().Be("Jana");
        result[0].DueDate.Should().Be(new DateTime(2026, 6, 1));
        result[1].DueDate.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_strips_markdown_code_fence_before_parsing()
    {
        SetupChat(
            """
            ```json
            [
              { "title": "X", "description": "Y", "assignee": "Z", "dueDate": null }
            ]
            ```
            """);

        var result = await CreateExtractor().ExtractAsync("s", "t");

        result.Should().HaveCount(1);
        result[0].Title.Should().Be("X");
    }

    [Fact]
    public async Task ExtractAsync_returns_empty_list_on_chat_exception()
    {
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var result = await CreateExtractor().ExtractAsync("s", "t");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_returns_empty_list_on_malformed_json()
    {
        SetupChat("not json at all");

        var result = await CreateExtractor().ExtractAsync("s", "t");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_returns_empty_list_when_response_is_empty()
    {
        SetupChat(string.Empty);

        var result = await CreateExtractor().ExtractAsync("s", "t");

        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Verify tests fail (extractor not defined)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests"
```
Expected: compile failure on `ClaudeMeetingTaskExtractor`.

- [ ] **Step 3: Implement the extractor**

`backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public sealed class ClaudeMeetingTaskExtractor : IMeetingTaskExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

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
            var prompt = BuildPrompt(summary, transcript);
            var messages = new[]
            {
                new ChatMessage(ChatRole.User, prompt),
            };

            var response = await _chatClient.GetResponseAsync(messages, options: null, cancellationToken: ct);
            var text = response.Messages.FirstOrDefault()?.Text ?? string.Empty;
            var cleaned = StripCodeFence(text);

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return new List<ExtractedTask>();
            }

            var parsed = JsonSerializer.Deserialize<List<ExtractedTask>>(cleaned, JsonOptions);
            return parsed ?? new List<ExtractedTask>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to extract tasks via Claude — transcript will be imported without tasks");
            return new List<ExtractedTask>();
        }
    }

    private static string BuildPrompt(string summary, string transcript) =>
        $$"""
        Z následujícího shrnutí a přepisu schůzky vyextrahuj seznam akčních úkolů.
        Pro každý úkol vrať: title (krátký název), description (kontext), assignee (jméno z přepisu) a dueDate (datum ve formátu YYYY-MM-DD nebo null).
        Vrať POUZE JSON pole bez doprovodného textu.

        Shrnutí:
        {{summary}}

        Přepis:
        {{transcript}}
        """;

    private static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }
            if (trimmed.EndsWith("```"))
            {
                trimmed = trimmed[..^3];
            }
        }
        return trimmed.Trim();
    }
}
```

- [ ] **Step 4: Verify tests pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests"
```
Expected: all 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs backend/test/Anela.Heblo.Tests/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractorTests.cs
git commit -m "feat: add ClaudeMeetingTaskExtractor with graceful-degradation tests"
```

---

## Task 13: `IngestPlaudRecordingRequest` and `IngestPlaudRecordingResponse`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingResponse.cs`

- [ ] **Step 1: Write the request class**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;

public class IngestPlaudRecordingRequest : IRequest<IngestPlaudRecordingResponse>
{
    public string PlaudRecordingId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTime PlaudCreatedAt { get; set; }
}
```

- [ ] **Step 2: Write the response class**

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;

public class IngestPlaudRecordingResponse
{
    public bool Success { get; set; } = true;

    public bool Skipped { get; set; }

    public Guid? TranscriptId { get; set; }
}
```

- [ ] **Step 3: Build**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: zero errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/
git commit -m "feat: add IngestPlaudRecording request/response DTOs"
```

---

## Task 14: `IngestPlaudRecordingHandler` — TDD

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Adapters.Plaud;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;
using Anela.Heblo.Domain.Features.MeetingTasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks.UseCases.IngestPlaudRecording;

public class IngestPlaudRecordingHandlerTests
{
    private readonly Mock<IPlaudClient> _plaudClient = new();
    private readonly Mock<IMeetingTaskExtractor> _extractor = new();
    private readonly Mock<IMeetingTranscriptRepository> _repository = new();
    private readonly Mock<ILogger<IngestPlaudRecordingHandler>> _logger = new();

    private IngestPlaudRecordingHandler CreateHandler() =>
        new(_plaudClient.Object, _extractor.Object, _repository.Object, _logger.Object);

    private static IngestPlaudRecordingRequest NewRequest() => new()
    {
        PlaudRecordingId = "rec-001",
        Name = "Team weekly",
        PlaudCreatedAt = new DateTime(2026, 5, 10, 9, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Handle_skips_when_recording_already_exists()
    {
        _repository.Setup(r => r.ExistsByPlaudIdAsync("rec-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var response = await CreateHandler().Handle(NewRequest(), CancellationToken.None);

        response.Skipped.Should().BeTrue();
        response.TranscriptId.Should().BeNull();
        _plaudClient.Verify(c => c.GetTranscriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _plaudClient.Verify(c => c.GetSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _extractor.Verify(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_creates_transcript_with_pending_review_and_proposed_tasks()
    {
        _repository.Setup(r => r.ExistsByPlaudIdAsync("rec-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _plaudClient.Setup(c => c.GetTranscriptAsync("rec-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync("transcript-text");
        _plaudClient.Setup(c => c.GetSummaryAsync("rec-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync("summary-text");
        _extractor.Setup(e => e.ExtractAsync("summary-text", "transcript-text", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>
            {
                new("Send proposal", "Send Q3 proposal", "Jana", new DateTime(2026, 6, 1)),
                new("Book venue", "Find venue", "Pavel", null),
            });

        MeetingTranscript? saved = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .Callback<MeetingTranscript, CancellationToken>((entity, _) => saved = entity)
            .Returns(Task.CompletedTask);

        var response = await CreateHandler().Handle(NewRequest(), CancellationToken.None);

        response.Skipped.Should().BeFalse();
        response.Success.Should().BeTrue();
        response.TranscriptId.Should().NotBeNull();

        saved.Should().NotBeNull();
        saved!.PlaudRecordingId.Should().Be("rec-001");
        saved.Subject.Should().Be("Team weekly");
        saved.RawTranscript.Should().Be("transcript-text");
        saved.Summary.Should().Be("summary-text");
        saved.Status.Should().Be(MeetingTranscriptStatus.PendingReview);
        saved.PlaudCreatedAt.Should().Be(new DateTime(2026, 5, 10, 9, 0, 0, DateTimeKind.Utc));
        saved.Tasks.Should().HaveCount(2);
        saved.Tasks.Should().AllSatisfy(t =>
        {
            t.Status.Should().Be(ProposedTaskStatus.Pending);
            t.IsManuallyAdded.Should().BeFalse();
            t.Id.Should().NotBe(Guid.Empty);
        });
        saved.Tasks[0].Title.Should().Be("Send proposal");
        saved.Tasks[1].DueDate.Should().BeNull();

        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_saves_transcript_with_empty_task_list_when_extractor_returns_empty()
    {
        _repository.Setup(r => r.ExistsByPlaudIdAsync("rec-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _plaudClient.Setup(c => c.GetTranscriptAsync("rec-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync("t");
        _plaudClient.Setup(c => c.GetSummaryAsync("rec-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync("s");
        _extractor.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedTask>());

        MeetingTranscript? saved = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .Callback<MeetingTranscript, CancellationToken>((entity, _) => saved = entity)
            .Returns(Task.CompletedTask);

        var response = await CreateHandler().Handle(NewRequest(), CancellationToken.None);

        response.Skipped.Should().BeFalse();
        saved.Should().NotBeNull();
        saved!.Tasks.Should().BeEmpty();
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Verify tests fail (handler not defined)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IngestPlaudRecordingHandlerTests"
```
Expected: compile failure on `IngestPlaudRecordingHandler`.

- [ ] **Step 3: Implement the handler**

```csharp
using Anela.Heblo.Adapters.Plaud;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;

public sealed class IngestPlaudRecordingHandler
    : IRequestHandler<IngestPlaudRecordingRequest, IngestPlaudRecordingResponse>
{
    private readonly IPlaudClient _plaudClient;
    private readonly IMeetingTaskExtractor _extractor;
    private readonly IMeetingTranscriptRepository _repository;
    private readonly ILogger<IngestPlaudRecordingHandler> _logger;

    public IngestPlaudRecordingHandler(
        IPlaudClient plaudClient,
        IMeetingTaskExtractor extractor,
        IMeetingTranscriptRepository repository,
        ILogger<IngestPlaudRecordingHandler> logger)
    {
        _plaudClient = plaudClient;
        _extractor = extractor;
        _repository = repository;
        _logger = logger;
    }

    public async Task<IngestPlaudRecordingResponse> Handle(
        IngestPlaudRecordingRequest request,
        CancellationToken cancellationToken)
    {
        if (await _repository.ExistsByPlaudIdAsync(request.PlaudRecordingId, cancellationToken))
        {
            _logger.LogDebug(
                "Plaud recording {PlaudRecordingId} already ingested — skipping",
                request.PlaudRecordingId);
            return new IngestPlaudRecordingResponse { Skipped = true };
        }

        var transcript = await _plaudClient.GetTranscriptAsync(request.PlaudRecordingId, cancellationToken);
        var summary = await _plaudClient.GetSummaryAsync(request.PlaudRecordingId, cancellationToken);
        var extracted = await _extractor.ExtractAsync(summary, transcript, cancellationToken);

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
            Tasks = extracted.Select(t => new ProposedTask
            {
                Id = Guid.NewGuid(),
                Title = t.Title,
                Description = t.Description,
                Assignee = t.Assignee,
                DueDate = t.DueDate,
                Status = ProposedTaskStatus.Pending,
                IsManuallyAdded = false,
            }).ToList(),
        };

        await _repository.AddAsync(entity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Ingested Plaud recording {PlaudRecordingId} as transcript {TranscriptId} with {TaskCount} proposed tasks ({Name})",
            request.PlaudRecordingId, entity.Id, entity.Tasks.Count, request.Name);

        return new IngestPlaudRecordingResponse
        {
            Skipped = false,
            Success = true,
            TranscriptId = entity.Id,
        };
    }
}
```

- [ ] **Step 4: Verify tests pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IngestPlaudRecordingHandlerTests"
```
Expected: all 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs backend/test/Anela.Heblo.Tests/Features/MeetingTasks/UseCases/
git commit -m "feat: add IngestPlaudRecordingHandler with dedupe and pending-review persistence"
```

---

## Task 15: `PlaudPollingJob` — TDD

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJobTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Adapters.Plaud;
using Anela.Heblo.Application.Features.MeetingTasks.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks.Infrastructure.Jobs;

public class PlaudPollingJobTests
{
    private readonly Mock<IPlaudClient> _plaud = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();
    private readonly Mock<ILogger<PlaudPollingJob>> _logger = new();

    private PlaudPollingJob CreateJob(PlaudOptions? opts = null, bool enabled = true)
    {
        opts ??= new PlaudOptions();
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-polling", It.IsAny<CancellationToken>()))
            .ReturnsAsync(enabled);

        return new PlaudPollingJob(
            _plaud.Object,
            _mediator.Object,
            _statusChecker.Object,
            Options.Create(opts),
            _logger.Object);
    }

    [Fact]
    public async Task Execute_does_nothing_when_job_is_disabled()
    {
        var job = CreateJob(enabled: false);

        await job.ExecuteAsync();

        _plaud.Verify(p => p.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _mediator.Verify(m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_filters_recordings_missing_transcript_or_summary()
    {
        var job = CreateJob();
        _plaud.Setup(p => p.ListRecentAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary>
            {
                new() { Id = "ready", Name = "ok", CreatedAt = DateTime.UtcNow, HasTranscript = true, HasSummary = true },
                new() { Id = "no-transcript", Name = "x", HasTranscript = false, HasSummary = true },
                new() { Id = "no-summary", Name = "y", HasTranscript = true, HasSummary = false },
                new() { Id = "neither", Name = "z", HasTranscript = false, HasSummary = false },
            });
        _mediator.Setup(m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestPlaudRecordingResponse { Skipped = false, TranscriptId = Guid.NewGuid() });

        await job.ExecuteAsync();

        _mediator.Verify(
            m => m.Send(It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "ready"), It.IsAny<CancellationToken>()),
            Times.Once);
        _mediator.Verify(
            m => m.Send(It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId != "ready"), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_continues_after_per_recording_failure()
    {
        var job = CreateJob();
        _plaud.Setup(p => p.ListRecentAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary>
            {
                new() { Id = "bad", Name = "boom", HasTranscript = true, HasSummary = true },
                new() { Id = "good", Name = "ok", HasTranscript = true, HasSummary = true },
            });
        _mediator.Setup(m => m.Send(It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "bad"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient"));
        _mediator.Setup(m => m.Send(It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "good"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestPlaudRecordingResponse { Skipped = false, TranscriptId = Guid.NewGuid() });

        await job.ExecuteAsync();

        _mediator.Verify(
            m => m.Send(It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "good"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_uses_MaxRecordingAgeDays_from_options()
    {
        var opts = new PlaudOptions { MaxRecordingAgeDays = 14 };
        var job = CreateJob(opts);

        _plaud.Setup(p => p.ListRecentAsync(14, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary>());

        await job.ExecuteAsync();

        _plaud.Verify(p => p.ListRecentAsync(14, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_counts_skipped_responses_separately_from_ingested()
    {
        var job = CreateJob();
        _plaud.Setup(p => p.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary>
            {
                new() { Id = "new-1", HasTranscript = true, HasSummary = true },
                new() { Id = "dup-1", HasTranscript = true, HasSummary = true },
            });
        _mediator.Setup(m => m.Send(It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "new-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestPlaudRecordingResponse { Skipped = false, TranscriptId = Guid.NewGuid() });
        _mediator.Setup(m => m.Send(It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "dup-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestPlaudRecordingResponse { Skipped = true });

        await job.ExecuteAsync();

        // Both dispatched; no exception
        _mediator.Verify(m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void Metadata_uses_kebab_case_name_and_disabled_default()
    {
        var job = CreateJob();

        job.Metadata.JobName.Should().Be("plaud-polling");
        job.Metadata.CronExpression.Should().Be("*/5 * * * *");
        job.Metadata.DefaultIsEnabled.Should().BeFalse();
        job.Metadata.Description.Should().NotBeNullOrWhiteSpace();
        job.Metadata.DisplayName.Should().NotBeNullOrWhiteSpace();
    }
}
```

- [ ] **Step 2: Verify tests fail (job not defined)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PlaudPollingJobTests"
```
Expected: compile failure on `PlaudPollingJob`.

- [ ] **Step 3: Implement the job**

```csharp
using Anela.Heblo.Adapters.Plaud;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MeetingTasks.Infrastructure.Jobs;

public sealed class PlaudPollingJob : IRecurringJob
{
    private readonly IPlaudClient _plaudClient;
    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly PlaudOptions _options;
    private readonly ILogger<PlaudPollingJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "plaud-polling",
        DisplayName = "Plaud — pull meeting transcripts",
        Description = "Polls Plaud CLI every 5 minutes for completed recordings, extracts action items via Claude, and stores them as proposed tasks awaiting human review.",
        CronExpression = "*/5 * * * *",
        DefaultIsEnabled = false,
    };

    public PlaudPollingJob(
        IPlaudClient plaudClient,
        IMediator mediator,
        IRecurringJobStatusChecker statusChecker,
        IOptions<PlaudOptions> options,
        ILogger<PlaudPollingJob> logger)
    {
        _plaudClient = plaudClient;
        _mediator = mediator;
        _statusChecker = statusChecker;
        _options = options.Value;
        _logger = logger;
    }

    [DisableConcurrentExecution(60)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var recordings = await _plaudClient.ListRecentAsync(_options.MaxRecordingAgeDays, cancellationToken);
        var ready = recordings.Where(r => r.HasTranscript && r.HasSummary).ToList();
        _logger.LogInformation(
            "{JobName}: {Total} found, {Ready} ready",
            Metadata.JobName, recordings.Count, ready.Count);

        var ingested = 0;
        var skipped = 0;

        foreach (var rec in ready)
        {
            try
            {
                var response = await _mediator.Send(new IngestPlaudRecordingRequest
                {
                    PlaudRecordingId = rec.Id,
                    Name = rec.Name,
                    PlaudCreatedAt = rec.CreatedAt,
                }, cancellationToken);

                if (response.Skipped)
                {
                    skipped++;
                }
                else
                {
                    ingested++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to ingest Plaud recording {PlaudRecordingId} ({Name}) — continuing with remaining recordings",
                    rec.Id, rec.Name);
            }
        }

        _logger.LogInformation(
            "{JobName} complete: {Ingested} new, {Skipped} already known",
            Metadata.JobName, ingested, skipped);
    }
}
```

- [ ] **Step 4: Verify tests pass and add the Hangfire reference if needed**

If the Application project does not already reference Hangfire (the `[DisableConcurrentExecution]` attribute lives in `Hangfire.Core`), add the package. Check first:

```bash
grep -n "Hangfire" backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
If no Hangfire reference is present, add `<PackageReference Include="Hangfire.Core" Version="1.8.14" />` to the Application csproj (use whatever version the API csproj already pins — check `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`). The Application layer should depend only on `Hangfire.Core`, never on the runtime/server packages.

Then run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PlaudPollingJobTests"
```
Expected: all 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/ backend/test/Anela.Heblo.Tests/Features/MeetingTasks/Infrastructure/
git commit -m "feat: add PlaudPollingJob with kebab-case name, disabled default, per-recording isolation"
```

---

## Task 16: `MeetingTasksModule` and wire into `ApplicationModule`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

- [ ] **Step 1: Write the module**

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.MeetingTasks;

public static class MeetingTasksModule
{
    public static IServiceCollection AddMeetingTasksModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IMeetingTaskExtractor, ClaudeMeetingTaskExtractor>();

        // PlaudPollingJob is auto-discovered via IRecurringJob assembly scan in AddRecurringJobs().
        // IMeetingTranscriptRepository is registered in PersistenceModule.
        // MediatR handlers are auto-registered via AddApplicationServices() assembly scan.
        return services;
    }
}
```

- [ ] **Step 2: Register from `ApplicationModule`**

Open `backend/src/Anela.Heblo.Application/ApplicationModule.cs`. After the line:
```csharp
services.AddLeafletModule(configuration);
```
add the using at the top:
```csharp
using Anela.Heblo.Application.Features.MeetingTasks;
```
and add the registration line right after `AddLeafletModule`:
```csharp
        services.AddMeetingTasksModule(configuration);
```

- [ ] **Step 3: Build whole solution**

Run:
```bash
dotnet build Anela.Heblo.sln
```
Expected: zero errors.

- [ ] **Step 4: Run all tests touched so far**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MeetingTasks"
```
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs backend/src/Anela.Heblo.Application/ApplicationModule.cs
git commit -m "feat: register MeetingTasksModule from ApplicationModule"
```

---

## Task 17: Dockerfile — install `plaud` CLI

**Files:**
- Modify: `Dockerfile` (around line 56 — the runtime `apt-get install` block)

- [ ] **Step 1: Add the CLI install to the runtime stage**

In `Dockerfile`, find the runtime stage block:
```dockerfile
RUN apt-get update && apt-get install -y \
    tzdata \
    curl \
    wget \
    libfontconfig1 \
    libfreetype6 \
    && ln -sf /usr/share/zoneinfo/Europe/Prague /etc/localtime \
    && echo "Europe/Prague" > /etc/timezone \
    && apt-get clean && rm -rf /var/lib/apt/lists/*
```

Replace it with (substitute `X.Y.Z` with the version pinned in Task P2):
```dockerfile
RUN apt-get update && apt-get install -y \
    tzdata \
    curl \
    wget \
    libfontconfig1 \
    libfreetype6 \
    python3 \
    python3-pip \
    ca-certificates \
    && pip3 install --no-cache-dir --break-system-packages "plaud-cli==X.Y.Z" \
    && plaud --version \
    && ln -sf /usr/share/zoneinfo/Europe/Prague /etc/localtime \
    && echo "Europe/Prague" > /etc/timezone \
    && apt-get clean && rm -rf /var/lib/apt/lists/*
```

The `plaud --version` line is the smoke test required by the spec amendment in `arch-review.r1.md` — it fails the build if the CLI is not on PATH. If the chosen distribution channel in Task P2 was **not** pip, replace this block with the equivalent install command for that channel; keep the `plaud --version` smoke test.

- [ ] **Step 2: Build the image locally (verification)**

Run:
```bash
docker build -t anela-heblo:plaud-test .
```
Expected: build succeeds, `plaud --version` prints `X.Y.Z` during the build, image is created. If the docker daemon is unavailable in the executing agent's environment, skip this step and rely on CI; note this in the commit message.

- [ ] **Step 3: Commit**

```bash
git add Dockerfile
git commit -m "feat: install pinned plaud CLI binary in runtime container"
```

---

## Task 18: End-to-end integration smoke (manual gate, no automated test)

**Files:**
- N/A — this task validates the wired-up host without writing code.

This is the only place the assembled pipeline runs against process boundaries. The unit tests cover every seam; what they cannot cover is "the CLI is installed, options are bound, the hosted service writes tokens, the polling job appears in Hangfire." Confirm all four with a manual run.

- [ ] **Step 1: Set local secrets**

Run:
```bash
cd backend/src/Anela.Heblo.API
dotnet user-secrets set "Plaud:TokensJson" "$(cat ~/.plaud/tokens.json)"
```
Expected: secret stored. (Requires you to have already run `plaud login` locally — see `docs/integrations/plaud-cli.md`.)

- [ ] **Step 2: Run the API**

Run:
```bash
dotnet run --project backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```
Expected logs:
- `Wrote Plaud tokens to /home/.../.plaud/tokens.json` (PlaudTokenBootstrapper)
- `Registered recurring job: plaud-polling (PlaudPollingJob)` (RecurringJobDiscoveryService)

- [ ] **Step 3: Verify the job is visible in Hangfire dashboard**

Open `http://localhost:5001/hangfire` (port per `docs/architecture/environments.md`). Navigate to **Recurring Jobs**. Confirm `plaud-polling` is listed and **disabled** (or "off").

- [ ] **Step 4: Trigger the job manually from the dashboard**

Click **Trigger now** on `plaud-polling`. Watch the API log output:
- If the job is disabled in DB config → log line `Job plaud-polling is disabled. Skipping.` Confirms gating works.
- After enabling via the dashboard (set status to Enabled) and triggering again → log lines: `{Total} found, {Ready} ready` and `{Ingested} new, {Skipped} already known`.

- [ ] **Step 5: Verify a transcript landed in DB**

Run:
```bash
psql $HEBLO_DB_URL -c "SELECT \"Id\", \"PlaudRecordingId\", \"Status\", \"Subject\", array_length(string_to_array(\"RawTranscript\", ' '), 1) AS approx_words FROM \"MeetingTranscripts\" ORDER BY \"ReceivedAt\" DESC LIMIT 3;"
```
Expected: at least one row in `PendingReview` status, `RawTranscript` non-empty. (`$HEBLO_DB_URL` — substitute your local Postgres connection.)

- [ ] **Step 6: Stop the API. No commit — this is a manual gate.**

If any step fails, jump back to the relevant earlier task to fix. Do not push forward to PR.

---

## Task 19: Validate before completion (final gate)

**Files:**
- N/A.

- [ ] **Step 1: Format the codebase**

Run:
```bash
dotnet format Anela.Heblo.sln
```
Expected: no diff, or formatting-only diff (review before committing).

If there are diffs, run:
```bash
git add -A && git commit -m "chore: dotnet format"
```

- [ ] **Step 2: Full backend build**

Run:
```bash
dotnet build Anela.Heblo.sln
```
Expected: zero errors, zero new warnings beyond the existing baseline.

- [ ] **Step 3: Full backend test run**

Run:
```bash
dotnet test Anela.Heblo.sln
```
Expected: every test passes. (`MeetingTranscriptRepositoryTests` from the epic must still pass — confirms rebase did not break that.)

- [ ] **Step 4: Verify no secrets were committed**

Run:
```bash
git diff origin/feat/meeting-task-validation-epic...HEAD -- backend/src/Anela.Heblo.API/appsettings*.json
```
Expected: only the non-secret `"Plaud"` block was added; no `"TokensJson"` value, no Anthropic API key changes.

- [ ] **Step 5: Push the branch**

Run:
```bash
git push -u origin feat-meeting-tasks-plaud-cli-adapter-polling-
```
Expected: branch pushed; PR target is `feat/meeting-task-validation-epic`, **not** `main`.

---

## Self-Review Notes (for the planner)

### Spec coverage check

| Spec section | Task(s) |
|---|---|
| FR-1 Plaud CLI Adapter Project | Tasks 1, 3, 4, 5, 7, 9 |
| FR-2 Plaud CLI Output Parser | Task 6 |
| FR-3 Plaud Token Bootstrapper | Task 8 |
| FR-4 Claude Task Extractor | Tasks 11, 12 (arch-review Decision 4 amendment: plain text + manual JSON, code-fence stripping) |
| FR-5 IngestPlaudRecording handler | Tasks 13, 14 |
| FR-6 Hangfire polling job | Task 15 (kebab-case name, required Description, `DisableConcurrentExecution`, no manual transient registration per arch-review Decisions 1 & 6) |
| FR-7 Idempotency | Task 14 (handler `ExistsByPlaudIdAsync` short-circuit; explicit test) |
| FR-8 Error isolation | Task 15 (per-recording try-catch test) |
| FR-9 Configuration & registration | Tasks 10, 16, 17 |
| FR-10 Unit test coverage | Tasks 6, 12, 14, 15 |
| NFR-1 Performance / `[DisableConcurrentExecution]` | Task 15 |
| NFR-2 Security / 0600 file mode | Task 8 |
| NFR-2 Security / `ArgumentList` | Task 7 |
| NFR-3 Reliability / kill-on-timeout | Task 7 |
| NFR-3 Reliability / extractor graceful degradation | Task 12 |
| NFR-4 Observability | Tasks 12, 14, 15 (log statements verified by behavior — counts) |
| Prerequisites (rebase, CLI distribution, fixture) | P1, P2, P3 |
| Arch amendments (parser fixture provenance, plain-text Claude, kebab-case, try-catch, ArgumentList, kill, 0600 perms) | Reflected throughout the task code blocks. |

### Risk re-check

- **Fixture format unknown** — Task P3 forces a real capture before parser TDD; Task 6 tests fail if fixture is wrong, surfacing the issue early rather than silently in prod.
- **CLI install channel unverified** — Task P2 picks one explicitly; Task 17 wires it into Dockerfile with `plaud --version` smoke; the executing agent is told to ask the user if unsure.
- **`GetResponseAsync<T>` would break against `AnthropicChatClient`** — Avoided by Task 12 using plain text + `JsonSerializer.Deserialize`, mirroring arch-review Decision 4.
- **Lifetime double-registration** — Task 16 explicitly omits `AddTransient<PlaudPollingJob>` and includes a comment, mirroring `LeafletModule.cs:28`.
- **Per-recording failure aborting cycle** — Task 15's `Execute_continues_after_per_recording_failure` test enforces the contract.

### Type & method consistency

- `IngestPlaudRecordingRequest.PlaudRecordingId` / `Name` / `PlaudCreatedAt` — used consistently in Tasks 13, 14, 15.
- `MeetingTranscript` shape (from epic): `Id`, `PlaudRecordingId`, `PlaudCreatedAt`, `Subject`, `Summary`, `RawTranscript`, `Status`, `ReceivedAt`, `Tasks` — Task 14 handler sets all of them; `ReviewedAt`/`ReviewedByUser` intentionally untouched.
- `ProposedTask` shape (from epic): `Id`, `MeetingTranscriptId` (set by EF Core when added through `Tasks`), `Title`, `Description`, `Assignee`, `DueDate`, `Status`, `ExternalTaskId` (untouched), `IsManuallyAdded` (set false).
- `IPlaudClient` methods used in Task 14 match the contract in Task 5.
- `PlaudPollingJob` registered as `Scoped` exclusively via `AddRecurringJobs()` (Task 16's `MeetingTasksModule` comment).
- `RecurringJobMetadata` uses required-init properties (`JobName`, `DisplayName`, `Description`, `CronExpression`); Task 15 sets all of them. Matches `LeafletIngestionJob`.
