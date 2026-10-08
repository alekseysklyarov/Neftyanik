using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Tests;

internal static class PrePlatformManagementDbContext
{
    public static ApplicationDbContext Create(DbContextOptions<ApplicationDbContext> options, IAssociationContext? association = null) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>(options)
            .ReplaceService<IModelCustomizer, HistoricalModelCustomizer>().Options, association);

    private sealed class HistoricalModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder builder, DbContext context)
        {
            base.Customize(builder, context);
            builder.Entity<Member>().Ignore(x => x.HasTwoElectricityMeters);
            builder.Entity<MemberElectricityReading>().Ignore(x => x.PhysicalMeterReadingsJson);
            builder.Entity<Charge>().Ignore(x => x.Member).Ignore(x => x.MemberId);
            builder.Entity<ChargeType>().Ignore(x => x.IsMembershipFee);
            builder.Entity<Expense>().Ignore(x => x.PaymentMethod).Ignore(x => x.FundingSource);
            builder.Ignore<AssociationAccountBinding>();
            builder.Ignore<PlatformAuditLog>();
            builder.Entity<Association>().Ignore(x => x.ContactEmail).Ignore(x => x.ContactPhone)
                .Ignore(x => x.PostalAddress).Ignore(x => x.Revision);
        }
    }
}
