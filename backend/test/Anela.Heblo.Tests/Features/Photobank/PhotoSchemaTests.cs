using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

// Regression guard for the photobank-auto-tag job failure:
// "Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'".
// The global DbContext convention forces DateTime values to Kind=Unspecified, which is only
// valid for 'timestamp without time zone' columns. Every Photo DateTime column must therefore
// be mapped to 'timestamp' (without time zone) via AsUtcTimestamp(), or writes throw at runtime.
public class PhotoSchemaTests
{
    private static ApplicationDbContext NewNpgsqlContext()
    {
        // Npgsql provider so relational column-type metadata resolves; no connection is opened
        // because only the model is inspected.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=schema_inspection")
            .Options;
        return new ApplicationDbContext(options);
    }

    [Theory]
    [InlineData(nameof(Photo.IndexedAt))]
    [InlineData(nameof(Photo.ModifiedAt))]
    [InlineData(nameof(Photo.TakenAt))]
    [InlineData(nameof(Photo.LastAutoTaggedAt))]
    public void Photo_DateTimeColumns_AreTimestampWithoutTimeZone(string propertyName)
    {
        using var db = NewNpgsqlContext();

        var property = db.Model
            .FindEntityType(typeof(Photo))!
            .FindProperty(propertyName)!;

        property.GetColumnType().Should().Be(
            "timestamp",
            $"{propertyName} stores UTC and must map to 'timestamp without time zone' to match the " +
            "global UTC->Unspecified converter; 'timestamp with time zone' rejects Unspecified writes");
    }
}
