namespace Anela.Heblo.Xcc.Services.Email;

public class EmailAttachment
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty; // Base64-encoded
    public string ContentType { get; set; } = "application/octet-stream";
}
