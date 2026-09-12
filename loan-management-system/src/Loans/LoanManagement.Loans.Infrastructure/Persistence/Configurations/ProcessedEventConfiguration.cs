using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LoanManagement.Loans.Infrastructure.Persistence.Configurations;

internal sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("processed_events");

        // Composite key: one row per (event, consumer). A unique insert here is what
        // makes duplicate delivery a no-op for a consumer.
        builder.HasKey(e => new { e.EventId, e.Consumer });
        builder.Property(e => e.Consumer).HasMaxLength(200);
    }
}
