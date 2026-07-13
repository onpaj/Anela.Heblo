using Anela.Heblo.Application.Features.Smartsupp.Presence;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppPresenceServiceTests
{
    private readonly Mock<ISmartsuppPresenceRepository> _repo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly SmartsuppSendMessageOptions _sendOptions = new();
    private readonly SmartsuppPresenceOptions _presenceOptions = new();

    private SmartsuppPresenceService CreateService() =>
        new(_repo.Object, _currentUser.Object,
            Options.Create(_sendOptions), Options.Create(_presenceOptions));

    private void SetUser(string? email, string? name = "Ondra", string? id = "u1") =>
        _currentUser.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser(id, name, email, true));

    [Fact]
    public async Task RecordCurrentOperator_UsesMappedAgentId_WhenEmailIsInAgentMap()
    {
        SetUser("ondra@anela.cz");
        _sendOptions.AgentMap["ondra@anela.cz"] = "1257997";
        var service = CreateService();

        await service.RecordCurrentOperatorAsync("c1", CancellationToken.None);

        _repo.Verify(r => r.UpsertAsync(
            It.Is<SmartsuppConversationPresence>(p =>
                p.ConversationId == "c1" &&
                p.AgentId == "1257997" &&
                p.DisplayName == "Ondra" &&
                p.Source == SmartsuppPresenceSource.Heblo),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordCurrentOperator_FallsBackToEmail_WhenNotMapped()
    {
        SetUser("nemo@anela.cz");
        var service = CreateService();

        await service.RecordCurrentOperatorAsync("c1", CancellationToken.None);

        _repo.Verify(r => r.UpsertAsync(
            It.Is<SmartsuppConversationPresence>(p => p.AgentId == "nemo@anela.cz"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordCurrentOperator_DoesNothing_WhenNoIdentity()
    {
        SetUser(email: null, name: null, id: null);
        var service = CreateService();

        await service.RecordCurrentOperatorAsync("c1", CancellationToken.None);

        _repo.Verify(r => r.UpsertAsync(
            It.IsAny<SmartsuppConversationPresence>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveCurrentOperator_RemovesHebloPresenceForMappedKey()
    {
        SetUser("ondra@anela.cz");
        _sendOptions.AgentMap["ondra@anela.cz"] = "1257997";
        var service = CreateService();

        await service.RemoveCurrentOperatorAsync("c1", CancellationToken.None);

        _repo.Verify(r => r.RemoveAsync(
            "c1", "1257997", SmartsuppPresenceSource.Heblo, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetActiveViewers_MarksCurrentUser_AndDedupsAcrossSources()
    {
        SetUser("ondra@anela.cz");
        _sendOptions.AgentMap["ondra@anela.cz"] = "1257997";

        var rows = new List<SmartsuppConversationPresence>
        {
            // Same operator present from both native app and Heblo tab -> should collapse to one, flagged as me.
            new() { ConversationId = "c1", AgentId = "1257997", DisplayName = "Ondra", Source = SmartsuppPresenceSource.Smartsupp, EnteredAt = new DateTime(2026, 7, 9, 10, 0, 0), LastSeenAt = new DateTime(2026, 7, 9, 10, 5, 0) },
            new() { ConversationId = "c1", AgentId = "1257997", DisplayName = "Ondra", Source = SmartsuppPresenceSource.Heblo, EnteredAt = new DateTime(2026, 7, 9, 10, 1, 0), LastSeenAt = new DateTime(2026, 7, 9, 10, 6, 0) },
            new() { ConversationId = "c1", AgentId = "999", DisplayName = "Jana", Source = SmartsuppPresenceSource.Smartsupp, EnteredAt = new DateTime(2026, 7, 9, 10, 2, 0), LastSeenAt = new DateTime(2026, 7, 9, 10, 4, 0) },
        };
        _repo.Setup(r => r.GetActiveByConversationAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, List<SmartsuppConversationPresence>> { ["c1"] = rows });
        var service = CreateService();

        var result = await service.GetActiveViewersAsync(new[] { "c1" }, CancellationToken.None);

        var viewers = result["c1"];
        viewers.Should().HaveCount(2); // deduped: one Ondra + one Jana
        viewers.Single(v => v.AgentId == "1257997").IsCurrentUser.Should().BeTrue();
        viewers.Single(v => v.AgentId == "999").IsCurrentUser.Should().BeFalse();
    }

    [Fact]
    public async Task GetActiveViewers_ReturnsEmpty_WhenNoConversationIds()
    {
        var service = CreateService();

        var result = await service.GetActiveViewersAsync(System.Array.Empty<string>(), CancellationToken.None);

        result.Should().BeEmpty();
        _repo.Verify(r => r.GetActiveByConversationAsync(
            It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetActiveViewers_UsesConfiguredTtls_ForThresholds()
    {
        SetUser("ondra@anela.cz");
        _presenceOptions.HeartbeatTtlSeconds = 90;
        _presenceOptions.SmartsuppTtlMinutes = 30;
        DateTime hebloThreshold = default, smartsuppThreshold = default;
        _repo.Setup(r => r.GetActiveByConversationAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<string>, DateTime, DateTime, CancellationToken>((_, h, s, _) =>
            {
                hebloThreshold = h;
                smartsuppThreshold = s;
            })
            .ReturnsAsync(new Dictionary<string, List<SmartsuppConversationPresence>>());
        var service = CreateService();

        await service.GetActiveViewersAsync(new[] { "c1" }, CancellationToken.None);

        // Heblo threshold is nearer to "now" than the Smartsupp one (90s vs 30min).
        hebloThreshold.Should().BeAfter(smartsuppThreshold);
    }
}
