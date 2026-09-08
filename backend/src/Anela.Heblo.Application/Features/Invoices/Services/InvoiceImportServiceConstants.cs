namespace Anela.Heblo.Application.Features.Invoices.Services;

/// <summary>
/// Sole owner of the invoice-import Hangfire job's display-name text. Every other component
/// that needs the prefix or the full display-name format reads it from here instead of
/// holding its own literal.
/// </summary>
public static class InvoiceImportServiceConstants
{
    /// <summary>
    /// The prefix used to identify invoice-import Hangfire jobs (e.g. for <c>StartsWith</c> matching).
    /// </summary>
    public const string ImportPrefix = "Import faktur:";

    /// <summary>
    /// The full Hangfire <see cref="System.ComponentModel.DisplayNameAttribute"/> format string,
    /// derived from <see cref="ImportPrefix"/>. Both <c>[DisplayName]</c> usages on
    /// <see cref="IInvoiceImportService.ImportInvoicesAsync"/> and
    /// <see cref="InvoiceImportService.ImportInvoicesAsync"/> reference this constant and must
    /// not be hand-edited independently of it.
    /// </summary>
    public const string DisplayNameFormat = $"{ImportPrefix} {{0}}";
}
