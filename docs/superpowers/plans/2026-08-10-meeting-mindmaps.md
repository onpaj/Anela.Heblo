# Meeting Mind Maps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A MindMaps module: users create project/workstream mind maps, attach MeetingTasks meetings, and an in-app Claude service evolves the map JSON after each attach — while user-edited nodes stay locked and every update is snapshotted for rollback. Frontend is a React Flow editor.

**Architecture:** New Clean-Architecture vertical slice (`MindMaps`) mirroring the existing `MeetingTasks` module. Map data is one JSON document in a jsonb column. LLM updates run in a Hangfire job that calls a keyed Anthropic `IChatClient`; a deterministic C# "guard pass" enforces node locks and tombstones after every LLM response. Spec: `docs/superpowers/specs/2026-08-10-meeting-mindmap-design.md`.

**Tech Stack:** .NET 8, EF Core + PostgreSQL (jsonb), MediatR, Hangfire, `Microsoft.Extensions.AI.IChatClient` (Anthropic adapter), React 18 + CRA, @tanstack/react-query v5, new FE deps `@xyflow/react` + `dagre`.

## Global Constraints

- Branch: work on `feature/meeting-mindmap` (already checked out). Commit after every task.
- **DTOs are classes, never C# records** (OpenAPI generator constraint). Internal domain types may be records, but this plan uses classes throughout.
- **Every Application `*Response` class MUST inherit `BaseResponse`** (`Anela.Heblo.Application.Shared`) — a reflection contract test fails in CI otherwise.
- New `ErrorCodes` entries REQUIRE: a module-range bucket in `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs` AND Czech translations in `frontend/src/i18n.ts` AND the enum mirror in `frontend/src/types/errors.ts` — or tests fail.
- **FE API hooks use absolute URLs** built as `${apiClient.baseUrl}${relativeUrl}` via the raw-fetch pattern from `frontend/src/api/hooks/useMeetingTasks.ts`. Do NOT use the generated NSwag client for this module.
- FE dates from raw-JSON hooks arrive as ISO **strings** — always wrap in `new Date(value)` before formatting; never call `.getTime()` on the raw value.
- UI copy is Czech (match sidebar entries like "Porady").
- Backend test project uses **xUnit + Moq** (see `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/*`); use plain `Assert.*`, AAA structure, behavior-describing test names.
- Backend build/format gate: `dotnet build` + `dotnet format` from repo root (sln at root). FE gate: `CI=false npm run build` + `npm run lint` in `frontend/` (plain `npx tsc` false-greens — don't rely on it). FE unit tests: `npm test -- --watchAll=false` (react-scripts), never `npx jest`.
- If `dotnet test` hangs at 0% CPU, another worktree is running tests concurrently: build first, then `dotnet test --no-build -p:UseSharedCompilation=false`. An `AccessMatrixGen` crash printed during tests is non-fatal noise.
- No secrets in code or appsettings. The Anthropic API key already exists in Key Vault (`Anthropic:ApiKey`) — nothing new needed.
- Immutability: services that transform `MindMapDocument` return a **new** document (clone-then-build), never mutate their inputs.
- Files ≤ 800 lines; functions ≤ 50 lines where practical.

## File Structure (created/modified across all tasks)

```
backend/src/Anela.Heblo.Domain/Features/MindMaps/
    MindMap.cs  MindMapMeeting.cs  MindMapVersion.cs  MindMapStatus.cs  IMindMapRepository.cs
backend/src/Anela.Heblo.Persistence/MindMaps/
    MindMapConfiguration.cs  MindMapMeetingConfiguration.cs  MindMapVersionConfiguration.cs  MindMapRepository.cs
backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs            (add DbSets)
backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddMindMapsTables.cs   (generated)
backend/src/Anela.Heblo.Application/Features/MindMaps/
    MindMapsModule.cs  MindMapsOptions.cs  MindMapsConstants.cs
    Model/MindMapDocument.cs  Model/MindMapJson.cs  Model/MindMapDocumentValidator.cs
    Services/MindMapGuard.cs  Services/MindMapLockService.cs
    Services/IMindMapUpdater.cs  Services/ClaudeMindMapUpdater.cs  Services/StubMindMapUpdater.cs
    Prompts/mindmap-update-skill.md                                   (embedded resource)
    Infrastructure/Jobs/MindMapUpdateJob.cs
    UseCases/<Name>/{...Request,Handler,Response}.cs                  (9 use cases)
backend/src/Anela.Heblo.API/Controllers/MindMapsController.cs
backend/src/Anela.Heblo.Application/ApplicationModule.cs               (wire module)
backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs               (34XX block)
backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs (keyed client)
access-matrix.json                                                     (feature + menu path + seed roles)
backend/test/Anela.Heblo.Tests/Features/MindMaps/*Tests.cs
backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs                   (34XX bucket)
frontend/src/api/hooks/useMindMaps.ts
frontend/src/components/pages/automation/mindmaps/
    MindMapListPage.tsx  MindMapDetailPage.tsx  MindMapCanvas.tsx  MindMapFlowNode.tsx
    MindMapSidePanel.tsx  mindMapDocument.ts  mindMapFlow.ts  __tests__/*.test.ts
frontend/src/App.tsx  frontend/src/components/Layout/Sidebar.tsx       (route + nav)
frontend/src/types/errors.ts  frontend/src/i18n.ts                     (error mirrors)
frontend/test/e2e/mindmaps/mindmap.spec.ts  frontend/playwright.config.ts
```

---

### Task 1: Map document model, serializer, validator

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Model/MindMapDocument.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Model/MindMapJson.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Model/MindMapDocumentValidator.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapDocumentValidatorTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapJsonTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `MindMapDocument { int SchemaVersion; string RootNodeId; List<MindMapNode> Nodes; List<SuppressedNode> SuppressedNodes }`, `MindMapNode { string Id; string? ParentId; string Title; string? Notes; string Status; string? Owner; string? LockedBy; List<Guid> SourceMeetingIds; NodePosition? Position; bool Collapsed }`, `NodePosition { double X; double Y }`, `SuppressedNode { string Title; string? DeletedBy }`, `MindMapNodeStatus.{Active,Done,Blocked,Idea,All}`, `MindMapJson.Serialize/Deserialize/Clone`, `MindMapDocumentValidator.Validate(doc) → List<string>`. Every later backend task uses these exact names.

- [ ] **Step 1: Write the failing tests**

`backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapJsonTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class MindMapJsonTests
{
    [Fact]
    public void Serialize_UsesCamelCasePropertyNames()
    {
        var doc = new MindMapDocument
        {
            RootNodeId = "root",
            Nodes = new List<MindMapNode> { new() { Id = "root", Title = "Test" } }
        };

        var json = MindMapJson.Serialize(doc);

        Assert.Contains("\"rootNodeId\":\"root\"", json);
        Assert.Contains("\"lockedBy\":null", json);
        Assert.DoesNotContain("RootNodeId", json);
    }

    [Fact]
    public void Deserialize_RoundTripsAllNodeFields()
    {
        var meetingId = Guid.NewGuid();
        var doc = new MindMapDocument
        {
            RootNodeId = "root",
            Nodes = new List<MindMapNode>
            {
                new() { Id = "root", Title = "Projekt" },
                new()
                {
                    Id = "n1", ParentId = "root", Title = "Web", Notes = "poznámka",
                    Status = MindMapNodeStatus.Blocked, Owner = "Ondra", LockedBy = "ondra@anela.cz",
                    SourceMeetingIds = new List<Guid> { meetingId },
                    Position = new NodePosition { X = 12.5, Y = -3 }, Collapsed = true
                }
            },
            SuppressedNodes = new List<SuppressedNode> { new() { Title = "Smazané", DeletedBy = "ondra@anela.cz" } }
        };

        var restored = MindMapJson.Deserialize(MindMapJson.Serialize(doc));

        var n1 = restored.Nodes.Single(n => n.Id == "n1");
        Assert.Equal("Web", n1.Title);
        Assert.Equal("poznámka", n1.Notes);
        Assert.Equal(MindMapNodeStatus.Blocked, n1.Status);
        Assert.Equal("ondra@anela.cz", n1.LockedBy);
        Assert.Equal(meetingId, n1.SourceMeetingIds.Single());
        Assert.Equal(12.5, n1.Position!.X);
        Assert.True(n1.Collapsed);
        Assert.Equal("Smazané", restored.SuppressedNodes.Single().Title);
    }

    [Fact]
    public void Clone_ReturnsIndependentCopy()
    {
        var doc = new MindMapDocument
        {
            RootNodeId = "root",
            Nodes = new List<MindMapNode> { new() { Id = "root", Title = "Original" } }
        };

        var clone = MindMapJson.Clone(doc);
        clone.Nodes[0].Title = "Changed";

        Assert.Equal("Original", doc.Nodes[0].Title);
    }
}
```

`backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapDocumentValidatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class MindMapDocumentValidatorTests
{
    private static MindMapDocument ValidDoc() => new()
    {
        RootNodeId = "root",
        Nodes = new List<MindMapNode>
        {
            new() { Id = "root", Title = "Projekt" },
            new() { Id = "a", ParentId = "root", Title = "Větev A" },
            new() { Id = "b", ParentId = "a", Title = "List B" }
        }
    };

    [Fact]
    public void Validate_ReturnsNoErrors_ForValidTree()
    {
        Assert.Empty(MindMapDocumentValidator.Validate(ValidDoc()));
    }

    [Fact]
    public void Validate_Fails_WhenDocumentHasNoNodes()
    {
        var errors = MindMapDocumentValidator.Validate(new MindMapDocument { RootNodeId = "x" });
        Assert.Contains(errors, e => e.Contains("no nodes"));
    }

    [Fact]
    public void Validate_Fails_OnDuplicateNodeIds()
    {
        var doc = ValidDoc();
        doc.Nodes.Add(new MindMapNode { Id = "a", ParentId = "root", Title = "Dup" });
        Assert.Contains(MindMapDocumentValidator.Validate(doc), e => e.Contains("Duplicate node id 'a'"));
    }

    [Fact]
    public void Validate_Fails_WhenMoreThanOneRoot()
    {
        var doc = ValidDoc();
        doc.Nodes.Add(new MindMapNode { Id = "r2", ParentId = null, Title = "Second root" });
        Assert.Contains(MindMapDocumentValidator.Validate(doc), e => e.Contains("exactly one root"));
    }

    [Fact]
    public void Validate_Fails_WhenRootNodeIdDoesNotMatchParentlessNode()
    {
        var doc = ValidDoc();
        doc.RootNodeId = "a";
        Assert.Contains(MindMapDocumentValidator.Validate(doc), e => e.Contains("does not match"));
    }

    [Fact]
    public void Validate_Fails_OnMissingParentReference()
    {
        var doc = ValidDoc();
        doc.Nodes.Add(new MindMapNode { Id = "orphan", ParentId = "ghost", Title = "Orphan" });
        Assert.Contains(MindMapDocumentValidator.Validate(doc), e => e.Contains("missing parent 'ghost'"));
    }

    [Fact]
    public void Validate_Fails_OnParentCycle()
    {
        var doc = ValidDoc();
        doc.Nodes.Single(n => n.Id == "a").ParentId = "b"; // a -> b -> a
        Assert.Contains(MindMapDocumentValidator.Validate(doc), e => e.Contains("Cycle"));
    }

    [Fact]
    public void Validate_Fails_OnUnknownStatus()
    {
        var doc = ValidDoc();
        doc.Nodes[1].Status = "wip";
        Assert.Contains(MindMapDocumentValidator.Validate(doc), e => e.Contains("unknown status 'wip'"));
    }

    [Fact]
    public void Validate_Fails_OnEmptyTitle()
    {
        var doc = ValidDoc();
        doc.Nodes[1].Title = "  ";
        Assert.Contains(MindMapDocumentValidator.Validate(doc), e => e.Contains("empty title"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run (repo root): `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMap" -p:UseSharedCompilation=false`
Expected: build FAILURE — `MindMapDocument` does not exist.

- [ ] **Step 3: Implement the model**

`backend/src/Anela.Heblo.Application/Features/MindMaps/Model/MindMapDocument.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MindMaps.Model;

/// <summary>
/// The whole mind map document persisted as one jsonb value (camelCase on the wire).
/// Position/Collapsed/LockedBy are UI/system metadata the LLM never writes —
/// MindMapGuard restores them from the previous version after every LLM update.
/// </summary>
public class MindMapDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string RootNodeId { get; set; } = null!;
    public List<MindMapNode> Nodes { get; set; } = new();
    public List<SuppressedNode> SuppressedNodes { get; set; } = new();
}

public class MindMapNode
{
    public string Id { get; set; } = null!;
    public string? ParentId { get; set; }
    public string Title { get; set; } = null!;
    public string? Notes { get; set; }
    public string Status { get; set; } = MindMapNodeStatus.Active;
    public string? Owner { get; set; }
    public string? LockedBy { get; set; }
    public List<Guid> SourceMeetingIds { get; set; } = new();
    public NodePosition? Position { get; set; }
    public bool Collapsed { get; set; }
}

public class NodePosition
{
    public double X { get; set; }
    public double Y { get; set; }
}

public class SuppressedNode
{
    public string Title { get; set; } = null!;
    public string? DeletedBy { get; set; }
}

public static class MindMapNodeStatus
{
    public const string Active = "active";
    public const string Done = "done";
    public const string Blocked = "blocked";
    public const string Idea = "idea";

    public static readonly IReadOnlyList<string> All = new[] { Active, Done, Blocked, Idea };
}
```

`backend/src/Anela.Heblo.Application/Features/MindMaps/Model/MindMapJson.cs`:

```csharp
using System.Text.Json;

namespace Anela.Heblo.Application.Features.MindMaps.Model;

public static class MindMapJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(MindMapDocument document) =>
        JsonSerializer.Serialize(document, Options);

    public static MindMapDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<MindMapDocument>(json, Options)
            ?? throw new JsonException("Mind map document deserialized to null.");

    public static MindMapDocument Clone(MindMapDocument document) =>
        Deserialize(Serialize(document));
}
```

`backend/src/Anela.Heblo.Application/Features/MindMaps/Model/MindMapDocumentValidator.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MindMaps.Model;

public static class MindMapDocumentValidator
{
    public static List<string> Validate(MindMapDocument doc)
    {
        var errors = new List<string>();
        if (doc.Nodes.Count == 0)
        {
            errors.Add("Document has no nodes.");
            return errors;
        }

        var ids = new HashSet<string>();
        foreach (var node in doc.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
                errors.Add("Node with empty id.");
            else if (!ids.Add(node.Id))
                errors.Add($"Duplicate node id '{node.Id}'.");
            if (string.IsNullOrWhiteSpace(node.Title))
                errors.Add($"Node '{node.Id}' has an empty title.");
            if (!MindMapNodeStatus.All.Contains(node.Status))
                errors.Add($"Node '{node.Id}' has unknown status '{node.Status}'.");
        }
        if (errors.Count > 0) return errors;

        var roots = doc.Nodes.Where(n => n.ParentId == null).ToList();
        if (roots.Count != 1)
            errors.Add($"Expected exactly one root node, found {roots.Count}.");
        else if (roots[0].Id != doc.RootNodeId)
            errors.Add($"RootNodeId '{doc.RootNodeId}' does not match the parentless node '{roots[0].Id}'.");

        foreach (var node in doc.Nodes.Where(n => n.ParentId != null))
        {
            if (!ids.Contains(node.ParentId!))
                errors.Add($"Node '{node.Id}' references missing parent '{node.ParentId}'.");
        }
        if (errors.Count > 0) return errors;

        var byId = doc.Nodes.ToDictionary(n => n.Id);
        foreach (var node in doc.Nodes)
        {
            var seen = new HashSet<string>();
            var current = node;
            while (current.ParentId != null)
            {
                if (!seen.Add(current.Id))
                {
                    errors.Add($"Cycle detected at node '{node.Id}'.");
                    break;
                }
                current = byId[current.ParentId];
            }
        }
        return errors;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMap" -p:UseSharedCompilation=false`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MindMaps backend/test/Anela.Heblo.Tests/Features/MindMaps
git commit -m "feat: add mind map document model, serializer and validator"
```

---

### Task 2: Domain entities, EF configuration, migration, repository

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/MindMaps/MindMap.cs`, `MindMapMeeting.cs`, `MindMapVersion.cs`, `MindMapStatus.cs`, `IMindMapRepository.cs`
- Create: `backend/src/Anela.Heblo.Persistence/MindMaps/MindMapConfiguration.cs`, `MindMapMeetingConfiguration.cs`, `MindMapVersionConfiguration.cs`, `MindMapRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` (add DbSets next to the MeetingTasks block at lines 128-131)
- Create (generated): migration `AddMindMapsTables`

**Interfaces:**
- Consumes: `MeetingTranscript` (`Anela.Heblo.Domain.Features.MeetingTasks`).
- Produces: entities below and `IMindMapRepository { GetByIdAsync(Guid, ct) → MindMap? (Meetings incl. transcripts + Versions loaded); GetListAsync(ct) → List<MindMap> (Meetings loaded); AddAsync(MindMap, ct); DeleteAsync(MindMap, ct); SaveChangesAsync(ct) }`. Tasks 7–10 depend on these exact members.

- [ ] **Step 1: Write the domain entities**

`backend/src/Anela.Heblo.Domain/Features/MindMaps/MindMapStatus.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MindMaps;

public enum MindMapStatus
{
    Idle,
    Updating,
    Failed
}
```

`backend/src/Anela.Heblo.Domain/Features/MindMaps/MindMap.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MindMaps;

public class MindMap
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public MindMapStatus Status { get; set; } = MindMapStatus.Idle;
    public string CurrentJson { get; set; } = null!;
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<MindMapMeeting> Meetings { get; set; } = new();
    public List<MindMapVersion> Versions { get; set; } = new();
}
```

`backend/src/Anela.Heblo.Domain/Features/MindMaps/MindMapMeeting.cs`:

```csharp
using Anela.Heblo.Domain.Features.MeetingTasks;

namespace Anela.Heblo.Domain.Features.MindMaps;

public class MindMapMeeting
{
    public Guid Id { get; set; }
    public Guid MindMapId { get; set; }
    public Guid MeetingTranscriptId { get; set; }
    public DateTime AttachedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public MindMap MindMap { get; set; } = null!;
    public MeetingTranscript MeetingTranscript { get; set; } = null!;
}
```

`backend/src/Anela.Heblo.Domain/Features/MindMaps/MindMapVersion.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MindMaps;

public class MindMapVersion
{
    public Guid Id { get; set; }
    public Guid MindMapId { get; set; }
    public int VersionNumber { get; set; }
    public string Json { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public Guid? TriggerMeetingId { get; set; }
    public MindMap MindMap { get; set; } = null!;
}
```

`backend/src/Anela.Heblo.Domain/Features/MindMaps/IMindMapRepository.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MindMaps;

public interface IMindMapRepository
{
    /// <summary>Loads the map including Meetings (with transcripts) and Versions.</summary>
    Task<MindMap?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Loads all maps including Meetings (for counts), newest first.</summary>
    Task<List<MindMap>> GetListAsync(CancellationToken ct = default);

    Task AddAsync(MindMap map, CancellationToken ct = default);

    Task DeleteAsync(MindMap map, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the EF configurations and repository**

`backend/src/Anela.Heblo.Persistence/MindMaps/MindMapConfiguration.cs` (mirrors `MeetingTranscriptConfiguration` style; `AsUtcTimestamp()` comes from `Anela.Heblo.Persistence.Extensions`):

```csharp
using Anela.Heblo.Domain.Features.MindMaps;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MindMaps;

public class MindMapConfiguration : IEntityTypeConfiguration<MindMap>
{
    public void Configure(EntityTypeBuilder<MindMap> builder)
    {
        builder.ToTable("MindMaps", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).IsRequired(false).HasMaxLength(2000);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(20).HasConversion<string>();
        builder.Property(x => x.CurrentJson).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.LastError).IsRequired(false);
        builder.Property(x => x.CreatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.UpdatedAt).IsRequired().AsUtcTimestamp();

        builder.HasMany(x => x.Meetings)
            .WithOne(x => x.MindMap)
            .HasForeignKey(x => x.MindMapId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Versions)
            .WithOne(x => x.MindMap)
            .HasForeignKey(x => x.MindMapId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

`backend/src/Anela.Heblo.Persistence/MindMaps/MindMapMeetingConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.MindMaps;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MindMaps;

public class MindMapMeetingConfiguration : IEntityTypeConfiguration<MindMapMeeting>
{
    public void Configure(EntityTypeBuilder<MindMapMeeting> builder)
    {
        builder.ToTable("MindMapMeetings", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AttachedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.ProcessedAt).IsRequired(false).AsUtcTimestamp();

        builder.HasOne(x => x.MeetingTranscript)
            .WithMany()
            .HasForeignKey(x => x.MeetingTranscriptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.MindMapId, x.MeetingTranscriptId })
            .IsUnique()
            .HasDatabaseName("UX_MindMapMeetings_MindMapId_MeetingTranscriptId");
    }
}
```

`backend/src/Anela.Heblo.Persistence/MindMaps/MindMapVersionConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.MindMaps;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MindMaps;

public class MindMapVersionConfiguration : IEntityTypeConfiguration<MindMapVersion>
{
    public void Configure(EntityTypeBuilder<MindMapVersion> builder)
    {
        builder.ToTable("MindMapVersions", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Json).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.TriggerMeetingId).IsRequired(false);

        builder.HasIndex(x => new { x.MindMapId, x.VersionNumber })
            .IsUnique()
            .HasDatabaseName("UX_MindMapVersions_MindMapId_VersionNumber");
    }
}
```

`backend/src/Anela.Heblo.Persistence/MindMaps/MindMapRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.MindMaps;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.MindMaps;

public class MindMapRepository : IMindMapRepository
{
    private readonly ApplicationDbContext _context;

    public MindMapRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<MindMap?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return _context.MindMaps
            .Include(x => x.Meetings)
                .ThenInclude(m => m.MeetingTranscript)
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<List<MindMap>> GetListAsync(CancellationToken ct = default)
    {
        return _context.MindMaps
            .Include(x => x.Meetings)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(MindMap map, CancellationToken ct = default)
    {
        await _context.MindMaps.AddAsync(map, ct);
    }

    public Task DeleteAsync(MindMap map, CancellationToken ct = default)
    {
        _context.MindMaps.Remove(map);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _context.SaveChangesAsync(ct);
    }
}
```

In `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`, after the Meeting Tasks DbSet block add (and add `using Anela.Heblo.Domain.Features.MindMaps;`):

```csharp
    // Mind Maps module
    public DbSet<MindMap> MindMaps { get; set; } = null!;
    public DbSet<MindMapMeeting> MindMapMeetings { get; set; } = null!;
    public DbSet<MindMapVersion> MindMapVersions { get; set; } = null!;
```

Configurations are picked up automatically by `ApplyConfigurationsFromAssembly` in `OnModelCreating`.

- [ ] **Step 3: Build and generate the migration**

```bash
dotnet build
dotnet ef migrations add AddMindMapsTables --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
```

Expected: migration created under `backend/src/Anela.Heblo.Persistence/Migrations/`. Review it: three tables (`MindMaps`, `MindMapMeetings`, `MindMapVersions`), jsonb columns for `CurrentJson`/`Json`, the two unique indexes, cascade FKs. **Do not run `database update`** — migrations are applied manually per environment (`docs/development/setup.md`).

- [ ] **Step 4: Verify the whole solution still builds and existing tests pass**

Run: `dotnet build && dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMap" --no-build -p:UseSharedCompilation=false`
Expected: PASS (Task 1 tests still green; no repository unit tests here — the repository is thin EF pass-through covered by handler/job tests via mocks and by the E2E flow).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MindMaps backend/src/Anela.Heblo.Persistence
git commit -m "feat: add mind map entities, EF configuration and migration"
```

---

### Task 3: ErrorCodes 34XX block + test bucket + frontend mirrors

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (after the Label Identification 33XX block, ~line 444)
- Modify: `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs` (range buckets ~lines 74-99, assertions ~line 124, sum ~lines 127-135)
- Modify: `frontend/src/types/errors.ts` (string-enum mirror)
- Modify: `frontend/src/i18n.ts` (Czech `errors:` section)

**Interfaces:**
- Produces: `ErrorCodes.MindMapUpdateInProgress = 3401` (Conflict), `ErrorCodes.MindMapMeetingAlreadyAttached = 3402` (BadRequest), `ErrorCodes.MindMapInvalidDocument = 3403` (BadRequest). Tasks 8-10 handlers return these.

- [ ] **Step 1: Add the enum entries**

In `ErrorCodes.cs`, after the 33XX block:

```csharp
    // Mind Maps module errors (34XX)
    [HttpStatusCode(HttpStatusCode.Conflict)]
    MindMapUpdateInProgress = 3401,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    MindMapMeetingAlreadyAttached = 3402,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    MindMapInvalidDocument = 3403,
```

- [ ] **Step 2: Run ErrorHandlingTests to see the bucket test fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ErrorHandlingTests" -p:UseSharedCompilation=false`
Expected: FAIL — the categorized sum no longer equals the enum count.

- [ ] **Step 3: Add the 34XX bucket**

In `ErrorHandlingTests.cs`, next to the other range declarations add:

```csharp
        var mindMapErrors = errorCodes.Where(code => code >= 3400 && code < 3500).ToList(); // 34XX range (Mind Maps)
```

next to the other assertions add:

```csharp
        Assert.True(mindMapErrors.Count > 0, "Should have Mind Maps errors in 34XX range");
```

and add `mindMapErrors.Count` into the `categorizedCount` sum expression.

- [ ] **Step 4: Mirror in the frontend**

`frontend/src/types/errors.ts` is NOT hand-edited — it only re-exports `ErrorCodes` from `frontend/src/api/generated/api-client.ts`, which NSwag generates from the backend OpenAPI schema. Automatic generation on backend build is disabled (`Anela.Heblo.API.csproj:106`, `Condition="false"`), so regenerate explicitly and commit the regenerated client:

```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

Then confirm the three new members landed in the generated enum:

```bash
grep -n "MindMapUpdateInProgress\|MindMapMeetingAlreadyAttached\|MindMapInvalidDocument" frontend/src/api/generated/api-client.ts
```

Expected: three matches inside `export enum ErrorCodes`. (`npm run build`'s `prebuild` script regenerates the same file, so an un-regenerated client would also self-correct at the next frontend build — but committing it here keeps the tree coherent.)

In `frontend/src/i18n.ts`, inside the `errors:` object add:

```typescript
        // Mind Maps module errors (34XX)
        MindMapUpdateInProgress: "Mapa se právě aktualizuje, zkuste to za chvíli",
        MindMapMeetingAlreadyAttached: "Tato porada je už k mapě připojena",
        MindMapInvalidDocument: "Neplatný dokument myšlenkové mapy",
```

- [ ] **Step 5: Run tests and FE build to verify**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ErrorHandlingTests" -p:UseSharedCompilation=false`
Expected: PASS.
Run: `cd frontend && npm run lint`
Expected: no new errors.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs frontend/src/types/errors.ts frontend/src/i18n.ts
git commit -m "feat: add mind maps 34XX error codes with test bucket and czech translations"
```

---

### Task 4: MindMapGuard — the server-side guard pass

This is the heart of the feature: pure C#, no I/O. It takes the previous document and the LLM's proposed document and produces the safe merged document.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapGuard.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapGuardException.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapGuardTests.cs`

**Interfaces:**
- Consumes: `MindMapDocument`, `MindMapJson`, `MindMapDocumentValidator` (Task 1).
- Produces: `MindMapGuard.ApplyLlmUpdate(MindMapDocument previous, MindMapDocument llmResult, Guid meetingId) → MindMapDocument` (new instance; inputs untouched); throws `MindMapGuardException` when the merged result is unrecoverable. Task 7's job calls exactly this.

Guard rules (from the spec):
1. Root node id must not change; result must stay a valid single-root tree.
2. Locked nodes (`LockedBy != null` in previous): `Title`/`Notes`/`Owner` restored if the LLM changed them; re-inserted (under the nearest surviving previous ancestor, else root) if the LLM deleted them. LLM's `Status` change and new children under locked nodes are allowed.
3. Nodes the LLM newly created whose title matches a `SuppressedNodes` tombstone (case-insensitive, trimmed) are removed again; their children re-parent to the removed node's parent.
4. New nodes get server-assigned ids (`Guid.NewGuid().ToString("N")`), children re-pointed, and `meetingId` added to their `SourceMeetingIds`.
5. UI metadata (`Position`, `Collapsed`, `LockedBy`) is carried over from the previous document by node id; new nodes get `Position = null`, `Collapsed = false`, `LockedBy = null`. For existing nodes, `SourceMeetingIds` is the union of the previous value and whatever the LLM returned — the model may add provenance but can never drop it. (Mirrors the client-side rule in Task 5: provenance is enforced in code, never trusted to the caller.)
6. `SuppressedNodes` and `SchemaVersion` always carry over from the previous document verbatim.
7. Final document must pass `MindMapDocumentValidator` — otherwise throw. In addition, malformed LLM output that would crash the merge before that gate — duplicate or empty node ids, or a null `Title` — is rejected up front with `MindMapGuardException`. Do NOT run the full validator on `llmResult` before merging: a document that is structurally invalid on arrival can legitimately become valid after the guard (e.g. the LLM deletes a locked node but leaves a child pointing at it, and rule 2 reinserts the parent).

- [ ] **Step 1: Write the failing tests**

`backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapGuardTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class MindMapGuardTests
{
    private readonly MindMapGuard _guard = new();
    private static readonly Guid MeetingId = Guid.NewGuid();

    private static MindMapDocument Previous() => new()
    {
        RootNodeId = "root",
        Nodes = new List<MindMapNode>
        {
            new() { Id = "root", Title = "Projekt" },
            new()
            {
                Id = "locked", ParentId = "root", Title = "Ruční název", Notes = "ruční poznámka",
                Owner = "Ondra", LockedBy = "ondra@anela.cz", Status = MindMapNodeStatus.Active,
                Position = new NodePosition { X = 100, Y = 50 }, Collapsed = true
            },
            new() { Id = "free", ParentId = "root", Title = "Volný uzel" }
        },
        SuppressedNodes = new List<SuppressedNode> { new() { Title = "Zrušený nápad", DeletedBy = "ondra@anela.cz" } }
    };

    /// <summary>LLM result echoing Previous() structure without UI metadata (as the LLM sees it).</summary>
    private static MindMapDocument LlmEcho() => new()
    {
        RootNodeId = "root",
        Nodes = new List<MindMapNode>
        {
            new() { Id = "root", Title = "Projekt" },
            new() { Id = "locked", ParentId = "root", Title = "Ruční název", Notes = "ruční poznámka", Owner = "Ondra" },
            new() { Id = "free", ParentId = "root", Title = "Volný uzel" }
        }
    };

    [Fact]
    public void ApplyLlmUpdate_RestoresLockedNodeContent_WhenLlmRewroteIt()
    {
        var llm = LlmEcho();
        var lockedInLlm = llm.Nodes.Single(n => n.Id == "locked");
        lockedInLlm.Title = "Přepsáno LLM";
        lockedInLlm.Notes = "jiná poznámka";
        lockedInLlm.Owner = "Nikdo";

        var result = _guard.ApplyLlmUpdate(Previous(), llm, MeetingId);

        var locked = result.Nodes.Single(n => n.Id == "locked");
        Assert.Equal("Ruční název", locked.Title);
        Assert.Equal("ruční poznámka", locked.Notes);
        Assert.Equal("Ondra", locked.Owner);
    }

    [Fact]
    public void ApplyLlmUpdate_AllowsStatusChangeOnLockedNode()
    {
        var llm = LlmEcho();
        llm.Nodes.Single(n => n.Id == "locked").Status = MindMapNodeStatus.Done;

        var result = _guard.ApplyLlmUpdate(Previous(), llm, MeetingId);

        Assert.Equal(MindMapNodeStatus.Done, result.Nodes.Single(n => n.Id == "locked").Status);
    }

    [Fact]
    public void ApplyLlmUpdate_ReinsertsDeletedLockedNode()
    {
        var llm = LlmEcho();
        llm.Nodes.RemoveAll(n => n.Id == "locked");

        var result = _guard.ApplyLlmUpdate(Previous(), llm, MeetingId);

        var locked = result.Nodes.Single(n => n.Id == "locked");
        Assert.Equal("root", locked.ParentId);
        Assert.Equal("Ruční název", locked.Title);
        Assert.Equal("ondra@anela.cz", locked.LockedBy);
    }

    [Fact]
    public void ApplyLlmUpdate_ReinsertsLockedNodeUnderNearestSurvivingAncestor()
    {
        var previous = Previous();
        // deep chain: root -> free -> mid -> deepLocked
        previous.Nodes.Add(new MindMapNode { Id = "mid", ParentId = "free", Title = "Mezi" });
        previous.Nodes.Add(new MindMapNode
        {
            Id = "deepLocked", ParentId = "mid", Title = "Hluboký", LockedBy = "ondra@anela.cz"
        });
        var llm = LlmEcho(); // LLM dropped both "mid" and "deepLocked"; "free" survives

        var result = _guard.ApplyLlmUpdate(previous, llm, MeetingId);

        Assert.Equal("free", result.Nodes.Single(n => n.Id == "deepLocked").ParentId);
    }

    [Fact]
    public void ApplyLlmUpdate_RemovesRecreatedSuppressedNode_AndReparentsItsChildren()
    {
        var llm = LlmEcho();
        llm.Nodes.Add(new MindMapNode { Id = "new-1", ParentId = "root", Title = "Zrušený nápad" });
        llm.Nodes.Add(new MindMapNode { Id = "new-2", ParentId = "new-1", Title = "Dítě zombie uzlu" });

        var result = _guard.ApplyLlmUpdate(Previous(), llm, MeetingId);

        Assert.DoesNotContain(result.Nodes, n => n.Title == "Zrušený nápad");
        Assert.Equal("root", result.Nodes.Single(n => n.Title == "Dítě zombie uzlu").ParentId);
    }

    [Fact]
    public void ApplyLlmUpdate_AssignsServerIdsAndMeetingSource_ToNewNodes()
    {
        var llm = LlmEcho();
        llm.Nodes.Add(new MindMapNode { Id = "new-1", ParentId = "free", Title = "Nová větev" });
        llm.Nodes.Add(new MindMapNode { Id = "new-2", ParentId = "new-1", Title = "Nový list" });

        var result = _guard.ApplyLlmUpdate(Previous(), llm, MeetingId);

        var branch = result.Nodes.Single(n => n.Title == "Nová větev");
        var leaf = result.Nodes.Single(n => n.Title == "Nový list");
        Assert.NotEqual("new-1", branch.Id);
        Assert.Equal(32, branch.Id.Length); // Guid "N" format
        Assert.Equal(branch.Id, leaf.ParentId);
        Assert.Contains(MeetingId, branch.SourceMeetingIds);
        Assert.Null(branch.Position);
        Assert.Null(branch.LockedBy);
    }

    [Fact]
    public void ApplyLlmUpdate_CarriesOverUiMetadataByNodeId()
    {
        var result = _guard.ApplyLlmUpdate(Previous(), LlmEcho(), MeetingId);

        var locked = result.Nodes.Single(n => n.Id == "locked");
        Assert.Equal(100, locked.Position!.X);
        Assert.True(locked.Collapsed);
        Assert.Equal("ondra@anela.cz", locked.LockedBy);
    }

    [Fact]
    public void ApplyLlmUpdate_CarriesOverSuppressedNodesVerbatim()
    {
        var llm = LlmEcho();
        llm.SuppressedNodes = new List<SuppressedNode>(); // LLM tried to clear tombstones

        var result = _guard.ApplyLlmUpdate(Previous(), llm, MeetingId);

        Assert.Equal("Zrušený nápad", result.SuppressedNodes.Single().Title);
    }

    [Fact]
    public void ApplyLlmUpdate_Throws_WhenRootIdChanged()
    {
        var llm = LlmEcho();
        llm.RootNodeId = "free";
        llm.Nodes.Single(n => n.Id == "root").ParentId = "free";
        llm.Nodes.Single(n => n.Id == "free").ParentId = null;

        Assert.Throws<MindMapGuardException>(() => _guard.ApplyLlmUpdate(Previous(), llm, MeetingId));
    }

    [Fact]
    public void ApplyLlmUpdate_Throws_WhenMergedResultIsInvalid()
    {
        var llm = LlmEcho();
        llm.Nodes.Single(n => n.Id == "free").ParentId = "ghost";

        Assert.Throws<MindMapGuardException>(() => _guard.ApplyLlmUpdate(Previous(), llm, MeetingId));
    }

    [Fact]
    public void ApplyLlmUpdate_DoesNotMutateInputs()
    {
        var previous = Previous();
        var llm = LlmEcho();
        llm.Nodes.Add(new MindMapNode { Id = "new-1", ParentId = "root", Title = "Nový" });

        _guard.ApplyLlmUpdate(previous, llm, MeetingId);

        Assert.Equal(3, previous.Nodes.Count);
        Assert.Equal("new-1", llm.Nodes.Single(n => n.Title == "Nový").Id);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMapGuard" -p:UseSharedCompilation=false`
Expected: build FAILURE — `MindMapGuard` does not exist.

- [ ] **Step 3: Implement the guard**

`backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapGuardException.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MindMaps.Services;

public class MindMapGuardException : Exception
{
    public MindMapGuardException(string message) : base(message) { }
}
```

`backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapGuard.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;

namespace Anela.Heblo.Application.Features.MindMaps.Services;

/// <summary>
/// Deterministic post-processing of every LLM map update. Locks are enforced here,
/// in code — never by trusting the model. Works on clones; inputs are never mutated.
/// </summary>
public class MindMapGuard
{
    public MindMapDocument ApplyLlmUpdate(MindMapDocument previous, MindMapDocument llmResult, Guid meetingId)
    {
        if (llmResult.RootNodeId != previous.RootNodeId)
            throw new MindMapGuardException(
                $"LLM changed the root node id from '{previous.RootNodeId}' to '{llmResult.RootNodeId}'.");

        var result = MindMapJson.Clone(llmResult);
        var prevById = previous.Nodes.ToDictionary(n => n.Id);

        RemoveRecreatedSuppressedNodes(result, previous, prevById);
        EnforceLockedNodes(result, previous, prevById);
        AssignServerIdsToNewNodes(result, prevById, meetingId);
        MergeUiMetadata(result, prevById);

        result.SchemaVersion = previous.SchemaVersion;
        result.SuppressedNodes = previous.SuppressedNodes
            .Select(s => new SuppressedNode { Title = s.Title, DeletedBy = s.DeletedBy })
            .ToList();

        var errors = MindMapDocumentValidator.Validate(result);
        if (errors.Count > 0)
            throw new MindMapGuardException($"Guarded document failed validation: {string.Join(" ", errors)}");

        return result;
    }

    private static void RemoveRecreatedSuppressedNodes(
        MindMapDocument result, MindMapDocument previous, Dictionary<string, MindMapNode> prevById)
    {
        var suppressedTitles = new HashSet<string>(
            previous.SuppressedNodes.Select(s => s.Title.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var recreated = result.Nodes
            .Where(n => !prevById.ContainsKey(n.Id) && suppressedTitles.Contains(n.Title.Trim()))
            .ToList();

        foreach (var node in recreated)
        {
            foreach (var child in result.Nodes.Where(c => c.ParentId == node.Id))
                child.ParentId = node.ParentId;
            result.Nodes.Remove(node);
        }
    }

    private static void EnforceLockedNodes(
        MindMapDocument result, MindMapDocument previous, Dictionary<string, MindMapNode> prevById)
    {
        var resultById = result.Nodes.ToDictionary(n => n.Id);

        foreach (var prev in previous.Nodes.Where(n => n.LockedBy != null))
        {
            if (resultById.TryGetValue(prev.Id, out var node))
            {
                node.Title = prev.Title;
                node.Notes = prev.Notes;
                node.Owner = prev.Owner;
            }
            else
            {
                var reinserted = new MindMapNode
                {
                    Id = prev.Id,
                    ParentId = FindNearestSurvivingAncestor(prev, prevById, resultById, result.RootNodeId),
                    Title = prev.Title,
                    Notes = prev.Notes,
                    Status = prev.Status,
                    Owner = prev.Owner,
                    LockedBy = prev.LockedBy,
                    SourceMeetingIds = prev.SourceMeetingIds.ToList()
                };
                result.Nodes.Add(reinserted);
                resultById[prev.Id] = reinserted;
            }
        }
    }

    private static string FindNearestSurvivingAncestor(
        MindMapNode node,
        Dictionary<string, MindMapNode> prevById,
        Dictionary<string, MindMapNode> resultById,
        string rootId)
    {
        var parentId = node.ParentId;
        var hops = 0;
        while (parentId != null && hops++ <= prevById.Count)
        {
            if (resultById.ContainsKey(parentId)) return parentId;
            parentId = prevById.TryGetValue(parentId, out var parent) ? parent.ParentId : null;
        }
        return rootId;
    }

    private static void AssignServerIdsToNewNodes(
        MindMapDocument result, Dictionary<string, MindMapNode> prevById, Guid meetingId)
    {
        var idMap = new Dictionary<string, string>();
        foreach (var node in result.Nodes.Where(n => !prevById.ContainsKey(n.Id)))
        {
            var newId = Guid.NewGuid().ToString("N");
            idMap[node.Id] = newId;
            node.Id = newId;
            if (!node.SourceMeetingIds.Contains(meetingId))
                node.SourceMeetingIds.Add(meetingId);
        }

        foreach (var node in result.Nodes)
        {
            if (node.ParentId != null && idMap.TryGetValue(node.ParentId, out var mapped))
                node.ParentId = mapped;
        }
    }

    private static void MergeUiMetadata(MindMapDocument result, Dictionary<string, MindMapNode> prevById)
    {
        foreach (var node in result.Nodes)
        {
            if (prevById.TryGetValue(node.Id, out var prev))
            {
                node.Position = prev.Position == null
                    ? null
                    : new NodePosition { X = prev.Position.X, Y = prev.Position.Y };
                node.Collapsed = prev.Collapsed;
                node.LockedBy = prev.LockedBy;
            }
            else
            {
                node.Position = null;
                node.Collapsed = false;
                node.LockedBy = null;
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMapGuard" -p:UseSharedCompilation=false`
Expected: all 11 PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MindMaps/Services backend/test/Anela.Heblo.Tests/Features/MindMaps
git commit -m "feat: add mind map guard pass enforcing locks, tombstones and metadata merge"
```

---

### Task 5: MindMapLockService — auto-lock diff on user save

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapLockService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapLockServiceTests.cs`

**Interfaces:**
- Consumes: `MindMapDocument`, `MindMapJson` (Task 1).
- Produces: `MindMapLockService.ApplyUserEdit(MindMapDocument current, MindMapDocument submitted, string userEmail) → MindMapDocument`. Caller (Task 10's SaveDocument handler) validates `submitted` with `MindMapDocumentValidator` and checks `submitted.RootNodeId == current.RootNodeId` BEFORE calling; the service assumes structurally valid input.

Rules:
1. Existing node with changed `Title`/`Notes`/`Owner` → `LockedBy = userEmail`. Unchanged content → `LockedBy` preserved from `current` (the client can never set, clear, or spoof locks). `Status`, `Position`, `Collapsed` changes never lock.
2. Node ids not present in `current` are user-added → server-assigned id (`Guid.NewGuid().ToString("N")`), children re-pointed, `LockedBy = userEmail`, `SourceMeetingIds` empty.
3. `SourceMeetingIds` is always taken from `current` for existing nodes (client cannot rewrite provenance).
4. Nodes present in `current` but missing from `submitted` are user-deleted → tombstone `{ Title, DeletedBy = userEmail }` appended to `current.SuppressedNodes` (client-sent `SuppressedNodes` are ignored).
5. `SchemaVersion` carries over from `current`. Result is a new document; inputs untouched.

- [ ] **Step 1: Write the failing tests**

`backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapLockServiceTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class MindMapLockServiceTests
{
    private const string UserEmail = "ondra@anela.cz";
    private readonly MindMapLockService _service = new();

    private static MindMapDocument Current() => new()
    {
        RootNodeId = "root",
        Nodes = new List<MindMapNode>
        {
            new() { Id = "root", Title = "Projekt" },
            new()
            {
                Id = "a", ParentId = "root", Title = "Větev A",
                SourceMeetingIds = new List<Guid> { Guid.Parse("11111111-1111-1111-1111-111111111111") }
            },
            new() { Id = "b", ParentId = "root", Title = "Větev B", LockedBy = "jina@anela.cz" }
        }
    };

    [Fact]
    public void ApplyUserEdit_LocksNode_WhenTitleChanged()
    {
        var submitted = MindMapJson.Clone(Current());
        submitted.Nodes.Single(n => n.Id == "a").Title = "Přejmenováno";

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        Assert.Equal(UserEmail, result.Nodes.Single(n => n.Id == "a").LockedBy);
    }

    [Fact]
    public void ApplyUserEdit_DoesNotLock_OnPositionStatusOrCollapseChange()
    {
        var submitted = MindMapJson.Clone(Current());
        var a = submitted.Nodes.Single(n => n.Id == "a");
        a.Position = new NodePosition { X = 5, Y = 5 };
        a.Collapsed = true;
        a.Status = MindMapNodeStatus.Done;

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        Assert.Null(result.Nodes.Single(n => n.Id == "a").LockedBy);
        Assert.Equal(MindMapNodeStatus.Done, result.Nodes.Single(n => n.Id == "a").Status);
        Assert.Equal(5, result.Nodes.Single(n => n.Id == "a").Position!.X);
    }

    [Fact]
    public void ApplyUserEdit_PreservesExistingLock_AndIgnoresClientLockTampering()
    {
        var submitted = MindMapJson.Clone(Current());
        submitted.Nodes.Single(n => n.Id == "b").LockedBy = null;   // client tried to unlock
        submitted.Nodes.Single(n => n.Id == "a").LockedBy = "spoof@anela.cz"; // client tried to lock as someone else

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        Assert.Equal("jina@anela.cz", result.Nodes.Single(n => n.Id == "b").LockedBy);
        Assert.Null(result.Nodes.Single(n => n.Id == "a").LockedBy);
    }

    [Fact]
    public void ApplyUserEdit_AssignsServerIdAndLock_ToUserAddedNodes()
    {
        var submitted = MindMapJson.Clone(Current());
        submitted.Nodes.Add(new MindMapNode { Id = "tmp-1", ParentId = "a", Title = "Nový uzel" });
        submitted.Nodes.Add(new MindMapNode { Id = "tmp-2", ParentId = "tmp-1", Title = "Vnořený" });

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        var added = result.Nodes.Single(n => n.Title == "Nový uzel");
        var nested = result.Nodes.Single(n => n.Title == "Vnořený");
        Assert.Equal(32, added.Id.Length);
        Assert.Equal(added.Id, nested.ParentId);
        Assert.Equal(UserEmail, added.LockedBy);
        Assert.Empty(added.SourceMeetingIds);
    }

    [Fact]
    public void ApplyUserEdit_PreservesSourceMeetingIdsFromCurrent()
    {
        var submitted = MindMapJson.Clone(Current());
        submitted.Nodes.Single(n => n.Id == "a").SourceMeetingIds = new List<Guid>(); // client wiped provenance

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        Assert.Single(result.Nodes.Single(n => n.Id == "a").SourceMeetingIds);
    }

    [Fact]
    public void ApplyUserEdit_TombstonesDeletedNodes()
    {
        var submitted = MindMapJson.Clone(Current());
        submitted.Nodes.RemoveAll(n => n.Id == "a");

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        var tombstone = result.SuppressedNodes.Single();
        Assert.Equal("Větev A", tombstone.Title);
        Assert.Equal(UserEmail, tombstone.DeletedBy);
    }

    [Fact]
    public void ApplyUserEdit_IgnoresClientSuppressedNodes_AndKeepsExistingOnes()
    {
        var current = Current();
        current.SuppressedNodes.Add(new SuppressedNode { Title = "Staré", DeletedBy = "jina@anela.cz" });
        var submitted = MindMapJson.Clone(current);
        submitted.SuppressedNodes = new List<SuppressedNode> { new() { Title = "Podvržené" } };

        var result = _service.ApplyUserEdit(current, submitted, UserEmail);

        Assert.Equal("Staré", result.SuppressedNodes.Single().Title);
    }

    [Fact]
    public void ApplyUserEdit_DoesNotMutateInputs()
    {
        var current = Current();
        var submitted = MindMapJson.Clone(current);
        submitted.Nodes.Single(n => n.Id == "a").Title = "Změna";

        _service.ApplyUserEdit(current, submitted, UserEmail);

        Assert.Equal("Větev A", current.Nodes.Single(n => n.Id == "a").Title);
        Assert.Null(submitted.Nodes.Single(n => n.Id == "a").LockedBy);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMapLockService" -p:UseSharedCompilation=false`
Expected: build FAILURE — `MindMapLockService` does not exist.

- [ ] **Step 3: Implement the service**

`backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapLockService.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;

namespace Anela.Heblo.Application.Features.MindMaps.Services;

/// <summary>
/// Applies a user-submitted document over the current one: content edits auto-lock
/// the node, deletions become tombstones, and locks/provenance can never be set
/// or cleared by the client. Assumes the caller already validated the submitted
/// document and that the root id is unchanged.
/// </summary>
public class MindMapLockService
{
    public MindMapDocument ApplyUserEdit(MindMapDocument current, MindMapDocument submitted, string userEmail)
    {
        var result = MindMapJson.Clone(submitted);
        var currentById = current.Nodes.ToDictionary(n => n.Id);
        var submittedIds = new HashSet<string>(result.Nodes.Select(n => n.Id));

        var idMap = new Dictionary<string, string>();
        foreach (var node in result.Nodes)
        {
            if (currentById.TryGetValue(node.Id, out var existing))
            {
                var contentChanged = node.Title != existing.Title
                    || node.Notes != existing.Notes
                    || node.Owner != existing.Owner;
                node.LockedBy = contentChanged ? userEmail : existing.LockedBy;
                node.SourceMeetingIds = existing.SourceMeetingIds.ToList();
            }
            else
            {
                var newId = Guid.NewGuid().ToString("N");
                idMap[node.Id] = newId;
                node.Id = newId;
                node.LockedBy = userEmail;
                node.SourceMeetingIds = new List<Guid>();
            }
        }

        foreach (var node in result.Nodes)
        {
            if (node.ParentId != null && idMap.TryGetValue(node.ParentId, out var mapped))
                node.ParentId = mapped;
        }

        var tombstones = current.Nodes
            .Where(n => !submittedIds.Contains(n.Id))
            .Select(n => new SuppressedNode { Title = n.Title, DeletedBy = userEmail });

        result.SuppressedNodes = current.SuppressedNodes
            .Select(s => new SuppressedNode { Title = s.Title, DeletedBy = s.DeletedBy })
            .Concat(tombstones)
            .ToList();
        result.SchemaVersion = current.SchemaVersion;
        return result;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMapLockService" -p:UseSharedCompilation=false`
Expected: all 8 PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapLockService.cs backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapLockServiceTests.cs
git commit -m "feat: add auto-lock diff service for user mind map edits"
```

---

### Task 6: LLM updater — prompt file, Claude implementation, stub, options, keyed client

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Prompts/mindmap-update-skill.md`
- Modify: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` (embed the prompt)
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/IMindMapUpdater.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapUpdateException.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/ClaudeMindMapUpdater.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/StubMindMapUpdater.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/MindMapsOptions.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/MindMapsConstants.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs` (second keyed client)
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/ClaudeMindMapUpdaterTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/StubMindMapUpdaterTests.cs`

**Interfaces:**
- Consumes: Task 1 model; `MeetingTranscript` (Subject, Summary, RawTranscript, PlaudCreatedAt); `Microsoft.Extensions.AI.IChatClient`.
- Produces: `IMindMapUpdater { Task<MindMapDocument> UpdateAsync(MindMapDocument current, MeetingTranscript meeting, CancellationToken ct = default) }` (throws `MindMapUpdateException` after 2 failed attempts), `MindMapsOptions { SectionName = "MindMaps"; bool UseStubUpdater; int UpdaterMaxOutputTokens = 16384 }`, `MindMapsConstants.UpdaterChatClientKey = "mindmap-updater"`, adapter const `AnthropicAdapterServiceCollectionExtensions.MindMapUpdaterClientKey = "mindmap-updater"`. Task 7 job and Task 8 module registration depend on these.

- [ ] **Step 1: Write the failing tests**

`backend/test/Anela.Heblo.Tests/Features/MindMaps/ClaudeMindMapUpdaterTests.cs` — fake the chat client with Moq on `IChatClient.GetResponseAsync`:

```csharp
using Anela.Heblo.Application.Features.MindMaps;
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class ClaudeMindMapUpdaterTests
{
    private static MindMapDocument Current() => new()
    {
        RootNodeId = "root",
        Nodes = new List<MindMapNode> { new() { Id = "root", Title = "Projekt" } }
    };

    private static MeetingTranscript Meeting() => new()
    {
        Id = Guid.NewGuid(),
        PlaudRecordingId = "rec-1",
        Subject = "Porada o webu",
        Summary = "Souhrn porady",
        RawTranscript = "Celý přepis…",
        PlaudCreatedAt = new DateTime(2026, 8, 1)
    };

    private static ClaudeMindMapUpdater CreateSut(Mock<IChatClient> chatClient) => new(
        chatClient.Object,
        Options.Create(new MindMapsOptions()),
        NullLogger<ClaudeMindMapUpdater>.Instance);

    private static Mock<IChatClient> ChatClientReturning(params string[] texts)
    {
        var mock = new Mock<IChatClient>();
        var queue = new Queue<string>(texts);
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, queue.Dequeue())));
        return mock;
    }

    [Fact]
    public async Task UpdateAsync_ReturnsParsedDocument_OnValidJson()
    {
        var valid = """{"rootNodeId":"root","nodes":[{"id":"root","parentId":null,"title":"Projekt","status":"active"},{"id":"new-1","parentId":"root","title":"Web","status":"active"}]}""";
        var chatClient = ChatClientReturning(valid);

        var result = await CreateSut(chatClient).UpdateAsync(Current(), Meeting());

        Assert.Equal(2, result.Nodes.Count);
        Assert.Contains(result.Nodes, n => n.Title == "Web");
    }

    [Fact]
    public async Task UpdateAsync_StripsMarkdownCodeFence()
    {
        var fenced = "```json\n{\"rootNodeId\":\"root\",\"nodes\":[{\"id\":\"root\",\"parentId\":null,\"title\":\"Projekt\",\"status\":\"active\"}]}\n```";
        var chatClient = ChatClientReturning(fenced);

        var result = await CreateSut(chatClient).UpdateAsync(Current(), Meeting());

        Assert.Single(result.Nodes);
    }

    [Fact]
    public async Task UpdateAsync_RetriesOnce_WhenFirstResponseIsMalformed()
    {
        var valid = """{"rootNodeId":"root","nodes":[{"id":"root","parentId":null,"title":"Projekt","status":"active"}]}""";
        var chatClient = ChatClientReturning("not json at all", valid);

        var result = await CreateSut(chatClient).UpdateAsync(Current(), Meeting());

        Assert.Single(result.Nodes);
        chatClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task UpdateAsync_RetriesOnce_WhenDocumentFailsValidation()
    {
        var invalid = """{"rootNodeId":"root","nodes":[{"id":"root","parentId":null,"title":"Projekt","status":"active"},{"id":"a","parentId":"ghost","title":"Sirotek","status":"active"}]}""";
        var valid = """{"rootNodeId":"root","nodes":[{"id":"root","parentId":null,"title":"Projekt","status":"active"}]}""";
        var chatClient = ChatClientReturning(invalid, valid);

        var result = await CreateSut(chatClient).UpdateAsync(Current(), Meeting());

        Assert.Single(result.Nodes);
    }

    [Fact]
    public async Task UpdateAsync_Throws_AfterTwoInvalidResponses()
    {
        var chatClient = ChatClientReturning("garbage", "more garbage");

        await Assert.ThrowsAsync<MindMapUpdateException>(
            () => CreateSut(chatClient).UpdateAsync(Current(), Meeting()));
    }

    [Fact]
    public async Task UpdateAsync_SendsLockedFlagAndTombstones_NotUiMetadata()
    {
        var current = Current();
        current.Nodes.Add(new MindMapNode
        {
            Id = "l1", ParentId = "root", Title = "Zamčený", LockedBy = "ondra@anela.cz",
            Position = new NodePosition { X = 1, Y = 2 }
        });
        current.SuppressedNodes.Add(new SuppressedNode { Title = "Smazaný nápad" });
        string? sentUserMessage = null;
        var valid = """{"rootNodeId":"root","nodes":[{"id":"root","parentId":null,"title":"Projekt","status":"active"},{"id":"l1","parentId":"root","title":"Zamčený","status":"active"}]}""";
        var chatClient = new Mock<IChatClient>();
        chatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((msgs, _, _) =>
                sentUserMessage = msgs.First(m => m.Role == ChatRole.User).Text)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, valid)));

        await CreateSut(chatClient).UpdateAsync(current, Meeting());

        Assert.Contains("\"locked\":true", sentUserMessage);
        Assert.Contains("Smazaný nápad", sentUserMessage);
        Assert.DoesNotContain("position", sentUserMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lockedBy", sentUserMessage!, StringComparison.OrdinalIgnoreCase);
    }
}
```

Note: if `ChatResponse`'s constructor differs in the installed `Microsoft.Extensions.AI` version, mirror however `ClaudeMeetingTaskExtractor`'s tests fake responses (see `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/`) — the assertion targets stay the same.

`backend/test/Anela.Heblo.Tests/Features/MindMaps/StubMindMapUpdaterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class StubMindMapUpdaterTests
{
    [Fact]
    public async Task UpdateAsync_AddsOneDeterministicNodeUnderRoot()
    {
        var current = new MindMapDocument
        {
            RootNodeId = "root",
            Nodes = new List<MindMapNode> { new() { Id = "root", Title = "Projekt" } }
        };
        var meeting = new MeetingTranscript
        {
            Id = Guid.NewGuid(), PlaudRecordingId = "r", Subject = "Týmová porada",
            Summary = "s", RawTranscript = "t"
        };

        var result = await new StubMindMapUpdater().UpdateAsync(current, meeting);

        var added = result.Nodes.Single(n => n.Id != "root");
        Assert.Equal("root", added.ParentId);
        Assert.Equal("Porada: Týmová porada", added.Title);
        Assert.Equal(MindMapNodeStatus.Idea, added.Status);
        Assert.Single(current.Nodes); // input untouched
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMapUpdater" -p:UseSharedCompilation=false`
Expected: build FAILURE — types do not exist.

- [ ] **Step 3: Write the prompt file (the "skill")**

`backend/src/Anela.Heblo.Application/Features/MindMaps/Prompts/mindmap-update-skill.md`:

```markdown
# Role

Jsi správce projektové myšlenkové mapy firmy Anela (kosmetika). Mapa zachycuje
projekty a pracovní oblasti (workstreamy) napříč sérií porad: iniciativy, jejich
podvětve, stav a vlastníky. Po každé poradě mapu aktualizuješ podle přepisu.

# Vstup

Dostaneš JSON aktuální mapy (uzly: id, parentId, title, notes, status, owner,
locked, sourceMeetingIds), seznam `doNotRecreate` (názvy uzlů, které uživatel
smazal) a text nové porady (předmět, souhrn, přepis).

# Pravidla aktualizace

1. **Zachovej id existujících uzlů beze změny.** Nikdy nerecykluj id.
2. Nové uzly označ id ve tvaru `new-1`, `new-2`, … Server jim přidělí finální id.
3. Uzly s `"locked": true` upravil uživatel ručně: **nesmíš měnit jejich title,
   notes ani owner a nesmíš je smazat.** Smíš jim změnit `status` a přidávat pod
   ně děti.
4. Nikdy nevytvářej uzel s názvem ze seznamu `doNotRecreate`.
5. Mapa je strom: každý uzel kromě kořene má `parentId`. Kořen (`rootNodeId`)
   nesmíš měnit ani přejmenovat, pokud není zamčený — kořen reprezentuje celou mapu.
6. Uzly, které porada nezmiňuje, ponech beze změny. Odstraňuj pouze uzly, které
   porada výslovně zrušila (a nejsou zamčené).
7. Aktualizuj `status` podle obsahu porady: `active` (běží), `done` (hotovo),
   `blocked` (blokováno), `idea` (nápad/návrh).
8. `owner` vyplň jménem z porady, pokud zaznělo, jinak ponech.
9. Do `notes` piš stručná fakta z porad (rozhodnutí, termíny, kontext). Piš česky.
10. `sourceMeetingIds` u existujících uzlů ponech; u nových uzlů pole vynech.
11. Drž mapu přehlednou: slučuj duplicitní témata, preferuj 2–4 úrovně hloubky.

# Výstup

Vrať POUZE validní JSON (bez markdownu, bez komentářů) ve tvaru:

{"rootNodeId": "...", "nodes": [{"id": "...", "parentId": null, "title": "...",
"notes": "...", "status": "active", "owner": "...", "sourceMeetingIds": []}]}

Statusy: active | done | blocked | idea. Žádné jiné hodnoty.
```

In `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` add (inside a new or existing `<ItemGroup>`):

```xml
  <ItemGroup>
    <EmbeddedResource Include="Features\MindMaps\Prompts\mindmap-update-skill.md" />
  </ItemGroup>
```

- [ ] **Step 4: Implement options, constants, interface, exceptions**

`backend/src/Anela.Heblo.Application/Features/MindMaps/MindMapsOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MindMaps;

public class MindMapsOptions
{
    public const string SectionName = "MindMaps";

    /// <summary>Replaces the Claude updater with a deterministic stub (E2E/staging).</summary>
    public bool UseStubUpdater { get; set; }

    [Range(1024, 64000)]
    public int UpdaterMaxOutputTokens { get; set; } = 16384;
}
```

`backend/src/Anela.Heblo.Application/Features/MindMaps/MindMapsConstants.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MindMaps;

internal static class MindMapsConstants
{
    internal const string UpdaterChatClientKey = "mindmap-updater";
}
```

`backend/src/Anela.Heblo.Application/Features/MindMaps/Services/IMindMapUpdater.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Domain.Features.MeetingTasks;

namespace Anela.Heblo.Application.Features.MindMaps.Services;

public interface IMindMapUpdater
{
    /// <summary>
    /// Produces the LLM's proposed next document for the map given one new meeting.
    /// Throws <see cref="MindMapUpdateException"/> when no valid document could be obtained.
    /// </summary>
    Task<MindMapDocument> UpdateAsync(MindMapDocument current, MeetingTranscript meeting, CancellationToken ct = default);
}
```

`backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapUpdateException.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MindMaps.Services;

public class MindMapUpdateException : Exception
{
    public MindMapUpdateException(string message) : base(message) { }
}
```

- [ ] **Step 5: Implement ClaudeMindMapUpdater and StubMindMapUpdater**

`backend/src/Anela.Heblo.Application/Features/MindMaps/Services/ClaudeMindMapUpdater.cs`:

```csharp
using System.Reflection;
using System.Text.Json;
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MindMaps.Services;

public class ClaudeMindMapUpdater : IMindMapUpdater
{
    private const int MaxAttempts = 2;
    private const string PromptResourceName =
        "Anela.Heblo.Application.Features.MindMaps.Prompts.mindmap-update-skill.md";

    private static readonly Lazy<string> SystemPrompt = new(LoadSystemPrompt);

    private readonly IChatClient _chatClient;
    private readonly MindMapsOptions _options;
    private readonly ILogger<ClaudeMindMapUpdater> _logger;

    public ClaudeMindMapUpdater(
        IChatClient chatClient,
        IOptions<MindMapsOptions> options,
        ILogger<ClaudeMindMapUpdater> logger)
    {
        _chatClient = chatClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MindMapDocument> UpdateAsync(
        MindMapDocument current, MeetingTranscript meeting, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt.Value),
            new(ChatRole.User, BuildUserMessage(current, meeting))
        };
        var chatOptions = new ChatOptions { MaxOutputTokens = _options.UpdaterMaxOutputTokens };

        string? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var response = await _chatClient.GetResponseAsync(messages, chatOptions, ct);
            var text = StripMarkdownCodeFence(response.Text ?? string.Empty);

            try
            {
                var doc = MindMapJson.Deserialize(text);
                var errors = MindMapDocumentValidator.Validate(doc);
                if (errors.Count == 0) return doc;
                lastError = string.Join(" ", errors);
            }
            catch (JsonException ex)
            {
                lastError = ex.Message;
            }

            _logger.LogWarning(
                "Mind map update attempt {Attempt}/{Max} returned an invalid document: {Error}",
                attempt, MaxAttempts, lastError);
            messages.Add(new ChatMessage(ChatRole.Assistant, response.Text ?? string.Empty));
            messages.Add(new ChatMessage(ChatRole.User,
                $"Tvá předchozí odpověď nebyla validní ({lastError}). " +
                "Vrať POUZE validní JSON dokument mapy podle zadaného schématu."));
        }

        throw new MindMapUpdateException(
            $"LLM returned an invalid mind map document after {MaxAttempts} attempts: {lastError}");
    }

    private static string BuildUserMessage(MindMapDocument current, MeetingTranscript meeting)
    {
        // The LLM view deliberately omits position/collapsed/lockedBy — those are
        // UI/system metadata the guard pass restores after the update.
        var llmView = new
        {
            rootNodeId = current.RootNodeId,
            nodes = current.Nodes.Select(n => new
            {
                id = n.Id,
                parentId = n.ParentId,
                title = n.Title,
                notes = n.Notes,
                status = n.Status,
                owner = n.Owner,
                locked = n.LockedBy != null,
                sourceMeetingIds = n.SourceMeetingIds
            }),
            doNotRecreate = current.SuppressedNodes.Select(s => s.Title)
        };
        var mapJson = JsonSerializer.Serialize(llmView);

        return $"Aktuální mapa:\n{mapJson}\n\n" +
               $"Nová porada — {meeting.Subject} ({meeting.PlaudCreatedAt:yyyy-MM-dd}):\n\n" +
               $"Souhrn:\n{meeting.Summary}\n\n" +
               $"Transkript:\n{meeting.RawTranscript}";
    }

    private static string LoadSystemPrompt()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PromptResourceName)
            ?? throw new InvalidOperationException($"Embedded prompt '{PromptResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string StripMarkdownCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["```json".Length..];
        else if (trimmed.StartsWith("```"))
            trimmed = trimmed["```".Length..];
        if (trimmed.EndsWith("```"))
            trimmed = trimmed[..^"```".Length];
        return trimmed.Trim();
    }
}
```

`backend/src/Anela.Heblo.Application/Features/MindMaps/Services/StubMindMapUpdater.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Domain.Features.MeetingTasks;

