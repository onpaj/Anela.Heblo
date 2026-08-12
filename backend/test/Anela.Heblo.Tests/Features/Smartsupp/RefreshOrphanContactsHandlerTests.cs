using Anela.Heblo.Application.Features.Smartsupp.UseCases.RefreshOrphanContacts;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class RefreshOrphanContactsHandlerTests
{
    private static SmartsuppConversation MakeConversation(string id) => new()
    {
        Id = id,
        Status = SmartsuppConversationStatus.Open,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        SyncedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task Handle_ReattachesContactId_ForEachOrphanWithARemoteContact()
    {
        var repository = new Mock<ISmartsuppRepository>();
        repository.Setup(r => r.ListOrphanContactConversationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "conv-1" });
        var local = MakeConversation("conv-1");
        repository.Setup(r => r.FindConversationByIdAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(local);

        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient.Setup(a => a.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-1", ContactId = "contact-9" });

        var handler = new RefreshOrphanContactsHandler(
            repository.Object, apiClient.Object, NullLogger<RefreshOrphanContactsHandler>.Instance);

        var response = await handler.Handle(new RefreshOrphanContactsRequest(), default);

        response.Scanned.Should().Be(1);
        response.Updated.Should().Be(1);
        response.Failed.Should().Be(0);
        local.ContactId.Should().Be("contact-9");
        repository.Verify(r => r.UpsertConversationAsync(local, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SkipsConversation_WhenRemoteHasNoContactId()
    {
        var repository = new Mock<ISmartsuppRepository>();
        repository.Setup(r => r.ListOrphanContactConversationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "conv-1" });

        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient.Setup(a => a.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-1", ContactId = null });

        var handler = new RefreshOrphanContactsHandler(
            repository.Object, apiClient.Object, NullLogger<RefreshOrphanContactsHandler>.Instance);

        var response = await handler.Handle(new RefreshOrphanContactsRequest(), default);

        response.SkippedNoContactId.Should().Be(1);
        response.Updated.Should().Be(0);
        repository.Verify(r => r.FindConversationByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SkipsConversation_WhenLocalRowNoLongerExists()
    {
        var repository = new Mock<ISmartsuppRepository>();
        repository.Setup(r => r.ListOrphanContactConversationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "conv-1" });
        repository.Setup(r => r.FindConversationByIdAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SmartsuppConversation?)null);

        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient.Setup(a => a.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-1", ContactId = "contact-9" });

        var handler = new RefreshOrphanContactsHandler(
            repository.Object, apiClient.Object, NullLogger<RefreshOrphanContactsHandler>.Instance);

        var response = await handler.Handle(new RefreshOrphanContactsRequest(), default);

        response.SkippedNoContactId.Should().Be(1);
        response.Updated.Should().Be(0);
        repository.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ContinuesToNextConversation_WhenOneFailsMidLoop()
    {
        // Regression test for dropping _db.ChangeTracker.Clear(): confirms a failure on the first
        // conversation in a batch does not prevent the second one from being processed and updated.
        var repository = new Mock<ISmartsuppRepository>();
        repository.Setup(r => r.ListOrphanContactConversationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "conv-fail", "conv-ok" });

        var local = MakeConversation("conv-ok");
        repository.Setup(r => r.FindConversationByIdAsync("conv-fail", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        repository.Setup(r => r.FindConversationByIdAsync("conv-ok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(local);

        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient.Setup(a => a.GetConversationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
                new SmartsuppConversationData { Id = id, ContactId = "contact-9" });

        var handler = new RefreshOrphanContactsHandler(
            repository.Object, apiClient.Object, NullLogger<RefreshOrphanContactsHandler>.Instance);

        var response = await handler.Handle(new RefreshOrphanContactsRequest(), default);

        response.Scanned.Should().Be(2);
        response.Failed.Should().Be(1);
        response.FailedIds.Should().ContainSingle().Which.Should().Be("conv-fail");
        response.Updated.Should().Be(1);
        local.ContactId.Should().Be("contact-9");
    }
}
