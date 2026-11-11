namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

/// <summary>A file the operator attaches to an outgoing Smartsupp message.</summary>
public class SendMessageFile
{
    public string FileName { get; set; } = null!;

    /// <summary>Content type declared by the client; the extension decides the type actually sent.</summary>
    public string? ContentType { get; set; }

    public byte[] Content { get; set; } = [];
}
