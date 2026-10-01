namespace Anela.Heblo.Application.Features.ProcessDocs.Contracts;

public class ProcessSummaryDto
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string VerifiedAt { get; set; } = string.Empty;
    public List<string> Related { get; set; } = [];
}

public class ProcessDocDto : ProcessSummaryDto
{
    public string Markdown { get; set; } = string.Empty;
}
