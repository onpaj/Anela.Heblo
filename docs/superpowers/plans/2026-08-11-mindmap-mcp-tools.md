# Mind Map MCP Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose three MCP tools so Claude app / Claude Code can read a mind map, discuss it, and write node-level changes back into Heblo with the same locking, versioning and validation the web UI gets.

**Architecture:** Two new pure services in the MindMaps Application slice — `MindMapOutlineRenderer` (document → text tree) and `MindMapOperationApplier` (document + operations → new document) — behind one new MediatR use case (`ApplyMindMapOperations`) that reuses the existing `MindMapLockService` for auto-locking and tombstones. A thin `MindMapMcpTools` class in the API layer wraps the two existing read handlers plus the new write handler. No REST route (it would leak into the generated TypeScript client), no frontend work, no database migration.

**Tech Stack:** .NET 8, MediatR, ModelContextProtocol.AspNetCore 1.0.0, xUnit + Moq, EF Core (Npgsql).

**Spec:** `docs/superpowers/specs/2026-08-11-mindmap-mcp-tools-design.md`

## Global Constraints

- DTOs and contracts are **classes, never C# records** (OpenAPI generators mishandle record parameter order). Small internal parameter/result objects that never cross the wire may be records.
- Every `*Response` in the Application layer **must inherit `BaseResponse`** or a reflection contract test fails in CI.
- A new `ErrorCodes` member needs a Czech string in `frontend/src/i18n.ts` or the translation-coverage test fails. The 34XX Mind Maps bucket already exists in `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs`, so no bucket edit is needed for codes in that range.
- When adding a `MindMapVersion` to `map.Versions`, **never set `Id`** — EF marks a keyed child added to a tracked parent's navigation collection as `Modified` and issues an `UPDATE` against a row that does not exist.
- Test file layout: Application-layer tests in `backend/test/Anela.Heblo.Tests/Features/MindMaps/`, MCP tool tests in `backend/test/Anela.Heblo.Tests/MCP/Tools/`.
- Node status vocabulary is exactly `active | done | blocked | idea` (`MindMapNodeStatus.All`).
- Validation gate before declaring any task done: `dotnet build` and `dotnet format` from the repo root, plus the tests named in that task.
- Solution file is at the repo root. If another worktree is running tests concurrently, `dotnet test` can hang at 0% CPU — build first, then run with `--no-build -p:UseSharedCompilation=false`.

---