namespace Anela.Heblo.Application.Features.MindMaps.Services;

/// <summary>
/// Deterministic updater used on staging/E2E (MindMaps:UseStubUpdater=true):
/// adds one "Porada: &lt;subject&gt;" node under the root, nothing else.
/// </summary>
public class StubMindMapUpdater : IMindMapUpdater
{
    public Task<MindMapDocument> UpdateAsync(
        MindMapDocument current, MeetingTranscript meeting, CancellationToken ct = default)
    {
        var result = MindMapJson.Clone(current);
        result.Nodes.Add(new MindMapNode
        {
            Id = $"new-{result.Nodes.Count}",
            ParentId = result.RootNodeId,
            Title = $"Porada: {meeting.Subject}",
            Status = MindMapNodeStatus.Idea
        });
        return Task.FromResult(result);
    }
}
```

In `AnthropicAdapterServiceCollectionExtensions.cs` add below `MeetingExtractionClientKey`:

```csharp
    public const string MindMapUpdaterClientKey = "mindmap-updater";
```

and below the existing `AddKeyedSingleton` block:

```csharp
        services.AddKeyedSingleton<IChatClient>(MindMapUpdaterClientKey, (sp, _) =>
            new AnthropicChatClient(
                sp.GetRequiredService<IOptions<AnthropicOptions>>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<AnthropicChatClient>>()));
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMapUpdater" -p:UseSharedCompilation=false`
Expected: all 7 PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application backend/src/Adapters/Anela.Heblo.Adapters.Anthropic backend/test/Anela.Heblo.Tests/Features/MindMaps
git commit -m "feat: add claude mind map updater with skill prompt, retry and stub variant"
```

---

### Task 7: MindMapUpdateJob — the Hangfire pipeline

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Infrastructure/Jobs/MindMapUpdateJob.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapUpdateJobTests.cs`

