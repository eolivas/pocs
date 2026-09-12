using LoanManagement.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LoanManagement.Loans.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasMaxLength(1024).IsRequired();
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.CorrelationId).HasMaxLength(100);
        builder.Property(m => m.FailureReason).HasMaxLength(2000);

        // Fast lookup of pending messages by the outbox processor.
        builder.HasIndex(m => new { m.ProcessedAt, m.FailedAt });
    }
}
