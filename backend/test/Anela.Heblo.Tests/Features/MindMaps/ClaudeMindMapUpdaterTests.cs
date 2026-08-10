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
