using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

// Regression guard for issue #3069: a Smartsupp webhook payload with a long
// referer URL / subject / avatar URL overflowed the varchar(500) columns and
// produced a 500 (Npgsql 22001). These free-text/URL fields carry arbitrary
// external content and must remain unbounded (text).
public class SmartsuppConversationSchemaTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Theory]
    [InlineData(nameof(SmartsuppConversation.Subject))]
    [InlineData(nameof(SmartsuppConversation.Referer))]
    [InlineData(nameof(SmartsuppConversation.ContactAvatarUrl))]
    public void Conversation_FreeTextAndUrlColumns_AreUnbounded(string propertyName)
    {
        using var db = NewContext();

        var property = db.Model
            .FindEntityType(typeof(SmartsuppConversation))!
            .FindProperty(propertyName)!;

        property.GetMaxLength().Should().BeNull(
            $"{propertyName} carries arbitrary external content and must not be capped");
    }
}
