using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.Mappers;

public class SmartsuppConversationConfigurationTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"cfg_{Guid.NewGuid()}")
            .Options;
        using var ctx = new ApplicationDbContext(options);
        return ctx.Model;
    }

    [Theory]
    [InlineData(nameof(SmartsuppConversation.Subject), 2000)]
    [InlineData(nameof(SmartsuppConversation.ContactAvatarUrl), 2000)]
    public void Configuration_WidensBoundedColumns_ToTwoThousandChars(string property, int expectedMaxLength)
    {
        var entity = BuildModel().FindEntityType(typeof(SmartsuppConversation))!;
        var prop = entity.FindProperty(property)!;
        prop.GetMaxLength().Should().Be(expectedMaxLength);
    }

    [Fact]
    public void Configuration_MakesRefererUnbounded()
    {
        var entity = BuildModel().FindEntityType(typeof(SmartsuppConversation))!;
        var prop = entity.FindProperty(nameof(SmartsuppConversation.Referer))!;
        prop.GetMaxLength().Should().BeNull();
    }

    [Fact]
    public void Configuration_LeavesLastMessagePreview_AtFiveHundredChars()
    {
        var entity = BuildModel().FindEntityType(typeof(SmartsuppConversation))!;
        entity.FindProperty(nameof(SmartsuppConversation.LastMessagePreview))!
            .GetMaxLength().Should().Be(500);
    }
}
