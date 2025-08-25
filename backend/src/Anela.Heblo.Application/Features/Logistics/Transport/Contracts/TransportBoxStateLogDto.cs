namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class TransportBoxStateLogDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime StateDate { get; set; }
    public string? User { get; set; }
    public string? Description { get; set; }
}