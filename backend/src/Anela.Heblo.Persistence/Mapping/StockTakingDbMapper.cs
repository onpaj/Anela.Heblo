using Anela.Heblo.Catalog.StockTaking;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Anela.Heblo.EntityFrameworkCore;

public static class StockTakingDbMapper
{
    public static ModelBuilder ConfigureStockTaking(this ModelBuilder builder)
    {
        builder.Entity<StockTakingResult>(b =>
        {
            b.ToTable("StockTakingResults", "dbo");
            b.HasKey(p => p.Id);
        });

        return builder;
    }
}