using LoanManagement.Loans.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LoanManagement.Loans.Infrastructure.Persistence.Configurations;

internal sealed class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ToTable("loans");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasConversion(id => id.Value, value => new LoanId(value))
            .ValueGeneratedNever();

        builder.Property(l => l.ContractId)
            .HasConversion(id => id.Value, value => new ContractId(value));

        builder.Property(l => l.ApplicationId)
            .HasConversion(id => id.Value, value => new Domain.ApplicationId(value));

        // Money as an owned value object (single-currency principal).
        builder.OwnsOne(l => l.Principal, money =>
        {
            money.Property(m => m.Amount).HasColumnName("principal_amount").HasColumnType("decimal(18,2)");
            money.Property(m => m.Currency).HasColumnName("principal_currency").HasMaxLength(3);
        });

        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.FailureReason).HasMaxLength(500);

        // A contract funds exactly one loan — enforce idempotency at the DB level too.
        builder.HasIndex(l => l.ContractId).IsUnique();

        // Domain events are not persisted columns.
        builder.Ignore(l => l.DomainEvents);
    }
}
