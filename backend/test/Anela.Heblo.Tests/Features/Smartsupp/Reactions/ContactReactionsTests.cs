using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.Reactions;

public class ContactReactionsTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();

    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static string ContactJson(string id = "ct1") => $@"{{
        ""id"":""{id}"",
        ""email"":""test@example.com"",
        ""gdpr_approved"":false,
        ""created_at"":""2026-05-13T10:00:00Z"",
        ""updated_at"":""2026-05-13T10:01:00Z""
    }}";

    private static WebhookEventContext MakeCtx(string eventName, string dataJson) =>
        new()
        {
            EventName = eventName,
            Timestamp = DateTime.UtcNow,
            AccountId = "acc-1",
            AppId = "app-1",
            Data = Parse(dataJson),
        };

    [Theory]
    [InlineData("contact.created")]
    [InlineData("contact.updated")]
    [InlineData("contact.acquired")]
    [InlineData("contact.banned")]
    [InlineData("contact.unbanned")]
    public async Task AllContactReactions_UpsertContact(string eventName)
    {
        ISmartsuppWebhookReaction reaction = eventName switch
        {
            "contact.created" => new ContactCreatedReaction(_repo.Object),
            "contact.updated" => new ContactUpdatedReaction(_repo.Object),
            "contact.acquired" => new ContactAcquiredReaction(_repo.Object),
            "contact.banned" => new ContactBannedReaction(_repo.Object),
            "contact.unbanned" => new ContactUnbannedReaction(_repo.Object),
            _ => throw new InvalidOperationException()
        };

        var ctx = MakeCtx(eventName, $@"{{""contact"":{ContactJson()}}}");

        await reaction.HandleAsync(ctx, CancellationToken.None);

        _repo.Verify(r => r.UpsertContactAsync(
            It.Is<SmartsuppContact>(c => c.Id == "ct1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("contact.created", "contact.created")]
    [InlineData("contact.updated", "contact.updated")]
    [InlineData("contact.acquired", "contact.acquired")]
    [InlineData("contact.banned", "contact.banned")]
    [InlineData("contact.unbanned", "contact.unbanned")]
    public void AllContactReactions_HaveCorrectEventName(string eventName, string expected)
    {
        ISmartsuppWebhookReaction reaction = eventName switch
        {
            "contact.created" => new ContactCreatedReaction(_repo.Object),
            "contact.updated" => new ContactUpdatedReaction(_repo.Object),
            "contact.acquired" => new ContactAcquiredReaction(_repo.Object),
            "contact.banned" => new ContactBannedReaction(_repo.Object),
            "contact.unbanned" => new ContactUnbannedReaction(_repo.Object),
            _ => throw new InvalidOperationException()
        };
        reaction.EventName.Should().Be(expected);
    }
}
