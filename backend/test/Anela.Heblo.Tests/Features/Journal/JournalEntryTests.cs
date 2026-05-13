using Anela.Heblo.Domain.Features.Journal;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class JournalEntryTests
{
    private static JournalEntry NewEntry() => new JournalEntry
    {
        Id = 1,
        Content = "test",
        EntryDate = DateTime.UtcNow.Date,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "user-1"
    };

    // ----- AssociateWithProduct: existing-behavior regression coverage -----

    [Fact]
    public void AssociateWithProduct_NormalizesCodeToTrimmedUpper()
    {
        var entry = NewEntry();

        entry.AssociateWithProduct("  ab-1  ");

        entry.ProductAssociations.Should().ContainSingle()
            .Which.ProductCodePrefix.Should().Be("AB-1");
    }

    [Fact]
    public void AssociateWithProduct_ThrowsOnWhitespaceCode()
    {
        var entry = NewEntry();

        var act = () => entry.AssociateWithProduct("   ");

        act.Should().Throw<ArgumentException>();
    }
}
