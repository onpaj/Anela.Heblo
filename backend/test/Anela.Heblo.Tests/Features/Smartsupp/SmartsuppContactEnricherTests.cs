using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppContactEnricherTests
{
    private static SmartsuppConversation MakeConversation(string id, string contactId, DateTime updatedAt) =>
        new()
        {
            Id = id,
            ContactId = contactId,
            Status = SmartsuppConversationStatus.Open,
            CreatedAt = DateTime.SpecifyKind(updatedAt.AddHours(-1), DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(updatedAt, DateTimeKind.Unspecified),
            SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        };

    private static SmartsuppContactData MakeContactData(string id, string? name = null, string? email = null) =>
        new()
        {
            Id = id,
            Name = name,
            Email = email,
            CreatedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc),
        };

    private static SmartsuppContactEnricher CreateSut(
        Mock<ISmartsuppApiClient> apiClient,
        Mock<ISmartsuppRepository> repository) =>
        new(apiClient.Object, repository.Object, NullLogger<SmartsuppContactEnricher>.Instance);

    [Fact]
    public async Task EnrichContactAsync_FetchesContactViaRest_WhenLocalContactMissing()
    {
        // Arrange
        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient
            .Setup(c => c.GetContactAsync("ct-unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeContactData("ct-unknown", name: "Michaela", email: "michaela@example.com"));

        var repository = new Mock<ISmartsuppRepository>();
        repository
            .Setup(r => r.ContactExistsAsync("ct-unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut(apiClient, repository);
        var incoming = MakeConversation("conv-1", "ct-unknown", new DateTime(2026, 6, 8, 10, 0, 0));

        // Act
        var result = await sut.EnrichContactAsync(incoming, CancellationToken.None);

        // Assert
        apiClient.Verify(c => c.GetContactAsync("ct-unknown", It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.UpsertContactAsync(
            It.Is<SmartsuppContact>(c => c.Id == "ct-unknown" && c.Name == "Michaela"),
            It.IsAny<CancellationToken>()), Times.Once);
        result.ContactId.Should().Be("ct-unknown");
        result.ContactName.Should().Be("Michaela");
        result.ContactEmail.Should().Be("michaela@example.com");
    }

    [Fact]
    public async Task EnrichContactAsync_WipesContactId_WhenRestReturnsNull()
    {
        // Arrange
        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient
            .Setup(c => c.GetContactAsync("ct-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SmartsuppContactData?)null);

        var repository = new Mock<ISmartsuppRepository>();
        repository
            .Setup(r => r.ContactExistsAsync("ct-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut(apiClient, repository);
        var incoming = MakeConversation("conv-1", "ct-gone", new DateTime(2026, 6, 8, 10, 0, 0));

        // Act
        var result = await sut.EnrichContactAsync(incoming, CancellationToken.None);

        // Assert — REST attempted; ContactId wiped because REST returned null (fail-open)
        apiClient.Verify(c => c.GetContactAsync("ct-gone", It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.UpsertContactAsync(It.IsAny<SmartsuppContact>(), It.IsAny<CancellationToken>()), Times.Never);
        result.ContactId.Should().BeNull();
        result.ContactName.Should().BeNull();
        result.ContactEmail.Should().BeNull();
    }

    [Fact]
    public async Task EnrichContactAsync_WipesContactId_WhenRestThrows()
    {
        // Arrange — REST blows up (e.g., 500). Webhook must still persist the conversation.
        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient
            .Setup(c => c.GetContactAsync("ct-broken", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Smartsupp 500"));

        var repository = new Mock<ISmartsuppRepository>();
        repository
            .Setup(r => r.ContactExistsAsync("ct-broken", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut(apiClient, repository);
        var incoming = MakeConversation("conv-1", "ct-broken", new DateTime(2026, 6, 8, 10, 0, 0));

        // Act — fail-open: REST exception is caught and ContactId is cleared.
        var result = await sut.EnrichContactAsync(incoming, CancellationToken.None);

        // Assert
        result.ContactId.Should().BeNull();
        result.ContactName.Should().BeNull();
        repository.Verify(r => r.UpsertContactAsync(It.IsAny<SmartsuppContact>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnrichContactAsync_DoesNotCallRest_WhenContactAlreadyKnownLocally()
    {
        // Arrange — happy path: contact already synced via contact.acquired earlier.
        var apiClient = new Mock<ISmartsuppApiClient>(MockBehavior.Strict);
        var repository = new Mock<ISmartsuppRepository>();
        repository
            .Setup(r => r.ContactExistsAsync("ct-known", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut(apiClient, repository);
        var incoming = MakeConversation("conv-1", "ct-known", new DateTime(2026, 6, 8, 10, 0, 0));

        // Act — strict mock: any unexpected REST call would fail.
        var result = await sut.EnrichContactAsync(incoming, CancellationToken.None);

        // Assert
        result.ContactId.Should().Be("ct-known");
        repository.Verify(r => r.UpsertContactAsync(It.IsAny<SmartsuppContact>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnrichContactAsync_ReturnsUnchanged_WhenContactIdIsNull()
    {
        // Arrange
        var apiClient = new Mock<ISmartsuppApiClient>(MockBehavior.Strict);
        var repository = new Mock<ISmartsuppRepository>(MockBehavior.Strict);
        var sut = CreateSut(apiClient, repository);
        var incoming = MakeConversation("conv-1", contactId: null!, new DateTime(2026, 6, 8, 10, 0, 0));
        incoming.ContactId = null;

        // Act
        var result = await sut.EnrichContactAsync(incoming, CancellationToken.None);

        // Assert — no repository or REST calls at all.
        result.Should().BeSameAs(incoming);
    }
}
