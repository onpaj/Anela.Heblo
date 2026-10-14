namespace Anela.Heblo.Application.Features.Reporting.Data;

public sealed class ReportQueryTimeoutException : Exception
{
    public ReportQueryTimeoutException(Exception inner)
        : base("The reporting query exceeded its statement timeout.", inner) { }
}
