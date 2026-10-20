using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Attendance.UseCases.RunBreakInsertion;

public class RunBreakInsertionResponse : BaseResponse
{
    public int DaysScanned { get; set; }
    public int BreaksInserted { get; set; }

    /// <summary>Days that already carried our break but whose work records were not yet recreated.</summary>
    public int DaysHealed { get; set; }

    /// <summary>Original work records replaced by new ones around the break.</summary>
    public int RecordsRecreated { get; set; }

    /// <summary>Days whose break is in place but whose work records could not be recreated; retried next run.</summary>
    public int RecreateFailed { get; set; }

    public int SkippedExistingBreak { get; set; }
    public int SkippedInProgress { get; set; }
    public int SkippedBelowThreshold { get; set; }
    public int SkippedHoursOnly { get; set; }
    public int SkippedNoSlot { get; set; }
    public int Failed { get; set; }

    public RunBreakInsertionResponse() : base() { }

    public RunBreakInsertionResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