**Interfaces:**
- Consumes: `IMindMapRepository` (Task 2), `IMindMapUpdater` (Task 6), `MindMapGuard` (Task 4), `MindMapJson` (Task 1).
- Produces: `MindMapUpdateJob.RunAsync(Guid mindMapId, CancellationToken ct)` — enqueued by Task 9's attach/regenerate handlers via `IBackgroundJobClient.Enqueue<MindMapUpdateJob>(j => j.RunAsync(id, CancellationToken.None))`. Registered `AddScoped<MindMapUpdateJob>()` in Task 8 (on-demand job pattern, like `GenerateArticleJob` in `ArticleModule.cs:31`).

Behavior:
1. Load the map; if missing, log a warning and return.
2. Pending = attached meetings with `ProcessedAt == null`, ordered by `MeetingTranscript.PlaudCreatedAt` ascending — chronological replay.
3. Per meeting: deserialize `CurrentJson` → `updater.UpdateAsync` → `guard.ApplyLlmUpdate` → append a `MindMapVersion` snapshot of the PRE-update json (`VersionNumber = max + 1`, `TriggerMeetingId`) → write new `CurrentJson`, set `ProcessedAt`, `UpdatedAt`, save. Saving per meeting keeps partial progress on later failure.
4. Any exception: set `Status = Failed`, `LastError = ex.Message`, save, STOP (remaining meetings stay pending; the map is never overwritten with a bad document because the guard threw before `CurrentJson` was touched).
5. All processed: `Status = Idle`, `LastError = null`, save.
6. `[DisableConcurrentExecution(timeoutInSeconds: 600)]` on `RunAsync` so two enqueued jobs for the same map never interleave.

