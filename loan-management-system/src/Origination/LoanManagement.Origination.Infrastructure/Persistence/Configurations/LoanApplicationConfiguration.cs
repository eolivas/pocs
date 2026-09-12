using LoanManagement.Origination.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LoanManagement.Origination.Infrastructure.Persistence.Configurations;

internal sealed class LoanApplicationConfiguration : IEntityTypeConfiguration<LoanApplication>
{
    public void Configure(EntityTypeBuilder<LoanApplication> builder)
    {
        builder.ToTable("loan_applications");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasConversion(id => id.Value, value => new Domain.ApplicationId(value))
            .ValueGeneratedNever();

        builder.Property(a => a.PartnerName).HasMaxLength(200);
        builder.Property(a => a.Currency).HasMaxLength(3);
        builder.Property(a => a.RequestedAmount).HasColumnType("decimal(18,2)");
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

        builder.Ignore(a => a.DomainEvents);
    }
}
