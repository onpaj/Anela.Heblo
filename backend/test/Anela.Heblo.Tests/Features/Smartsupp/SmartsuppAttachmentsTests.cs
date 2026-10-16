using System.Text.Json;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppAttachmentsTests
{
    private const string PdfUrl = "https://files.smartsuppcdn.com/files/accounts/924500/uploads/sEYrSOqaL.pdf";
    private const string ImageUrl = "https://files.smartsuppcdn.com/files/accounts/924500/attachments/7m8eef-Fr.png";
    private const string ThumbUrl = "https://files.smartsuppcdn.com/files/accounts/924500/attachments/7m8eef-Fr_thumb400.jpg";

    private const string UploadData =
        @"{""type"":""file"",""file_name"":""1040963052.pdf"",""file_type"":""application/pdf"",""url"":""" + PdfUrl + @""",""size"":598612}";

    private const string AgentAttachments =
        @"[{""type"":""image"",""file_name"":""test.png"",""file_type"":""image/png"",""url"":""" + ImageUrl + @""",""size"":177,""width"":64,""height"":64,""thumb_400"":{""url"":""" + ThumbUrl + @""",""width"":64,""height"":64}}]";

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IReadOnlyList<SmartsuppAttachment> ParseBack(string json)
    {
        SmartsuppAttachments.TryParse(json, out var list).Should().BeTrue();
        return list;
    }

    [Fact]
    public void BuildJson_IncludesCustomerUploadFromContentData()
    {
        var json = SmartsuppAttachments.BuildJson(Parse("[]"), "upload", Parse(UploadData));

        var items = ParseBack(json);
        items.Should().ContainSingle();
        items[0].Type.Should().Be("file");
        items[0].FileName.Should().Be("1040963052.pdf");
        items[0].Size.Should().Be(598612);
        items[0].Url.Should().Be(PdfUrl);
    }

    [Fact]
    public void BuildJson_KeepsAgentAttachmentWithThumb()
    {
        var json = SmartsuppAttachments.BuildJson(Parse(AgentAttachments), "text", null);

        var items = ParseBack(json);
        items.Should().ContainSingle();
        items[0].Type.Should().Be("image");
        items[0].ThumbUrl.Should().Be(ThumbUrl);
        json.Should().Contain("\"thumb_400\":{\"url\"");
    }

    [Fact]
    public void BuildJson_SkipsCardsAndReturnsEmptyArrayWhenNothing()
    {
        var json = SmartsuppAttachments.BuildJson(Parse(@"[{""type"":""cards"",""cards"":[]}]"), "text", null);

        json.Should().Be("[]");
        SmartsuppAttachments.BuildJson(null, null, null).Should().Be("[]");
    }

    [Fact]
    public void BuildJson_IgnoresContentDataWhenContentTypeIsText()
    {
        var json = SmartsuppAttachments.BuildJson(Parse("[]"), "text", Parse(UploadData));

        json.Should().Be("[]");
    }

    [Fact]
    public void TryParse_DropsNonHttpsUrls()
    {
        var raw = @"[{""type"":""file"",""file_name"":""a"",""url"":""http://x.test/a.pdf""},"
            + @"{""type"":""file"",""file_name"":""b"",""url"":""javascript:alert(1)""},"
            + @"{""type"":""file"",""file_name"":""c""},"
            + @"{""type"":""file"",""file_name"":""d"",""url"":""https://x.test/d.pdf""}]";

        SmartsuppAttachments.TryParse(raw, out var items).Should().BeTrue();

        items.Select(i => i.FileName).Should().Equal("d");
    }

    [Fact]
    public void TryParse_ReturnsFalseOnMalformedJson()
    {
        SmartsuppAttachments.TryParse("{not json", out var items).Should().BeFalse();

        items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TryParse_NullOrEmpty_ReturnsTrueAndEmpty(string? json)
    {
        SmartsuppAttachments.TryParse(json, out var items).Should().BeTrue();

        items.Should().BeEmpty();
    }

    [Fact]
    public void TryParse_ReadsLegacyRawRow()
    {
        SmartsuppAttachments.TryParse(AgentAttachments, out var items).Should().BeTrue();

        items.Should().ContainSingle();
        items[0].Url.Should().Be(ImageUrl);
        items[0].FileType.Should().Be("image/png");
        items[0].Size.Should().Be(177);
        items[0].ThumbUrl.Should().Be(ThumbUrl);
    }
}
