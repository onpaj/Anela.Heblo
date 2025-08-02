namespace Anela.Heblo.Adapters.Flexi.ProductAttributes;

public interface ISeasonalDataParser
{
    int[] GetSeasonalMonths(string? value);
}