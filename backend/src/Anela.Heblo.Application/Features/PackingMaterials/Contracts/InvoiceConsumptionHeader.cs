namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

/// <summary>
/// Immutable projection of an invoice header containing only the fields the
/// daily consumption calculation needs. Owned by PackingMaterials; populated
/// by the Invoices module via <see cref="IInvoiceConsumptionSource"/> adapter.
/// </summary>
public sealed record InvoiceConsumptionHeader(string Id, int ItemsCount);
