using Anela.Heblo.Domain.Features.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Rag;

public class RagInteractionLogConfiguration : IEntityTypeConfiguration<RagInteractionLog>
{
    public void Configure(EntityTypeBuilder<RagInteractionLog> builder)
    {
        builder.ToTable("RagInteractionLogs", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Feature).HasConversion<int>().IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UserId).IsRequired(false);

        builder.Property(x => x.Question).IsRequired().HasColumnType("text");
        builder.Property(x => x.ExpandedQuery).IsRequired(false).HasColumnType("text");
        builder.Property(x => x.TopK).IsRequired();
        builder.Property(x => x.SourceCount).IsRequired();
        builder.Property(x => x.RetrievedChunksJson).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.SystemPrompt).IsRequired().HasColumnType("text");
        builder.Property(x => x.Answer).IsRequired().HasColumnType("text");

        builder.Property(x => x.ConversationId).IsRequired(false);
        builder.Property(x => x.Topic).IsRequired(false).HasColumnType("text");
        builder.Property(x => x.SentAnswer).IsRequired(false).HasColumnType("text");
        builder.Property(x => x.WasEdited).IsRequired(false);
        builder.Property(x => x.SentAt).IsRequired(false);

        builder.Property(x => x.PrecisionScore).IsRequired(false);
        builder.Property(x => x.StyleScore).IsRequired(false);
        builder.Property(x => x.FeedbackComment).IsRequired(false).HasColumnType("text");

        builder.Property(x => x.DurationMs).IsRequired();

        builder.HasIndex(x => new { x.Feature, x.CreatedAt });
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.ConversationId);
    }
}
