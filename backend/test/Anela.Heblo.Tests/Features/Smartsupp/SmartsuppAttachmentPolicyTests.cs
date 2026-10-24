using Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppAttachmentPolicyTests
{
    private const string DocxType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    [Theory]
    [InlineData("report.docx", "application/octet-stream", DocxType)]
    [InlineData("report.docx", null, DocxType)]
    [InlineData("scan.pdf", "", "application/pdf")]
    [InlineData("photo.PNG", "image/png", "image/png")]
    [InlineData("photo.jpeg", "IMAGE/JPEG", "image/jpeg")]
    [InlineData("photo.jpg", null, "image/jpeg")]
    [InlineData("note.txt", "text/plain", "text/plain")]
    public void TryResolveFileType_ReturnsMappedType_WhenAllowed(string fileName, string? declared, string expected)
    {
        var ok = SmartsuppAttachmentPolicy.TryResolveFileType(fileName, declared, out var fileType);

        ok.Should().BeTrue();
        fileType.Should().Be(expected);
    }

    [Theory]
    [InlineData("photo.png", "application/pdf")]
    [InlineData("malware.exe", null)]
    [InlineData("malware.exe", "application/octet-stream")]
    [InlineData("noextension", null)]
    [InlineData("", null)]
    public void TryResolveFileType_ReturnsFalse_WhenRejected(string fileName, string? declared)
    {
        var ok = SmartsuppAttachmentPolicy.TryResolveFileType(fileName, declared, out var fileType);

        ok.Should().BeFalse();
        fileType.Should().BeEmpty();
    }
}
