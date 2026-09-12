using LoanManagement.CustomerPortal.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LoanManagement.CustomerPortal.Infrastructure.Persistence.Configurations;

internal sealed class BorrowerLoanViewConfiguration : IEntityTypeConfiguration<BorrowerLoanView>
{
    public void Configure(EntityTypeBuilder<BorrowerLoanView> builder)
    {
        builder.ToTable("borrower_loan_views");

        builder.HasKey(v => v.LoanId);
        builder.Property(v => v.Status).HasMaxLength(20);
        builder.Property(v => v.Currency).HasMaxLength(3);
        builder.Property(v => v.Amount).HasColumnType("decimal(18,2)");

        builder.HasIndex(v => v.ApplicationId);
    }
}
