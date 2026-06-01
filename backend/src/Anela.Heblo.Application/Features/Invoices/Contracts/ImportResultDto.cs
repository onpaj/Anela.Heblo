namespace Anela.Heblo.Application.Features.Invoices.Contracts;

public class ImportResultDto
{
    public string RequestId { get; set; } = string.Empty;
    public List<string> Succeeded { get; set; } = new();
    public List<string> Failed { get; set; } = new();
}