- [ ] **Step 1: Write the failing tests**

`backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapUpdateJobTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Application.Features.MindMaps.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.MindMaps;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class MindMapUpdateJobTests
{
    private readonly Mock<IMindMapRepository> _repository = new();
    private readonly Mock<IMindMapUpdater> _updater = new();

    private MindMapUpdateJob CreateSut() => new(
        _repository.Object,
        _updater.Object,
        new MindMapGuard(),
        NullLogger<MindMapUpdateJob>.Instance);

    private static MindMap MapWithMeetings(params (Guid id, DateTime createdAt)[] meetings)
    {
        var doc = new MindMapDocument
        {
            RootNodeId = "root",
            Nodes = new List<MindMapNode> { new() { Id = "root", Title = "Projekt" } }
        };
        var map = new MindMap
        {
            Id = Guid.NewGuid(),
            Name = "Projekt",
            Status = MindMapStatus.Updating,
            CurrentJson = MindMapJson.Serialize(doc)
        };
        foreach (var (id, createdAt) in meetings)
        {
            map.Meetings.Add(new MindMapMeeting
            {
                Id = Guid.NewGuid(),
                MindMapId = map.Id,
                MeetingTranscriptId = id,
                MeetingTranscript = new MeetingTranscript
                {
                    Id = id, PlaudRecordingId = id.ToString(), Subject = $"Porada {createdAt:d}",
                    Summary = "s", RawTranscript = "t", PlaudCreatedAt = createdAt
                }
            });
        }
        return map;
    }

    /// <summary>Updater echo: returns the current doc plus one node named after the meeting.</summary>
    private void UpdaterAddsNodePerMeeting()
    {
        _updater.Setup(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMapDocument current, MeetingTranscript meeting, CancellationToken _) =>
            {
                var next = MindMapJson.Clone(current);
                next.Nodes.Add(new MindMapNode
                {
                    Id = $"new-{meeting.Id:N}", ParentId = next.RootNodeId, Title = meeting.Subject
                });
                return next;
            });
    }

    [Fact]
    public async Task RunAsync_ProcessesPendingMeetingsChronologically_AndEndsIdle()
    {
        var early = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var late = (Guid.NewGuid(), new DateTime(2026, 8, 1));
        var map = MapWithMeetings(late, early); // attached out of order
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        var processedOrder = new List<Guid>();
        _updater.Setup(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .Callback<MindMapDocument, MeetingTranscript, CancellationToken>((_, m, _) => processedOrder.Add(m.Id))
            .ReturnsAsync((MindMapDocument current, MeetingTranscript _, CancellationToken _) => MindMapJson.Clone(current));

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(new[] { early.Item1, late.Item1 }, processedOrder);
        Assert.Equal(MindMapStatus.Idle, map.Status);
        Assert.Null(map.LastError);
        Assert.All(map.Meetings, m => Assert.NotNull(m.ProcessedAt));
    }

    [Fact]
    public async Task RunAsync_SnapshotsPreviousVersion_PerProcessedMeeting()
    {
        var m1 = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var m2 = (Guid.NewGuid(), new DateTime(2026, 8, 1));
        var map = MapWithMeetings(m1, m2);
        var originalJson = map.CurrentJson;
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        UpdaterAddsNodePerMeeting();

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(2, map.Versions.Count);
        var v1 = map.Versions.Single(v => v.VersionNumber == 1);
        Assert.Equal(originalJson, v1.Json);
        Assert.Equal(m1.Item1, v1.TriggerMeetingId);
        Assert.Equal(3, MindMapJson.Deserialize(map.CurrentJson).Nodes.Count);
    }

    [Fact]
    public async Task RunAsync_SetsFailedAndStops_WhenUpdaterThrows_KeepingCurrentJson()
    {
        var m1 = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var m2 = (Guid.NewGuid(), new DateTime(2026, 8, 1));
        var map = MapWithMeetings(m1, m2);
        var originalJson = map.CurrentJson;
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _updater.Setup(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MindMapUpdateException("LLM vrátil nevalidní dokument"));

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(MindMapStatus.Failed, map.Status);
        Assert.Contains("nevalidní", map.LastError);
        Assert.Equal(originalJson, map.CurrentJson);
        Assert.All(map.Meetings, m => Assert.Null(m.ProcessedAt));
        Assert.Empty(map.Versions);
    }

    [Fact]
    public async Task RunAsync_KeepsEarlierProgress_WhenSecondMeetingFails()
    {
        var m1 = (Guid.NewGuid(), new DateTime(2026, 7, 1));
        var m2 = (Guid.NewGuid(), new DateTime(2026, 8, 1));
        var map = MapWithMeetings(m1, m2);
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        var call = 0;
        _updater.Setup(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMapDocument current, MeetingTranscript _, CancellationToken _) =>
            {
                if (++call == 2) throw new MindMapUpdateException("selhalo");
                return MindMapJson.Clone(current);
            });

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(MindMapStatus.Failed, map.Status);
        Assert.NotNull(map.Meetings.Single(m => m.MeetingTranscriptId == m1.Item1).ProcessedAt);
        Assert.Null(map.Meetings.Single(m => m.MeetingTranscriptId == m2.Item1).ProcessedAt);
        Assert.Single(map.Versions);
    }

    [Fact]
    public async Task RunAsync_ReturnsQuietly_WhenMapMissing()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMap?)null);

        await CreateSut().RunAsync(Guid.NewGuid(), CancellationToken.None);

        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_EndsIdle_WhenNothingPending()
    {
        var map = MapWithMeetings();
        map.Status = MindMapStatus.Updating;
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        await CreateSut().RunAsync(map.Id, CancellationToken.None);

        Assert.Equal(MindMapStatus.Idle, map.Status);
        _updater.Verify(u => u.UpdateAsync(It.IsAny<MindMapDocument>(), It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMapUpdateJob" -p:UseSharedCompilation=false`
Expected: build FAILURE.

- [ ] **Step 3: Implement the job**

`backend/src/Anela.Heblo.Application/Features/MindMaps/Infrastructure/Jobs/MindMapUpdateJob.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MindMaps.Infrastructure.Jobs;

/// <summary>
/// Processes all pending meetings of one mind map sequentially (chronological by
/// meeting date). Enqueued on attach and on manual regenerate. Saves after each
/// meeting so a later failure keeps earlier progress.
/// </summary>
public class MindMapUpdateJob
{
    private readonly IMindMapRepository _repository;
    private readonly IMindMapUpdater _updater;
    private readonly MindMapGuard _guard;
    private readonly ILogger<MindMapUpdateJob> _logger;

    public MindMapUpdateJob(
        IMindMapRepository repository,
        IMindMapUpdater updater,
        MindMapGuard guard,
        ILogger<MindMapUpdateJob> logger)
    {
        _repository = repository;
        _updater = updater;
        _guard = guard;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(Guid mindMapId, CancellationToken ct)
    {
        var map = await _repository.GetByIdAsync(mindMapId, ct);
        if (map is null)
        {
            _logger.LogWarning("Mind map {MindMapId} not found — nothing to update", mindMapId);
            return;
        }

        var pending = map.Meetings
            .Where(m => m.ProcessedAt == null && m.MeetingTranscript != null)
            .OrderBy(m => m.MeetingTranscript.PlaudCreatedAt)
            .ToList();

        foreach (var meeting in pending)
        {
            try
            {
                var current = MindMapJson.Deserialize(map.CurrentJson);
                var llmResult = await _updater.UpdateAsync(current, meeting.MeetingTranscript, ct);
                var next = _guard.ApplyLlmUpdate(current, llmResult, meeting.MeetingTranscriptId);

                var nextVersionNumber = map.Versions.Count == 0
                    ? 1
                    : map.Versions.Max(v => v.VersionNumber) + 1;
                map.Versions.Add(new MindMapVersion
                {
                    Id = Guid.NewGuid(),
                    MindMapId = map.Id,
                    VersionNumber = nextVersionNumber,
                    Json = map.CurrentJson,
                    CreatedAt = DateTime.UtcNow,
                    TriggerMeetingId = meeting.MeetingTranscriptId
                });

                map.CurrentJson = MindMapJson.Serialize(next);
                meeting.ProcessedAt = DateTime.UtcNow;
                map.UpdatedAt = DateTime.UtcNow;
                await _repository.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Mind map {MindMapId} update failed on meeting {MeetingId}",
                    map.Id, meeting.MeetingTranscriptId);
                map.Status = MindMapStatus.Failed;
                map.LastError = ex.Message;
                map.UpdatedAt = DateTime.UtcNow;
                await _repository.SaveChangesAsync(CancellationToken.None);
                return;
            }
        }

        map.Status = MindMapStatus.Idle;
        map.LastError = null;
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMapUpdateJob" -p:UseSharedCompilation=false`
Expected: all 6 PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MindMaps/Infrastructure backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapUpdateJobTests.cs
git commit -m "feat: add hangfire job processing pending mind map meetings sequentially"
```

---

### Task 8: Access matrix, module wiring, CRUD use cases, controller

**Files:**
- Modify: `access-matrix.json` (feature, menu path, seed group roles)
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/MindMapsModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (add `services.AddMindMapsModule(configuration);` next to `AddMeetingTasksModule`, ~line 119)
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Contracts/MindMapDtos.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/CreateMindMap/{CreateMindMapRequest,CreateMindMapHandler,CreateMindMapResponse}.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/GetMindMapList/{GetMindMapListRequest,GetMindMapListHandler,GetMindMapListResponse}.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/GetMindMapDetail/{GetMindMapDetailRequest,GetMindMapDetailHandler,GetMindMapDetailResponse}.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/DeleteMindMap/{DeleteMindMapRequest,DeleteMindMapHandler,DeleteMindMapResponse}.cs`
- Create: `backend/src/Anela.Heblo.API/Controllers/MindMapsController.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/CreateMindMapHandlerTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/GetMindMapDetailHandlerTests.cs`

**Interfaces:**
- Consumes: Tasks 1-7 types; `Feature` enum (regenerated); `BaseApiController.HandleResponse`; MediatR.
- Produces: `Feature.Anela_MindMaps`; controller routes `api/mind-maps` (GET `/`, POST `/`, GET `/{id}`, DELETE `/{id}`); DTOs `MindMapListItemDto`, `AttachedMeetingDto`, `MindMapVersionDto`; responses listed below. Tasks 9-11 extend the controller and consume the DTOs.

- [ ] **Step 1: Add the feature to access-matrix.json and regenerate**

In `access-matrix.json` `features` array, after the `Anela_OrgChart` line:

```json
    { "key": "Anela_MindMaps", "label": "Myšlenkové mapy", "hasWrite": true },
```

In `menuPaths`, after the `/automation/meeting-tasks` line:

```json
    { "path": "/automation/mind-maps", "requires": [{ "feature": "Anela_MindMaps", "level": "Read" }] },
```

In `seedGroups`: add `"anela.mind_maps.read", "anela.mind_maps.write"` to the `Spravce` roles array and `"anela.mind_maps.read"` to the `Vedeni` roles array.

Run `dotnet build` — `Anela.Heblo.AccessMatrixGen` regenerates `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs` (adds `Anela_MindMaps`) and `frontend/src/auth/accessMatrix.generated.ts` (adds the route permission). Verify both generated files contain the new entries.

- [ ] **Step 2: Write the failing handler tests**

