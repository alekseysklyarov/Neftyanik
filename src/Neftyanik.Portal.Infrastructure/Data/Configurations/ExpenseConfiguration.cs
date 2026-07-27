using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount)
            .HasPrecision(18, 2);

        builder.Property(x => x.Description)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.Payee)
            .HasMaxLength(200);

        builder.Property(x => x.DocumentNumber)
            .HasMaxLength(100);

        builder.Property(x => x.AttachmentPath)
            .HasMaxLength(500);

        builder.Property(x => x.CreatedByUserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.HasIndex(x => x.ExpenseDate);

        builder.HasOne(x => x.ExpenseCategory)
            .WithMany(x => x.Expenses)
            .HasForeignKey(x => x.ExpenseCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.CreatedExpenses)
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(x => x.CategoryId);
        builder.Ignore(x => x.Category);
        builder.Ignore(x => x.Date);
        builder.Ignore(x => x.PaidById);
    }
}