using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LoanManagement.CustomerPortal.Infrastructure.Persistence.Configurations;

internal sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("processed_events");
        builder.HasKey(e => new { e.EventId, e.Consumer });
        builder.Property(e => e.Consumer).HasMaxLength(200);
    }
}
