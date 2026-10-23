using Anela.Heblo.Application.Features.FileStorage.Infrastructure;

namespace Anela.Heblo.Tests.Features.FileStorage;

public class BlobNamingTests
{
    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/webp", ".webp")]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("text/plain", ".txt")]
    [InlineData("application/json", ".json")]
    [InlineData("application/xml", ".xml")]
    [InlineData("application/unknown", ".bin")]
    [InlineData(null, ".bin")]
    public void GetExtensionFromContentType_MapsKnownTypesAndDefaultsToBin(string? mediaType, string expected)
    {
        Assert.Equal(expected, BlobNaming.GetExtensionFromContentType(mediaType));
    }

    [Theory]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("a.JPG", "image/jpeg")]
    [InlineData("a.png", "image/png")]
    [InlineData("a.gif", "image/gif")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.pdf", "application/pdf")]
    [InlineData("a.txt", "text/plain")]
    [InlineData("a.json", "application/json")]
    [InlineData("a.xml", "application/xml")]
    [InlineData("a.zip", "application/zip")]
    [InlineData("a.doc", "application/msword")]
    [InlineData("a.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("a.xls", "application/vnd.ms-excel")]
    [InlineData("a.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("a.unknown", "application/octet-stream")]
    [InlineData("noextension", "application/octet-stream")]
    public void GetContentTypeFromExtension_MapsKnownExtensionsAndDefaults(string fileName, string expected)
    {
        Assert.Equal(expected, BlobNaming.GetContentTypeFromExtension(fileName));
    }

    [Fact]
    public void ResolveBlobName_ReturnsRequestedNameWhenProvided()
    {
        Assert.Equal("mine.png", BlobNaming.ResolveBlobName("mine.png", "https://example.com/other.jpg", "image/jpeg"));
    }

    [Fact]
    public void ResolveBlobName_UsesUrlPathFileNameWhenNoRequestedName()
    {
        Assert.Equal("photo.jpg", BlobNaming.ResolveBlobName(null, "https://example.com/dir/photo.jpg?x=1", "image/png"));
        Assert.Equal("photo.jpg", BlobNaming.ResolveBlobName("", "https://example.com/dir/photo.jpg", null));
    }

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("application/unknown", ".bin")]
    [InlineData(null, ".bin")]
    public void ResolveBlobName_GeneratesNameWhenUrlHasNoFileName(string? mediaType, string expectedExtension)
    {
        var name = BlobNaming.ResolveBlobName(null, "https://example.com/", mediaType);

        Assert.StartsWith("downloaded-file-", name);
        Assert.EndsWith(expectedExtension, name);
    }
}