`backend/test/Anela.Heblo.Tests/Features/MindMaps/CreateMindMapHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.UseCases.CreateMindMap;
using Anela.Heblo.Domain.Features.MindMaps;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class CreateMindMapHandlerTests
{
    private readonly Mock<IMindMapRepository> _repository = new();

    [Fact]
    public async Task Handle_CreatesMapWithSingleRootNodeNamedAfterMap()
    {
        MindMap? saved = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<MindMap>(), It.IsAny<CancellationToken>()))
            .Callback<MindMap, CancellationToken>((m, _) => saved = m);
        var handler = new CreateMindMapHandler(_repository.Object, NullLogger<CreateMindMapHandler>.Instance);

        var response = await handler.Handle(
            new CreateMindMapRequest { Name = "Web relaunch", Description = "popis" },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(saved);
        Assert.Equal("Web relaunch", saved!.Name);
        Assert.Equal(MindMapStatus.Idle, saved.Status);
        var doc = MindMapJson.Deserialize(saved.CurrentJson);
        var root = Assert.Single(doc.Nodes);
        Assert.Equal(doc.RootNodeId, root.Id);
        Assert.Equal("Web relaunch", root.Title);
        Assert.Null(root.ParentId);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

`backend/test/Anela.Heblo.Tests/Features/MindMaps/GetMindMapDetailHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.MindMaps;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class GetMindMapDetailHandlerTests
{
    private readonly Mock<IMindMapRepository> _repository = new();

    [Fact]
    public async Task Handle_ReturnsResourceNotFound_WhenMapMissing()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMap?)null);
        var handler = new GetMindMapDetailHandler(_repository.Object);

        var response = await handler.Handle(new GetMindMapDetailRequest { Id = Guid.NewGuid() }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_MapsMeetingsAndVersions_NewestFirst()
    {
        var meetingId = Guid.NewGuid();
        var map = new MindMap
        {
            Id = Guid.NewGuid(), Name = "Mapa", CurrentJson = "{}",
            Status = MindMapStatus.Failed, LastError = "chyba"
        };
        map.Meetings.Add(new MindMapMeeting
        {
            MeetingTranscriptId = meetingId,
            AttachedAt = new DateTime(2026, 8, 1),
            MeetingTranscript = new MeetingTranscript
            {
                Id = meetingId, PlaudRecordingId = "r", Subject = "Porada",
                Summary = "s", RawTranscript = "t", PlaudCreatedAt = new DateTime(2026, 7, 30)
            }
        });
        map.Versions.Add(new MindMapVersion { VersionNumber = 1, Json = "{}", TriggerMeetingId = meetingId });
        map.Versions.Add(new MindMapVersion { VersionNumber = 2, Json = "{}" });
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        var handler = new GetMindMapDetailHandler(_repository.Object);

        var response = await handler.Handle(new GetMindMapDetailRequest { Id = map.Id }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("Failed", response.Status);
        Assert.Equal("chyba", response.LastError);
        Assert.Equal("Porada", response.Meetings.Single().Subject);
        Assert.Equal(2, response.Versions.First().VersionNumber);
        Assert.Equal("Porada", response.Versions.Last().TriggerMeetingSubject);
    }
}
```

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMapHandler | FullyQualifiedName~CreateMindMap | FullyQualifiedName~GetMindMapDetail" -p:UseSharedCompilation=false`
Expected: build FAILURE.

- [ ] **Step 3: Implement DTOs and the four use cases**

`backend/src/Anela.Heblo.Application/Features/MindMaps/Contracts/MindMapDtos.cs` (classes, not records):

```csharp
namespace Anela.Heblo.Application.Features.MindMaps.Contracts;

public class MindMapListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Status { get; set; } = null!;
    public int MeetingCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AttachedMeetingDto
{
    public Guid MeetingTranscriptId { get; set; }
    public string Subject { get; set; } = null!;
    public DateTime PlaudCreatedAt { get; set; }
    public DateTime AttachedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public class MindMapVersionDto
{
    public int VersionNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? TriggerMeetingId { get; set; }
    public string? TriggerMeetingSubject { get; set; }
}
```

`UseCases/CreateMindMap/CreateMindMapRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.CreateMindMap;

public class CreateMindMapRequest : IRequest<CreateMindMapResponse>
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = null!;

    [MaxLength(2000)]
    public string? Description { get; set; }
}
```

`UseCases/CreateMindMap/CreateMindMapResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.CreateMindMap;

public class CreateMindMapResponse : BaseResponse
{
    public Guid Id { get; set; }

    public CreateMindMapResponse() { }
    public CreateMindMapResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`UseCases/CreateMindMap/CreateMindMapHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Domain.Features.MindMaps;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.CreateMindMap;

public class CreateMindMapHandler : IRequestHandler<CreateMindMapRequest, CreateMindMapResponse>
{
    private readonly IMindMapRepository _repository;
    private readonly ILogger<CreateMindMapHandler> _logger;

    public CreateMindMapHandler(IMindMapRepository repository, ILogger<CreateMindMapHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<CreateMindMapResponse> Handle(CreateMindMapRequest request, CancellationToken cancellationToken)
    {
        var rootId = Guid.NewGuid().ToString("N");
        var document = new MindMapDocument
        {
            RootNodeId = rootId,
            Nodes = new List<MindMapNode> { new() { Id = rootId, Title = request.Name } }
        };

        var now = DateTime.UtcNow;
        var map = new MindMap
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Status = MindMapStatus.Idle,
            CurrentJson = MindMapJson.Serialize(document),
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repository.AddAsync(map, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Created mind map {MindMapId} '{Name}'", map.Id, map.Name);
        return new CreateMindMapResponse { Id = map.Id };
    }
}
```

`UseCases/GetMindMapList/GetMindMapListRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapList;

public class GetMindMapListRequest : IRequest<GetMindMapListResponse>
{
}
```

`UseCases/GetMindMapList/GetMindMapListResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapList;

public class GetMindMapListResponse : BaseResponse
{
    public List<MindMapListItemDto> Items { get; set; } = new();
}
```

`UseCases/GetMindMapList/GetMindMapListHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Contracts;
using Anela.Heblo.Domain.Features.MindMaps;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapList;

public class GetMindMapListHandler : IRequestHandler<GetMindMapListRequest, GetMindMapListResponse>
{
    private readonly IMindMapRepository _repository;

    public GetMindMapListHandler(IMindMapRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetMindMapListResponse> Handle(GetMindMapListRequest request, CancellationToken cancellationToken)
    {
        var maps = await _repository.GetListAsync(cancellationToken);
        return new GetMindMapListResponse
        {
            Items = maps.Select(m => new MindMapListItemDto
            {
                Id = m.Id,
                Name = m.Name,
                Description = m.Description,
                Status = m.Status.ToString(),
                MeetingCount = m.Meetings.Count,
                UpdatedAt = m.UpdatedAt
            }).ToList()
        };
    }
}
```

`UseCases/GetMindMapDetail/GetMindMapDetailRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapDetail;

public class GetMindMapDetailRequest : IRequest<GetMindMapDetailResponse>
{
    public Guid Id { get; set; }
}
```

`UseCases/GetMindMapDetail/GetMindMapDetailResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapDetail;

public class GetMindMapDetailResponse : BaseResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Status { get; set; } = null!;
    public string? LastError { get; set; }
    public string DocumentJson { get; set; } = null!;
    public List<AttachedMeetingDto> Meetings { get; set; } = new();
    public List<MindMapVersionDto> Versions { get; set; } = new();

    public GetMindMapDetailResponse() { }
    public GetMindMapDetailResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`UseCases/GetMindMapDetail/GetMindMapDetailHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapDetail;

public class GetMindMapDetailHandler : IRequestHandler<GetMindMapDetailRequest, GetMindMapDetailResponse>
{
    private readonly IMindMapRepository _repository;

    public GetMindMapDetailHandler(IMindMapRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetMindMapDetailResponse> Handle(GetMindMapDetailRequest request, CancellationToken cancellationToken)
    {
        var map = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (map is null)
        {
            return new GetMindMapDetailResponse(ErrorCodes.ResourceNotFound);
        }

        var subjectsByMeetingId = map.Meetings
            .Where(m => m.MeetingTranscript != null)
            .ToDictionary(m => m.MeetingTranscriptId, m => m.MeetingTranscript.Subject);

        return new GetMindMapDetailResponse
        {
            Id = map.Id,
            Name = map.Name,
            Description = map.Description,
            Status = map.Status.ToString(),
            LastError = map.LastError,
            DocumentJson = map.CurrentJson,
            Meetings = map.Meetings
                .Where(m => m.MeetingTranscript != null)
                .OrderByDescending(m => m.MeetingTranscript.PlaudCreatedAt)
                .Select(m => new AttachedMeetingDto
                {
                    MeetingTranscriptId = m.MeetingTranscriptId,
                    Subject = m.MeetingTranscript.Subject,
                    PlaudCreatedAt = m.MeetingTranscript.PlaudCreatedAt,
                    AttachedAt = m.AttachedAt,
                    ProcessedAt = m.ProcessedAt
                }).ToList(),
            Versions = map.Versions
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => new MindMapVersionDto
                {
                    VersionNumber = v.VersionNumber,
                    CreatedAt = v.CreatedAt,
                    TriggerMeetingId = v.TriggerMeetingId,
                    TriggerMeetingSubject = v.TriggerMeetingId != null
                        && subjectsByMeetingId.TryGetValue(v.TriggerMeetingId.Value, out var subject)
                            ? subject
                            : null
                }).ToList()
        };
    }
}
```

`UseCases/DeleteMindMap/DeleteMindMapRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.DeleteMindMap;

public class DeleteMindMapRequest : IRequest<DeleteMindMapResponse>
{
    public Guid Id { get; set; }
}
```

`UseCases/DeleteMindMap/DeleteMindMapResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.DeleteMindMap;

public class DeleteMindMapResponse : BaseResponse
{
    public DeleteMindMapResponse() { }
    public DeleteMindMapResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`UseCases/DeleteMindMap/DeleteMindMapHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.DeleteMindMap;

public class DeleteMindMapHandler : IRequestHandler<DeleteMindMapRequest, DeleteMindMapResponse>
{
    private readonly IMindMapRepository _repository;

    public DeleteMindMapHandler(IMindMapRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeleteMindMapResponse> Handle(DeleteMindMapRequest request, CancellationToken cancellationToken)
    {
        var map = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (map is null)
        {
            return new DeleteMindMapResponse(ErrorCodes.ResourceNotFound);
        }

        await _repository.DeleteAsync(map, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return new DeleteMindMapResponse();
    }
}
```

- [ ] **Step 4: Implement the module and wire it**

`backend/src/Anela.Heblo.Application/Features/MindMaps/MindMapsModule.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Domain.Features.MindMaps;
using Anela.Heblo.Persistence.MindMaps;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MindMaps;

public static class MindMapsModule
{
    public static IServiceCollection AddMindMapsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MindMapsOptions>()
            .Bind(configuration.GetSection(MindMapsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var useStubUpdater = configuration.GetValue<bool>(
            $"{MindMapsOptions.SectionName}:UseStubUpdater", false);
        if (useStubUpdater)
        {
            services.AddScoped<IMindMapUpdater, StubMindMapUpdater>();
        }
        else
        {
            services.AddScoped<IMindMapUpdater>(sp =>
                new ClaudeMindMapUpdater(
                    sp.GetRequiredKeyedService<IChatClient>(MindMapsConstants.UpdaterChatClientKey),
                    sp.GetRequiredService<IOptions<MindMapsOptions>>(),
                    sp.GetRequiredService<ILogger<ClaudeMindMapUpdater>>()));
        }

        services.AddSingleton<MindMapGuard>();
        services.AddSingleton<MindMapLockService>();

        // On-demand Hangfire job (enqueued by attach/regenerate handlers)
        services.AddScoped<MindMapUpdateJob>();

        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IMindMapRepository, MindMapRepository>();

        // MediatR handlers are auto-registered by the MediatR assembly scan in ApplicationModule.
        return services;
    }
}
```

In `ApplicationModule.cs`, next to `services.AddMeetingTasksModule(configuration);` add:

```csharp
        services.AddMindMapsModule(configuration);
```

- [ ] **Step 5: Implement the controller**

`backend/src/Anela.Heblo.API/Controllers/MindMapsController.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.UseCases.CreateMindMap;
using Anela.Heblo.Application.Features.MindMaps.UseCases.DeleteMindMap;
using Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapDetail;
using Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapList;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Anela_MindMaps)]
[ApiController]
[Route("api/mind-maps")]
public sealed class MindMapsController : BaseApiController
{
    private readonly IMediator _mediator;

    public MindMapsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetMindMapListResponse>> List(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMindMapListRequest(), ct);
        return HandleResponse(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetMindMapDetailResponse>> Detail(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMindMapDetailRequest { Id = id }, ct);
        return HandleResponse(result);
    }

    [HttpPost]
    [FeatureAuthorize(Feature.Anela_MindMaps, AccessLevel.Write)]
    public async Task<ActionResult<CreateMindMapResponse>> Create(
        [FromBody] CreateMindMapRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpDelete("{id:guid}")]
    [FeatureAuthorize(Feature.Anela_MindMaps, AccessLevel.Write)]
    public async Task<ActionResult<DeleteMindMapResponse>> Delete(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new DeleteMindMapRequest { Id = id }, ct);
        return HandleResponse(result);
    }
}
```

- [ ] **Step 6: Run tests and full build**

Run: `dotnet build && dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMap" --no-build -p:UseSharedCompilation=false`
Expected: PASS (including the `BaseResponse` contract reflection test — run it explicitly: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ResponseContract" --no-build -p:UseSharedCompilation=false`; if the filter matches nothing, the contract check runs inside the full suite in Task 15).

- [ ] **Step 7: Commit**

```bash
git add access-matrix.json backend frontend/src/auth/accessMatrix.generated.ts
git commit -m "feat: add mind maps module wiring, CRUD use cases and controller"
```

---

### Task 9: Attach, detach, regenerate use cases + endpoints

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/AttachMeeting/{AttachMeetingRequest,AttachMeetingHandler,AttachMeetingResponse}.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/DetachMeeting/{DetachMeetingRequest,DetachMeetingHandler,DetachMeetingResponse}.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/RegenerateMindMap/{RegenerateMindMapRequest,RegenerateMindMapHandler,RegenerateMindMapResponse}.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/MindMapsController.cs` (3 new endpoints)
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/AttachMeetingHandlerTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/RegenerateMindMapHandlerTests.cs`

**Interfaces:**
- Consumes: `IMindMapRepository`, `IMeetingTranscriptRepository.GetByIdAsync`, `IMeetingAccessGuard.CanAccess` (`Anela.Heblo.Application.Features.MeetingTasks.Services`), `IBackgroundJobClient` (Hangfire), `MindMapUpdateJob`, `ErrorCodes` from Task 3.
- Produces: endpoints `POST /{id}/meetings` (body `{ meetingTranscriptId }`), `DELETE /{id}/meetings/{meetingId}`, `POST /{id}/regenerate`. All responses are plain `BaseResponse` subclasses with error ctor, named `AttachMeetingResponse`, `DetachMeetingResponse`, `RegenerateMindMapResponse`.

Handler rules:
- **Attach**: map missing → `ResourceNotFound`. Meeting missing OR `!_accessGuard.CanAccess(transcript)` → `ResourceNotFound` (mirrors MeetingTasks: don't leak existence). Already attached (any state) → `MindMapMeetingAlreadyAttached`. Otherwise: add `MindMapMeeting { AttachedAt = DateTime.UtcNow }`, set `map.Status = MindMapStatus.Updating`, `UpdatedAt`, save, then `_backgroundJobClient.Enqueue<MindMapUpdateJob>(j => j.RunAsync(map.Id, CancellationToken.None))`. Attaching while `Updating` is allowed — the new meeting just joins the pending queue; `[DisableConcurrentExecution]` serializes the jobs.
- **Detach**: map missing → `ResourceNotFound`; link missing → `ResourceNotFound`; otherwise remove the `MindMapMeeting` row (regardless of `ProcessedAt`), save. The document is NOT rewritten (spec).
- **Regenerate**: map missing → `ResourceNotFound`. `Status == Updating` → `MindMapUpdateInProgress`. If any meeting has `ProcessedAt == null`: set `Status = Updating`, save, enqueue the job. Otherwise (nothing pending — e.g. Failed after a partial run that later got detached): set `Status = Idle`, `LastError = null`, save, return success.

- [ ] **Step 1: Write the failing tests**

`backend/test/Anela.Heblo.Tests/Features/MindMaps/AttachMeetingHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MindMaps.UseCases.AttachMeeting;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class AttachMeetingHandlerTests
{
    private readonly Mock<IMindMapRepository> _mapRepository = new();
    private readonly Mock<IMeetingTranscriptRepository> _meetingRepository = new();
    private readonly Mock<IMeetingAccessGuard> _accessGuard = new();
    private readonly Mock<IBackgroundJobClient> _backgroundJobClient = new();

    private AttachMeetingHandler CreateSut() => new(
        _mapRepository.Object,
        _meetingRepository.Object,
        _accessGuard.Object,
        _backgroundJobClient.Object,
        NullLogger<AttachMeetingHandler>.Instance);

    private static MindMap Map() => new()
    {
        Id = Guid.NewGuid(), Name = "Mapa", CurrentJson = "{}", Status = MindMapStatus.Idle
    };

    private static MeetingTranscript Meeting() => new()
    {
        Id = Guid.NewGuid(), PlaudRecordingId = "r", Subject = "Porada", Summary = "s", RawTranscript = "t"
    };

    [Fact]
    public async Task Handle_AttachesMeeting_SetsUpdating_AndEnqueuesJob()
    {
        var map = Map();
        var meeting = Meeting();
        _mapRepository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _meetingRepository.Setup(r => r.GetByIdAsync(meeting.Id, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        _accessGuard.Setup(g => g.CanAccess(meeting)).Returns(true);

        var response = await CreateSut().Handle(
            new AttachMeetingRequest { MindMapId = map.Id, MeetingTranscriptId = meeting.Id },
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(MindMapStatus.Updating, map.Status);
        Assert.Single(map.Meetings, m => m.MeetingTranscriptId == meeting.Id && m.ProcessedAt == null);
        // Enqueue<T> is an extension over Create(Job, IState)
        _backgroundJobClient.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsResourceNotFound_WhenUserCannotAccessMeeting()
    {
        var map = Map();
        var meeting = Meeting();
        _mapRepository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _meetingRepository.Setup(r => r.GetByIdAsync(meeting.Id, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        _accessGuard.Setup(g => g.CanAccess(meeting)).Returns(false);

        var response = await CreateSut().Handle(
            new AttachMeetingRequest { MindMapId = map.Id, MeetingTranscriptId = meeting.Id },
            CancellationToken.None);

        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
        Assert.Empty(map.Meetings);
    }

    [Fact]
    public async Task Handle_ReturnsAlreadyAttached_WhenLinkExists()
    {
        var map = Map();
        var meeting = Meeting();
        map.Meetings.Add(new MindMapMeeting { MeetingTranscriptId = meeting.Id });
        _mapRepository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        _meetingRepository.Setup(r => r.GetByIdAsync(meeting.Id, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        _accessGuard.Setup(g => g.CanAccess(meeting)).Returns(true);

        var response = await CreateSut().Handle(
            new AttachMeetingRequest { MindMapId = map.Id, MeetingTranscriptId = meeting.Id },
            CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapMeetingAlreadyAttached, response.ErrorCode);
        _backgroundJobClient.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()), Times.Never);
    }
}
```

`backend/test/Anela.Heblo.Tests/Features/MindMaps/RegenerateMindMapHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.UseCases.RegenerateMindMap;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class RegenerateMindMapHandlerTests
{
    private readonly Mock<IMindMapRepository> _repository = new();
    private readonly Mock<IBackgroundJobClient> _backgroundJobClient = new();

    private RegenerateMindMapHandler CreateSut() => new(_repository.Object, _backgroundJobClient.Object);

    [Fact]
    public async Task Handle_ReturnsUpdateInProgress_WhenAlreadyUpdating()
    {
        var map = new MindMap { Id = Guid.NewGuid(), Name = "M", CurrentJson = "{}", Status = MindMapStatus.Updating };
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(new RegenerateMindMapRequest { Id = map.Id }, CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapUpdateInProgress, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_EnqueuesJob_WhenPendingMeetingsExist()
    {
        var map = new MindMap { Id = Guid.NewGuid(), Name = "M", CurrentJson = "{}", Status = MindMapStatus.Failed, LastError = "x" };
        map.Meetings.Add(new MindMapMeeting { MeetingTranscriptId = Guid.NewGuid(), ProcessedAt = null });
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(new RegenerateMindMapRequest { Id = map.Id }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(MindMapStatus.Updating, map.Status);
        _backgroundJobClient.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ClearsFailedState_WhenNothingPending()
    {
        var map = new MindMap { Id = Guid.NewGuid(), Name = "M", CurrentJson = "{}", Status = MindMapStatus.Failed, LastError = "x" };
        map.Meetings.Add(new MindMapMeeting { MeetingTranscriptId = Guid.NewGuid(), ProcessedAt = DateTime.UtcNow });
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(new RegenerateMindMapRequest { Id = map.Id }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(MindMapStatus.Idle, map.Status);
        Assert.Null(map.LastError);
        _backgroundJobClient.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()), Times.Never);
    }
}
```

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~AttachMeeting | FullyQualifiedName~RegenerateMindMap" -p:UseSharedCompilation=false`
Expected: build FAILURE.

- [ ] **Step 2: Implement the three use cases**

`UseCases/AttachMeeting/AttachMeetingRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.AttachMeeting;

public class AttachMeetingRequest : IRequest<AttachMeetingResponse>
{
    public Guid MindMapId { get; set; }

    [Required]
    public Guid MeetingTranscriptId { get; set; }
}
```

`UseCases/AttachMeeting/AttachMeetingResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.AttachMeeting;

public class AttachMeetingResponse : BaseResponse
{
    public AttachMeetingResponse() { }
    public AttachMeetingResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`UseCases/AttachMeeting/AttachMeetingHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MindMaps.Infrastructure.Jobs;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.AttachMeeting;

public class AttachMeetingHandler : IRequestHandler<AttachMeetingRequest, AttachMeetingResponse>
{
    private readonly IMindMapRepository _mapRepository;
    private readonly IMeetingTranscriptRepository _meetingRepository;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<AttachMeetingHandler> _logger;

    public AttachMeetingHandler(
        IMindMapRepository mapRepository,
        IMeetingTranscriptRepository meetingRepository,
        IMeetingAccessGuard accessGuard,
        IBackgroundJobClient backgroundJobClient,
        ILogger<AttachMeetingHandler> logger)
    {
        _mapRepository = mapRepository;
        _meetingRepository = meetingRepository;
        _accessGuard = accessGuard;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    public async Task<AttachMeetingResponse> Handle(AttachMeetingRequest request, CancellationToken cancellationToken)
    {
        var map = await _mapRepository.GetByIdAsync(request.MindMapId, cancellationToken);
        if (map is null)
        {
            return new AttachMeetingResponse(ErrorCodes.ResourceNotFound);
        }

        var meeting = await _meetingRepository.GetByIdAsync(request.MeetingTranscriptId, cancellationToken);
        if (meeting is null || !_accessGuard.CanAccess(meeting))
        {
            _logger.LogWarning(
                "Meeting {MeetingId} not found or not accessible for mind map {MindMapId}",
                request.MeetingTranscriptId, request.MindMapId);
            return new AttachMeetingResponse(ErrorCodes.ResourceNotFound);
        }

        if (map.Meetings.Any(m => m.MeetingTranscriptId == meeting.Id))
        {
            return new AttachMeetingResponse(ErrorCodes.MindMapMeetingAlreadyAttached);
        }

        map.Meetings.Add(new MindMapMeeting
        {
            Id = Guid.NewGuid(),
            MindMapId = map.Id,
            MeetingTranscriptId = meeting.Id,
            AttachedAt = DateTime.UtcNow
        });
        map.Status = MindMapStatus.Updating;
        map.UpdatedAt = DateTime.UtcNow;
        await _mapRepository.SaveChangesAsync(cancellationToken);

        _backgroundJobClient.Enqueue<MindMapUpdateJob>(j => j.RunAsync(map.Id, CancellationToken.None));
        _logger.LogInformation(
            "Attached meeting {MeetingId} to mind map {MindMapId} and enqueued update",
            meeting.Id, map.Id);
        return new AttachMeetingResponse();
    }
}
```

`UseCases/DetachMeeting/DetachMeetingRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.DetachMeeting;

public class DetachMeetingRequest : IRequest<DetachMeetingResponse>
{
    public Guid MindMapId { get; set; }
    public Guid MeetingTranscriptId { get; set; }
}
```

`UseCases/DetachMeeting/DetachMeetingResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.DetachMeeting;

public class DetachMeetingResponse : BaseResponse
{
    public DetachMeetingResponse() { }
    public DetachMeetingResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`UseCases/DetachMeeting/DetachMeetingHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.DetachMeeting;

public class DetachMeetingHandler : IRequestHandler<DetachMeetingRequest, DetachMeetingResponse>
{
    private readonly IMindMapRepository _repository;

    public DetachMeetingHandler(IMindMapRepository repository)
    {
        _repository = repository;
    }

    public async Task<DetachMeetingResponse> Handle(DetachMeetingRequest request, CancellationToken cancellationToken)
    {
        var map = await _repository.GetByIdAsync(request.MindMapId, cancellationToken);
        if (map is null)
        {
            return new DetachMeetingResponse(ErrorCodes.ResourceNotFound);
        }

        var link = map.Meetings.FirstOrDefault(m => m.MeetingTranscriptId == request.MeetingTranscriptId);
        if (link is null)
        {
            return new DetachMeetingResponse(ErrorCodes.ResourceNotFound);
        }

        map.Meetings.Remove(link);
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);
        return new DetachMeetingResponse();
    }
}
```

`UseCases/RegenerateMindMap/RegenerateMindMapRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.RegenerateMindMap;

public class RegenerateMindMapRequest : IRequest<RegenerateMindMapResponse>
{
    public Guid Id { get; set; }
}
```

`UseCases/RegenerateMindMap/RegenerateMindMapResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.RegenerateMindMap;

public class RegenerateMindMapResponse : BaseResponse
{
    public RegenerateMindMapResponse() { }
    public RegenerateMindMapResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`UseCases/RegenerateMindMap/RegenerateMindMapHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Infrastructure.Jobs;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.RegenerateMindMap;

public class RegenerateMindMapHandler : IRequestHandler<RegenerateMindMapRequest, RegenerateMindMapResponse>
{
    private readonly IMindMapRepository _repository;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public RegenerateMindMapHandler(IMindMapRepository repository, IBackgroundJobClient backgroundJobClient)
    {
        _repository = repository;
        _backgroundJobClient = backgroundJobClient;
    }

    public async Task<RegenerateMindMapResponse> Handle(RegenerateMindMapRequest request, CancellationToken cancellationToken)
    {
        var map = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (map is null)
        {
            return new RegenerateMindMapResponse(ErrorCodes.ResourceNotFound);
        }

        if (map.Status == MindMapStatus.Updating)
        {
            return new RegenerateMindMapResponse(ErrorCodes.MindMapUpdateInProgress);
        }

        var hasPending = map.Meetings.Any(m => m.ProcessedAt == null);
        if (!hasPending)
        {
            map.Status = MindMapStatus.Idle;
            map.LastError = null;
            map.UpdatedAt = DateTime.UtcNow;
            await _repository.SaveChangesAsync(cancellationToken);
            return new RegenerateMindMapResponse();
        }

        map.Status = MindMapStatus.Updating;
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);
        _backgroundJobClient.Enqueue<MindMapUpdateJob>(j => j.RunAsync(map.Id, CancellationToken.None));
        return new RegenerateMindMapResponse();
    }
}
```

- [ ] **Step 3: Add the controller endpoints**

Add to `MindMapsController.cs` (plus the three new `using` directives):

```csharp
    [HttpPost("{id:guid}/meetings")]
    [FeatureAuthorize(Feature.Anela_MindMaps, AccessLevel.Write)]
    public async Task<ActionResult<AttachMeetingResponse>> AttachMeeting(
        Guid id,
        [FromBody] AttachMeetingRequest request,
        CancellationToken ct = default)
    {
        request.MindMapId = id;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpDelete("{id:guid}/meetings/{meetingId:guid}")]
    [FeatureAuthorize(Feature.Anela_MindMaps, AccessLevel.Write)]
    public async Task<ActionResult<DetachMeetingResponse>> DetachMeeting(
        Guid id,
        Guid meetingId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new DetachMeetingRequest { MindMapId = id, MeetingTranscriptId = meetingId }, ct);
        return HandleResponse(result);
    }

    [HttpPost("{id:guid}/regenerate")]
    [FeatureAuthorize(Feature.Anela_MindMaps, AccessLevel.Write)]
    public async Task<ActionResult<RegenerateMindMapResponse>> Regenerate(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RegenerateMindMapRequest { Id = id }, ct);
        return HandleResponse(result);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~AttachMeeting | FullyQualifiedName~RegenerateMindMap" -p:UseSharedCompilation=false`
Expected: all 6 PASS.

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: add attach, detach and regenerate mind map use cases"
```

---

### Task 10: Save document (auto-lock) and restore version use cases + endpoints

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/SaveMindMapDocument/{SaveMindMapDocumentRequest,SaveMindMapDocumentHandler,SaveMindMapDocumentResponse}.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/RestoreMindMapVersion/{RestoreMindMapVersionRequest,RestoreMindMapVersionHandler,RestoreMindMapVersionResponse}.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/MindMapsController.cs` (2 new endpoints)
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/SaveMindMapDocumentHandlerTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/RestoreMindMapVersionHandlerTests.cs`

**Interfaces:**
- Consumes: `MindMapLockService` (Task 5), `MindMapDocumentValidator`/`MindMapJson` (Task 1), `ICurrentUserService` (same interface `MeetingAccessGuard` uses — copy its `using` directives from `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingAccessGuard.cs` for the exact namespace; email via `_currentUserService.GetCurrentUser().Email`).
- Produces: `PUT /{id}/document` (body `{ documentJson }`) → `SaveMindMapDocumentResponse { string DocumentJson }`; `POST /{id}/versions/{versionNumber}/restore` → `RestoreMindMapVersionResponse { string DocumentJson }`. Task 11's hooks call these.

Handler rules:
- **SaveDocument**: map missing → `ResourceNotFound`. `Status == Updating` → `MindMapUpdateInProgress`. `request.DocumentJson` unparseable (`JsonException`) → `MindMapInvalidDocument` with `Params = { { "Error", ex.Message } }`. `MindMapDocumentValidator` errors → `MindMapInvalidDocument` with `Params = { { "Errors", string.Join(" ", errors) } }`. `submitted.RootNodeId != current.RootNodeId` → `MindMapInvalidDocument` with `Params = { { "Errors", "Root node cannot be changed." } }`. User email empty → `ValidationError`. Otherwise apply `MindMapLockService.ApplyUserEdit(current, submitted, email)`, save serialized result to `CurrentJson`, bump `UpdatedAt`, return the saved JSON (client swaps its local state for the server's canonical document with real ids/locks).
- **RestoreVersion**: map missing → `ResourceNotFound`. `Status == Updating` → `MindMapUpdateInProgress`. Version number missing → `ResourceNotFound`. Otherwise snapshot `CurrentJson` as a new version (`VersionNumber = max + 1`, `TriggerMeetingId = null`), set `CurrentJson = version.Json`, bump `UpdatedAt`, save, return the restored JSON.

- [ ] **Step 1: Write the failing tests**

`backend/test/Anela.Heblo.Tests/Features/MindMaps/SaveMindMapDocumentHandlerTests.cs` (mock `ICurrentUserService` the same way `MeetingAccessGuardTests` does — returning a user whose `Email` is `ondra@anela.cz`):

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Application.Features.MindMaps.UseCases.SaveMindMapDocument;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class SaveMindMapDocumentHandlerTests
{
    private readonly Mock<IMindMapRepository> _repository = new();

    private static MindMap MapWithDoc(out MindMapDocument doc)
    {
        doc = new MindMapDocument
        {
            RootNodeId = "root",
            Nodes = new List<MindMapNode>
            {
                new() { Id = "root", Title = "Projekt" },
                new() { Id = "a", ParentId = "root", Title = "Větev" }
            }
        };
        return new MindMap
        {
            Id = Guid.NewGuid(), Name = "Projekt",
            CurrentJson = MindMapJson.Serialize(doc), Status = MindMapStatus.Idle
        };
    }

    // CreateSut: construct SaveMindMapDocumentHandler with _repository, new MindMapLockService(),
    // and a mocked ICurrentUserService returning Email "ondra@anela.cz"
    // (copy the mock setup style from MeetingAccessGuardTests).

    [Fact]
    public async Task Handle_LocksEditedNode_AndReturnsCanonicalJson()
    {
        var map = MapWithDoc(out var doc);
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        var submitted = MindMapJson.Clone(doc);
        submitted.Nodes.Single(n => n.Id == "a").Title = "Přejmenováno";

        var response = await CreateSut().Handle(new SaveMindMapDocumentRequest
        {
            Id = map.Id,
            DocumentJson = MindMapJson.Serialize(submitted)
        }, CancellationToken.None);

        Assert.True(response.Success);
        var saved = MindMapJson.Deserialize(map.CurrentJson);
        Assert.Equal("ondra@anela.cz", saved.Nodes.Single(n => n.Id == "a").LockedBy);
        Assert.Equal(map.CurrentJson, response.DocumentJson);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhileUpdating()
    {
        var map = MapWithDoc(out _);
        map.Status = MindMapStatus.Updating;
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(new SaveMindMapDocumentRequest
        {
            Id = map.Id, DocumentJson = map.CurrentJson
        }, CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapUpdateInProgress, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_ReturnsInvalidDocument_OnMalformedJson()
    {
        var map = MapWithDoc(out _);
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(new SaveMindMapDocumentRequest
        {
            Id = map.Id, DocumentJson = "not json"
        }, CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapInvalidDocument, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_ReturnsInvalidDocument_WhenRootChanged()
    {
        var map = MapWithDoc(out var doc);
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);
        var submitted = MindMapJson.Clone(doc);
        submitted.RootNodeId = "a";
        submitted.Nodes.Single(n => n.Id == "root").ParentId = "a";
        submitted.Nodes.Single(n => n.Id == "a").ParentId = null;

        var response = await CreateSut().Handle(new SaveMindMapDocumentRequest
        {
            Id = map.Id, DocumentJson = MindMapJson.Serialize(submitted)
        }, CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapInvalidDocument, response.ErrorCode);
    }
}
```

`backend/test/Anela.Heblo.Tests/Features/MindMaps/RestoreMindMapVersionHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.UseCases.RestoreMindMapVersion;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class RestoreMindMapVersionHandlerTests
{
    private readonly Mock<IMindMapRepository> _repository = new();

    private RestoreMindMapVersionHandler CreateSut() => new(_repository.Object);

    [Fact]
    public async Task Handle_RestoresVersionJson_AndSnapshotsCurrent()
    {
        var map = new MindMap { Id = Guid.NewGuid(), Name = "M", CurrentJson = "{\"v\":\"current\"}", Status = MindMapStatus.Idle };
        map.Versions.Add(new MindMapVersion { VersionNumber = 1, Json = "{\"v\":\"old\"}" });
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(
            new RestoreMindMapVersionRequest { Id = map.Id, VersionNumber = 1 }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("{\"v\":\"old\"}", map.CurrentJson);
        Assert.Equal("{\"v\":\"old\"}", response.DocumentJson);
        var snapshot = map.Versions.Single(v => v.VersionNumber == 2);
        Assert.Equal("{\"v\":\"current\"}", snapshot.Json);
        Assert.Null(snapshot.TriggerMeetingId);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_ForUnknownVersion()
    {
        var map = new MindMap { Id = Guid.NewGuid(), Name = "M", CurrentJson = "{}", Status = MindMapStatus.Idle };
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(
            new RestoreMindMapVersionRequest { Id = map.Id, VersionNumber = 99 }, CancellationToken.None);

        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhileUpdating()
    {
        var map = new MindMap { Id = Guid.NewGuid(), Name = "M", CurrentJson = "{}", Status = MindMapStatus.Updating };
        map.Versions.Add(new MindMapVersion { VersionNumber = 1, Json = "{}" });
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

        var response = await CreateSut().Handle(
            new RestoreMindMapVersionRequest { Id = map.Id, VersionNumber = 1 }, CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapUpdateInProgress, response.ErrorCode);
    }
}
```

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~SaveMindMapDocument | FullyQualifiedName~RestoreMindMapVersion" -p:UseSharedCompilation=false`
Expected: build FAILURE.

- [ ] **Step 2: Implement the two use cases**

`UseCases/SaveMindMapDocument/SaveMindMapDocumentRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.SaveMindMapDocument;

public class SaveMindMapDocumentRequest : IRequest<SaveMindMapDocumentResponse>
{
    public Guid Id { get; set; }

    [Required]
    public string DocumentJson { get; set; } = null!;
}
```

`UseCases/SaveMindMapDocument/SaveMindMapDocumentResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.SaveMindMapDocument;

public class SaveMindMapDocumentResponse : BaseResponse
{
    public string DocumentJson { get; set; } = null!;

    public SaveMindMapDocumentResponse() { }
    public SaveMindMapDocumentResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`UseCases/SaveMindMapDocument/SaveMindMapDocumentHandler.cs` (add the `ICurrentUserService` `using` matching `MeetingAccessGuard.cs`):

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.SaveMindMapDocument;

public class SaveMindMapDocumentHandler : IRequestHandler<SaveMindMapDocumentRequest, SaveMindMapDocumentResponse>
{
    private readonly IMindMapRepository _repository;
    private readonly MindMapLockService _lockService;
    private readonly ICurrentUserService _currentUserService;

    public SaveMindMapDocumentHandler(
        IMindMapRepository repository,
        MindMapLockService lockService,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _lockService = lockService;
        _currentUserService = currentUserService;
    }

    public async Task<SaveMindMapDocumentResponse> Handle(SaveMindMapDocumentRequest request, CancellationToken cancellationToken)
    {
        var map = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (map is null)
        {
            return new SaveMindMapDocumentResponse(ErrorCodes.ResourceNotFound);
        }

        if (map.Status == MindMapStatus.Updating)
        {
            return new SaveMindMapDocumentResponse(ErrorCodes.MindMapUpdateInProgress);
        }

        MindMapDocument submitted;
        try
        {
            submitted = MindMapJson.Deserialize(request.DocumentJson);
        }
        catch (JsonException ex)
        {
            return new SaveMindMapDocumentResponse(
                ErrorCodes.MindMapInvalidDocument,
                new Dictionary<string, string> { { "Error", ex.Message } });
        }

        var errors = MindMapDocumentValidator.Validate(submitted);
        if (errors.Count > 0)
        {
            return new SaveMindMapDocumentResponse(
                ErrorCodes.MindMapInvalidDocument,
                new Dictionary<string, string> { { "Errors", string.Join(" ", errors) } });
        }

        var current = MindMapJson.Deserialize(map.CurrentJson);
        if (submitted.RootNodeId != current.RootNodeId)
        {
            return new SaveMindMapDocumentResponse(
                ErrorCodes.MindMapInvalidDocument,
                new Dictionary<string, string> { { "Errors", "Root node cannot be changed." } });
        }

        var userEmail = _currentUserService.GetCurrentUser().Email;
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return new SaveMindMapDocumentResponse(ErrorCodes.ValidationError);
        }

        var result = _lockService.ApplyUserEdit(current, submitted, userEmail);
        map.CurrentJson = MindMapJson.Serialize(result);
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);

        return new SaveMindMapDocumentResponse { DocumentJson = map.CurrentJson };
    }
}
```

`UseCases/RestoreMindMapVersion/RestoreMindMapVersionRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.RestoreMindMapVersion;

public class RestoreMindMapVersionRequest : IRequest<RestoreMindMapVersionResponse>
{
    public Guid Id { get; set; }
    public int VersionNumber { get; set; }
}
```

`UseCases/RestoreMindMapVersion/RestoreMindMapVersionResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.RestoreMindMapVersion;

public class RestoreMindMapVersionResponse : BaseResponse
{
    public string DocumentJson { get; set; } = null!;

    public RestoreMindMapVersionResponse() { }
    public RestoreMindMapVersionResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`UseCases/RestoreMindMapVersion/RestoreMindMapVersionHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.RestoreMindMapVersion;

public class RestoreMindMapVersionHandler : IRequestHandler<RestoreMindMapVersionRequest, RestoreMindMapVersionResponse>
{
    private readonly IMindMapRepository _repository;

    public RestoreMindMapVersionHandler(IMindMapRepository repository)
    {
        _repository = repository;
    }

    public async Task<RestoreMindMapVersionResponse> Handle(RestoreMindMapVersionRequest request, CancellationToken cancellationToken)
    {
        var map = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (map is null)
        {
            return new RestoreMindMapVersionResponse(ErrorCodes.ResourceNotFound);
        }

        if (map.Status == MindMapStatus.Updating)
        {
            return new RestoreMindMapVersionResponse(ErrorCodes.MindMapUpdateInProgress);
        }

        var version = map.Versions.FirstOrDefault(v => v.VersionNumber == request.VersionNumber);
        if (version is null)
        {
            return new RestoreMindMapVersionResponse(ErrorCodes.ResourceNotFound);
        }

        var nextVersionNumber = map.Versions.Max(v => v.VersionNumber) + 1;
        map.Versions.Add(new MindMapVersion
        {
            Id = Guid.NewGuid(),
            MindMapId = map.Id,
            VersionNumber = nextVersionNumber,
            Json = map.CurrentJson,
            CreatedAt = DateTime.UtcNow,
            TriggerMeetingId = null
        });
        map.CurrentJson = version.Json;
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);

        return new RestoreMindMapVersionResponse { DocumentJson = map.CurrentJson };
    }
}
```

- [ ] **Step 3: Add the controller endpoints**

Add to `MindMapsController.cs` (plus `using` directives):

```csharp
    [HttpPut("{id:guid}/document")]
    [FeatureAuthorize(Feature.Anela_MindMaps, AccessLevel.Write)]
    public async Task<ActionResult<SaveMindMapDocumentResponse>> SaveDocument(
        Guid id,
        [FromBody] SaveMindMapDocumentRequest request,
        CancellationToken ct = default)
    {
        request.Id = id;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpPost("{id:guid}/versions/{versionNumber:int}/restore")]
    [FeatureAuthorize(Feature.Anela_MindMaps, AccessLevel.Write)]
    public async Task<ActionResult<RestoreMindMapVersionResponse>> RestoreVersion(
        Guid id,
        int versionNumber,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new RestoreMindMapVersionRequest { Id = id, VersionNumber = versionNumber }, ct);
        return HandleResponse(result);
    }
```

- [ ] **Step 4: Run backend suite for the module + build + format**

Run: `dotnet build && dotnet format && dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMap" --no-build -p:UseSharedCompilation=false`
Expected: all MindMap tests PASS, no format diffs left.

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: add mind map document save with auto-lock and version restore"
```

---

### Task 11: Frontend — dependencies, API hooks, list page, routes, sidebar

**Files:**
- Modify: `frontend/package.json` (add `@xyflow/react`, `dagre`, `@types/dagre`)
- Create: `frontend/src/api/hooks/useMindMaps.ts`
- Create: `frontend/src/components/pages/automation/mindmaps/MindMapListPage.tsx`
- Modify: `frontend/src/App.tsx` (2 routes next to the meeting-tasks routes, ~line 444)
- Modify: `frontend/src/components/Layout/Sidebar.tsx` (item in the "anela" section, ~line 100)

**Interfaces:**
- Consumes: `getAuthenticatedApiClient` from `../client`; backend endpoints from Tasks 8-10; `useMeetingTasksList` from `frontend/src/api/hooks/useMeetingTasks.ts` (reused later by the attach dialog).
- Produces: `MIND_MAPS_KEYS`, hooks `useMindMapsList`, `useMindMapDetail`, `useCreateMindMap`, `useDeleteMindMap`, `useAttachMeeting`, `useDetachMeeting`, `useRegenerateMindMap`, `useSaveMindMapDocument`, `useRestoreMindMapVersion`; TS types `MindMapListItem`, `MindMapDetail`, `AttachedMeeting`, `MindMapVersionInfo`, `MindMapStatusValue`. Tasks 12-13 consume these.

- [ ] **Step 1: Install dependencies**

```bash
cd frontend && npm install @xyflow/react dagre && npm install --save-dev @types/dagre
```

Expected: versions land in `package.json` (React Flow v12 `@xyflow/react` supports React 18; `dagre` is the layout engine).

- [ ] **Step 2: Write the hooks**

`frontend/src/api/hooks/useMindMaps.ts` (mirrors `useMeetingTasks.ts` raw-fetch pattern):

```typescript
// TODO: migrate to generated client when /api/mind-maps is added to NSwag.
// Pattern matches useMeetingTasks.ts.
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

// --- Types (raw JSON: dates are ISO strings) ---

export type MindMapStatusValue = "Idle" | "Updating" | "Failed";

export interface MindMapListItem {
  id: string;
  name: string;
  description: string | null;
  status: MindMapStatusValue;
  meetingCount: number;
  updatedAt: string;
}

export interface MindMapListResponse {
  items: MindMapListItem[];
}

export interface AttachedMeeting {
  meetingTranscriptId: string;
  subject: string;
  plaudCreatedAt: string;
  attachedAt: string;
  processedAt: string | null;
}

export interface MindMapVersionInfo {
  versionNumber: number;
  createdAt: string;
  triggerMeetingId: string | null;
  triggerMeetingSubject: string | null;
}

export interface MindMapDetail {
  id: string;
  name: string;
  description: string | null;
  status: MindMapStatusValue;
  lastError: string | null;
  documentJson: string;
  meetings: AttachedMeeting[];
  versions: MindMapVersionInfo[];
}

// --- Query keys ---

export const MIND_MAPS_KEYS = {
  all: ["mindMaps"] as const,
  list: ["mindMaps"] as const,
  detail: (id: string) => ["mindMaps", id] as const,
} as const;

// --- Raw-fetch client helper ---

async function fetchJson<T>(path: string, init: RequestInit): Promise<T> {
  const apiClient = await getAuthenticatedApiClient();
  const url = `${(apiClient as any).baseUrl}${path}`;
  const response = await (apiClient as any).http.fetch(url, init);
  if (!response.ok) {
    throw new Error(`API error: ${response.status}`);
  }
  return response.json() as Promise<T>;
}

const JSON_HEADERS = { "Content-Type": "application/json", Accept: "application/json" };

// --- Queries ---

export function useMindMapsList() {
  return useQuery<MindMapListResponse>({
    queryKey: MIND_MAPS_KEYS.list,
    queryFn: () =>
      fetchJson<MindMapListResponse>("/api/mind-maps", {
        method: "GET",
        headers: { Accept: "application/json" },
      }),
  });
}

const UPDATING_POLL_INTERVAL_MS = 3000;

export function useMindMapDetail(id: string) {
  return useQuery<MindMapDetail>({
    queryKey: MIND_MAPS_KEYS.detail(id),
    enabled: !!id,
    refetchInterval: (query) =>
      query.state.data?.status === "Updating" ? UPDATING_POLL_INTERVAL_MS : false,
    queryFn: () =>
      fetchJson<MindMapDetail>(`/api/mind-maps/${encodeURIComponent(id)}`, {
        method: "GET",
        headers: { Accept: "application/json" },
      }),
  });
}

// --- Mutations ---

export function useCreateMindMap() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: { name: string; description: string | null }) =>
      fetchJson<{ id: string }>("/api/mind-maps", {
        method: "POST",
        headers: JSON_HEADERS,
        body: JSON.stringify(input),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.all }),
  });
}

