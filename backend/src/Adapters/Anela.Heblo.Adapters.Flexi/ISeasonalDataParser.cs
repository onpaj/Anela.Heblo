namespace Anela.Heblo.Adapters.Flexi;

public interface ISeasonalDataParser
{
    int[] GetSeasonalMonths(string? value);
}