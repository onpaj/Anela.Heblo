using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Contacts;
using Rem.FlexiBeeSDK.Model.Contacts;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class ContactSyncServiceTests
{
    private static AnalyticsDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ContactSyncService CreateService(
        IContactListClient client,
        AnalyticsDbContext ctx)
    {
        var repo = new SyncWatermarkRepository(ctx);
        var options = Options.Create(new FlexiAnalyticsSyncOptions { BatchSize = 500 });
        return new ContactSyncService(
            client,
            repo,
            ctx,
            options,
            Mock.Of<ILogger<ContactSyncService>>());
    }

    private static ContactFlexiDto MakeContactDto(long id, string code, string name) => new()
    {
        Id = id,
        Code = code,
        Name = name,
        CIN = $"CIN{id:D8}",
        VATIN = $"CZ{id:D8}",
        LastUpdate = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task SyncAsync_WhenNoContacts_ReturnsSuccessWithZeroRows()
    {
        // Arrange
        var client = new Mock<IContactListClient>();
        await using var ctx = CreateInMemoryContext();

        client.Setup(c => c.GetAsync(
                It.IsAny<IEnumerable<ContactType>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContactFlexiDto>());

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RowsFetched.Should().Be(0);
        result.RowsUpserted.Should().Be(0);

        var state = await ctx.SyncStates.FindAsync("contact");
        state!.LastRunStatus.Should().Be("OK");
    }

    [Fact]
    public async Task SyncAsync_WhenContactsExist_UpsertsAllAndSetsOkStatus()
    {
        // Arrange
        var client = new Mock<IContactListClient>();
        await using var ctx = CreateInMemoryContext();

        var dtos = new List<ContactFlexiDto>
        {
            MakeContactDto(1L, "SUP001", "Supplier One"),
            MakeContactDto(2L, "CUST002", "Customer Two"),
        };

        client
            .SetupSequence(c => c.GetAsync(
                It.IsAny<IEnumerable<ContactType>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(dtos)
            .ReturnsAsync(new List<ContactFlexiDto>());

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RowsFetched.Should().Be(2);
        result.RowsUpserted.Should().Be(2);

        var contacts = await ctx.Contacts.ToListAsync();
        contacts.Should().HaveCount(2);
        contacts.Should().Contain(c => c.FlexiId == 1L && c.Code == "SUP001");
        contacts.Should().Contain(c => c.FlexiId == 2L && c.Code == "CUST002");
        contacts.Should().AllSatisfy(c => c.LastModified.Should().NotBeNull());

        var state = await ctx.SyncStates.FindAsync("contact");
        state!.LastRunStatus.Should().Be("OK");
        state.LastRunRowsFetched.Should().Be(2);
        state.LastRunRowsUpserted.Should().Be(2);
        state.Watermark.Should().BeNull();
    }

    [Fact]
    public async Task SyncAsync_OnClientError_RecordsFailedStatus()
    {
        // Arrange
        var client = new Mock<IContactListClient>();
        await using var ctx = CreateInMemoryContext();

        client.Setup(c => c.GetAsync(
                It.IsAny<IEnumerable<ContactType>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Flexi unreachable"));

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();

        var state = await ctx.SyncStates.FindAsync("contact");
        state!.LastRunStatus.Should().Be("FAILED");
        state.LastErrorMessage.Should().Contain("Flexi unreachable");
        state.Watermark.Should().BeNull();
    }

    [Fact]
    public void Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified()
    {
        // Regression test: SDK returns Kind=Unspecified representing Prague local time.
        // DateTimeOffset.DateTime always strips offset and returns Kind=Unspecified,
        // so passing any DateTimeOffset exercises the Unspecified path in ConvertTimeToUtc.
        var unspecified = new DateTime(2025, 6, 19, 10, 0, 0, DateTimeKind.Unspecified);
        var dto = new ContactFlexiDto
        {
            Id = 99L,
            Code = "TEST99",
            Name = "Test Contact",
            CIN = "CIN00000099",
            VATIN = "CZ00000099",
            LastUpdate = new DateTimeOffset(unspecified, TimeSpan.Zero),
        };

        var contact = ContactSyncService.Map(dto);

        Assert.NotNull(contact.LastModified);
        Assert.Equal(TimeSpan.Zero, contact.LastModified!.Value.Offset);
        var expected = TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
        Assert.Equal(expected, contact.LastModified.Value.UtcDateTime);
    }
}