export function useDeleteMindMap() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      fetchJson<{ success: boolean }>(`/api/mind-maps/${encodeURIComponent(id)}`, {
        method: "DELETE",
        headers: { Accept: "application/json" },
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.all }),
  });
}

export function useAttachMeeting() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: { mindMapId: string; meetingTranscriptId: string }) =>
      fetchJson<{ success: boolean }>(
        `/api/mind-maps/${encodeURIComponent(input.mindMapId)}/meetings`,
        {
          method: "POST",
          headers: JSON_HEADERS,
          body: JSON.stringify({ meetingTranscriptId: input.meetingTranscriptId }),
        },
      ),
    onSuccess: (_d, vars) =>
      qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.detail(vars.mindMapId) }),
  });
}

export function useDetachMeeting() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: { mindMapId: string; meetingTranscriptId: string }) =>
      fetchJson<{ success: boolean }>(
        `/api/mind-maps/${encodeURIComponent(input.mindMapId)}/meetings/${encodeURIComponent(input.meetingTranscriptId)}`,
        { method: "DELETE", headers: { Accept: "application/json" } },
      ),
    onSuccess: (_d, vars) =>
      qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.detail(vars.mindMapId) }),
  });
}

export function useRegenerateMindMap() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      fetchJson<{ success: boolean }>(`/api/mind-maps/${encodeURIComponent(id)}/regenerate`, {
        method: "POST",
        headers: { Accept: "application/json" },
      }),
    onSuccess: (_d, id) => qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.detail(id) }),
  });
}

