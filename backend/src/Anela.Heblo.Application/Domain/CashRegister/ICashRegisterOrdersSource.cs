namespace Anela.Heblo.Application.Domain.CashRegister;

public interface ICashRegisterOrdersSource
{
    Task<List<CashRegisterOrder>> GetAllAsync(CashRegistryRequest query);
}