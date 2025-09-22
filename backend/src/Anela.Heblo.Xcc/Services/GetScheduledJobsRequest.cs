namespace Anela.Heblo.Xcc.Services;

public class GetScheduledJobsRequest
{
    public int Offset { get; set; } = 0;
    public int Count { get; set; } = 50;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}