export function useSaveMindMapDocument() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: { mindMapId: string; documentJson: string }) =>
      fetchJson<{ documentJson: string }>(
        `/api/mind-maps/${encodeURIComponent(input.mindMapId)}/document`,
        {
          method: "PUT",
          headers: JSON_HEADERS,
          body: JSON.stringify({ documentJson: input.documentJson }),
        },
      ),
    onSuccess: (_d, vars) =>
      qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.detail(vars.mindMapId) }),
  });
}

export function useRestoreMindMapVersion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: { mindMapId: string; versionNumber: number }) =>
      fetchJson<{ documentJson: string }>(
        `/api/mind-maps/${encodeURIComponent(input.mindMapId)}/versions/${input.versionNumber}/restore`,
        { method: "POST", headers: { Accept: "application/json" } },
      ),
    onSuccess: (_d, vars) =>
      qc.invalidateQueries({ queryKey: MIND_MAPS_KEYS.detail(vars.mindMapId) }),
  });
}
```

- [ ] **Step 3: Write the list page**

`frontend/src/components/pages/automation/mindmaps/MindMapListPage.tsx`. Follow the layout conventions of `MeetingTasksPage.tsx` (same header/table/tailwind classes — read it first and reuse its structure). Content requirements:

```tsx
import React, { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Plus, Trash2 } from "lucide-react";
import toast from "react-hot-toast";
import {
  MindMapListItem,
  useCreateMindMap,
  useDeleteMindMap,
  useMindMapsList,
} from "../../../../api/hooks/useMindMaps";

const STATUS_BADGE: Record<string, { label: string; className: string }> = {
  Idle: { label: "Aktuální", className: "bg-emerald-100 text-emerald-800" },
  Updating: { label: "Aktualizuje se…", className: "bg-amber-100 text-amber-800" },
  Failed: { label: "Chyba", className: "bg-red-100 text-red-800" },
};

const MindMapListPage: React.FC = () => {
  const navigate = useNavigate();
  const { data, isLoading, error } = useMindMapsList();
  const createMap = useCreateMindMap();
  const deleteMap = useDeleteMindMap();
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [newName, setNewName] = useState("");
  const [newDescription, setNewDescription] = useState("");

  const handleCreate = async () => {
    if (!newName.trim()) return;
    try {
      const result = await createMap.mutateAsync({
        name: newName.trim(),
        description: newDescription.trim() || null,
      });
      setIsCreateOpen(false);
      setNewName("");
      setNewDescription("");
      navigate(`/automation/mind-maps/${result.id}`);
    } catch {
      toast.error("Vytvoření mapy se nezdařilo");
    }
  };

  const handleDelete = async (map: MindMapListItem) => {
    if (!window.confirm(`Smazat mapu „${map.name}"? Tato akce je nevratná.`)) return;
    try {
      await deleteMap.mutateAsync(map.id);
      toast.success("Mapa smazána");
    } catch {
      toast.error("Smazání mapy se nezdařilo");
    }
  };

  // Render (reuse MeetingTasksPage.tsx page skeleton/classes):
  // - header: h1 "Myšlenkové mapy" + primary button [Plus] "Nová mapa" (opens create dialog)
  //   with data-testid="mindmap-create-button"
  // - loading spinner / error state / empty state ("Zatím žádné mapy…")
  // - table rows (data-testid="mindmap-row"): name (link to detail), description,
  //   STATUS_BADGE pill, meetingCount, new Date(updatedAt).toLocaleDateString("cs-CZ"),
  //   Trash2 icon button (stopPropagation, handleDelete)
  // - create dialog (same modal pattern as ManageAccessModal.tsx): name input
  //   (data-testid="mindmap-name-input"), description textarea, submit button
  //   (data-testid="mindmap-create-submit", disabled while createMap.isPending)
  return (/* ... per above ... */);
};

export default MindMapListPage;
```

The commented render block is a binding spec — implement all of it (the table, states, and dialog), copying concrete class names from `MeetingTasksPage.tsx` so the page is visually identical to the rest of the app.

- [ ] **Step 4: Register route and sidebar entry**

In `frontend/src/App.tsx`, next to the meeting-tasks routes add (import the page at the top near the MeetingTasks imports). Only the list route is registered in this task — the detail route arrives with the editor page in Task 13, so do NOT create a placeholder detail component:

```tsx
                        <Route path="/automation/mind-maps" element={guard("/automation/mind-maps", <MindMapListPage />)} />
```

Consequence for this task: `MindMapListPage`'s create flow navigates to `/automation/mind-maps/${result.id}`, which has no route yet — that is expected and resolves in Task 13.

In `frontend/src/components/Layout/Sidebar.tsx`, in the `anela` section's `items` after the `meeting-tasks` entry add:

```tsx
        {
          id: "mind-maps",
          name: "Myšlenkové mapy",
          href: "/automation/mind-maps",
          key: "/automation/mind-maps",
        },
```

Check `frontend/src/components/Layout/__tests__/Sidebar.test.tsx` — if it asserts the exact item list of the anela section, extend the expectation.

- [ ] **Step 5: Build, lint, test**

Run: `cd frontend && npm run lint && CI=false npm run build && npm test -- --watchAll=false`
Expected: build green, existing tests (incl. Sidebar tests) pass.

- [ ] **Step 6: Commit**

```bash
git add frontend/package.json frontend/package-lock.json frontend/src
git commit -m "feat: add mind maps hooks, list page, route and sidebar entry"
```

---

### Task 12: Frontend — document utils and flow-graph conversion (tested)

**Files:**
- Create: `frontend/src/components/pages/automation/mindmaps/mindMapDocument.ts`
- Create: `frontend/src/components/pages/automation/mindmaps/mindMapFlow.ts`
- Test: `frontend/src/components/pages/automation/mindmaps/__tests__/mindMapDocument.test.ts`
- Test: `frontend/src/components/pages/automation/mindmaps/__tests__/mindMapFlow.test.ts`

**Interfaces:**
- Consumes: nothing (pure TS) + `dagre`.
- Produces: the types and pure functions below; Task 13's page/canvas consume them verbatim.

- [ ] **Step 1: Write the failing tests**

`__tests__/mindMapDocument.test.ts`:

```typescript
import {
  addChildNode,
  deleteNode,
  MindMapDocument,
  parseDocument,
  renameNode,
  setNodePosition,
  toggleCollapsed,
  updateNodeFields,
  visibleNodeIds,
} from "../mindMapDocument";

