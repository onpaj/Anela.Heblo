namespace Anela.Heblo.Application.Features.Smartsupp.Contracts;

public class MessageDto
{
    public string Id { get; set; } = null!;
    public string AuthorType { get; set; } = null!;
    public string? AuthorName { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
}
