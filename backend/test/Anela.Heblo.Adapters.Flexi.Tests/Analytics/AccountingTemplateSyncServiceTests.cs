using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting;
using Rem.FlexiBeeSDK.Model.Accounting.AccountingTemplates;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class AccountingTemplateSyncServiceTests
{
    private static AnalyticsDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AccountingTemplateSyncService CreateService(
        IAccountingTemplateClient client,
        AnalyticsDbContext ctx)
    {
        var repo = new SyncWatermarkRepository(ctx);
        return new AccountingTemplateSyncService(
            client,
            repo,
            ctx,
            Mock.Of<ILogger<AccountingTemplateSyncService>>());
    }

    private static AccountingTemplateFlexiDto MakeTemplateDto(int id, string code, string name) => new()
    {
        Id = id,
        Code = code,
        Name = name,
        Description = $"Desc {name}",
        LastUpdate = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task SyncAsync_WhenNoTemplates_ReturnsSuccessWithZeroRows()
    {
        // Arrange
        var client = new Mock<IAccountingTemplateClient>();
        await using var ctx = CreateInMemoryContext();

        client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountingTemplateFlexiDto>());

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RowsFetched.Should().Be(0);
        result.RowsUpserted.Should().Be(0);

        var state = await ctx.SyncStates.FindAsync("accounting_template");
        state!.LastRunStatus.Should().Be("OK");
    }

    [Fact]
    public async Task SyncAsync_WhenTemplatesExist_UpsertsAllAndSetsOkStatus()
    {
        // Arrange
        var client = new Mock<IAccountingTemplateClient>();
        await using var ctx = CreateInMemoryContext();

        var dtos = new List<AccountingTemplateFlexiDto>
        {
            MakeTemplateDto(1, "INT", "Internal"),
            MakeTemplateDto(2, "EXT", "External"),
        };

        client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dtos);

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RowsFetched.Should().Be(2);
        result.RowsUpserted.Should().Be(2);

        var templates = await ctx.AccountingTemplates.ToListAsync();
        templates.Should().HaveCount(2);
        templates.Should().Contain(t => t.FlexiId == 1 && t.Code == "INT");
        templates.Should().Contain(t => t.FlexiId == 2 && t.Code == "EXT");
        templates.Should().AllSatisfy(t => t.LastModified.Should().NotBeNull());

        var state = await ctx.SyncStates.FindAsync("accounting_template");
        state!.LastRunStatus.Should().Be("OK");
        state.LastRunRowsFetched.Should().Be(2);
        state.LastRunRowsUpserted.Should().Be(2);
        state.Watermark.Should().BeNull();
    }

    [Fact]
    public async Task SyncAsync_OnClientError_RecordsFailedStatus()
    {
        // Arrange
        var client = new Mock<IAccountingTemplateClient>();
        await using var ctx = CreateInMemoryContext();

        client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Flexi unreachable"));

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();

        var state = await ctx.SyncStates.FindAsync("accounting_template");
        state!.LastRunStatus.Should().Be("FAILED");
        state.LastErrorMessage.Should().Contain("Flexi unreachable");
        state.Watermark.Should().BeNull();
    }

    [Fact]
    public void Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified()
    {
        // Regression test: SDK returns Kind=Unspecified representing Prague local time.
        // AccountingTemplateFlexiDto.LastUpdate is non-nullable DateTime; Map() applies
        // DateTime.SpecifyKind(dto.LastUpdate, DateTimeKind.Unspecified) before ConvertTimeToUtc.
        var unspecified = new DateTime(2025, 6, 19, 10, 0, 0, DateTimeKind.Unspecified);
        var dto = new AccountingTemplateFlexiDto
        {
            Id = 99,
            Code = "TEST",
            Name = "Test Template",
            Description = "Regression test",
            LastUpdate = unspecified,
        };

        var template = AccountingTemplateSyncService.Map(dto);

        Assert.NotNull(template.LastModified);
        // LastModified is DateTimeOffset?; Offset=0 confirms UTC.
        Assert.Equal(TimeSpan.Zero, template.LastModified!.Value.Offset);
        var expected = TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
        Assert.Equal(expected, template.LastModified.Value.UtcDateTime);
    }
}
