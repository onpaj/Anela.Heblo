using System;
using Anela.Heblo.Invoices;
using Anela.Heblo.Xcc.Domain;
using Volo.Abp.Domain.Entities;

namespace Anela.Heblo.IssuedInvoices;

public class IssuedInvoiceSyncData : Entity<int>
{
    public string? Data { get; set; }
    public bool IsSuccess { get; set; } = true;
    public IssuedInvoiceError? Error { get; set; }
    public DateTime SyncTime { get; set; }
}