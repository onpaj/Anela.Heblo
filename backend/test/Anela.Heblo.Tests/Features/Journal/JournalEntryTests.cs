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

    // ----- ReplaceProductAssociations -----

    [Fact]
    public void ReplaceProductAssociations_WithNull_ClearsAll()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("A");
        entry.AssociateWithProduct("B");

        entry.ReplaceProductAssociations(null);

        entry.ProductAssociations.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceProductAssociations_WithEmpty_ClearsAll()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("A");

        entry.ReplaceProductAssociations(Array.Empty<string>());

        entry.ProductAssociations.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceProductAssociations_WithSuperset_AddsMissingCodes()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("A");

        entry.ReplaceProductAssociations(new[] { "A", "B", "C" });

        entry.ProductAssociations.Select(p => p.ProductCodePrefix)
            .Should().BeEquivalentTo(new[] { "A", "B", "C" });
    }

    [Fact]
    public void ReplaceProductAssociations_WithDisjointSet_RemovesOldAndAddsNew()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("A");
        entry.AssociateWithProduct("B");

        entry.ReplaceProductAssociations(new[] { "C", "D" });

        entry.ProductAssociations.Select(p => p.ProductCodePrefix)
            .Should().BeEquivalentTo(new[] { "C", "D" });
    }

    [Fact]
    public void ReplaceProductAssociations_WithOverlap_PreservesExistingInstance()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("X");
        entry.AssociateWithProduct("Y");
        var originalX = entry.ProductAssociations.Single(p => p.ProductCodePrefix == "X");

        entry.ReplaceProductAssociations(new[] { "X" });

        entry.ProductAssociations.Should().ContainSingle()
            .Which.Should().BeSameAs(originalX);
    }

    [Fact]
    public void ReplaceProductAssociations_WithDifferentCaseOverlap_PreservesExistingInstance()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("X");
        entry.AssociateWithProduct("Y");
        var originalX = entry.ProductAssociations.Single(p => p.ProductCodePrefix == "X");

        entry.ReplaceProductAssociations(new[] { "x" });

        entry.ProductAssociations.Should().ContainSingle()
            .Which.Should().BeSameAs(originalX);
    }

    [Fact]
    public void ReplaceProductAssociations_DedupesCaseAndWhitespaceInsensitively()
    {
        var entry = NewEntry();

        entry.ReplaceProductAssociations(new[] { "AB-1", "ab-1", "  AB-1  " });

        entry.ProductAssociations.Should().ContainSingle()
            .Which.ProductCodePrefix.Should().Be("AB-1");
    }

    [Fact]
    public void ReplaceProductAssociations_WithWhitespaceItem_ThrowsAndLeavesStateUnchanged()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("A");
        var snapshot = entry.ProductAssociations.ToList();

        var act = () => entry.ReplaceProductAssociations(new[] { "B", "   ", "C" });

        act.Should().Throw<ArgumentException>();
        entry.ProductAssociations.Should().BeEquivalentTo(snapshot);
    }
}
