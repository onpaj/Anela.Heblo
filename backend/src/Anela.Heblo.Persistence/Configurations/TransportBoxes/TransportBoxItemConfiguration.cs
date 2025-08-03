using Anela.Heblo.Domain.Features.Logistics.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Configurations.TransportBoxes;

public class TransportBoxItemConfiguration : IEntityTypeConfiguration<TransportBoxItem>
{
    public void Configure(EntityTypeBuilder<TransportBoxItem> builder)
    {
        builder.ToTable("TransportBoxItem", "dbo");

        builder.HasKey(x => x.Id);
    }
}