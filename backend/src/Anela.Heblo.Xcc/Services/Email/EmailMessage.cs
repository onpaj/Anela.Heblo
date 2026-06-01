namespace Anela.Heblo.Xcc.Services.Email;

public class EmailMessage
{
    public string From { get; set; } = string.Empty;
    public List<string> To { get; set; } = new();
    public string Subject { get; set; } = string.Empty;
    public string HtmlContent { get; set; } = string.Empty;
    public string? PlainTextContent { get; set; }
    public List<EmailAttachment> Attachments { get; set; } = new();
}
