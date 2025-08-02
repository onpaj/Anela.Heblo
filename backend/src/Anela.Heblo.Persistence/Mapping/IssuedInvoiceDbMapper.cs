using Anela.Heblo.Invoices;
using Anela.Heblo.IssuedInvoices;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Anela.Heblo.EntityFrameworkCore;

public static class IssuedInvoiceDbMapper
{
    public static ModelBuilder ConfigureIssuedInvoices(this ModelBuilder builder)
    {
        builder.Entity<IssuedInvoice>(b =>
        {
            b.ToTable("IssuedInvoice", "dbo");
            b.HasKey(p => p.Id);
            b.HasMany(e => e.SyncHistory)
                .WithOne()
                .IsRequired();
        });
        
        
        builder.Entity<IssuedInvoiceSyncData>(b =>
        {
            b.ToTable("IssuedInvoiceSyncData", "dbo");
            b.OwnsOne<IssuedInvoiceError>(p => p.Error);
        });

        return builder;
    }
}