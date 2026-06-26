using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;

public class ImportBankStatementResponse : BaseResponse
{
    public List<BankStatementImportDto> Statements { get; set; } = new List<BankStatementImportDto>();

    /// <summary>Newly-attempted statements that imported successfully this run.</summary>
    public int SuccessCount { get; set; }

    /// <summary>Newly-attempted statements that failed this run.</summary>
    public int ErrorCount { get; set; }

    /// <summary>Statements skipped because they were already imported successfully.</summary>
    public int SkippedCount { get; set; }

    public bool HasErrors => ErrorCount > 0;
}