### Task 1: Outline renderer and revision token

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Model/MindMapRevision.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapOutlineRenderer.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapOutlineRendererTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapRevisionTests.cs`

**Interfaces:**
- Consumes: `MindMapDocument`, `MindMapNode`, `SuppressedNode` from `Anela.Heblo.Application.Features.MindMaps.Model`.
- Produces:
  - `MindMapRevision.For(DateTime updatedAt) → string`
  - `MindMapRevision.Matches(string? revision, DateTime updatedAt) → bool`
  - `MindMapOutlineHeader(string Name, string Status, int MeetingCount, DateTime UpdatedAt, string Revision)` — record, internal parameter object
  - `MindMapOutlineRenderer.Render(MindMapDocument document, MindMapOutlineHeader header) → string`

- [ ] **Step 1: Write the failing revision test**

Create `backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapRevisionTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class MindMapRevisionTests
{
    [Fact]
    public void For_IgnoresSubMillisecondPrecision_SoTheTokenSurvivesADatabaseRoundTrip()
    {
        // Arrange — the same instant, one with sub-millisecond ticks a timestamp column drops
        var written = new DateTime(2026, 8, 11, 10, 30, 0, DateTimeKind.Utc).AddTicks(9_999);
        var readBack = new DateTime(2026, 8, 11, 10, 30, 0, DateTimeKind.Utc).AddTicks(1_000);

        // Act & Assert
        Assert.Equal(MindMapRevision.For(written), MindMapRevision.For(readBack));
    }

    [Fact]
    public void Matches_ReturnsFalse_ForADifferentTimestamp()
    {
        var updatedAt = new DateTime(2026, 8, 11, 10, 30, 0, DateTimeKind.Utc);
        var stale = MindMapRevision.For(updatedAt.AddSeconds(-5));

        Assert.False(MindMapRevision.Matches(stale, updatedAt));
        Assert.True(MindMapRevision.Matches(MindMapRevision.For(updatedAt), updatedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Matches_ReturnsFalse_WhenRevisionIsMissing(string? revision)
    {
        Assert.False(MindMapRevision.Matches(revision, DateTime.UtcNow));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~MindMapRevisionTests`
Expected: build error — `MindMapRevision` does not exist.

- [ ] **Step 3: Implement `MindMapRevision`**

Create `backend/src/Anela.Heblo.Application/Features/MindMaps/Model/MindMapRevision.cs`:

```csharp
using System.Globalization;

namespace Anela.Heblo.Application.Features.MindMaps.Model;

/// <summary>
/// Change marker handed to external (MCP) callers so a write can be rejected when the map moved
/// underneath them. Derived from <see cref="Domain.Features.MindMaps.MindMap.UpdatedAt"/>, which
/// every write path already bumps (UI save, update job, version restore).
/// </summary>
public static class MindMapRevision
{
    /// <summary>
    /// PostgreSQL timestamps keep microsecond precision, so the raw ticks a handler writes are not
    /// the ticks read back later. Truncating to whole milliseconds keeps the token stable across a
    /// database round trip — without it, the revision returned by a write would never match the one
    /// read afterwards and every second write would fail as stale.
    /// </summary>
    public static string For(DateTime updatedAt) =>
        (updatedAt.Ticks - updatedAt.Ticks % TimeSpan.TicksPerMillisecond)
            .ToString(CultureInfo.InvariantCulture);

    public static bool Matches(string? revision, DateTime updatedAt) =>
        !string.IsNullOrWhiteSpace(revision) && revision == For(updatedAt);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~MindMapRevisionTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Write the failing outline renderer tests**

Create `backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapOutlineRendererTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class MindMapOutlineRendererTests
{
    private readonly MindMapOutlineRenderer _renderer = new();

    private static readonly DateTime UpdatedAt = new(2026, 8, 9, 14, 22, 3, DateTimeKind.Utc);

    private static MindMapOutlineHeader Header() =>
        new("Web relaunch", "Idle", 7, UpdatedAt, MindMapRevision.For(UpdatedAt));

    private static MindMapDocument Document() => new()
    {
        RootNodeId = "root",
        Nodes = new List<MindMapNode>
        {
            new() { Id = "root", Title = "Web relaunch" },
            new()
            {
                Id = "a", ParentId = "root", Title = "Nový e-shop", Owner = "Ondra",
                LockedBy = "ondra@anela.cz", Notes = "Migrujeme na Shoptet"
            },
            new()
            {
                Id = "a1", ParentId = "a", Title = "Migrace produktů", Status = MindMapNodeStatus.Done,
                SourceMeetingIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() }
            },
            new() { Id = "b", ParentId = "root", Title = "Obaly", Status = MindMapNodeStatus.Idea }
        }
    };

    [Fact]
    public void Render_WritesHeader_WithStatusMeetingsUpdatedAndRevision()
    {
        var outline = _renderer.Render(Document(), Header());

        Assert.StartsWith("# Web relaunch", outline);
        Assert.Contains(
            $"status: Idle | meetings: 7 | updated: 2026-08-09T14:22:03Z | revision: {MindMapRevision.For(UpdatedAt)}",
            outline);
    }

    [Fact]
    public void Render_IndentsChildrenTwoSpacesPerLevel_InDocumentOrder()
    {
        var lines = _renderer.Render(Document(), Header()).Split(Environment.NewLine);

        var rootLine = Array.FindIndex(lines, l => l.Contains("Web relaunch") && !l.StartsWith("#"));
        Assert.Equal("root  Web relaunch  [active]", lines[rootLine]);
        Assert.Equal("  a  Nový e-shop  [active]  @Ondra  (locked)", lines[rootLine + 1]);
        Assert.Equal("    notes: Migrujeme na Shoptet", lines[rootLine + 2]);
        Assert.Equal("    a1  Migrace produktů  [done]  (2 meetings)", lines[rootLine + 3]);
        Assert.Equal("  b  Obaly  [idea]", lines[rootLine + 4]);
    }

    [Fact]
    public void Render_TruncatesNotesAt200Characters()
    {
        var document = Document();
        document.Nodes.Single(n => n.Id == "a").Notes = new string('x', 250);

        var outline = _renderer.Render(document, Header());

        Assert.Contains("notes: " + new string('x', 200) + "…", outline);
        Assert.DoesNotContain(new string('x', 201), outline);
    }

    [Fact]
    public void Render_UsesSingularForASingleSourceMeeting()
    {
        var document = Document();
        document.Nodes.Single(n => n.Id == "a1").SourceMeetingIds = new List<Guid> { Guid.NewGuid() };

        Assert.Contains("(1 meeting)", _renderer.Render(document, Header()));
    }

    [Fact]
    public void Render_ListsSuppressedTitles_WhenPresent()
    {
        var document = Document();
        document.SuppressedNodes = new List<SuppressedNode>
        {
            new() { Title = "Starý blog" },
            new() { Title = "Newsletter v2" }
        };

        var outline = _renderer.Render(document, Header());

        Assert.Contains("suppressed (do not re-create): \"Starý blog\", \"Newsletter v2\"", outline);
    }

    [Fact]
    public void Render_OmitsSuppressedLine_WhenThereAreNoTombstones()
    {
        Assert.DoesNotContain("suppressed", _renderer.Render(Document(), Header()));
    }

    [Fact]
    public void Render_HandlesASingleNodeMap()
    {
        var document = new MindMapDocument
        {
            RootNodeId = "root",
            Nodes = new List<MindMapNode> { new() { Id = "root", Title = "Projekt" } }
        };

        Assert.Contains("root  Projekt  [active]", _renderer.Render(document, Header()));
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~MindMapOutlineRendererTests`
Expected: build error — `MindMapOutlineRenderer` does not exist.

- [ ] **Step 7: Implement the renderer**

Create `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapOutlineRenderer.cs`:

```csharp
using System.Globalization;
using System.Text;
using Anela.Heblo.Application.Features.MindMaps.Model;

namespace Anela.Heblo.Application.Features.MindMaps.Services;

/// <summary>Map metadata the outline header needs; not part of any wire contract.</summary>
public record MindMapOutlineHeader(
    string Name,
    string Status,
    int MeetingCount,
    DateTime UpdatedAt,
    string Revision);

/// <summary>
/// Renders a mind map document as a compact indented text tree for MCP callers — far cheaper to
/// load and far easier to discuss than the raw document JSON. Pure and deterministic: no I/O.
/// </summary>
public class MindMapOutlineRenderer
{
    private const int NotesMaxLength = 200;
    private const string Indent = "  ";

    public string Render(MindMapDocument document, MindMapOutlineHeader header)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(header.Name);
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "status: {0} | meetings: {1} | updated: {2:yyyy-MM-ddTHH:mm:ss}Z | revision: {3}",
            header.Status, header.MeetingCount, header.UpdatedAt, header.Revision));
        sb.AppendLine();

        var childrenByParent = document.Nodes
            .Where(n => n.ParentId != null)
            .GroupBy(n => n.ParentId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        var root = document.Nodes.FirstOrDefault(n => n.Id == document.RootNodeId);
        if (root != null)
        {
            AppendNode(sb, root, childrenByParent, depth: 0);
        }

        if (document.SuppressedNodes.Count > 0)
        {
            sb.AppendLine();
            sb.Append("suppressed (do not re-create): ")
              .AppendLine(string.Join(", ", document.SuppressedNodes.Select(s => $"\"{s.Title}\"")));
        }

        return sb.ToString();
    }

    private static void AppendNode(
        StringBuilder sb,
        MindMapNode node,
        IReadOnlyDictionary<string, List<MindMapNode>> childrenByParent,
        int depth)
    {
        var prefix = string.Concat(Enumerable.Repeat(Indent, depth));

        sb.Append(prefix).Append(node.Id).Append(Indent).Append(node.Title)
          .Append(Indent).Append('[').Append(node.Status).Append(']');
        if (!string.IsNullOrWhiteSpace(node.Owner))
        {
            sb.Append(Indent).Append('@').Append(node.Owner);
        }
        if (node.LockedBy != null)
        {
            sb.Append(Indent).Append("(locked)");
        }
        if (node.SourceMeetingIds.Count > 0)
        {
            sb.Append(Indent).Append('(').Append(node.SourceMeetingIds.Count)
              .Append(node.SourceMeetingIds.Count == 1 ? " meeting)" : " meetings)");
        }
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(node.Notes))
        {
            sb.Append(prefix).Append(Indent).Append("notes: ").AppendLine(Truncate(node.Notes!));
        }

        if (childrenByParent.TryGetValue(node.Id, out var children))
        {
            foreach (var child in children)
            {
                AppendNode(sb, child, childrenByParent, depth + 1);
            }
        }
    }

    private static string Truncate(string text) =>
        text.Length <= NotesMaxLength ? text : text[..NotesMaxLength] + "…";
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMapOutlineRendererTests|FullyQualifiedName~MindMapRevisionTests"`
Expected: PASS (11 tests).

- [ ] **Step 9: Format and commit**

```bash
dotnet format
git add backend/src/Anela.Heblo.Application/Features/MindMaps/Model/MindMapRevision.cs \
        backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapOutlineRenderer.cs \
        backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapRevisionTests.cs \
        backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapOutlineRendererTests.cs
git commit -m "feat: render mind maps as a compact outline for external agents"
```

---

### Task 2: Operation applier

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Contracts/MindMapOperationDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapOperationException.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapOperationApplier.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapOperationApplierTests.cs`

**Interfaces:**
- Consumes: `MindMapDocument`, `MindMapNode`, `MindMapNodeStatus`, `MindMapJson.Clone` from Task 0 (existing code).
- Produces:
  - `MindMapOperationDto` — class with `Op`, `NodeId`, `ParentId`, `TempParentId`, `TempId`, `NewParentId`, `Title`, `Notes`, `Status`, `Owner` (all `string?` except `Op`)
  - `MindMapOperations.AddNode/UpdateNode/MoveNode/DeleteNode` — string constants
  - `MindMapOperationException(int operationIndex, string message)` with `OperationIndex`
  - `MindMapOperationApplier.Apply(MindMapDocument current, IReadOnlyList<MindMapOperationDto> operations) → MindMapOperationResult`
  - `MindMapOperationResult` — `Document`, `TempIdToNodeId` (`Dictionary<string,string>`), `AddedCount`, `UpdatedCount`, `MovedCount`, `DeletedCount`

- [ ] **Step 1: Write the failing applier tests**

Create `backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapOperationApplierTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Contracts;
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class MindMapOperationApplierTests
{
    private readonly MindMapOperationApplier _applier = new();

    private static MindMapDocument Document() => new()
    {
        RootNodeId = "root",
        Nodes = new List<MindMapNode>
        {
            new() { Id = "root", Title = "Projekt" },
            new() { Id = "a", ParentId = "root", Title = "Větev A", Notes = "poznámka", Owner = "Ondra" },
            new() { Id = "a1", ParentId = "a", Title = "Podvětev" },
            new() { Id = "b", ParentId = "root", Title = "Větev B" }
        }
    };

    private static MindMapOperationDto Add(string? parentId, string title, string? tempId = null, string? tempParentId = null) =>
        new() { Op = MindMapOperations.AddNode, ParentId = parentId, Title = title, TempId = tempId, TempParentId = tempParentId };

    [Fact]
    public void Apply_AddNode_AppendsANodeUnderTheGivenParent()
    {
        var result = _applier.Apply(Document(), new[] { Add("root", "Nová větev") });

        var added = result.Document.Nodes.Single(n => n.Title == "Nová větev");
        Assert.Equal("root", added.ParentId);
        Assert.Equal(MindMapNodeStatus.Active, added.Status);
        Assert.NotEmpty(added.Id);
        Assert.Equal(1, result.AddedCount);
    }

    [Fact]
    public void Apply_AddNode_BuildsASubtreeInOneCall_ViaTempIds()
    {
        var result = _applier.Apply(Document(), new[]
        {
            Add("root", "Kampaň", tempId: "t1"),
            Add(null, "Podklady", tempParentId: "t1"),
            Add(null, "Rozpočet", tempParentId: "t1")
        });

        var parent = result.Document.Nodes.Single(n => n.Title == "Kampaň");
        Assert.Equal(2, result.Document.Nodes.Count(n => n.ParentId == parent.Id));
        Assert.Equal(parent.Id, result.TempIdToNodeId["t1"]);
        Assert.Equal(3, result.AddedCount);
    }

    [Fact]
    public void Apply_AddNode_RejectsAnUnknownTempParent()
    {
        var ex = Assert.Throws<MindMapOperationException>(() =>
            _applier.Apply(Document(), new[] { Add(null, "Sirotek", tempParentId: "nope") }));

        Assert.Equal(0, ex.OperationIndex);
    }

    [Fact]
    public void Apply_AddNode_RejectsAnUnknownStatus()
    {
        var op = Add("root", "Nová větev");
        op.Status = "postponed";

        var ex = Assert.Throws<MindMapOperationException>(() => _applier.Apply(Document(), new[] { op }));

        Assert.Contains("postponed", ex.Message);
    }

    [Fact]
    public void Apply_UpdateNode_ChangesOnlyTheSuppliedFields()
    {
        var result = _applier.Apply(Document(), new[]
        {
            new MindMapOperationDto { Op = MindMapOperations.UpdateNode, NodeId = "a", Status = MindMapNodeStatus.Done }
        });

        var node = result.Document.Nodes.Single(n => n.Id == "a");
        Assert.Equal(MindMapNodeStatus.Done, node.Status);
        Assert.Equal("Větev A", node.Title);
        Assert.Equal("poznámka", node.Notes);
        Assert.Equal("Ondra", node.Owner);
        Assert.Equal(1, result.UpdatedCount);
    }

    [Fact]
    public void Apply_UpdateNode_ClearsNotesAndOwner_OnAnEmptyString()
    {
        var result = _applier.Apply(Document(), new[]
        {
            new MindMapOperationDto { Op = MindMapOperations.UpdateNode, NodeId = "a", Notes = "", Owner = "" }
        });

        var node = result.Document.Nodes.Single(n => n.Id == "a");
        Assert.Null(node.Notes);
        Assert.Null(node.Owner);
    }

    [Fact]
    public void Apply_UpdateNode_RefusesToClearTheTitle()
    {
        Assert.Throws<MindMapOperationException>(() => _applier.Apply(Document(), new[]
        {
            new MindMapOperationDto { Op = MindMapOperations.UpdateNode, NodeId = "a", Title = "" }
        }));
    }

    [Fact]
    public void Apply_MoveNode_ReparentsTheNode()
    {
        var result = _applier.Apply(Document(), new[]
        {
            new MindMapOperationDto { Op = MindMapOperations.MoveNode, NodeId = "a1", NewParentId = "b" }
        });

        Assert.Equal("b", result.Document.Nodes.Single(n => n.Id == "a1").ParentId);
        Assert.Equal(1, result.MovedCount);
    }

    [Fact]
    public void Apply_MoveNode_RejectsMovingANodeUnderItsOwnDescendant()
    {
        Assert.Throws<MindMapOperationException>(() => _applier.Apply(Document(), new[]
        {
            new MindMapOperationDto { Op = MindMapOperations.MoveNode, NodeId = "a", NewParentId = "a1" }
        }));
    }

    [Fact]
    public void Apply_MoveNode_RejectsMovingTheRoot()
    {
        Assert.Throws<MindMapOperationException>(() => _applier.Apply(Document(), new[]
        {
            new MindMapOperationDto { Op = MindMapOperations.MoveNode, NodeId = "root", NewParentId = "a" }
        }));
    }

    [Fact]
    public void Apply_DeleteNode_RemovesTheWholeSubtree()
    {
        var result = _applier.Apply(Document(), new[]
        {
            new MindMapOperationDto { Op = MindMapOperations.DeleteNode, NodeId = "a" }
        });

        Assert.DoesNotContain(result.Document.Nodes, n => n.Id is "a" or "a1");
        Assert.Equal(2, result.DeletedCount);
    }

    [Fact]
    public void Apply_DeleteNode_RejectsTheRoot()
    {
        Assert.Throws<MindMapOperationException>(() => _applier.Apply(Document(), new[]
        {
            new MindMapOperationDto { Op = MindMapOperations.DeleteNode, NodeId = "root" }
        }));
    }

    [Fact]
    public void Apply_ReportsTheIndexOfTheFailingOperation_AndAppliesNothing()
    {
        var current = Document();

        var ex = Assert.Throws<MindMapOperationException>(() => _applier.Apply(current, new[]
        {
            Add("root", "První"),
            new MindMapOperationDto { Op = MindMapOperations.UpdateNode, NodeId = "chybí", Title = "X" }
        }));

        Assert.Equal(1, ex.OperationIndex);
        Assert.Equal(4, current.Nodes.Count); // the caller's document is untouched
    }

    [Fact]
    public void Apply_RejectsAnUnknownOperationName()
    {
        var ex = Assert.Throws<MindMapOperationException>(() => _applier.Apply(Document(), new[]
        {
            new MindMapOperationDto { Op = "renameEverything", NodeId = "a" }
        }));

        Assert.Contains("renameEverything", ex.Message);
    }

    [Fact]
    public void Apply_RejectsAnEmptyBatch()
    {
        Assert.Throws<MindMapOperationException>(() => _applier.Apply(Document(), Array.Empty<MindMapOperationDto>()));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~MindMapOperationApplierTests`
Expected: build error — `MindMapOperationDto` / `MindMapOperationApplier` do not exist.

- [ ] **Step 3: Create the operation DTO**

Create `backend/src/Anela.Heblo.Application/Features/MindMaps/Contracts/MindMapOperationDto.cs`:

```csharp
using System.ComponentModel;

namespace Anela.Heblo.Application.Features.MindMaps.Contracts;

/// <summary>
/// One change to a mind map. A flat shape (rather than a polymorphic union) so the MCP SDK can
/// generate a single JSON schema the model can fill in. Class, not record — contract type.
/// </summary>
public class MindMapOperationDto
{
    [Description("addNode | updateNode | moveNode | deleteNode")]
    public string Op { get; set; } = null!;

    [Description("Existing node id — required by updateNode, moveNode and deleteNode")]
    public string? NodeId { get; set; }

    [Description("addNode: id of an existing node to add under")]
    public string? ParentId { get; set; }

    [Description("addNode: tempId of a node added earlier in this same batch to add under")]
    public string? TempParentId { get; set; }

    [Description("addNode: your own label for the new node so later operations in this batch can reference it as tempParentId")]
    public string? TempId { get; set; }

    [Description("moveNode: id of the new parent node")]
    public string? NewParentId { get; set; }

    [Description("Node title. Required by addNode; on updateNode it may be changed but not cleared.")]
    public string? Title { get; set; }

    [Description("Free-text notes. On updateNode an empty string clears them; omit to leave unchanged.")]
    public string? Notes { get; set; }

    [Description("active | done | blocked | idea. Defaults to active on addNode.")]
    public string? Status { get; set; }

    [Description("Owner name. On updateNode an empty string clears it; omit to leave unchanged.")]
    public string? Owner { get; set; }
}

public static class MindMapOperations
{
    public const string AddNode = "addNode";
    public const string UpdateNode = "updateNode";
    public const string MoveNode = "moveNode";
    public const string DeleteNode = "deleteNode";
}
```

- [ ] **Step 4: Create the operation exception**

Create `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapOperationException.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MindMaps.Services;

/// <summary>
/// A single operation in a batch was rejected. Carries the operation's index so the caller can be
/// told exactly which one failed — the whole batch is discarded either way.
/// </summary>
public class MindMapOperationException : Exception
{
    public int OperationIndex { get; }

    public MindMapOperationException(int operationIndex, string message) : base(message)
    {
        OperationIndex = operationIndex;
    }
}
```

- [ ] **Step 5: Implement the applier**

Create `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapOperationApplier.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Contracts;
using Anela.Heblo.Application.Features.MindMaps.Model;

namespace Anela.Heblo.Application.Features.MindMaps.Services;

public class MindMapOperationResult
{
    public MindMapDocument Document { get; init; } = null!;

    /// <summary>Caller-supplied tempId → the id this applier assigned. Not yet the final id:
    /// MindMapLockService re-assigns ids for nodes it sees as new, so the handler maps these
    /// through its AssignedIds before reporting them back.</summary>
    public Dictionary<string, string> TempIdToNodeId { get; init; } = new();

    public int AddedCount { get; init; }
    public int UpdatedCount { get; init; }
    public int MovedCount { get; init; }
    public int DeletedCount { get; init; }
}

/// <summary>
/// Applies a batch of node operations to a mind map document. Pure and all-or-nothing: it works on
/// a clone, so a rejected operation leaves the caller's document untouched and nothing is written.
/// </summary>
public class MindMapOperationApplier
{
    public MindMapOperationResult Apply(MindMapDocument current, IReadOnlyList<MindMapOperationDto> operations)
    {
        if (operations is not { Count: > 0 })
        {
            throw new MindMapOperationException(0, "No operations supplied.");
        }

        var document = MindMapJson.Clone(current);
        var tempIds = new Dictionary<string, string>();
        int added = 0, updated = 0, moved = 0, deleted = 0;

        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            switch (operation.Op)
            {
                case MindMapOperations.AddNode:
                    added += ApplyAdd(document, operation, index, tempIds);
                    break;
                case MindMapOperations.UpdateNode:
                    updated += ApplyUpdate(document, operation, index);
                    break;
                case MindMapOperations.MoveNode:
                    moved += ApplyMove(document, operation, index);
                    break;
                case MindMapOperations.DeleteNode:
                    deleted += ApplyDelete(document, operation, index);
                    break;
                default:
                    throw new MindMapOperationException(index, $"Unknown operation '{operation.Op}'.");
            }
        }

        return new MindMapOperationResult
        {
            Document = document,
            TempIdToNodeId = tempIds,
            AddedCount = added,
            UpdatedCount = updated,
            MovedCount = moved,
            DeletedCount = deleted
        };
    }

    private static int ApplyAdd(
        MindMapDocument document,
        MindMapOperationDto operation,
        int index,
        Dictionary<string, string> tempIds)
    {
        if (string.IsNullOrWhiteSpace(operation.Title))
        {
            throw new MindMapOperationException(index, "addNode requires a title.");
        }

        var parentId = ResolveParentId(document, operation, index, tempIds);
        var status = operation.Status ?? MindMapNodeStatus.Active;
        EnsureKnownStatus(status, index);

        var id = Guid.NewGuid().ToString("N");
        document.Nodes.Add(new MindMapNode
        {
            Id = id,
            ParentId = parentId,
            Title = operation.Title!,
            Notes = string.IsNullOrEmpty(operation.Notes) ? null : operation.Notes,
            Status = status,
            Owner = string.IsNullOrEmpty(operation.Owner) ? null : operation.Owner
        });

        if (!string.IsNullOrWhiteSpace(operation.TempId) && !tempIds.TryAdd(operation.TempId!, id))
        {
            throw new MindMapOperationException(index, $"Duplicate tempId '{operation.TempId}'.");
        }

        return 1;
    }

    private static string ResolveParentId(
        MindMapDocument document,
        MindMapOperationDto operation,
        int index,
        IReadOnlyDictionary<string, string> tempIds)
    {
        if (!string.IsNullOrWhiteSpace(operation.TempParentId))
        {
            return tempIds.TryGetValue(operation.TempParentId!, out var resolved)
                ? resolved
                : throw new MindMapOperationException(
                    index, $"Unknown tempParentId '{operation.TempParentId}' — no earlier operation in this batch declared it.");
        }

        if (string.IsNullOrWhiteSpace(operation.ParentId))
        {
            throw new MindMapOperationException(index, "addNode requires parentId or tempParentId.");
        }

        return document.Nodes.Any(n => n.Id == operation.ParentId)
            ? operation.ParentId!
            : throw new MindMapOperationException(index, $"Unknown parent node '{operation.ParentId}'.");
    }

    private static int ApplyUpdate(MindMapDocument document, MindMapOperationDto operation, int index)
    {
        var node = FindNode(document, operation.NodeId, index);

        if (operation.Title != null)
        {
            if (string.IsNullOrWhiteSpace(operation.Title))
            {
                throw new MindMapOperationException(index, "Title cannot be cleared.");
            }
            node.Title = operation.Title;
        }

        if (operation.Notes != null)
        {
            node.Notes = operation.Notes.Length == 0 ? null : operation.Notes;
        }

        if (operation.Owner != null)
        {
            node.Owner = operation.Owner.Length == 0 ? null : operation.Owner;
        }

        if (operation.Status != null)
        {
            EnsureKnownStatus(operation.Status, index);
            node.Status = operation.Status;
        }

        return 1;
    }

    private static int ApplyMove(MindMapDocument document, MindMapOperationDto operation, int index)
    {
        var node = FindNode(document, operation.NodeId, index);
        if (node.Id == document.RootNodeId)
        {
            throw new MindMapOperationException(index, "The root node cannot be moved.");
        }

        if (string.IsNullOrWhiteSpace(operation.NewParentId))
        {
            throw new MindMapOperationException(index, "moveNode requires newParentId.");
        }

        var parent = document.Nodes.FirstOrDefault(n => n.Id == operation.NewParentId)
            ?? throw new MindMapOperationException(index, $"Unknown parent node '{operation.NewParentId}'.");

        if (parent.Id == node.Id || IsDescendantOf(document, ancestorId: node.Id, candidateId: parent.Id))
        {
            throw new MindMapOperationException(index, "A node cannot be moved under itself or its own descendant.");
        }

        node.ParentId = parent.Id;
        return 1;
    }

    private static int ApplyDelete(MindMapDocument document, MindMapOperationDto operation, int index)
    {
        var node = FindNode(document, operation.NodeId, index);
        if (node.Id == document.RootNodeId)
        {
            throw new MindMapOperationException(index, "The root node cannot be deleted.");
        }

        var toRemove = new List<MindMapNode> { node };
        for (var i = 0; i < toRemove.Count; i++)
        {
            var parentId = toRemove[i].Id;
            toRemove.AddRange(document.Nodes.Where(n => n.ParentId == parentId));
        }

        foreach (var removed in toRemove)
        {
            document.Nodes.Remove(removed);
        }

        return toRemove.Count;
    }

    private static bool IsDescendantOf(MindMapDocument document, string ancestorId, string candidateId)
    {
        var byId = document.Nodes.ToDictionary(n => n.Id);
        var current = byId[candidateId];
        while (current.ParentId != null)
        {
            if (current.ParentId == ancestorId)
            {
                return true;
            }
            current = byId[current.ParentId];
        }
        return false;
    }

    private static MindMapNode FindNode(MindMapDocument document, string? nodeId, int index)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new MindMapOperationException(index, "Operation requires nodeId.");
        }

        return document.Nodes.FirstOrDefault(n => n.Id == nodeId)
            ?? throw new MindMapOperationException(index, $"Unknown node '{nodeId}'.");
    }

    private static void EnsureKnownStatus(string status, int index)
    {
        if (!MindMapNodeStatus.All.Contains(status))
        {
            throw new MindMapOperationException(
                index, $"Unknown status '{status}'. Allowed: {string.Join(", ", MindMapNodeStatus.All)}.");
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~MindMapOperationApplierTests`
Expected: PASS (15 tests).

- [ ] **Step 7: Format and commit**

```bash
dotnet format
git add backend/src/Anela.Heblo.Application/Features/MindMaps/Contracts/MindMapOperationDto.cs \
        backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapOperationException.cs \
        backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapOperationApplier.cs \
        backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapOperationApplierTests.cs
git commit -m "feat: apply batched node operations to a mind map document"
```

---

### Task 3: Report assigned node ids from the lock service

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapLockService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/SaveMindMapDocument/SaveMindMapDocumentHandler.cs:87-88`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapLockServiceTests.cs` (existing — update call sites, add one test)

**Interfaces:**
- Consumes: existing `MindMapLockService.ApplyUserEdit`.
- Produces: `MindMapLockService.ApplyUserEdit(MindMapDocument current, MindMapDocument submitted, string userEmail) → UserEditResult`, where `UserEditResult` has `MindMapDocument Document` and `IReadOnlyDictionary<string,string> AssignedIds` (submitted id → server-assigned id, for newly added nodes only).

**Why:** Task 4 must tell the MCP caller the real id of every node it added. `ApplyUserEdit` already builds that mapping internally and throws it away. The alternative — recovering ids by node ordering — is an implicit coupling that would break silently.

- [ ] **Step 1: Write the failing test for the new return shape**

Add to `backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapLockServiceTests.cs`:

```csharp
    [Fact]
    public void ApplyUserEdit_ReportsTheServerAssignedIdOfEachNewNode()
    {
        var submitted = MindMapJson.Clone(Current());
        submitted.Nodes.Add(new MindMapNode { Id = "client-temp", ParentId = "root", Title = "Nová větev" });

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        Assert.True(result.AssignedIds.TryGetValue("client-temp", out var assignedId));
        Assert.NotEqual("client-temp", assignedId);
        Assert.Equal("Nová větev", result.Document.Nodes.Single(n => n.Id == assignedId).Title);
        Assert.DoesNotContain(result.Document.Nodes, n => n.Id == "client-temp");
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~MindMapLockServiceTests`
Expected: build error — `MindMapDocument` has no `AssignedIds` / no `Document`.

- [ ] **Step 3: Change the lock service return type**

In `MindMapLockService.cs`, add the result type above the service class:

```csharp
/// <summary>
/// The saved document plus the id mapping for nodes the service treated as newly added
/// (submitted id → server-assigned id). Callers that need to report new ids back — the MCP
/// write path — read AssignedIds; the web UI simply reloads the document.
/// </summary>
public class UserEditResult
{
    public MindMapDocument Document { get; init; } = null!;
    public IReadOnlyDictionary<string, string> AssignedIds { get; init; } = new Dictionary<string, string>();
}
```

Change the method signature from `public MindMapDocument ApplyUserEdit(...)` to `public UserEditResult ApplyUserEdit(...)` and replace the final two lines of the method body:

```csharp
        result.SuppressedNodes = suppressedNodes;
        result.SchemaVersion = current.SchemaVersion;
        return new UserEditResult { Document = result, AssignedIds = idMap };
    }
```

- [ ] **Step 4: Update the existing caller**

In `SaveMindMapDocumentHandler.cs`, replace lines 87-88:

```csharp
        var result = _lockService.ApplyUserEdit(current, submitted, userEmail);
        map.CurrentJson = MindMapJson.Serialize(result.Document);
```

- [ ] **Step 5: Update the existing lock service tests**

In `MindMapLockServiceTests.cs`, every existing assertion reads from the returned document. Append `.Document` to each existing call so the assertions stay untouched:

```csharp
        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail).Document;
```

Leave the new test from Step 1 as-is (it uses the full result).

- [ ] **Step 6: Run the affected suites**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MindMapLockServiceTests|FullyQualifiedName~SaveMindMapDocumentHandlerTests"`
Expected: PASS, including the new `ApplyUserEdit_ReportsTheServerAssignedIdOfEachNewNode`.

- [ ] **Step 7: Format and commit**

```bash
dotnet format
git add backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapLockService.cs \
        backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/SaveMindMapDocument/SaveMindMapDocumentHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/MindMaps/MindMapLockServiceTests.cs
git commit -m "refactor: report server-assigned node ids from the mind map lock service"
```

---

### Task 4: ApplyMindMapOperations use case

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (after `MindMapInvalidDocument = 3403`)
- Modify: `frontend/src/i18n.ts` (Mind Maps module errors block, ~line 333)
- Modify: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/GetMindMapDetail/GetMindMapDetailResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/GetMindMapDetail/GetMindMapDetailHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MindMaps/MindMapsModule.cs:39-41`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/ApplyMindMapOperations/ApplyMindMapOperationsRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/ApplyMindMapOperations/ApplyMindMapOperationsResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/ApplyMindMapOperations/ApplyMindMapOperationsHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/ApplyMindMapOperationsHandlerTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MindMaps/GetMindMapDetailHandlerTests.cs` (existing — one added assertion)

**Interfaces:**
- Consumes: `MindMapOperationApplier.Apply` and `MindMapOperationResult` (Task 2), `MindMapLockService.ApplyUserEdit → UserEditResult` (Task 3), `MindMapRevision.For/Matches` (Task 1), existing `IMindMapRepository`, `MindMapDocumentValidator`, `MindMapJson`, `ICurrentUserService`.
- Produces:
  - `ApplyMindMapOperationsRequest { Guid Id; string Revision; List<MindMapOperationDto> Operations }`
  - `ApplyMindMapOperationsResponse : BaseResponse { string DocumentJson; string Revision; string Name; string Status; int MeetingCount; DateTime UpdatedAt; Dictionary<string,string> AssignedIds; int AddedCount; int UpdatedCount; int MovedCount; int DeletedCount }`
  - `ErrorCodes.MindMapRevisionMismatch = 3404`, `ErrorCodes.MindMapInvalidOperation = 3405`
  - `GetMindMapDetailResponse.UpdatedAt` (new property — the outline header needs it for the revision)

- [ ] **Step 1: Write the failing handler tests**

Create `backend/test/Anela.Heblo.Tests/Features/MindMaps/ApplyMindMapOperationsHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Contracts;
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Application.Features.MindMaps.UseCases.ApplyMindMapOperations;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using Anela.Heblo.Domain.Features.Users;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class ApplyMindMapOperationsHandlerTests
{
    private readonly Mock<IMindMapRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();

    public ApplyMindMapOperationsHandlerTests()
    {
        _currentUserService.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, "Ondra", "ondra@anela.cz", true));
        _repository.Setup(r => r.GetNextVersionNumberAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
    }

    private ApplyMindMapOperationsHandler CreateSut() =>
        new(_repository.Object, new MindMapOperationApplier(), new MindMapLockService(), _currentUserService.Object);

    private static MindMap Map(MindMapStatus status = MindMapStatus.Idle)
    {
        var document = new MindMapDocument
        {
            RootNodeId = "root",
            Nodes = new List<MindMapNode>
            {
                new() { Id = "root", Title = "Projekt" },
                new() { Id = "a", ParentId = "root", Title = "Větev A" }
            }
        };
        return new MindMap
        {
            Id = Guid.NewGuid(),
            Name = "Projekt",
            CurrentJson = MindMapJson.Serialize(document),
            Status = status,
            UpdatedAt = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc)
        };
    }

    private void Given(MindMap map) =>
        _repository.Setup(r => r.GetByIdAsync(map.Id, It.IsAny<CancellationToken>())).ReturnsAsync(map);

    private static ApplyMindMapOperationsRequest Request(MindMap map, params MindMapOperationDto[] operations) =>
        new()
        {
            Id = map.Id,
            Revision = MindMapRevision.For(map.UpdatedAt),
            Operations = operations.ToList()
        };

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenTheMapDoesNotExist()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MindMap?)null);

        var response = await CreateSut().Handle(new ApplyMindMapOperationsRequest
        {
            Id = Guid.NewGuid(),
            Revision = "1",
            Operations = new List<MindMapOperationDto>()
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_ReturnsUpdateInProgress_WhileTheJobIsRunning()
    {
        var map = Map(MindMapStatus.Updating);
        Given(map);

        var response = await CreateSut().Handle(Request(map), CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapUpdateInProgress, response.ErrorCode);
    }

    [Fact]
    public async Task Handle_ReturnsRevisionMismatch_WhenTheMapMovedSinceItWasRead()
    {
        var map = Map();
        Given(map);
        var request = Request(map);
        request.Revision = MindMapRevision.For(map.UpdatedAt.AddMinutes(-1));

        var response = await CreateSut().Handle(request, CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapRevisionMismatch, response.ErrorCode);
        Assert.Equal(MindMapRevision.For(map.UpdatedAt), response.Params!["Current"]);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsInvalidOperation_WithTheFailingIndex()
    {
        var map = Map();
        Given(map);

        var response = await CreateSut().Handle(Request(map,
            new MindMapOperationDto { Op = MindMapOperations.UpdateNode, NodeId = "a", Status = MindMapNodeStatus.Done },
            new MindMapOperationDto { Op = MindMapOperations.DeleteNode, NodeId = "chybí" }),
            CancellationToken.None);

        Assert.Equal(ErrorCodes.MindMapInvalidOperation, response.ErrorCode);
        Assert.Equal("1", response.Params!["OperationIndex"]);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_LocksAContentEditedNode_UnderTheCallersEmail()
    {
        var map = Map();
        Given(map);

        var response = await CreateSut().Handle(Request(map,
            new MindMapOperationDto { Op = MindMapOperations.UpdateNode, NodeId = "a", Title = "Přejmenováno" }),
            CancellationToken.None);

        Assert.True(response.Success);
        var saved = MindMapJson.Deserialize(response.DocumentJson);
        Assert.Equal("ondra@anela.cz", saved.Nodes.Single(n => n.Id == "a").LockedBy);
        Assert.Equal(1, response.UpdatedCount);
    }

    [Fact]
    public async Task Handle_DoesNotLockANodeThatWasOnlyMoved()
    {
        var map = Map();
        Given(map);

        var response = await CreateSut().Handle(Request(map,
            new MindMapOperationDto { Op = MindMapOperations.AddNode, ParentId = "root", Title = "Větev B", TempId = "t1" },
            new MindMapOperationDto { Op = MindMapOperations.MoveNode, NodeId = "a", NewParentId = "root" }),
            CancellationToken.None);

        var saved = MindMapJson.Deserialize(response.DocumentJson);
        Assert.Null(saved.Nodes.Single(n => n.Id == "a").LockedBy);
    }

    [Fact]
    public async Task Handle_ReportsTheFinalIdOfEachAddedNode_ByTempId()
    {
        var map = Map();
        Given(map);

        var response = await CreateSut().Handle(Request(map,
            new MindMapOperationDto { Op = MindMapOperations.AddNode, ParentId = "root", Title = "Kampaň", TempId = "t1" },
            new MindMapOperationDto { Op = MindMapOperations.AddNode, TempParentId = "t1", Title = "Rozpočet" }),
            CancellationToken.None);

        var saved = MindMapJson.Deserialize(response.DocumentJson);
        var finalId = response.AssignedIds["t1"];
        Assert.Equal("Kampaň", saved.Nodes.Single(n => n.Id == finalId).Title);
        Assert.Equal(finalId, saved.Nodes.Single(n => n.Title == "Rozpočet").ParentId);
        Assert.Equal(2, response.AddedCount);
    }

    [Fact]
    public async Task Handle_SnapshotsThePreChangeDocument_WithoutATriggerMeeting()
    {
        var map = Map();
        var before = map.CurrentJson;
        Given(map);

        await CreateSut().Handle(Request(map,
            new MindMapOperationDto { Op = MindMapOperations.DeleteNode, NodeId = "a" }),
            CancellationToken.None);

        var version = Assert.Single(map.Versions);
        Assert.Equal(3, version.VersionNumber);
        Assert.Equal(before, version.Json);
        Assert.Null(version.TriggerMeetingId);
        Assert.Equal(Guid.Empty, version.Id); // EF must generate the key, or it issues an UPDATE
    }

    [Fact]
    public async Task Handle_BumpsUpdatedAt_AndReturnsTheNewRevision()
    {
        var map = Map();
        var before = map.UpdatedAt;
        Given(map);

        var response = await CreateSut().Handle(Request(map,
            new MindMapOperationDto { Op = MindMapOperations.UpdateNode, NodeId = "a", Status = MindMapNodeStatus.Blocked }),
            CancellationToken.None);

        Assert.True(map.UpdatedAt > before);
        Assert.Equal(MindMapRevision.For(map.UpdatedAt), response.Revision);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsValidationError_WhenTheCallerHasNoEmail()
    {
        var map = Map();
        Given(map);
        _currentUserService.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, "Ondra", null, true));

        var response = await CreateSut().Handle(Request(map,
            new MindMapOperationDto { Op = MindMapOperations.UpdateNode, NodeId = "a", Title = "X" }),
            CancellationToken.None);

        Assert.Equal(ErrorCodes.ValidationError, response.ErrorCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~ApplyMindMapOperationsHandlerTests`
Expected: build error — the request/response/handler types do not exist.

If `CurrentUser`'s constructor signature differs from `(id, name, email, isAuthenticated)`, copy the exact usage from `SaveMindMapDocumentHandlerTests.cs:20-21`.

- [ ] **Step 3: Add the two error codes**

In `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, directly after `MindMapInvalidDocument = 3403,`:

```csharp
    [HttpStatusCode(HttpStatusCode.Conflict)]
    MindMapRevisionMismatch = 3404,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    MindMapInvalidOperation = 3405,
```

In `frontend/src/i18n.ts`, directly after `MindMapInvalidDocument: "Neplatný dokument myšlenkové mapy",`:

```typescript
        MindMapRevisionMismatch: "Mapa se mezitím změnila, načtěte ji znovu",
        MindMapInvalidOperation: "Neplatná úprava myšlenkové mapy",
```

- [ ] **Step 4: Add `UpdatedAt` to the detail response**

In `GetMindMapDetailResponse.cs`, after `public string? LastError { get; set; }`:

```csharp
    public DateTime UpdatedAt { get; set; }
```

In `GetMindMapDetailHandler.cs`, in the returned object initializer after `LastError = map.LastError,`:

```csharp
            UpdatedAt = map.UpdatedAt,
```

Add to `GetMindMapDetailHandlerTests.cs`, inside the existing test that asserts the mapped detail fields (the one that builds a map and asserts `Name`/`Status`), one more assertion:

```csharp
        Assert.Equal(map.UpdatedAt, response.UpdatedAt);
```

- [ ] **Step 5: Create the request**

Create `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/ApplyMindMapOperations/ApplyMindMapOperationsRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.MindMaps.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.ApplyMindMapOperations;

public class ApplyMindMapOperationsRequest : IRequest<ApplyMindMapOperationsResponse>
{
    public Guid Id { get; set; }

    /// <summary>Revision token the caller last read. Guards against writing over a change
    /// the caller has not seen.</summary>
    public string Revision { get; set; } = null!;

    public List<MindMapOperationDto> Operations { get; set; } = new();
}
```

- [ ] **Step 6: Create the response**

Create `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/ApplyMindMapOperations/ApplyMindMapOperationsResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.ApplyMindMapOperations;

public class ApplyMindMapOperationsResponse : BaseResponse
{
    public string DocumentJson { get; set; } = null!;
    public string Revision { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Status { get; set; } = null!;
    public int MeetingCount { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Caller-supplied tempId → the id the node actually got.</summary>
    public Dictionary<string, string> AssignedIds { get; set; } = new();

    public int AddedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int MovedCount { get; set; }
    public int DeletedCount { get; set; }

    public ApplyMindMapOperationsResponse() { }
    public ApplyMindMapOperationsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

- [ ] **Step 7: Create the handler**

Create `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/ApplyMindMapOperations/ApplyMindMapOperationsHandler.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.ApplyMindMapOperations;

/// <summary>
/// Applies a batch of node operations from an external agent (MCP). The result goes through the
/// same <see cref="MindMapLockService"/> the web UI save uses, so an agent edit is a user edit:
/// content changes lock the node against the meeting-update job and removals become tombstones.
/// </summary>
public class ApplyMindMapOperationsHandler
    : IRequestHandler<ApplyMindMapOperationsRequest, ApplyMindMapOperationsResponse>
{
    private readonly IMindMapRepository _repository;
    private readonly MindMapOperationApplier _applier;
    private readonly MindMapLockService _lockService;
    private readonly ICurrentUserService _currentUserService;

    public ApplyMindMapOperationsHandler(
        IMindMapRepository repository,
        MindMapOperationApplier applier,
        MindMapLockService lockService,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _applier = applier;
        _lockService = lockService;
        _currentUserService = currentUserService;
    }

    public async Task<ApplyMindMapOperationsResponse> Handle(
        ApplyMindMapOperationsRequest request,
        CancellationToken cancellationToken)
    {
        var map = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (map is null)
        {
            return new ApplyMindMapOperationsResponse(ErrorCodes.ResourceNotFound);
        }

        if (map.Status == MindMapStatus.Updating)
        {
            return new ApplyMindMapOperationsResponse(ErrorCodes.MindMapUpdateInProgress);
        }

        if (!MindMapRevision.Matches(request.Revision, map.UpdatedAt))
        {
            return new ApplyMindMapOperationsResponse(
                ErrorCodes.MindMapRevisionMismatch,
                new Dictionary<string, string> { { "Current", MindMapRevision.For(map.UpdatedAt) } });
        }

        var userEmail = _currentUserService.GetCurrentUser().Email;
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return new ApplyMindMapOperationsResponse(
                ErrorCodes.ValidationError,
                new Dictionary<string, string> { { "Error", "Missing user email" } });
        }

        MindMapDocument current;
        try
        {
            current = MindMapJson.Deserialize(map.CurrentJson);
        }
        catch (JsonException ex)
        {
            return new ApplyMindMapOperationsResponse(
                ErrorCodes.MindMapInvalidDocument,
                new Dictionary<string, string> { { "Error", ex.Message } });
        }

        MindMapOperationResult applied;
        try
        {
            applied = _applier.Apply(current, request.Operations);
        }
        catch (MindMapOperationException ex)
        {
            return new ApplyMindMapOperationsResponse(
                ErrorCodes.MindMapInvalidOperation,
                new Dictionary<string, string>
                {
                    { "OperationIndex", ex.OperationIndex.ToString(CultureInfo.InvariantCulture) },
                    { "Error", ex.Message }
                });
        }

        // Defence in depth: the applier should not be able to produce a cycle or an orphan.
        var errors = MindMapDocumentValidator.Validate(applied.Document);
        if (errors.Count > 0)
        {
            return new ApplyMindMapOperationsResponse(
                ErrorCodes.MindMapInvalidDocument,
                new Dictionary<string, string> { { "Errors", string.Join(" ", errors) } });
        }

        var edit = _lockService.ApplyUserEdit(current, applied.Document, userEmail);

        var nextVersionNumber = await _repository.GetNextVersionNumberAsync(map.Id, cancellationToken);
        map.Versions.Add(new MindMapVersion
        {
            // No explicit Id: EF marks an entity added to a tracked parent's navigation collection
            // as Modified when its key already has a value, which issues an UPDATE against a row
            // that does not exist yet.
            MindMapId = map.Id,
            VersionNumber = nextVersionNumber,
            Json = map.CurrentJson,
            CreatedAt = DateTime.UtcNow,
            TriggerMeetingId = null
        });

        map.CurrentJson = MindMapJson.Serialize(edit.Document);
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);

        return new ApplyMindMapOperationsResponse
        {
            DocumentJson = map.CurrentJson,
            Revision = MindMapRevision.For(map.UpdatedAt),
            Name = map.Name,
            Status = map.Status.ToString(),
            MeetingCount = map.Meetings.Count,
            UpdatedAt = map.UpdatedAt,
            AssignedIds = ResolveTempIds(applied.TempIdToNodeId, edit.AssignedIds),
            AddedCount = applied.AddedCount,
            UpdatedCount = applied.UpdatedCount,
            MovedCount = applied.MovedCount,
            DeletedCount = applied.DeletedCount
        };
    }

    /// <summary>
    /// The applier gives new nodes provisional ids; the lock service, which treats any unknown id
    /// as a new node, then assigns the final ones. Chain the two so the caller learns the id its
    /// tempId actually became.
    /// </summary>
    private static Dictionary<string, string> ResolveTempIds(
        IReadOnlyDictionary<string, string> tempToProvisional,
        IReadOnlyDictionary<string, string> provisionalToFinal) =>
        tempToProvisional.ToDictionary(
            entry => entry.Key,
            entry => provisionalToFinal.TryGetValue(entry.Value, out var final) ? final : entry.Value);
}
```

- [ ] **Step 8: Register the two new services**

In `MindMapsModule.cs`, after `services.AddSingleton<MindMapLockService>();`:

```csharp
        services.AddSingleton<MindMapOperationApplier>();
        services.AddSingleton<MindMapOutlineRenderer>();
```

(MediatR handlers are auto-registered by the assembly scan in `ApplicationModule`; no registration needed for the handler itself.)

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ApplyMindMapOperationsHandlerTests|FullyQualifiedName~GetMindMapDetailHandlerTests|FullyQualifiedName~ErrorHandlingTests"`
Expected: PASS (10 new handler tests + existing detail and error-handling tests).

- [ ] **Step 10: Format and commit**

```bash
dotnet format
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs \
        backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/ \
        backend/src/Anela.Heblo.Application/Features/MindMaps/MindMapsModule.cs \
        backend/test/Anela.Heblo.Tests/Features/MindMaps/ \
        frontend/src/i18n.ts
git commit -m "feat: add the ApplyMindMapOperations use case for external agents"
```

---

### Task 5: MCP tools

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/Tools/MindMapMcpTools.cs`
- Modify: `backend/src/Anela.Heblo.API/MCP/McpModule.cs:17-23`
- Test: `backend/test/Anela.Heblo.Tests/MCP/Tools/MindMapMcpToolsTests.cs`

**Interfaces:**
- Consumes: `GetMindMapListRequest` / `GetMindMapListResponse.Items` (`MindMapListItemDto`), `GetMindMapDetailRequest { Id }` / `GetMindMapDetailResponse { Name, Status, UpdatedAt, DocumentJson, Meetings }`, `ApplyMindMapOperationsRequest/Response` (Task 4), `MindMapOutlineRenderer.Render` + `MindMapOutlineHeader` (Task 1), `MindMapRevision.For` (Task 1), `MindMapOperationDto` (Task 2), `ICurrentUserService.EnsureFeatureAccess(Feature, string, AccessLevel)` from `Anela.Heblo.API.MCP.McpAuthorizationExtensions`.
- Produces: `MindMapMcpTools` with `ListMindMaps`, `GetMindMap`, `ApplyMindMapChanges`.

- [ ] **Step 1: Write the failing tool tests**

Create `backend/test/Anela.Heblo.Tests/MCP/Tools/MindMapMcpToolsTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.MindMaps.Contracts;
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Application.Features.MindMaps.UseCases.ApplyMindMapOperations;
using Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapDetail;
using Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapList;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class MindMapMcpToolsTests
{
    private static readonly string ReadRole = AccessRoles.For(Feature.Anela_MindMaps, AccessLevel.Read);
    private static readonly string WriteRole = AccessRoles.For(Feature.Anela_MindMaps, AccessLevel.Write);
    private static readonly DateTime UpdatedAt = new(2026, 8, 9, 14, 22, 3, DateTimeKind.Utc);

    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly MindMapMcpTools _tools;

    public MindMapMcpToolsTests()
    {
        // Default: caller has both. FORBIDDEN tests override.
        _currentUserService.Setup(s => s.IsInRole(ReadRole)).Returns(true);
        _currentUserService.Setup(s => s.IsInRole(WriteRole)).Returns(true);
        _tools = new MindMapMcpTools(
            _mediator.Object,
            _currentUserService.Object,
            new MindMapOutlineRenderer(),
            Mock.Of<ILogger<MindMapMcpTools>>());
    }

    private static string DocumentJson() => MindMapJson.Serialize(new MindMapDocument
    {
        RootNodeId = "root",
        Nodes = new List<MindMapNode>
        {
            new() { Id = "root", Title = "Projekt" },
            new() { Id = "a", ParentId = "root", Title = "Větev A", Owner = "Ondra" }
        }
    });

    private void GivenDetail(Guid id) =>
        _mediator.Setup(m => m.Send(It.IsAny<GetMindMapDetailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetMindMapDetailResponse
            {
                Id = id,
                Name = "Projekt",
                Status = "Idle",
                UpdatedAt = UpdatedAt,
                DocumentJson = DocumentJson()
            });

    [Fact]
    public async Task ListMindMaps_ReturnsARevisionTokenPerMap()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetMindMapListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetMindMapListResponse
            {
                Items = new List<MindMapListItemDto>
                {
                    new() { Id = Guid.NewGuid(), Name = "Projekt", Status = "Idle", MeetingCount = 2, UpdatedAt = UpdatedAt }
                }
            });

        var json = await _tools.ListMindMaps();

        Assert.Contains(MindMapRevision.For(UpdatedAt), json);
        Assert.Contains("Projekt", json);
    }

    [Fact]
    public async Task GetMindMap_RendersTheOutlineByDefault()
    {
        var id = Guid.NewGuid();
        GivenDetail(id);

        var outline = await _tools.GetMindMap(id);

        Assert.StartsWith("# Projekt", outline);
        Assert.Contains("root  Projekt  [active]", outline);
        Assert.Contains("@Ondra", outline);
        Assert.Contains($"revision: {MindMapRevision.For(UpdatedAt)}", outline);
    }

    [Fact]
    public async Task GetMindMap_ReturnsTheRawDocument_WhenFormatIsJson()
    {
        var id = Guid.NewGuid();
        GivenDetail(id);

        Assert.Equal(DocumentJson(), await _tools.GetMindMap(id, format: "json"));
    }

    [Fact]
    public async Task GetMindMap_Throws_WhenTheCallerLacksReadAccess()
    {
        _currentUserService.Setup(s => s.IsInRole(ReadRole)).Returns(false);

        var ex = await Assert.ThrowsAsync<McpException>(() => _tools.GetMindMap(Guid.NewGuid()));

        Assert.Contains("[FORBIDDEN]", ex.Message);
    }

    [Fact]
    public async Task ApplyMindMapChanges_Throws_WhenTheCallerHasReadButNotWriteAccess()
    {
        _currentUserService.Setup(s => s.IsInRole(WriteRole)).Returns(false);

        var ex = await Assert.ThrowsAsync<McpException>(() => _tools.ApplyMindMapChanges(
            Guid.NewGuid(), "1", new[] { new MindMapOperationDto { Op = MindMapOperations.DeleteNode, NodeId = "a" } }));

        Assert.Contains("[FORBIDDEN]", ex.Message);
        _mediator.Verify(m => m.Send(It.IsAny<ApplyMindMapOperationsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyMindMapChanges_MapsParametersOntoTheRequest_AndReturnsTheNewOutline()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<ApplyMindMapOperationsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplyMindMapOperationsResponse
            {
                DocumentJson = DocumentJson(),
                Revision = "999",
                Name = "Projekt",
                Status = "Idle",
                MeetingCount = 2,
                UpdatedAt = UpdatedAt,
                AssignedIds = new Dictionary<string, string> { { "t1", "abc" } },
                AddedCount = 1
            });

        var json = await _tools.ApplyMindMapChanges(id, "123", new[]
        {
            new MindMapOperationDto { Op = MindMapOperations.AddNode, ParentId = "root", Title = "Nová", TempId = "t1" }
        });

        _mediator.Verify(m => m.Send(
            It.Is<ApplyMindMapOperationsRequest>(r =>
                r.Id == id && r.Revision == "123" && r.Operations.Count == 1 && r.Operations[0].TempId == "t1"),
            It.IsAny<CancellationToken>()), Times.Once);

        // McpJsonOptions.Default applies no naming policy, so the anonymous object's
        // property names reach the wire as written.
        using var document = JsonDocument.Parse(json);
        Assert.Equal("999", document.RootElement.GetProperty("Revision").GetString());
        Assert.Contains("# Projekt", document.RootElement.GetProperty("Outline").GetString());
        Assert.Equal("abc", document.RootElement.GetProperty("AssignedIds").GetProperty("t1").GetString());
    }

    [Theory]
    [InlineData(ErrorCodes.MindMapRevisionMismatch, "[STALE]")]
    [InlineData(ErrorCodes.MindMapUpdateInProgress, "[BUSY]")]
    [InlineData(ErrorCodes.MindMapInvalidOperation, "[INVALID]")]
    [InlineData(ErrorCodes.ResourceNotFound, "[NOT FOUND]")]
    public async Task ApplyMindMapChanges_TranslatesErrorCodesIntoActionableMessages(ErrorCodes code, string marker)
    {
        _mediator.Setup(m => m.Send(It.IsAny<ApplyMindMapOperationsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplyMindMapOperationsResponse(code, new Dictionary<string, string>
            {
                { "Current", "999" }, { "OperationIndex", "2" }, { "Error", "Unknown node 'x'." }
            }));

        var ex = await Assert.ThrowsAsync<McpException>(() => _tools.ApplyMindMapChanges(
            Guid.NewGuid(), "123", new[] { new MindMapOperationDto { Op = MindMapOperations.DeleteNode, NodeId = "a" } }));

        Assert.Contains(marker, ex.Message);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~MindMapMcpToolsTests`
Expected: build error — `MindMapMcpTools` does not exist.

- [ ] **Step 3: Implement the tool class**

Create `backend/src/Anela.Heblo.API/MCP/Tools/MindMapMcpTools.cs`:

```csharp
using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.Application.Features.MindMaps.Contracts;
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Application.Features.MindMaps.UseCases.ApplyMindMapOperations;
using Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapDetail;
using Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapList;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP tools for mind maps: read the map, discuss it, write node-level changes back. Writes go
/// through the same lock/tombstone/version machinery as a web UI save, so an agent edit is a user
/// edit — the meeting-update job will never rewrite what was decided here.
/// </summary>
[McpServerToolType]
public class MindMapMcpTools
{
    private const string ResourceName = "Mind Maps";

    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;
    private readonly MindMapOutlineRenderer _renderer;
    private readonly ILogger<MindMapMcpTools> _logger;

    public MindMapMcpTools(
        IMediator mediator,
        ICurrentUserService currentUserService,
        MindMapOutlineRenderer renderer,
        ILogger<MindMapMcpTools> logger)
    {
        _mediator = mediator;
        _currentUserService = currentUserService;
        _renderer = renderer;
        _logger = logger;
    }

    [McpServerTool]
    [Description("List mind maps: id, name, description, status, attached meeting count, last update, and the revision token a write must quote.")]
    public async Task<string> ListMindMaps(CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Anela_MindMaps, ResourceName);

        try
        {
            var response = await _mediator.Send(new GetMindMapListRequest(), cancellationToken);
            return JsonSerializer.Serialize(
                response.Items.Select(m => new
                {
                    m.Id,
                    m.Name,
                    m.Description,
                    m.Status,
                    m.MeetingCount,
                    m.UpdatedAt,
                    Revision = MindMapRevision.For(m.UpdatedAt)
                }),
                McpJsonOptions.Default);
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP ListMindMaps failed");
            throw new McpException($"Failed to list mind maps: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description(
        "Read one mind map. format=\"outline\" (the default) returns an indented tree, one line per node: " +
        "`<id>  <title>  [status]  @owner  (locked)  (N meetings)`, with any notes on the following line " +
        "truncated at 200 characters. Status is one of active, done, blocked, idea. `(locked)` means a human " +
        "edited that node, so its title, notes and owner are protected from the automatic meeting updates. " +
        "A trailing `suppressed` line lists titles a human deleted — do not re-create them. Node titles are " +
        "Czech; keep new ones Czech too. format=\"json\" returns the raw document instead, for bulk restructuring. " +
        "Pass the header's revision to ApplyMindMapChanges.")]
    public async Task<string> GetMindMap(
        [Description("Mind map id (GUID), from ListMindMaps")] Guid mapId,
        [Description("\"outline\" (default) or \"json\"")] string format = "outline",
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Anela_MindMaps, ResourceName);

        try
        {
            var response = await _mediator.Send(new GetMindMapDetailRequest { Id = mapId }, cancellationToken);
            if (!response.Success)
            {
                throw ToMcpException(response.ErrorCode, mapId, response.Params);
            }

            if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            {
                return response.DocumentJson;
            }

            return Render(
                response.DocumentJson, response.Name, response.Status,
                response.Meetings.Count, response.UpdatedAt, MindMapRevision.For(response.UpdatedAt));
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP GetMindMap failed for map {MindMapId}", mapId);
            throw new McpException($"Failed to read mind map: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description(
        "Apply a batch of changes to a mind map. Operations run in order and the batch is all-or-nothing: " +
        "if one is rejected nothing is written. Operations: " +
        "addNode (parentId or tempParentId + title, optional notes/status/owner/tempId — the server assigns the id, " +
        "and a tempId can be referenced as tempParentId by a later operation to build a subtree in one call); " +
        "updateNode (nodeId + any of title/notes/status/owner — omit a field to leave it alone, send \"\" to clear " +
        "notes or owner; the title cannot be cleared); moveNode (nodeId + newParentId); deleteNode (nodeId — this " +
        "also deletes every descendant and tombstones their titles). The root node cannot be moved or deleted. " +
        "Every node whose title, notes or owner you change becomes locked under your account, which protects it " +
        "from the automatic meeting updates — so change content deliberately. The map is snapshotted first and can " +
        "be restored from the web UI. Pass the revision from the last GetMindMap; a stale revision is rejected.")]
    public async Task<string> ApplyMindMapChanges(
        [Description("Mind map id (GUID)")] Guid mapId,
        [Description("Revision token from the last GetMindMap or ListMindMaps call")] string revision,
        [Description("Operations to apply, in order")] MindMapOperationDto[] operations,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Anela_MindMaps, ResourceName, AccessLevel.Write);

        if (operations is not { Length: > 0 })
        {
            throw new McpException("[INVALID] Provide at least one operation. Nothing was written.");
        }

        try
        {
            var response = await _mediator.Send(
                new ApplyMindMapOperationsRequest
                {
                    Id = mapId,
                    Revision = revision,
                    Operations = operations.ToList()
                },
                cancellationToken);

            if (!response.Success)
            {
                throw ToMcpException(response.ErrorCode, mapId, response.Params);
            }

            var outline = Render(
                response.DocumentJson, response.Name, response.Status,
                response.MeetingCount, response.UpdatedAt, response.Revision);

            return JsonSerializer.Serialize(new
            {
                Outline = outline,
                response.Revision,
                response.AssignedIds,
                Counts = new
                {
                    response.AddedCount,
                    response.UpdatedCount,
                    response.MovedCount,
                    response.DeletedCount
                }
            }, McpJsonOptions.Default);
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP ApplyMindMapChanges failed for map {MindMapId}", mapId);
            throw new McpException($"Failed to apply mind map changes: {ex.Message}");
        }
    }

    private string Render(
        string documentJson, string name, string status, int meetingCount, DateTime updatedAt, string revision) =>
        _renderer.Render(
            MindMapJson.Deserialize(documentJson),
            new MindMapOutlineHeader(name, status, meetingCount, updatedAt, revision));

    private static McpException ToMcpException(
        ErrorCodes? errorCode, Guid mapId, IReadOnlyDictionary<string, string>? parameters) => errorCode switch
        {
            ErrorCodes.ResourceNotFound =>
                new McpException($"[NOT FOUND] Mind map {mapId} does not exist. Call ListMindMaps for the available maps."),
            ErrorCodes.MindMapUpdateInProgress =>
                new McpException("[BUSY] The map is being updated from a meeting right now. Retry in a few seconds."),
            ErrorCodes.MindMapRevisionMismatch =>
                new McpException(
                    $"[STALE] The map changed since you read it (current revision: {Param(parameters, "Current")}). " +
                    "Call GetMindMap again and re-apply your changes. Nothing was written."),
            ErrorCodes.MindMapInvalidOperation =>
                new McpException(
                    $"[INVALID] Operation {Param(parameters, "OperationIndex")} was rejected: " +
                    $"{Param(parameters, "Error")} Nothing was written."),
            ErrorCodes.MindMapInvalidDocument =>
                new McpException($"[INVALID] The result is not a valid mind map: {Param(parameters, "Errors")} Nothing was written."),
            _ => new McpException($"[ERROR] Mind map operation failed ({errorCode})."),
        };

    private static string Param(IReadOnlyDictionary<string, string>? parameters, string key) =>
        parameters is not null && parameters.TryGetValue(key, out var value) ? value : "unknown";
}
```

- [ ] **Step 4: Register the tools**

In `backend/src/Anela.Heblo.API/MCP/McpModule.cs`, extend the chain after `.WithTools<MeetingTasksMcpTools>()` — move the semicolon:

```csharp
            .WithTools<MeetingTasksMcpTools>()
            .WithTools<MindMapMcpTools>();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~MindMapMcpToolsTests`
Expected: PASS (9 tests).

If `response.Params` is typed `Dictionary<string,string>?` and does not implicitly convert to the `IReadOnlyDictionary<string,string>?` parameter, that conversion is valid in C# — no change needed. If the compiler disagrees on the `ToMcpException` overload, change the parameter type to `Dictionary<string, string>?`.

- [ ] **Step 6: Verify the MCP schema generates for the complex parameter**

The `operations` parameter is an array of a complex type; the SDK must be able to build a JSON schema for it. Verify against the running app:

Run: `cd backend/src/Anela.Heblo.API && dotnet run` (or start the app as usual), then in a second shell:

```bash
curl -sk -X POST https://localhost:5001/mcp \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | grep -o 'ApplyMindMapChanges'
```

Expected: `ApplyMindMapChanges` appears in the tool list without a server-side schema error. If the endpoint requires authentication in your local setup, an equally valid check is that the app starts without throwing during `WithTools<MindMapMcpTools>()` registration — schema generation happens there.

- [ ] **Step 7: Format and commit**

```bash
dotnet format
git add backend/src/Anela.Heblo.API/MCP/Tools/MindMapMcpTools.cs \
        backend/src/Anela.Heblo.API/MCP/McpModule.cs \
        backend/test/Anela.Heblo.Tests/MCP/Tools/MindMapMcpToolsTests.cs
git commit -m "feat: expose mind maps over MCP"
```

---

### Task 6: Documentation and full validation

**Files:**
- Modify: `docs/integrations/mcp-server.md`
- Modify: `CLAUDE.md` (the `docs/integrations/mcp-server.md` line in the documentation map)

- [ ] **Step 1: Document the tools**

In `docs/integrations/mcp-server.md`, after the "Meeting Notes (4)" block:

```markdown
**Mind Maps (3)** — require the `Anela_MindMaps` feature; `ApplyMindMapChanges` additionally requires Write access.
- `ListMindMaps` — all maps with status, meeting count and the revision token
- `GetMindMap` — one map as an indented outline (default) or as the raw document JSON
- `ApplyMindMapChanges` — batched `addNode` / `updateNode` / `moveNode` / `deleteNode`, applied in order, all-or-nothing

Write semantics: an MCP write is treated exactly like a web-UI save — nodes whose title, notes or
owner changed are locked under the caller's email and shielded from the meeting-update job, and
deleted titles become tombstones. Every write snapshots a version first, so it can be rolled back
from the History tab. Writes must quote the `revision` from the last read (derived from the map's
`UpdatedAt`); a stale revision is rejected, as is any write while the map's status is `Updating`.
Unlike the other tool groups, these call the MediatR handlers directly with no matching REST route.
```

Update the first line of the file to mention mind maps:

```markdown
The application exposes MCP tools for AI assistants to query catalog data, manufacturing orders, perform batch calculations, user-directory lookups, meeting notes, and to read and edit mind maps.
```

- [ ] **Step 2: Fix the stale tool count**

In `CLAUDE.md`, the documentation map line currently reads `- docs/integrations/mcp-server.md — MCP tools, endpoints, client config (20 tools)`. The real count before this work was 23 (8 catalog + 3 manufacture orders + 4 batch + 1 user + 2 knowledge base + 1 leaflet + 4 meetings). Change it to:

```markdown
- `docs/integrations/mcp-server.md` — MCP tools, endpoints, client config (26 tools)
```

- [ ] **Step 3: Run the full backend suite**

```bash
dotnet build
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false
```

Expected: PASS. Pay attention to `ErrorHandlingTests` (the two new codes must sit in the 34XX bucket and have Czech strings) and `MindMapChildEntityStateTests` (version snapshot entity state).

- [ ] **Step 4: Verify the frontend still builds**

The only frontend change is two i18n strings, but `GetMindMapDetailResponse.UpdatedAt` regenerates the TypeScript client on build.

```bash
cd frontend && CI=false npm run build && npm run lint
```

Expected: build succeeds. (`npx tsc --noEmit` is not a valid substitute here — it false-greens on this project.)

- [ ] **Step 5: Format and commit**

```bash
dotnet format
git add docs/integrations/mcp-server.md CLAUDE.md
git commit -m "docs: document the mind map MCP tools"
```

---

## Notes for the implementer

- **Do not add a controller route** for `ApplyMindMapOperations`. The MCP tool calls MediatR directly; a REST endpoint would add an unused method to the generated TypeScript client.
- **Do not touch the frontend mind map editor.** The write path deliberately reuses the existing lock semantics precisely so the UI needs no changes.
- **The web UI save still does not snapshot a version.** That asymmetry (LLM updates, restores and MCP writes snapshot; UI saves do not) is intentional and out of scope here.
