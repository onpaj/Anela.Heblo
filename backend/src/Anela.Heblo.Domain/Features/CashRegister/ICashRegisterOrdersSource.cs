namespace Anela.Heblo.Domain.Features.CashRegister;

public interface ICashRegisterOrdersSource
{
    Task<List<CashRegisterOrder>> GetAllAsync(CashRegistryRequest query);
}