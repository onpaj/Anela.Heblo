namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters;

/// <summary>
/// Implemented by ERP exceptions that can signal a material stock shortage detected
/// before anything was written to the ERP, so the manufacture must not be confirmed.
/// </summary>
public interface IManufactureStockShortage
{
    bool IsStockShortage { get; }
}
