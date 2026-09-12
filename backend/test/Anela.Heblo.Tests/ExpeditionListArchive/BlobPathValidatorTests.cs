using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class BlobPathValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValid_NullOrWhitespace_ReturnsFalse(string? blobPath)
    {
        // Act
        var result = BlobPathValidator.IsValid(blobPath!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_PathContainsDoubleDot_ReturnsFalse_BlocksPathTraversal()
    {
        // Arrange — a path that would otherwise look structurally valid if the
        // traversal guard were removed or narrowed. This is the security-critical
        // check: if it regresses, a caller could reach blobs outside the expected
        // date-folder layout (see issue #4093).
        const string blobPath = "2026-01-01/../../admin.pdf";

        // Act
        var result = BlobPathValidator.IsValid(blobPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_PathContainsDoubleDot_InFilenameSegment_ReturnsFalse_BlocksPathTraversal()
    {
        // Arrange — ".." embedded inside the filename segment rather than as a
        // standalone path component; guards against a narrowed check that only
        // looks for "/../" instead of the literal substring "..".
        const string blobPath = "2026-01-01/report..pdf";

        // Act
        var result = BlobPathValidator.IsValid(blobPath);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("2026-01-01/report.pdf")]
    [InlineData("2026-01-01/report.PDF")]
    public void IsValid_WellFormedPath_ReturnsTrue(string blobPath)
    {
        // Act
        var result = BlobPathValidator.IsValid(blobPath);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("2026-13-01/report.pdf")]  // month 13 does not exist
    [InlineData("2026-02-30/report.pdf")]  // February 30th does not exist
    public void IsValid_DateShapedPrefixIsNotARealDate_ReturnsFalse(string blobPath)
    {
        // Act
        var result = BlobPathValidator.IsValid(blobPath);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("notadate/report.pdf")]             // prefix is not date-shaped
    [InlineData("2026-01-01/subdir/report.pdf")]     // extra "/" segment
    [InlineData("2026-01-01/report.xlsx")]           // wrong extension
    [InlineData("2026-01-01report.pdf")]             // missing "/" separator
    public void IsValid_StructuralMismatch_ReturnsFalse(string blobPath)
    {
        // Act
        var result = BlobPathValidator.IsValid(blobPath);

        // Assert
        Assert.False(result);
    }
}
