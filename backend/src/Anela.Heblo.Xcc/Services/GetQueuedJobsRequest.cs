namespace Anela.Heblo.Xcc.Services;

public class GetQueuedJobsRequest
{
    public int Offset { get; set; } = 0;
    public int Count { get; set; } = 50;
    public string? Queue { get; set; } = "default";
    public string? State { get; set; }
}