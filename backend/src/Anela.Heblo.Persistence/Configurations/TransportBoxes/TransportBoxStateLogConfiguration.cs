using Anela.Heblo.Domain.Features.Logistics.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Configurations.TransportBoxes;

public class TransportBoxStateLogConfiguration : IEntityTypeConfiguration<TransportBoxStateLog>
{
    public void Configure(EntityTypeBuilder<TransportBoxStateLog> builder)
    {
        builder.ToTable("TransportBoxStateLog", "dbo");
        
        builder.HasKey(x => x.Id);
    }
}