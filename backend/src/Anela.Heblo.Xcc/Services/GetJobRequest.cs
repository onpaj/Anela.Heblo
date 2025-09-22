namespace Anela.Heblo.Xcc.Services;

public class GetJobRequest
{
    public string JobId { get; set; } = string.Empty;
    public bool IncludeHistory { get; set; } = false;
}