namespace Anela.Heblo.Application.Features.Smartsupp.Contracts;

public class MessageAttachmentDto
{
    public string Type { get; set; } = "file";
    public string FileName { get; set; } = "";
    public string? FileType { get; set; }
    public string Url { get; set; } = "";
    public long? Size { get; set; }
    public string? ThumbUrl { get; set; }
}
