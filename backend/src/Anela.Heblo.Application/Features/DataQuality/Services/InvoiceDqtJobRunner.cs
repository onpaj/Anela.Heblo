using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class InvoiceDqtJobRunner : IInvoiceDqtJobRunner
{
    private readonly IDqtRunRepository _repository;
    private readonly IInvoiceDqtComparer _comparer;
    private readonly ILogger<InvoiceDqtJobRunner> _logger;

    public InvoiceDqtJobRunner(
        IDqtRunRepository repository,
        IInvoiceDqtComparer comparer,
        ILogger<InvoiceDqtJobRunner> logger)
    {
        _repository = repository;
        _comparer = comparer;
        _logger = logger;
    }

    public async Task RunAsync(Guid dqtRunId, CancellationToken cancellationToken = default)
    {
        var run = await _repository.GetByIdAsync(dqtRunId, cancellationToken);
        if (run == null)
        {
            _logger.LogWarning("DQT run {DqtRunId} not found", dqtRunId);
            return;
        }

        _logger.LogInformation("Starting DQT run {DqtRunId} ({TestType}) for {DateFrom} to {DateTo}",
            dqtRunId, run.TestType, run.DateFrom, run.DateTo);

        try
        {
            var result = await _comparer.CompareAsync(run.DateFrom, run.DateTo, cancellationToken);

            foreach (var mismatch in result.Mismatches)
            {
                run.Results.Add(InvoiceDqtResult.Create(
                    run.Id,
                    mismatch.InvoiceCode,
                    mismatch.MismatchType,
                    mismatch.ShoptetValue,
                    mismatch.FlexiValue,
                    mismatch.Details));
            }

            run.Complete(result.TotalChecked, result.Mismatches.Count);
            await _repository.UpdateAsync(run, cancellationToken);

            _logger.LogInformation("DQT run {DqtRunId} completed: {Checked} checked, {Mismatches} mismatches",
                dqtRunId, result.TotalChecked, result.Mismatches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DQT run {DqtRunId} failed", dqtRunId);
            run.Fail(ex.Message);
            await _repository.UpdateAsync(run, cancellationToken);
        }
    }
}
