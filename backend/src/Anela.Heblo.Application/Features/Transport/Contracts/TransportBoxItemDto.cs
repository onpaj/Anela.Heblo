namespace Anela.Heblo.Application.Features.Transport.Contracts;

public class TransportBoxItemDto
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public double Amount { get; set; }
    public DateTime DateAdded { get; set; }
    public string UserAdded { get; set; } = string.Empty;
}