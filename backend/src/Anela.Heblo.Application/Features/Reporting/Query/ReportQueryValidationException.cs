namespace Anela.Heblo.Application.Features.Reporting.Query;

/// <summary>The request cannot be turned into a query; the message is shown to the caller.</summary>
public sealed class ReportQueryValidationException : Exception
{
    public ReportQueryValidationException(string reason) : base(reason) { }
}