const doc = (): MindMapDocument => ({
  schemaVersion: 1,
  rootNodeId: "root",
  nodes: [
    { id: "root", parentId: null, title: "Projekt", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
    { id: "a", parentId: "root", title: "Větev A", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
    { id: "b", parentId: "a", title: "List B", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
  ],
  suppressedNodes: [],
});

test("parseDocument throws on malformed json", () => {
  expect(() => parseDocument("not json")).toThrow();
});

test("renameNode returns new doc without mutating the original", () => {
  const original = doc();
  const renamed = renameNode(original, "a", "Nový název");
  expect(renamed.nodes.find((n) => n.id === "a")!.title).toBe("Nový název");
  expect(original.nodes.find((n) => n.id === "a")!.title).toBe("Větev A");
});

test("updateNodeFields patches only given fields", () => {
  const updated = updateNodeFields(doc(), "a", { status: "blocked", owner: "Ondra" });
  const a = updated.nodes.find((n) => n.id === "a")!;
  expect(a.status).toBe("blocked");
  expect(a.owner).toBe("Ondra");
  expect(a.title).toBe("Větev A");
});

test("addChildNode appends a node with a tmp- id under the parent", () => {
  const { doc: updated, newNodeId } = addChildNode(doc(), "a", "Nové dítě");
  const added = updated.nodes.find((n) => n.id === newNodeId)!;
  expect(newNodeId.startsWith("tmp-")).toBe(true);
  expect(added.parentId).toBe("a");
  expect(added.title).toBe("Nové dítě");
});

test("deleteNode removes the node and its descendants, never the root", () => {
  const updated = deleteNode(doc(), "a");
  expect(updated.nodes.map((n) => n.id)).toEqual(["root"]);
  expect(deleteNode(doc(), "root").nodes).toHaveLength(3);
});

test("setNodePosition stores the dragged position", () => {
  const updated = setNodePosition(doc(), "b", { x: 10, y: 20 });
  expect(updated.nodes.find((n) => n.id === "b")!.position).toEqual({ x: 10, y: 20 });
});

test("visibleNodeIds hides descendants of collapsed nodes", () => {
  const collapsed = toggleCollapsed(doc(), "a");
  expect(visibleNodeIds(collapsed)).toEqual(new Set(["root", "a"]));
});
```

`__tests__/mindMapFlow.test.ts`:

```typescript
import { MindMapDocument } from "../mindMapDocument";
import { toFlowGraph } from "../mindMapFlow";

const doc = (): MindMapDocument => ({
  schemaVersion: 1,
  rootNodeId: "root",
  nodes: [
    { id: "root", parentId: null, title: "Projekt", notes: null, status: "active", owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false },
    { id: "a", parentId: "root", title: "Větev", notes: null, status: "done", owner: null, lockedBy: "ondra@anela.cz", sourceMeetingIds: [], position: { x: 300, y: 40 }, collapsed: false },
  ],
  suppressedNodes: [],
});

test("toFlowGraph produces one flow node per visible doc node and edges to parents", () => {
  const { nodes, edges } = toFlowGraph(doc());
  expect(nodes).toHaveLength(2);
  expect(edges).toEqual([
    expect.objectContaining({ source: "root", target: "a" }),
  ]);
});

test("toFlowGraph keeps saved positions and lays out unsaved ones", () => {
  const { nodes } = toFlowGraph(doc());
  const a = nodes.find((n) => n.id === "a")!;
  const root = nodes.find((n) => n.id === "root")!;
  expect(a.position).toEqual({ x: 300, y: 40 });
  expect(Number.isFinite(root.position.x)).toBe(true);
});

test("toFlowGraph passes lock and status into node data", () => {
  const { nodes } = toFlowGraph(doc());
  const a = nodes.find((n) => n.id === "a")!;
  expect(a.data).toEqual(
    expect.objectContaining({ title: "Větev", status: "done", isLocked: true }),
  );
});
```

Run: `cd frontend && npm test -- --watchAll=false --testPathPattern=mindmaps`
Expected: FAIL — modules do not exist.

- [ ] **Step 2: Implement `mindMapDocument.ts`**

```typescript
// TS mirror of the backend MindMapDocument JSON contract (camelCase).
// All helpers are pure: they return a NEW document and never mutate inputs.

export type MindMapNodeStatus = "active" | "done" | "blocked" | "idea";

export interface MindMapNodePosition {
  x: number;
  y: number;
}

export interface MindMapNode {
  id: string;
  parentId: string | null;
  title: string;
  notes: string | null;
  status: MindMapNodeStatus;
  owner: string | null;
  lockedBy: string | null;
  sourceMeetingIds: string[];
  position: MindMapNodePosition | null;
  collapsed: boolean;
}

export interface SuppressedNode {
  title: string;
  deletedBy: string | null;
}

export interface MindMapDocument {
  schemaVersion: number;
  rootNodeId: string;
  nodes: MindMapNode[];
  suppressedNodes: SuppressedNode[];
}

export function parseDocument(json: string): MindMapDocument {
  const parsed = JSON.parse(json) as MindMapDocument;
  if (!parsed || !Array.isArray(parsed.nodes) || !parsed.rootNodeId) {
    throw new Error("Invalid mind map document");
  }
  return parsed;
}

function withNodes(doc: MindMapDocument, nodes: MindMapNode[]): MindMapDocument {
  return { ...doc, nodes };
}

function patchNode(
  doc: MindMapDocument,
  nodeId: string,
  patch: Partial<MindMapNode>,
): MindMapDocument {
  return withNodes(
    doc,
    doc.nodes.map((n) => (n.id === nodeId ? { ...n, ...patch } : n)),
  );
}

export function renameNode(doc: MindMapDocument, nodeId: string, title: string): MindMapDocument {
  return patchNode(doc, nodeId, { title });
}

export function updateNodeFields(
  doc: MindMapDocument,
  nodeId: string,
  patch: Partial<Pick<MindMapNode, "title" | "notes" | "owner" | "status">>,
): MindMapDocument {
  return patchNode(doc, nodeId, patch);
}

export function setNodePosition(
  doc: MindMapDocument,
  nodeId: string,
  position: MindMapNodePosition,
): MindMapDocument {
  return patchNode(doc, nodeId, { position });
}

export function toggleCollapsed(doc: MindMapDocument, nodeId: string): MindMapDocument {
  const node = doc.nodes.find((n) => n.id === nodeId);
  if (!node) return doc;
  return patchNode(doc, nodeId, { collapsed: !node.collapsed });
}

export function addChildNode(
  doc: MindMapDocument,
  parentId: string,
  title: string,
): { doc: MindMapDocument; newNodeId: string } {
  const newNodeId = `tmp-${crypto.randomUUID()}`;
  const node: MindMapNode = {
    id: newNodeId,
    parentId,
    title,
    notes: null,
    status: "active",
    owner: null,
    lockedBy: null,
    sourceMeetingIds: [],
    position: null,
    collapsed: false,
  };
  return { doc: withNodes(doc, [...doc.nodes, node]), newNodeId };
}

function descendantIds(doc: MindMapDocument, nodeId: string): Set<string> {
  const childrenByParent = new Map<string, string[]>();
  for (const node of doc.nodes) {
    if (node.parentId) {
      const siblings = childrenByParent.get(node.parentId) ?? [];
      childrenByParent.set(node.parentId, [...siblings, node.id]);
    }
  }
  const result = new Set<string>();
  const queue = [nodeId];
  while (queue.length > 0) {
    const current = queue.shift()!;
    result.add(current);
    for (const child of childrenByParent.get(current) ?? []) queue.push(child);
  }
  return result;
}

export function deleteNode(doc: MindMapDocument, nodeId: string): MindMapDocument {
  if (nodeId === doc.rootNodeId) return doc;
  const toRemove = descendantIds(doc, nodeId);
  return withNodes(doc, doc.nodes.filter((n) => !toRemove.has(n.id)));
}

/** Ids of nodes whose ancestors are all expanded (collapsed nodes stay visible, their subtrees hide). */
export function visibleNodeIds(doc: MindMapDocument): Set<string> {
  const byId = new Map(doc.nodes.map((n) => [n.id, n]));
  const visible = new Set<string>();
  for (const node of doc.nodes) {
    let ancestor = node.parentId ? byId.get(node.parentId) : null;
    let hidden = false;
    while (ancestor) {
      if (ancestor.collapsed) {
        hidden = true;
        break;
      }
      ancestor = ancestor.parentId ? byId.get(ancestor.parentId) : null;
    }
    if (!hidden) visible.add(node.id);
  }
  return visible;
}
```

Note for CRA/TS 4.9: if `crypto.randomUUID` typing complains, use
`` const newNodeId = `tmp-${Math.random().toString(36).slice(2)}${Math.random().toString(36).slice(2)}`; `` — temp ids only need page-local uniqueness; the server replaces them on save.

- [ ] **Step 3: Implement `mindMapFlow.ts`**

```typescript
import dagre from "dagre";
import type { Edge, Node } from "@xyflow/react";
import { MindMapDocument, MindMapNodeStatus, visibleNodeIds } from "./mindMapDocument";

export interface MindMapFlowData extends Record<string, unknown> {
  title: string;
  status: MindMapNodeStatus;
  owner: string | null;
  isLocked: boolean;
  isRoot: boolean;
  collapsed: boolean;
  childCount: number;
}

export type MindMapFlowNode = Node<MindMapFlowData>;

const NODE_WIDTH = 220;
const NODE_HEIGHT = 64;

export function toFlowGraph(doc: MindMapDocument): {
  nodes: MindMapFlowNode[];
  edges: Edge[];
} {
  const visible = visibleNodeIds(doc);
  const visibleNodes = doc.nodes.filter((n) => visible.has(n.id));

  // Auto-layout (left-to-right tree) for every visible node; saved positions win.
  const graph = new dagre.graphlib.Graph();
  graph.setGraph({ rankdir: "LR", nodesep: 24, ranksep: 80 });
  graph.setDefaultEdgeLabel(() => ({}));
  for (const node of visibleNodes) {
    graph.setNode(node.id, { width: NODE_WIDTH, height: NODE_HEIGHT });
  }
  for (const node of visibleNodes) {
    if (node.parentId && visible.has(node.parentId)) {
      graph.setEdge(node.parentId, node.id);
    }
  }
  dagre.layout(graph);

  const childCount = new Map<string, number>();
  for (const node of doc.nodes) {
    if (node.parentId) {
      childCount.set(node.parentId, (childCount.get(node.parentId) ?? 0) + 1);
    }
  }

  const nodes: MindMapFlowNode[] = visibleNodes.map((node) => {
    const layouted = graph.node(node.id);
    return {
      id: node.id,
      type: "mindMapNode",
      position: node.position ?? {
        x: layouted.x - NODE_WIDTH / 2,
        y: layouted.y - NODE_HEIGHT / 2,
      },
      data: {
        title: node.title,
        status: node.status,
        owner: node.owner,
        isLocked: node.lockedBy !== null,
        isRoot: node.id === doc.rootNodeId,
        collapsed: node.collapsed,
        childCount: childCount.get(node.id) ?? 0,
      },
    };
  });

  const edges: Edge[] = visibleNodes
    .filter((n) => n.parentId && visible.has(n.parentId))
    .map((n) => ({
      id: `${n.parentId}->${n.id}`,
      source: n.parentId!,
      target: n.id,
      type: "smoothstep",
    }));

  return { nodes, edges };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd frontend && npm test -- --watchAll=false --testPathPattern=mindmaps`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/automation/mindmaps
git commit -m "feat: add mind map document utils and react flow conversion"
```

---

### Task 13: Frontend — the editor page (canvas, side panel, save with auto-lock, leave guard)

**Files:**
- Create: `frontend/src/components/pages/automation/mindmaps/MindMapFlowNode.tsx`
- Create: `frontend/src/components/pages/automation/mindmaps/MindMapCanvas.tsx`
- Create: `frontend/src/components/pages/automation/mindmaps/MindMapSidePanel.tsx`
- Create: `frontend/src/components/pages/automation/mindmaps/MindMapDetailPage.tsx`
- Modify: `frontend/src/App.tsx` (register the detail route)

**Interfaces:**
- Consumes: Task 11 hooks, Task 12 utils, `useUnsavedChangesDialog` from `frontend/src/hooks/useUnsavedChangesDialog.ts`, `UnsavedChangesDialog` from `frontend/src/components/dialogs/UnsavedChangesDialog.tsx`, `useMeetingTasksList` from `useMeetingTasks.ts` (attach dialog source).
- Produces: the detail route page. Behavioral contract for the E2E test (Task 14): testids `mindmap-canvas`, `mindmap-node` (each canvas node), `mindmap-node-lock` (lock badge), `mindmap-save-button`, `mindmap-status-badge`, `mindmap-attach-button`, `mindmap-attach-option` (rows in attach dialog), `mindmap-panel-title-input`, `mindmap-regenerate-button`.

Before implementing, read `MeetingTaskDetailPage.tsx` for the page skeleton/back-navigation conventions and `useUnsavedChangesDialog.ts` (its doc comment explains that sidebar clicks are NOT blocked under BrowserRouter — route the page's own back/cancel buttons through `requestNavigation`).

- [ ] **Step 1: Implement `MindMapFlowNode.tsx`** (custom React Flow node)

```tsx
import React from "react";
import { Handle, NodeProps, Position } from "@xyflow/react";
import { ChevronDown, ChevronRight, Lock } from "lucide-react";
import { MindMapFlowNode as FlowNodeType } from "./mindMapFlow";

const STATUS_ACCENT: Record<string, string> = {
  active: "border-l-sky-500",
  done: "border-l-emerald-500",
  blocked: "border-l-red-500",
  idea: "border-l-amber-400",
};

const MindMapFlowNode: React.FC<NodeProps<FlowNodeType>> = ({ data, selected }) => (
  <div
    data-testid="mindmap-node"
    className={`w-[220px] rounded-md border border-l-4 bg-white dark:bg-graphite-surface px-3 py-2 shadow-sm
      ${STATUS_ACCENT[data.status] ?? "border-l-neutral-300"}
      ${selected ? "ring-2 ring-sky-400" : ""}`}
  >
    <Handle type="target" position={Position.Left} className="!bg-neutral-400" />
    <div className="flex items-center gap-1">
      <span className={`truncate text-sm ${data.isRoot ? "font-semibold" : ""}`}>{data.title}</span>
      {data.isLocked && (
        <Lock data-testid="mindmap-node-lock" className="h-3.5 w-3.5 shrink-0 text-neutral-500" />
      )}
      {data.childCount > 0 &&
        (data.collapsed ? (
          <ChevronRight className="h-3.5 w-3.5 shrink-0 text-neutral-400" />
        ) : (
          <ChevronDown className="h-3.5 w-3.5 shrink-0 text-neutral-400" />
        ))}
    </div>
    {data.owner && <div className="truncate text-xs text-neutral-500">{data.owner}</div>}
    <Handle type="source" position={Position.Right} className="!bg-neutral-400" />
  </div>
);

export default MindMapFlowNode;
```

- [ ] **Step 2: Implement `MindMapCanvas.tsx`**

```tsx
import React, { useMemo } from "react";
import { Background, Controls, ReactFlow } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { MindMapDocument } from "./mindMapDocument";
import { toFlowGraph } from "./mindMapFlow";
import MindMapFlowNode from "./MindMapFlowNode";

const nodeTypes = { mindMapNode: MindMapFlowNode };

interface MindMapCanvasProps {
  document: MindMapDocument;
  isReadOnly: boolean;
  selectedNodeId: string | null;
  onSelectNode: (nodeId: string | null) => void;
  onNodeDragStop: (nodeId: string, position: { x: number; y: number }) => void;
  onNodeDoubleClick: (nodeId: string) => void;
}

const MindMapCanvas: React.FC<MindMapCanvasProps> = ({
  document: doc,
  isReadOnly,
  selectedNodeId,
  onSelectNode,
  onNodeDragStop,
  onNodeDoubleClick,
}) => {
  const { nodes, edges } = useMemo(() => toFlowGraph(doc), [doc]);
  const nodesWithSelection = useMemo(
    () => nodes.map((n) => ({ ...n, selected: n.id === selectedNodeId, draggable: !isReadOnly })),
    [nodes, selectedNodeId, isReadOnly],
  );

  return (
    <div data-testid="mindmap-canvas" className="h-full w-full">
      <ReactFlow
        nodes={nodesWithSelection}
        edges={edges}
        nodeTypes={nodeTypes}
        fitView
        nodesConnectable={false}
        onNodeClick={(_e, node) => onSelectNode(node.id)}
        onPaneClick={() => onSelectNode(null)}
        onNodeDragStop={(_e, node) => onNodeDragStop(node.id, node.position)}
        onNodeDoubleClick={(_e, node) => onNodeDoubleClick(node.id)}
        proOptions={{ hideAttribution: true }}
      >
        <Background gap={16} />
        <Controls showInteractive={false} />
      </ReactFlow>
    </div>
  );
};

export default MindMapCanvas;
```

- [ ] **Step 3: Implement `MindMapSidePanel.tsx`**

Three tabs — full behavioral spec (implement everything listed; reuse form/tab classes from `MeetingTaskDetailPage.tsx`):

```tsx
// Props:
interface MindMapSidePanelProps {
  detail: MindMapDetail;                       // from useMindMapDetail
  document: MindMapDocument;                   // local (possibly dirty) doc
  selectedNodeId: string | null;
  isReadOnly: boolean;                         // status === "Updating"
  onUpdateNode: (nodeId: string, patch: Partial<Pick<MindMapNode, "title" | "notes" | "owner" | "status">>) => void;
  onAddChild: (parentId: string) => void;
  onDeleteNode: (nodeId: string) => void;
  onToggleCollapsed: (nodeId: string) => void;
}
// Tab "Uzel": when selectedNodeId is null show hint "Vyberte uzel na plátně".
//   Otherwise: title input (data-testid="mindmap-panel-title-input"), notes textarea,
//   owner input, status select (active/done/blocked/idea with Czech labels
//   Aktivní/Hotovo/Blokováno/Nápad), buttons "Přidat poduzel", "Sbalit/Rozbalit",
//   and "Smazat uzel" (hidden for the root; window.confirm before delete).
//   If the node is locked show "Uzamčeno uživatelem {lockedBy}" info line —
//   editing stays allowed (it re-locks to the current user on save).
//   All inputs disabled when isReadOnly.
// Tab "Porady": list detail.meetings (subject, new Date(plaudCreatedAt).toLocaleDateString("cs-CZ"),
//   pill "Zpracováno"/"Čeká" by processedAt), detach button per row (confirm),
//   and "Připojit poradu" button (data-testid="mindmap-attach-button") opening a modal
//   listing useMeetingTasksList() items not yet attached — each row
//   data-testid="mindmap-attach-option", click = useAttachMeeting().mutateAsync then close.
// Tab "Historie": list detail.versions (versionNumber, new Date(createdAt).toLocaleString("cs-CZ"),
//   triggerMeetingSubject ?? "Ruční obnova"), per row "Obnovit" button (confirm) calling
//   useRestoreMindMapVersion; disabled when isReadOnly or dirty (prompt to save first via toast).
```

- [ ] **Step 4: Implement `MindMapDetailPage.tsx`**

```tsx
import React, { useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { ArrowLeft, RefreshCw, Save } from "lucide-react";
import toast from "react-hot-toast";
import UnsavedChangesDialog from "../../../dialogs/UnsavedChangesDialog";
import { useUnsavedChangesDialog } from "../../../../hooks/useUnsavedChangesDialog";
import {
  useMindMapDetail,
  useRegenerateMindMap,
  useSaveMindMapDocument,
} from "../../../../api/hooks/useMindMaps";
import {
  addChildNode,
  deleteNode,
  MindMapDocument,
  parseDocument,
  setNodePosition,
  toggleCollapsed,
  updateNodeFields,
} from "./mindMapDocument";
import MindMapCanvas from "./MindMapCanvas";
import MindMapSidePanel from "./MindMapSidePanel";

const MindMapDetailPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { data: detail, isLoading, error } = useMindMapDetail(id ?? "");
  const saveDocument = useSaveMindMapDocument();
  const regenerate = useRegenerateMindMap();

  const [localDoc, setLocalDoc] = useState<MindMapDocument | null>(null);
  const [isDirty, setIsDirty] = useState(false);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const loadedJsonRef = useRef<string | null>(null);

  const isReadOnly = detail?.status === "Updating";

  // Adopt server document whenever it changes and there are no local edits.
  useEffect(() => {
    if (!detail) return;
    if (!isDirty && detail.documentJson !== loadedJsonRef.current) {
      loadedJsonRef.current = detail.documentJson;
      setLocalDoc(parseDocument(detail.documentJson));
    }
  }, [detail, isDirty]);

  const applyEdit = (next: MindMapDocument) => {
    setLocalDoc(next);
    setIsDirty(true);
  };

  const handleSave = async (): Promise<boolean> => {
    if (!id || !localDoc) return false;
    try {
      const result = await saveDocument.mutateAsync({
        mindMapId: id,
        documentJson: JSON.stringify(localDoc),
      });
      loadedJsonRef.current = result.documentJson;
      setLocalDoc(parseDocument(result.documentJson));
      setIsDirty(false);
      toast.success("Mapa uložena");
      return true;
    } catch {
      toast.error("Uložení mapy se nezdařilo");
      return false;
    }
  };

  const { dialogProps, requestNavigation } = useUnsavedChangesDialog(isDirty, handleSave);

  // Render spec (reuse MeetingTaskDetailPage.tsx page skeleton):
  // - loading / error / not-found states
  // - header: back button (ArrowLeft) via requestNavigation("/automation/mind-maps"),
  //   h1 detail.name, status badge (data-testid="mindmap-status-badge": Aktuální /
  //   Aktualizuje se… with spinner / Chyba with detail.lastError in a tooltip),
  //   "Regenerovat" button (data-testid="mindmap-regenerate-button", RefreshCw,
  //   visible when status === "Failed" or some meeting is pending, calls
  //   regenerate.mutateAsync(id)), Save button (data-testid="mindmap-save-button",
  //   disabled when !isDirty || isReadOnly || saveDocument.isPending)
  // - body: flex row — MindMapCanvas (flex-1, min-h-[70vh]) + MindMapSidePanel (w-96)
  //   wired to applyEdit via updateNodeFields/addChildNode/deleteNode/toggleCollapsed/
  //   setNodePosition; double-click on a node selects it and focuses the panel title input
  // - isReadOnly: amber banner "Mapa se právě aktualizuje — úpravy jsou dočasně zamčené."
  // - <UnsavedChangesDialog {...dialogProps} /> at the end
  return (/* ... per above ... */);
};

export default MindMapDetailPage;
```

The commented render spec is binding — implement all listed states, testids and wiring.

- [ ] **Step 5: Register the detail route**

In `frontend/src/App.tsx`, directly after the mind-maps list route added in Task 11, add (with the matching import next to `MindMapListPage`):

```tsx
                        <Route path="/automation/mind-maps/:id" element={<MindMapDetailPage />} />
```

This mirrors the meeting-tasks pair: the list route is menu-guarded, the detail route is not (matching `App.tsx:445`).

- [ ] **Step 6: Build, lint, run FE tests**

Run: `cd frontend && npm run lint && CI=false npm run build && npm test -- --watchAll=false --testPathPattern=mindmaps`
Expected: green. Manually sanity-check with `npm start` + backend `dotnet run` if desired (map create → attach → canvas renders).

- [ ] **Step 7: Commit**

```bash
git add frontend/src
git commit -m "feat: add react flow mind map editor with side panel, save and leave guard"
```

---

### Task 14: Staging stub config + E2E test

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.Staging.json` (enable stub updater)
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (document the default)
- Create: `frontend/test/e2e/mindmaps/mindmap.spec.ts`
- Modify: `frontend/playwright.config.ts` (mindmaps project)
- Modify: `docs/testing/e2e-module-guide.md` (register the new module folder)

**Interfaces:**
- Consumes: UI testids from Tasks 11/13; `navigateToApp` helper; deployed staging environment.
- Produces: nightly E2E coverage of create → attach → stub generation → rename → lock.

- [ ] **Step 1: Configuration**

In `backend/src/Anela.Heblo.API/appsettings.json` add (defaults, real Claude updater):

```json
  "MindMaps": {
    "UseStubUpdater": false
  },
```

In `backend/src/Anela.Heblo.API/appsettings.Staging.json` add:

```json
  "MindMaps": {
    "UseStubUpdater": true
  },
```

The stub makes staging E2E deterministic (each attached meeting adds exactly one `Porada: <subject>` node) and keeps nightly runs off the Anthropic bill. This is configuration, not a secret — appsettings is correct; do NOT put it in Key Vault. Note in the PR description that production keeps the real Claude updater.

- [ ] **Step 2: Write the E2E spec**

`frontend/test/e2e/mindmaps/mindmap.spec.ts`:

```typescript
import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';

// Runs against deployed staging (MindMaps:UseStubUpdater=true there), so the
// generated node is deterministic: "Porada: <subject>".
test.describe('Mind maps', () => {
  test('create map, attach meeting, stub generates node, rename locks it', async ({ page }) => {
    await navigateToApp(page);
    const mapName = `E2E mapa ${Date.now()}`;

    // Create
    await page.goto('/automation/mind-maps');
    await page.getByTestId('mindmap-create-button').click();
    await page.getByTestId('mindmap-name-input').fill(mapName);
    await page.getByTestId('mindmap-create-submit').click();

    // Lands on detail with just the root node
    await expect(page.getByTestId('mindmap-canvas')).toBeVisible({ timeout: 15000 });
    await expect(page.getByTestId('mindmap-node')).toHaveCount(1);

    // Attach the first available meeting — fixtures policy: throw, never skip
    await page.getByRole('tab', { name: 'Porady' }).click();
    await page.getByTestId('mindmap-attach-button').click();
    const options = page.getByTestId('mindmap-attach-option');
    if ((await options.count()) === 0) {
      throw new Error(
        'No meeting transcripts available on staging — seed at least one meeting (docs/testing/test-data-fixtures.md)',
      );
    }
    await options.first().click();

    // Stub updater runs in background; poll until status returns to Idle
    await expect(page.getByTestId('mindmap-status-badge')).toHaveText('Aktuální', {
      timeout: 60000,
    });
    await expect(page.getByTestId('mindmap-node')).toHaveCount(2);

    // Rename the generated node → auto-lock on save
    const generatedNode = page.getByTestId('mindmap-node').filter({ hasText: 'Porada:' });
    await generatedNode.dblclick();
    await page.getByTestId('mindmap-panel-title-input').fill('Ručně upravený uzel');
    await page.getByTestId('mindmap-save-button').click();
    await expect(
      page.getByTestId('mindmap-node').filter({ hasText: 'Ručně upravený uzel' })
        .getByTestId('mindmap-node-lock'),
    ).toBeVisible({ timeout: 15000 });

    // Cleanup
    await page.goto('/automation/mind-maps');
    const row = page.getByTestId('mindmap-row').filter({ hasText: mapName });
    page.once('dialog', (dialog) => dialog.accept());
    await row.getByRole('button').last().click();
    await expect(row).toHaveCount(0);
  });
});
```

- [ ] **Step 3: Register the Playwright project and module guide**

In `frontend/playwright.config.ts`, copy the existing per-folder project block pattern (e.g. the `core` one at ~line 141) and add:

```typescript
    {
      name: 'mindmaps',
      testDir: './test/e2e/mindmaps',
      use: { ...devices['Desktop Chrome'], storageState: authFile },
      dependencies: ['setup'],
    },
```

(Match the exact shape of the neighboring project entries — copy whatever fields they use verbatim.)

In `docs/testing/e2e-module-guide.md` add a `mindmaps` row/section mirroring the other modules (folder `frontend/test/e2e/mindmaps/`, owns routes `/automation/mind-maps*`).

- [ ] **Step 4: Run the E2E (post-deploy gate)**

E2E targets **deployed staging** — it cannot validate uncommitted local changes. Before the feature is deployed to staging, verify only that the spec compiles and is listed:

```bash
cd frontend && npx playwright test --project=mindmaps --list
```

Expected: 1 test listed. After the branch is merged and deployed to staging (with the staging DB migrated and `MindMaps:UseStubUpdater=true`), run for real:

```bash
./scripts/run-playwright-tests.sh mindmaps
```

Expected: PASS. If staging has no meetings, the test throws with a seeding instruction (by design — fixtures policy).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.json backend/src/Anela.Heblo.API/appsettings.Staging.json frontend/test/e2e/mindmaps frontend/playwright.config.ts docs/testing/e2e-module-guide.md
git commit -m "test: add mind maps e2e scenario with deterministic stub updater on staging"
```

---

### Task 15: Final validation gate

**Files:** none (verification only; fix whatever it surfaces).

- [ ] **Step 1: Backend full gate**

```bash
dotnet build
dotnet format --verify-no-changes || dotnet format
dotnet test --no-build -p:UseSharedCompilation=false
```

Expected: build clean, format clean, ALL backend tests pass — including `ErrorHandlingTests`, the `BaseResponse` reflection contract test, and `FeatureFlagRegistryFrontendMirrorTests` (untouched by this feature, must stay green). Reminder: `AccessMatrixGen` crash output during tests is non-fatal noise; a 0%-CPU hang means another worktree is testing concurrently.

- [ ] **Step 2: Frontend full gate**

```bash
cd frontend
npm run lint
CI=false npm run build
npm test -- --watchAll=false
```

Expected: all green. `npm run build` is the real type gate (tsc alone false-greens in this repo).

- [ ] **Step 3: Migration & deployment checklist (manual, note in PR)**

- Migration `AddMindMapsTables` must be applied manually to staging (`Heblo_TST`) before E2E can pass: `dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API` with staging connection string in user secrets. Production applies migrations automatically at startup.
- Staging config `MindMaps:UseStubUpdater=true` ships via `appsettings.Staging.json` (Task 14) — no Azure changes needed.
- Nightly E2E picks up the `mindmaps` project automatically; run `./scripts/run-playwright-tests.sh mindmaps` once post-deploy to confirm.

- [ ] **Step 4: Update memory + commit any fixes**

Append to `memory/context/state.md`: branch `feature/meeting-mindmap`, feature complete pending staging migration + post-deploy E2E. Commit:

```bash
git add -A
git commit -m "chore: final validation fixes for mind maps feature" || echo "nothing to fix"
```

---

## Plan self-review notes

- **Spec coverage**: data model & JSON schema → Tasks 1-2; auto-lock → Task 5 + Task 10; tombstones & guard → Task 4; skill-prompt + keyed client + retry → Task 6; sequential chronological pipeline + failure handling → Task 7; endpoints incl. conflict-on-updating → Tasks 8-10; feature gating via `Anela_MindMaps` + meeting-access check on attach → Tasks 8-9; React Flow editor, side panel tabs, polling, leave guard → Tasks 11-13; stubbed-LLM E2E → Task 14; validation-before-completion → Task 15. Out-of-scope items from the spec are not implemented anywhere (correct).
- **Type consistency**: `MindMapDocument`/`MindMapJson`/`MindMapDocumentValidator` names used identically in Tasks 1, 4-10; `IMindMapRepository` members used in Tasks 7-10 match Task 2's interface; FE `MindMapDetail.documentJson`/`status` match backend `GetMindMapDetailResponse` (camelCase serialization); testids in Task 13 match Task 14's spec.
- **Known deliberate simplifications** (approved in spec): no per-map ACLs, detach does not rewrite the document, single save conflict check (no optimistic concurrency on `PUT /document` beyond the Updating gate).
