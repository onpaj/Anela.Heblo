using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class DriftDqtJobRunner : IDriftDqtJobRunner
{
    private readonly IDqtRunRepository _repository;
    private readonly IEnumerable<IDriftDqtComparer> _comparers;
    private readonly ILogger<DriftDqtJobRunner> _logger;

    public DriftDqtJobRunner(
        IDqtRunRepository repository,
        IEnumerable<IDriftDqtComparer> comparers,
        ILogger<DriftDqtJobRunner> logger)
    {
        _repository = repository;
        _comparers = comparers;
        _logger = logger;
    }

    public async Task RunAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _repository.GetByIdAsync(runId, ct);
        if (run == null)
        {
            _logger.LogWarning("Drift DQT run {RunId} not found", runId);
            return;
        }

        _logger.LogInformation("Starting drift DQT run {RunId} ({TestType}) for {DateFrom} to {DateTo}",
            runId, run.TestType, run.DateFrom, run.DateTo);

        try
        {
            var comparer = _comparers.SingleOrDefault(c => c.TestType == run.TestType)
                ?? throw new InvalidOperationException(
                    $"No IDriftDqtComparer registered for {run.TestType}");

            var result = await comparer.CompareAsync(run.DateFrom, run.DateTo, ct);

            var entities = result.Mismatches
                .Select(m => DqtDriftResult.Create(
                    run.Id, run.TestType, m.EntityKey, m.MismatchCode,
                    m.HebloValue, m.ShoptetValue, m.Details))
                .ToList();

            await _repository.AddDriftResultsAsync(entities, ct);
            run.Complete(result.TotalChecked, result.Mismatches.Count);

            _logger.LogInformation("Drift DQT run {RunId} completed: {Checked} checked, {Mismatches} mismatches",
                runId, result.TotalChecked, result.Mismatches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Drift DQT run {RunId} ({TestType}) failed", runId, run.TestType);
            run.Fail(ex.Message);
        }
        finally
        {
            await _repository.SaveChangesAsync(ct);
        }
    }
}
