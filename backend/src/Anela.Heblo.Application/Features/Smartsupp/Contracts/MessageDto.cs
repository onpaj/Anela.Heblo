namespace Anela.Heblo.Application.Features.Smartsupp.Contracts;

public class MessageDto
{
    public string Id { get; set; } = null!;
    public string AuthorType { get; set; } = null!;
    public string? AuthorName { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }

    public string? AgentId { get; set; }
    public string? SubType { get; set; }
    public string? DeliveryStatus { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public int? ResponseTime { get; set; }
    public bool IsFirstReply { get; set; }
    public string? PageUrl { get; set; }
}
