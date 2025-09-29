using Anela.Heblo.Domain.Features.Bank;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence;

public static class BankStatementsDbMapper
{
    public static ModelBuilder ConfigureBankStatements(this ModelBuilder builder)
    {
        builder.Entity<BankStatementImport>(b =>
        {
            b.ToTable("BankStatements", "dbo");
            b.HasKey(p => p.Id);
            b.Property(p => p.StatementDate).IsRequired();
            b.Property(p => p.ImportDate).IsRequired();
            b.Property(p => p.Account).IsRequired().HasMaxLength(100);
            b.Property(p => p.Currency).IsRequired().HasMaxLength(10);
            b.Property(p => p.ItemCount).IsRequired();
            b.Property(p => p.ImportResult).IsRequired().HasMaxLength(50);
            b.Property(p => p.ExtraProperties).HasMaxLength(4000);
            b.Property(p => p.ConcurrencyStamp).HasMaxLength(50);
            b.Property(p => p.CreationTime).IsRequired();
            b.Property(p => p.CreatorId).HasMaxLength(100);
            b.Property(p => p.LastModificationTime);
            b.Property(p => p.LastModifierId).HasMaxLength(100);
        });

        return builder;
    }
}