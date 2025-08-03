using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.EntityFrameworkCore;

public static class BankStatementsDbMapper
{
    public static ModelBuilder ConfigureBankStatements(this ModelBuilder builder)
    {
        builder.Entity<BankStatementImport>(b =>
        {
            b.ToTable("BankStatements", "dbo");
            b.HasKey(p => p.Id);
        });

        return builder;
    }
}