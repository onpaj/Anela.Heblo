namespace Anela.Heblo.Domain.Features.Bank;

public interface IBankImportStateRepository
{
    Task<BankImportState?> GetByAccountAsync(string account, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BankImportState>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(BankImportState state, CancellationToken cancellationToken = default);
}
