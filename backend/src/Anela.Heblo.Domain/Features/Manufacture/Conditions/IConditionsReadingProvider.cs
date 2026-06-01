namespace Anela.Heblo.Domain.Features.Manufacture.Conditions;

public interface IConditionsReadingProvider
{
    Task<ConditionsSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken);
}
